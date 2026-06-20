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
        UpdateAvailable,
        UpToDate,
        Failed
    }

    internal sealed class UpdateCheckService : IDisposable {
        private const string UpdateChannelValueName = "UpdateChannel";
        private const string IncludeAlphaValueName = "IncludeAlpha";

        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan NormalInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);
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
            if (simulateUpdateAvailable) {
                UpdateState(UpdateCheckState.UpdateAvailable, isEnabled: true);
                return;
            }
            if (state == UpdateCheckState.UpdateAvailable) {
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
            if (delay < TimeSpan.Zero) {
                delay = TimeSpan.Zero;
            }

            checkTimer?.Dispose();
            checkTimer = new System.Threading.Timer(_ => _ = RunCheckAsync(force: false), null, delay, Timeout.InfiniteTimeSpan);
        }

        private async Task RunCheckAsync(bool force) {
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
            try {
                UpdateState(UpdateCheckState.Downloading, isEnabled: false);
                UpdateInfo target = lastTargetInfo;
                if (target == null || target.DownloadUri == null) {
                    UpdateState(UpdateCheckState.Failed, isEnabled: true);
                    return;
                }

                string tempRoot = GetTempRoot();
                downloadFolder = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(downloadFolder);

                Process installerProcess;
                using (var downloadCts = new CancellationTokenSource(InstallerDownloadTimeout)) {
                    installerProcess = await installerWorkflow.ExecuteAsync(target, downloadFolder, downloadCts.Token).ConfigureAwait(false);
                }

                UpdateState(UpdateCheckState.UpdateAvailable, isEnabled: true);
                if (installerProcess != null) {
                    _ = Task.Run(() => {
                        try {
                            installerProcess.WaitForExit();
                            IReadOnlyList<string> localBuilds = localBuildProvider?.Invoke() ?? Array.Empty<string>();
                            NotifyLocalVersionsChanged(localBuilds);
                        } catch (Exception ex) {
                            Debug.WriteLine(ex);
                        } finally {
                            installerProcess.Dispose();
                        }
                    });
                }

                _ = Task.Run(async () => {
                    await Task.Delay(CleanupAge).ConfigureAwait(false);
                    CleanupTempRoot(CleanupAge);
                });
            } catch (UpdateSecurityException ex) {
                Debug.WriteLine($"Update security validation failed: {ex.Failure}");
                TryDeleteDirectory(downloadFolder);
                UpdateState(UpdateCheckState.Failed, isEnabled: true);
            } catch (Exception ex) {
                Debug.WriteLine(ex);
                TryDeleteDirectory(downloadFolder);
                UpdateState(UpdateCheckState.Failed, isEnabled: true);
            } finally {
                operationLock.Release();
            }
        }

        private static Process StartInstallerProcess(string installerPath) {
            var startInfo = new ProcessStartInfo {
                FileName = installerPath,
                UseShellExecute = true
            };
            return Process.Start(startInfo);
        }

        private static void TryDeleteDirectory(string path) {
            try {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) {
                    Directory.Delete(path, recursive: true);
                }
            } catch {
            }
        }

        private void UpdateState(UpdateCheckState newState, bool isEnabled) {
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
            return Path.Combine(Path.GetTempPath(), "LoxTools", "LoxoneConfigUpdate");
        }

        private static void CleanupTempRoot(TimeSpan minAge) {
            try {
                string tempRoot = GetTempRoot();
                if (!Directory.Exists(tempRoot)) {
                    return;
                }

                foreach (string dir in Directory.GetDirectories(tempRoot)) {
                    try {
                        var info = new DirectoryInfo(dir);
                        if (minAge > TimeSpan.Zero) {
                            TimeSpan age = DateTime.UtcNow - info.LastWriteTimeUtc;
                            if (age < minAge) {
                                continue;
                            }
                        }

                        info.Delete(true);
                    } catch {
                    }
                }
            } catch {
            }
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
                string fileUrl = new Uri(installerPath).AbsoluteUri;

                string contents = "[InternetShortcut]" + Environment.NewLine
                    + "URL=" + fileUrl + Environment.NewLine
                    + "IconFile=" + installerPath + Environment.NewLine
                    + "IconIndex=0" + Environment.NewLine;

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
            checkTimer?.Dispose();
            httpClient?.Dispose();
            operationLock?.Dispose();
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
