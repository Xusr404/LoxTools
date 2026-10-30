using LoxTools.Core.Settings;
using LoxTools.Models;
using LoxTools.UI.Tray;
using AppSettings = LoxTools.Properties.Settings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.UpdateCheck {
    internal enum UpdateCheckState {
        Checking,
        Downloading,
        Verifying,
        Extracting,
        Cancelled,
        Installing,
        CancellingInstaller,
        InstallationCancelled,
        Installed,
        UpdateAvailable,
        UpToDate,
        Failed
    }

    internal sealed class UpdateCheckService : IDisposable {
        private const string UpdateChannelValueName = "UpdateChannel";
        private const string IncludeAlphaValueName = "IncludeAlpha";

        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan NormalInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(35);
        private static readonly TimeSpan InstallerDownloadTimeout = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan RetryDelayShort = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan RetryDelayLong = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan CleanupAge = TimeSpan.FromHours(6);
        private static readonly TimeSpan[] BackoffSchedule = new[] {
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(60),
            TimeSpan.FromHours(6)
        };

        private readonly ISynchronizeInvoke uiInvoker;
        private readonly Func<IReadOnlyList<string>> localBuildProvider;
        private readonly Action closeApplicationAction;
        private readonly bool simulateUpdateAvailable;
        private readonly SemaphoreSlim operationLock = new SemaphoreSlim(1, 1);
        private readonly HttpClient httpClient;
        private readonly UpdateCheckXmlClient xmlClient;
        private readonly UpdateCheckXmlParser xmlParser = new UpdateCheckXmlParser();
        private readonly UpdateInstallerWorkflow installerWorkflow;
        private System.Threading.Timer checkTimer;
        private UpdateCheckState state = UpdateCheckState.UpToDate;
        private UpdateInfo lastTargetInfo;
        private string lastRemoteVersionDisplay;
        private string lastLocalVersionDisplay;
        private UpdateChannel lastConfiguredChannel = UpdateChannel.Release;
        private UpdateChannel lastResolvedChannel = UpdateChannel.Release;
        private UpdateChannel? forcedChannel;
        private readonly object forcedChannelLock = new object();
        private readonly object downloadGate = new object();
        private CancellationTokenSource activeDownload;
        private volatile bool userCancelledDownload;
        private volatile bool disposed;
        private string installationRoot;
        public event Action<string> InstallationCompleted;

        public UpdateCheckService(
            ISynchronizeInvoke uiInvoker,
            Func<IReadOnlyList<string>> localBuildProvider,
            Action closeApplicationAction,
            bool simulateUpdateAvailable)
            : this(
                uiInvoker,
                localBuildProvider,
                closeApplicationAction,
                simulateUpdateAvailable,
                UpdateSecurityPolicy.CreateHttpClient(),
                new WindowsInstallerSignatureVerifier()) {
        }

        internal UpdateCheckService(
            ISynchronizeInvoke uiInvoker,
            Func<IReadOnlyList<string>> localBuildProvider,
            Action closeApplicationAction,
            bool simulateUpdateAvailable,
            HttpClient httpClient,
            IInstallerSignatureVerifier signatureVerifier) {
            this.uiInvoker = uiInvoker;
            this.localBuildProvider = localBuildProvider;
            this.closeApplicationAction = closeApplicationAction;
            this.simulateUpdateAvailable = simulateUpdateAvailable;
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            xmlClient = new UpdateCheckXmlClient(this.httpClient);
            var installerPipeline = new UpdateInstallerPipeline(this.httpClient, signatureVerifier);
            installerPipeline.ReportStatus = (phase, percent) => {
                if (state != phase) UpdateState(phase, isEnabled: true);
                if (percent.HasValue) InvokeOnUi(() => ContextMenuManager.UpdateDownloadProgress(percent.Value));
            };
            installerWorkflow = new UpdateInstallerWorkflow(installerPipeline, CreateInstallerShortcut, StartInstallerProcess);
        }

        public void Start() {
            if (simulateUpdateAvailable) {
                UpdateState(UpdateCheckState.UpdateAvailable, isEnabled: true);
                return;
            }
            CleanupTempRoot(CleanupAge);
            UpdateState(UpdateCheckState.Checking, isEnabled: false);
            _ = RunCheckAsync(force: true);
        }

        public void HandleMenuAction() {
            lock (downloadGate) {
                if (activeDownload != null) {
                    userCancelledDownload = true;
                    activeDownload.Cancel();
                    return;
                }
            }
            if (simulateUpdateAvailable) {
                UpdateState(UpdateCheckState.UpdateAvailable, isEnabled: true);
                return;
            }
            if (state == UpdateCheckState.UpdateAvailable || state == UpdateCheckState.Cancelled || state == UpdateCheckState.InstallationCancelled) {
                _ = DownloadAndInstallAsync();
            } else {
                SetForcedChannel(GetConfiguredChannel());
                _ = RunCheckAsync(force: true);
            }
        }

        public void RequestCheck(UpdateChannel channel) {
            if (simulateUpdateAvailable) {
                UpdateState(UpdateCheckState.UpdateAvailable, isEnabled: true);
                return;
            }

            SetForcedChannel(channel);
            _ = RunCheckAsync(force: true);
        }

        private TimeSpan CalculateInitialDelay() {
            DateTime lastAttempt = SettingsGuard.Execute(
                () => AppSettings.Default.UpdateLastCheckUtc,
                DateTime.MinValue);
            TimeSpan backoff = GetBackoffDelay();
            TimeSpan minimumDelay = StartupDelay;

            DateTime nextAllowed = lastAttempt == DateTime.MinValue
                ? DateTime.UtcNow
                : lastAttempt + (backoff == TimeSpan.Zero ? NormalInterval : backoff);

            TimeSpan untilAllowed = nextAllowed - DateTime.UtcNow;
            if (untilAllowed < TimeSpan.Zero) {
                untilAllowed = TimeSpan.Zero;
            }

            return untilAllowed > minimumDelay ? untilAllowed : minimumDelay;
        }

        private void ScheduleNextCheck(TimeSpan delay) {
            if (disposed) return;
            if (delay < TimeSpan.Zero) {
                delay = TimeSpan.Zero;
            }

            checkTimer?.Dispose();
            checkTimer = new System.Threading.Timer(_ => _ = RunCheckAsync(force: false), null, delay, Timeout.InfiniteTimeSpan);
        }

        private async Task RunCheckAsync(bool force) {
            if (disposed) return;
            if (!force && !IsCheckAllowedNow()) {
                ScheduleNextCheck(CalculateInitialDelay());
                return;
            }

            bool lockTaken = await operationLock.WaitAsync(0).ConfigureAwait(false);
            if (!lockTaken) {
                return;
            }

            try {
                lastRemoteVersionDisplay = null;
                lastLocalVersionDisplay = null;
                lastTargetInfo = null;
                UpdateChannel configuredChannel = ConsumeForcedChannel() ?? GetConfiguredChannel();
                lastConfiguredChannel = configuredChannel;
                lastResolvedChannel = configuredChannel;
                if (force || state != UpdateCheckState.Failed)
                    UpdateState(UpdateCheckState.Checking, isEnabled: false);
                UpdateLastAttempt(DateTime.UtcNow);

                bool success = false;
                bool updateAvailable = false;

                for (int attempt = 1; attempt <= 3; attempt++) {
                    try {
                        using (var cts = new CancellationTokenSource(HttpTimeout)) {
                            string xml = await xmlClient.DownloadUpdateCheckXmlAsync(cts.Token).ConfigureAwait(false);
                            UpdateInfo release = xmlParser.ParseRelease(xml);
                            UpdateInfo beta = null;
                            UpdateInfo alpha = null;
                            try {
                                if (configuredChannel >= UpdateChannel.Beta) {
                                    beta = xmlParser.ParseBeta(xml);
                                }

                                if (configuredChannel >= UpdateChannel.Alpha) {
                                    alpha = xmlParser.ParseAlpha(xml);
                                }
                            } catch (Exception ex) {
                                Debug.WriteLine(ex);
                            }

                            UpdateInfo target = SelectTarget(release, beta, alpha, configuredChannel);
                            lastResolvedChannel = target.Channel;
                            lastTargetInfo = target;
                            lastRemoteVersionDisplay = target?.VersionString;

                            Debug.WriteLine($"Target update: {target?.Channel} {target?.VersionString} {target?.DownloadUri}");

                            IReadOnlyList<string> localBuilds = localBuildProvider?.Invoke() ?? Array.Empty<string>();
                            var localVersions = ParseLocalVersions(localBuilds);

                            if (simulateUpdateAvailable) {
                                updateAvailable = true;
                            } else if (localVersions.Count == 0) {
                                updateAvailable = true;
                            } else {
                                LocalVersionInfo matchingLocalVersion = FindMatchingLocalVersion(localVersions, target.Version);
                                bool upToDate = matchingLocalVersion != null;
                                lastLocalVersionDisplay = matchingLocalVersion?.Display;
                                updateAvailable = !upToDate;
                            }

                            success = true;
                            break;
                        }
                    } catch (Exception ex) {
                        UpdateLog.Write("Feed check failed: " + ex.GetType().Name + ": " + ex.Message);
                        Debug.WriteLine(ex);
                        if (attempt == 1) {
                            await Task.Delay(RetryDelayShort).ConfigureAwait(false);
                        } else if (attempt == 2) {
                            await Task.Delay(RetryDelayLong).ConfigureAwait(false);
                        }
                    }
                }

                if (success) {
                    ResetBackoff();
                    if (force || state != UpdateCheckState.Failed)
                        UpdateState(updateAvailable ? UpdateCheckState.UpdateAvailable : UpdateCheckState.UpToDate, isEnabled: true);
                    ScheduleNextCheck(NormalInterval);
                } else {
                    IncreaseBackoff();
                    UpdateState(UpdateCheckState.Failed, isEnabled: true);
                    ScheduleNextCheck(GetBackoffDelay());
                }
            } finally {
                operationLock.Release();
                if (HasForcedChannel()) {
                    _ = RunCheckAsync(force: true);
                }
            }
        }

        private void SetForcedChannel(UpdateChannel channel) {
            lock (forcedChannelLock) {
                forcedChannel = channel;
            }
        }

        private UpdateChannel? ConsumeForcedChannel() {
            lock (forcedChannelLock) {
                UpdateChannel? channel = forcedChannel;
                forcedChannel = null;
                return channel;
            }
        }

        private bool HasForcedChannel() {
            lock (forcedChannelLock) {
                return forcedChannel.HasValue;
            }
        }

        private static UpdateInfo SelectTarget(UpdateInfo release, UpdateInfo beta, UpdateInfo alpha, UpdateChannel configuredChannel) {
            if (release == null) {
                throw new InvalidOperationException("Release entry missing in updatecheck.xml.");
            }

            if (configuredChannel == UpdateChannel.Alpha) {
                if (alpha == null) {
                    throw new InvalidOperationException("Alpha entry missing in updatecheck.xml.");
                }

                return alpha;
            }

            if (configuredChannel == UpdateChannel.Beta) {
                if (beta == null) {
                    throw new InvalidOperationException("Beta entry missing in updatecheck.xml.");
                }

                return beta;
            }

            return release;
        }

        private async Task DownloadAndInstallAsync() {
            if (!await operationLock.WaitAsync(0).ConfigureAwait(false)) {
                return;
            }

            string downloadFolder = null;
            UpdateWorkingFolder workingFolder = null;
            bool installerStarted = false;
            bool installerExited = false;
            try {
                userCancelledDownload = false;
                UpdateState(UpdateCheckState.Downloading, isEnabled: true);
                UpdateInfo target = lastTargetInfo;
                if (target == null || target.DownloadUri == null) {
                    UpdateState(UpdateCheckState.Failed, isEnabled: true);
                    return;
                }

                string tempRoot = GetTempRoot();
                workingFolder = new UpdateWorkingFolder(tempRoot);
                downloadFolder = workingFolder.Path;

                Process installerProcess;
                using (var downloadCts = new CancellationTokenSource(InstallerDownloadTimeout)) {
                    lock (downloadGate) activeDownload = downloadCts;
                    try {
                        installerProcess = await installerWorkflow.ExecuteAsync(target, downloadFolder, downloadCts.Token).ConfigureAwait(false);
                        if (installerProcess == null) throw new InvalidOperationException("Installer process did not start.");
                        installerStarted = true;
                        downloadCts.CancelAfter(Timeout.InfiniteTimeSpan);
                        UpdateState(UpdateCheckState.Installing, isEnabled: false);
                        using (installerProcess) {
                            workingFolder.TrackInstaller(installerProcess);
                            await InstallerProcessMonitor.WaitAsync(
                                token => installerProcess.WaitForExitAsync(token),
                                () => installerProcess.CloseMainWindow(),
                                requested => {
                                    UpdateLog.Write("Installer cancellation requested; close message sent: " + requested);
                                    UpdateState(requested ? UpdateCheckState.CancellingInstaller : UpdateCheckState.Installing, isEnabled: false);
                                }, downloadCts.Token).ConfigureAwait(false);
                            installerExited = true;
                            int exitCode = installerProcess.ExitCode;
                            UpdateLog.Write("Installer exit code: " + exitCode);
                            if (exitCode == 0) {
                                UpdateState(UpdateCheckState.Installed, isEnabled: true);
                                string completedRoot = installationRoot;
                                InvokeOnUi(() => InstallationCompleted?.Invoke(completedRoot));
                            } else if (exitCode == 2 || exitCode == 1602 || (exitCode == 5 && userCancelledDownload)) {
                                UpdateState(UpdateCheckState.InstallationCancelled, isEnabled: true);
                            } else {
                                throw new InvalidOperationException("Installer failed with exit code " + exitCode);
                            }
                        }
                    } finally {
                        lock (downloadGate) activeDownload = null;
                    }
                }

            } catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
                UpdateLog.Write("Administrator elevation was cancelled; installation did not start.");
                UpdateState(UpdateCheckState.InstallationCancelled, isEnabled: true);
            } catch (UpdateSecurityException ex) {
                UpdateLog.Write("Validation failed: " + ex.Failure);
                Debug.WriteLine($"Update security validation failed: {ex.Failure}");
                UpdateState(UpdateCheckState.Failed, isEnabled: true);
            } catch (OperationCanceledException) {
                UpdateLog.Write(userCancelledDownload ? "Download cancelled." : "Download timed out or application is closing.");
                UpdateState(userCancelledDownload ? UpdateCheckState.Cancelled : UpdateCheckState.Failed, isEnabled: true);
            } catch (Exception ex) {
                UpdateLog.Write("Update failed: " + ex.GetType().Name + ": " + ex.Message);
                Debug.WriteLine(ex);
                UpdateState(UpdateCheckState.Failed, isEnabled: true);
            } finally {
                if (!installerStarted || installerExited) workingFolder?.DeleteAfterExit();
                else workingFolder?.Dispose(); // Keep identity if monitoring failed while the installer is alive.
                operationLock.Release();
            }
        }

        private Process StartInstallerProcess(string installerPath) {
            string root = SettingsGuard.Execute(() => AppSettings.Default.SavedPaths, string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(root)) {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), InitialSetup.LoxoneFolderName);
            }
            bool versioned = SettingsGuard.Execute(() => AppSettings.Default.VersionedInstallationFolders, false);
            string destination = InstallationDestination.GetPath(root, lastTargetInfo, versioned);
            installationRoot = Path.GetDirectoryName(destination);
            UpdateLog.Write($"Installation target: {destination}; versioned folders: {versioned}; channel: {lastTargetInfo.Channel}");
            Directory.CreateDirectory(UpdateLog.Folder);
            string logPath = Path.Combine(UpdateLog.Folder, "installer-" + Guid.NewGuid().ToString("N") + ".log");
            UpdateLog.Write("Silent installer log: " + logPath);
            var startInfo = InstallationDestination.CreateStartInfo(installerPath, destination, logPath);
            return Process.Start(startInfo);
        }

        private void UpdateState(UpdateCheckState newState, bool isEnabled) {
            if (disposed) return;
            UpdateLog.Write("State: " + newState);
            state = newState;
            string versionLabel = null;
            if (newState == UpdateCheckState.UpdateAvailable) {
                versionLabel = lastRemoteVersionDisplay;
            } else if (newState == UpdateCheckState.UpToDate) {
                versionLabel = lastLocalVersionDisplay ?? lastRemoteVersionDisplay;
            }

            UpdateChannel displayChannel = newState == UpdateCheckState.UpdateAvailable
                ? lastResolvedChannel
                : lastConfiguredChannel;
            InvokeOnUi(() => ContextMenuManager.UpdateUpdateCheckMenuItem(newState, isEnabled, versionLabel, displayChannel));
        }

        private static UpdateChannel GetConfiguredChannel() {
            int storedValue = RegistryFlagReader.GetDwordValue(UpdateChannelValueName, defaultValue: int.MinValue);
            if (Enum.IsDefined(typeof(UpdateChannel), storedValue)) {
                return (UpdateChannel)storedValue;
            }

            bool includeAlpha = RegistryFlagReader.GetDwordFlag(IncludeAlphaValueName, defaultValue: false);
            return includeAlpha ? UpdateChannel.Alpha : UpdateChannel.Release;
        }

        public void NotifyLocalVersionsChanged(IReadOnlyList<string> localBuilds) {
            if (state == UpdateCheckState.Downloading || state == UpdateCheckState.Verifying || state == UpdateCheckState.Extracting || state == UpdateCheckState.Installing || state == UpdateCheckState.CancellingInstaller || state == UpdateCheckState.Failed) return;
            if (simulateUpdateAvailable) {
                return;
            }

            if (lastTargetInfo == null) {
                _ = RunCheckAsync(force: true);
                return;
            }

            var localVersions = ParseLocalVersions(localBuilds ?? Array.Empty<string>());
            if (localVersions.Count == 0) {
                UpdateState(UpdateCheckState.UpdateAvailable, isEnabled: true);
                return;
            }

            LocalVersionInfo matchingLocalVersion = FindMatchingLocalVersion(localVersions, lastTargetInfo.Version);
            lastLocalVersionDisplay = matchingLocalVersion?.Display;
            bool match = matchingLocalVersion != null;
            UpdateState(match ? UpdateCheckState.UpToDate : UpdateCheckState.UpdateAvailable, isEnabled: true);
        }

        internal static string GetTempRoot() {
            return UpdateLog.Folder;
        }

        private static void CleanupTempRoot(TimeSpan minAge) {
            UpdateWorkingFolder.Cleanup(GetTempRoot(), minAge);
        }

        private void CreateInstallerShortcut(string installerPath) {
            try {
                if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath)) {
                    return;
                }

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(userProfile)) {
                    return;
                }

                string downloadsFolder = Path.Combine(userProfile, "Downloads");
                if (!Directory.Exists(downloadsFolder)) {
                    return;
                }

                string versionPart = string.IsNullOrWhiteSpace(lastRemoteVersionDisplay) ? "Latest" : SanitizeFileNamePart(lastRemoteVersionDisplay);
                string fileName = $"LoxoneConfigInstaller_{versionPart}.url";
                string shortcutPath = Path.Combine(downloadsFolder, fileName);
                // The stable diagnostics folder survives individual installer cleanup.
                string fileUrl = new Uri(GetTempRoot() + Path.DirectorySeparatorChar).AbsoluteUri;

                string contents = "[InternetShortcut]" + Environment.NewLine
                    + "URL=" + fileUrl + Environment.NewLine;

                File.WriteAllText(shortcutPath, contents);
            } catch {
            }
        }

        private static string SanitizeFileNamePart(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return "Latest";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Latest" : sanitized;
        }

        private void InvokeOnUi(Action action) {
            if (action == null) {
                return;
            }

            if (uiInvoker != null && uiInvoker.InvokeRequired) {
                uiInvoker.BeginInvoke(action, null);
            } else {
                action();
            }
        }

        private static TimeSpan GetBackoffDelay(int level) {
            if (level <= 0) {
                return TimeSpan.Zero;
            }

            int index = Math.Min(level, BackoffSchedule.Length) - 1;
            return BackoffSchedule[index];
        }

        private TimeSpan GetBackoffDelay() {
            int level = SettingsGuard.Execute(() => AppSettings.Default.UpdateBackoffLevel, 0);
            return GetBackoffDelay(level);
        }

        private bool IsCheckAllowedNow() {
            DateTime lastAttempt = SettingsGuard.Execute(
                () => AppSettings.Default.UpdateLastCheckUtc,
                DateTime.MinValue);
            TimeSpan backoff = GetBackoffDelay();

            if (lastAttempt == DateTime.MinValue) {
                return true;
            }

            TimeSpan interval = backoff == TimeSpan.Zero ? NormalInterval : backoff;
            return DateTime.UtcNow >= lastAttempt + interval;
        }

        private void UpdateLastAttempt(DateTime utcNow) {
            SettingsGuard.Execute(() => {
                AppSettings.Default.UpdateLastCheckUtc = utcNow;
                AppSettings.Default.Save();
            });
        }

        private void ResetBackoff() {
            SettingsGuard.Execute(() => {
                AppSettings.Default.UpdateBackoffLevel = 0;
                AppSettings.Default.Save();
            });
        }

        private void IncreaseBackoff() {
            SettingsGuard.Execute(() => {
                int level = AppSettings.Default.UpdateBackoffLevel;
                if (level < BackoffSchedule.Length) {
                    level++;
                }
                AppSettings.Default.UpdateBackoffLevel = level;
                AppSettings.Default.Save();
            });
        }

        private static List<LocalVersionInfo> ParseLocalVersions(IEnumerable<string> buildIds) {
            var results = new List<LocalVersionInfo>();
            if (buildIds == null) {
                return results;
            }

            foreach (string buildId in buildIds) {
                if (string.IsNullOrWhiteSpace(buildId)) {
                    continue;
                }

                if (!TryParseLocalVersionToken(buildId, out UpdateVersion version, out string display)) {
                    continue;
                }

                results.Add(new LocalVersionInfo(version, display, buildId));
            }

            return results;
        }

        private static bool TryParseLocalVersionToken(string token, out UpdateVersion version, out string display) {
            version = default;
            display = null;
            if (string.IsNullOrWhiteSpace(token)) {
                return false;
            }

            string value = token.Trim();
            if (UpdateVersion.TryParseBuildId(value, out version)) {
                display = FormatBuildId(value) ?? version.ToString();
                return true;
            }

            Match match = Regex.Match(value, @"\d+(\.\d+){1,3}");
            if (!match.Success) {
                return false;
            }

            string[] parts = match.Value.Split('.');
            if (parts.Length > 4) {
                return false;
            }

            var numbers = new int[4];
            for (int i = 0; i < numbers.Length; i++) {
                if (i >= parts.Length) {
                    numbers[i] = 0;
                    continue;
                }

                if (!int.TryParse(parts[i], out int number) || number < 0) {
                    return false;
                }

                numbers[i] = number;
            }

            version = new UpdateVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
            display = version.ToString();
            return true;
        }

        private static LocalVersionInfo FindMatchingLocalVersion(List<LocalVersionInfo> versions, UpdateVersion targetVersion) {
            if (versions == null || versions.Count == 0) {
                return null;
            }

            return versions.FirstOrDefault(v => v.Version.Equals(targetVersion));
        }

        private static string FormatBuildId(string buildId) {
            if (string.IsNullOrWhiteSpace(buildId) || buildId.Length != 8) {
                return null;
            }

            return string.Join(".",
                buildId.Substring(0, 2),
                buildId.Substring(2, 2),
                buildId.Substring(4, 2),
                buildId.Substring(6, 2));
        }

        public void Dispose() {
            disposed = true;
            lock (downloadGate) activeDownload?.Cancel();
            checkTimer?.Dispose();
            httpClient?.Dispose();
            // In-flight operations still release the semaphore in their finally blocks.
        }

        private sealed class LocalVersionInfo {
            public UpdateVersion Version { get; }
            public string Display { get; }
            public string BuildId { get; }

            public LocalVersionInfo(UpdateVersion version, string display, string buildId) {
                Version = version;
                Display = display;
                BuildId = buildId;
            }
        }
    }
}
