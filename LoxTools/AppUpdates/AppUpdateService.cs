using LoxTools.Core.Settings;
using AppSettings = LoxTools.Properties.Settings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.AppUpdates {
    internal enum AppUpdateState {
        Idle,
        Checking,
        UpToDate,
        UpdateAvailable,
        Downloading,
        Verifying,
        StartingInstaller,
        Failed
    }

    internal sealed class AppUpdateSnapshot : EventArgs {
        public AppUpdateState State { get; }
        public AppReleaseInfo Release { get; }
        public string CurrentVersion { get; }
        public string ErrorMessage { get; }
        public DateTime LastCheckUtc { get; }
        public bool CanInstallAutomatically { get; }

        public AppUpdateSnapshot(AppUpdateState state, AppReleaseInfo release, string currentVersion, string errorMessage, DateTime lastCheckUtc, bool canInstallAutomatically) {
            State = state;
            Release = release;
            CurrentVersion = currentVersion;
            ErrorMessage = errorMessage;
            LastCheckUtc = lastCheckUtc;
            CanInstallAutomatically = canInstallAutomatically;
        }
    }

    internal sealed class AppUpdateService : IDisposable {
        private static readonly TimeSpan NormalInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan CleanupAge = TimeSpan.FromHours(24);
        private static readonly TimeSpan[] BackoffSchedule = { TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6) };
        private const long MaxInstallerBytes = 250L * 1024L * 1024L;
        private const long MaxChecksumBytes = 1024L * 1024L;

        private readonly ISynchronizeInvoke uiInvoker;
        private readonly Action closeApplicationAction;
        private readonly HttpClient httpClient;
        private readonly IAppReleaseSource releaseSource;
        private readonly IAppInstallerVerifier installerVerifier;
        private readonly SemaphoreSlim operationLock = new SemaphoreSlim(1, 1);
        private readonly AppSemanticVersion currentVersion;
        private readonly string currentVersionDisplay;
        private readonly bool isDevelopmentBuild;
        private readonly bool isInstalledCopy;
        private System.Threading.Timer timer;
        private AppReleaseInfo availableRelease;
        private AppUpdateSnapshot snapshot;
        private bool disposed;

        public event EventHandler<AppUpdateSnapshot> StateChanged;
        public event EventHandler<AppUpdateSnapshot> UpdateNotificationRequested;

        public AppUpdateSnapshot CurrentSnapshot => snapshot;
        public bool AutomaticChecksEnabled => ReadSetting(() => AppSettings.Default.AppUpdateAutoCheckEnabled, true);
        public AppUpdateChannel Channel => NormalizeChannel(ReadSetting(() => AppSettings.Default.AppUpdateChannel, 0));

        public AppUpdateService(ISynchronizeInvoke uiInvoker, Action closeApplicationAction)
            : this(uiInvoker, closeApplicationAction, CreateHttpClient(), null, CreateInstallerVerifier(), AppInstallationDetector.IsInstalledCopy()) { }

        internal AppUpdateService(
            ISynchronizeInvoke uiInvoker,
            Action closeApplicationAction,
            HttpClient httpClient,
            IAppReleaseSource releaseSource,
            IAppInstallerVerifier installerVerifier,
            bool isInstalledCopy) {
            this.uiInvoker = uiInvoker;
            this.closeApplicationAction = closeApplicationAction;
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.releaseSource = releaseSource ?? new GitHubReleaseClient(httpClient);
            this.installerVerifier = installerVerifier ?? throw new ArgumentNullException(nameof(installerVerifier));
            this.isInstalledCopy = isInstalledCopy;

            string informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            currentVersionDisplay = string.IsNullOrWhiteSpace(informational) ? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0" : informational.Split('+')[0];
            isDevelopmentBuild = currentVersionDisplay.IndexOf("-dev.", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!AppSemanticVersion.TryParse(currentVersionDisplay, out AppSemanticVersion parsedCurrentVersion)) {
                Version assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                parsedCurrentVersion = new AppSemanticVersion(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(0, assemblyVersion.Build), AppReleaseStage.Stable, 0);
            }
            currentVersion = parsedCurrentVersion;

            snapshot = CreateSnapshot(AppUpdateState.Idle, null, null);
        }

        public void Start() {
            CleanupTempRoot();
            Publish(AppUpdateState.Idle, null, null);
            if (AutomaticChecksEnabled && !isDevelopmentBuild) {
                Schedule(TimeSpan.Zero);
            }
        }

        public void ApplySettings(bool automaticChecksEnabled, AppUpdateChannel channel) {
            WriteSettings(() => {
                AppSettings.Default.AppUpdateAutoCheckEnabled = automaticChecksEnabled;
                AppSettings.Default.AppUpdateChannel = (int)channel;
                AppSettings.Default.Save();
            });

            timer?.Dispose();
            timer = null;
            if (automaticChecksEnabled && !isDevelopmentBuild) {
                CheckNow();
            }
        }

        public void CheckNow() => _ = CheckAsync(manual: true);

        public void CheckIfStale() {
            if (disposed || !AutomaticChecksEnabled || isDevelopmentBuild) return;

            DateTime lastCheck = ReadSetting(() => AppSettings.Default.AppUpdateLastCheckUtc, DateTime.MinValue);
            if (lastCheck == DateTime.MinValue || DateTime.UtcNow - lastCheck >= NormalInterval) {
                _ = CheckAsync(manual: false);
            }
        }

        public void HandlePrimaryAction() {
            if (snapshot.State != AppUpdateState.UpdateAvailable || availableRelease == null) {
                CheckNow();
                return;
            }

            _ = DownloadAndLaunchAsync(availableRelease);
        }

        internal async Task CheckAsync(bool manual) {
            if (disposed || (!manual && (!AutomaticChecksEnabled || isDevelopmentBuild))) return;
            if (!await operationLock.WaitAsync(0).ConfigureAwait(false)) return;

            try {
                Publish(AppUpdateState.Checking, availableRelease, null);
                using (var cancellation = new CancellationTokenSource(HttpTimeout)) {
                    IReadOnlyList<AppReleaseInfo> releases = await releaseSource.GetReleasesAsync(cancellation.Token).ConfigureAwait(false);
                    AppReleaseInfo target = SelectRelease(releases, currentVersion, Channel);
                    availableRelease = target;
                    DateTime now = DateTime.UtcNow;
                    WriteSettings(() => {
                        AppSettings.Default.AppUpdateLastCheckUtc = now;
                        AppSettings.Default.AppUpdateBackoffLevel = 0;
                        AppSettings.Default.Save();
                    });

                    if (target == null) {
                        Publish(AppUpdateState.UpToDate, null, null);
                    } else {
                        Publish(AppUpdateState.UpdateAvailable, target, null);
                        RequestNotificationOnce(target);
                    }
                    Schedule(NormalInterval);
                }
            } catch (Exception ex) {
                Debug.WriteLine(ex);
                int level = Math.Min(BackoffSchedule.Length, ReadSetting(() => AppSettings.Default.AppUpdateBackoffLevel, 0) + 1);
                WriteSettings(() => {
                    AppSettings.Default.AppUpdateLastCheckUtc = DateTime.UtcNow;
                    AppSettings.Default.AppUpdateBackoffLevel = level;
                    AppSettings.Default.Save();
                });
                Publish(AppUpdateState.Failed, availableRelease, manual ? ex.Message : null);
                Schedule(BackoffSchedule[Math.Max(0, level - 1)]);
            } finally {
                operationLock.Release();
            }
        }

        internal static AppReleaseInfo SelectRelease(IEnumerable<AppReleaseInfo> releases, AppSemanticVersion installedVersion, AppUpdateChannel channel) {
            return (releases ?? Enumerable.Empty<AppReleaseInfo>())
                .Where(release => release != null && release.Version.IsEligibleFor(channel) && release.Version.CompareTo(installedVersion) > 0)
                .OrderByDescending(release => release.Version)
                .FirstOrDefault();
        }

        private async Task DownloadAndLaunchAsync(AppReleaseInfo release) {
            if (!await operationLock.WaitAsync(0).ConfigureAwait(false)) return;
            string downloadDirectory = null;
            try {
                string installerName = $"LoxTools-Setup-{release.Version}.exe";
                AppReleaseAsset installerAsset = release.FindAsset(installerName);
                AppReleaseAsset checksumAsset = release.FindAsset("SHA256SUMS.txt");
                if (!IsValidAsset(installerAsset, MaxInstallerBytes) || !IsValidAsset(checksumAsset, MaxChecksumBytes)) {
                    throw new InvalidDataException("Required release assets are missing or invalid.");
                }

                downloadDirectory = Path.Combine(GetTempRoot(), release.Version.ToString());
                if (Directory.Exists(downloadDirectory)) Directory.Delete(downloadDirectory, true);
                Directory.CreateDirectory(downloadDirectory);
                string installerPath = Path.Combine(downloadDirectory, installerName);
                string checksumPath = Path.Combine(downloadDirectory, checksumAsset.Name);

                Publish(AppUpdateState.Downloading, release, null);
                using (var cancellation = new CancellationTokenSource(InstallerTimeout)) {
                    await DownloadFileAsync(checksumAsset, checksumPath, MaxChecksumBytes, cancellation.Token).ConfigureAwait(false);
                    await DownloadFileAsync(installerAsset, installerPath, MaxInstallerBytes, cancellation.Token).ConfigureAwait(false);
                }

                Publish(AppUpdateState.Verifying, release, null);
                if (!installerVerifier.Verify(installerPath, checksumPath, installerName)) {
                    throw new InvalidDataException("The installer signature or checksum is invalid.");
                }

                Publish(AppUpdateState.StartingInstaller, release, null);
                Process process = Process.Start(CreateInstallerStartInfo(installerPath));
                if (process == null) throw new InvalidOperationException("The installer could not be started.");
                process.Dispose();
                InvokeOnUi(closeApplicationAction);
            } catch (Exception ex) {
                Debug.WriteLine(ex);
                TryDeleteDirectory(downloadDirectory);
                Publish(AppUpdateState.Failed, release, ex.Message);
            } finally {
                operationLock.Release();
            }
        }

        private async Task DownloadFileAsync(AppReleaseAsset asset, string targetPath, long maximumBytes, CancellationToken cancellationToken) {
            if (!IsGitHubDownloadUri(asset.DownloadUri)) throw new InvalidDataException("Unexpected download host.");
            using (HttpResponseMessage response = await httpClient.GetAsync(asset.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)) {
                response.EnsureSuccessStatusCode();
                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && (contentLength.Value != asset.Size || contentLength.Value > maximumBytes)) throw new InvalidDataException("Unexpected download size.");
                using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) {
                    var buffer = new byte[81920];
                    long total = 0;
                    while (true) {
                        int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        total += read;
                        if (total > maximumBytes || total > asset.Size) throw new InvalidDataException("Download exceeded its declared size.");
                        await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }
                    if (total != asset.Size) throw new InvalidDataException("Download size did not match release metadata.");
                }
            }
        }

        private void RequestNotificationOnce(AppReleaseInfo release) {
            string notified = ReadSetting(() => AppSettings.Default.AppUpdateLastNotifiedVersion, string.Empty);
            string version = release.Version.ToString();
            if (!ShouldNotify(notified, version)) return;
            WriteSettings(() => {
                AppSettings.Default.AppUpdateLastNotifiedVersion = version;
                AppSettings.Default.Save();
            });
            InvokeOnUi(() => UpdateNotificationRequested?.Invoke(this, snapshot));
        }

        private bool CanInstallAutomatically() => isInstalledCopy && installerVerifier.IsExecutionConfigured;
        internal static ProcessStartInfo CreateInstallerStartInfo(string installerPath, int updaterProcessId = 0) {
            if (updaterProcessId <= 0) updaterProcessId = Process.GetCurrentProcess().Id;

            return new ProcessStartInfo {
                FileName = installerPath,
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LoxToolsPid={updaterProcessId}",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }
        internal static bool ShouldNotify(string lastNotifiedVersion, string availableVersion) => !string.IsNullOrWhiteSpace(availableVersion)
            && !string.Equals(lastNotifiedVersion, availableVersion, StringComparison.Ordinal);
        internal static bool IsValidAsset(AppReleaseAsset asset, long maximumBytes) => asset != null && asset.DownloadUri != null && asset.Size > 0 && asset.Size <= maximumBytes;
        internal static bool IsGitHubDownloadUri(Uri uri) => uri != null && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);

        private void Publish(AppUpdateState state, AppReleaseInfo release, string error) {
            snapshot = CreateSnapshot(state, release, error);
            InvokeOnUi(() => StateChanged?.Invoke(this, snapshot));
        }

        private AppUpdateSnapshot CreateSnapshot(AppUpdateState state, AppReleaseInfo release, string error) {
            DateTime lastCheck = ReadSetting(() => AppSettings.Default.AppUpdateLastCheckUtc, DateTime.MinValue);
            return new AppUpdateSnapshot(state, release, currentVersionDisplay, error, lastCheck, CanInstallAutomatically());
        }

        private void Schedule(TimeSpan delay) {
            if (disposed || !AutomaticChecksEnabled || isDevelopmentBuild) return;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            timer?.Dispose();
            timer = new System.Threading.Timer(_ => _ = CheckAsync(manual: false), null, delay, Timeout.InfiniteTimeSpan);
        }

        internal static string GetTempRoot() => Path.Combine(Path.GetTempPath(), "LoxTools", "AppUpdate");

        private static void CleanupTempRoot() {
            try {
                string root = GetTempRoot();
                if (!Directory.Exists(root)) return;
                foreach (string directory in Directory.GetDirectories(root)) {
                    var info = new DirectoryInfo(directory);
                    if (DateTime.UtcNow - info.LastWriteTimeUtc >= CleanupAge) TryDeleteDirectory(directory);
                }
            } catch { }
        }

        private static void TryDeleteDirectory(string path) {
            try { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        private void InvokeOnUi(Action action) {
            if (action == null) return;
            try {
                if (uiInvoker != null && uiInvoker.InvokeRequired) uiInvoker.BeginInvoke(action, Array.Empty<object>());
                else action();
            } catch (ObjectDisposedException) { }
        }

        private static T ReadSetting<T>(Func<T> reader, T fallback) => SettingsGuard.Execute(reader, fallback);
        private static void WriteSettings(Action writer) => SettingsGuard.Execute(writer);
        private static AppUpdateChannel NormalizeChannel(int value) => Enum.IsDefined(typeof(AppUpdateChannel), value) ? (AppUpdateChannel)value : AppUpdateChannel.Stable;

        private static HttpClient CreateHttpClient() => new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        private static IAppInstallerVerifier CreateInstallerVerifier() {
#if DEBUG
            if (Debugger.IsAttached) return new DebugAppInstallerVerifier();
#endif
            return new AppInstallerVerifier();
        }

        public void Dispose() {
            disposed = true;
            timer?.Dispose();
            httpClient.Dispose();
        }
    }
}
