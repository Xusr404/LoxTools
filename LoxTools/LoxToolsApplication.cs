using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LoxTools;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Text;
using System.Windows.Interop;
using LoxTools.Language;

namespace LoxTools {
    using SingleInstanceCore;
    using System.Globalization;
    using System.Reflection;
    using System.Security.Principal;
    using System.Windows.Media.Animation;
    using Core.Settings;
    using Core.Startup;
    using Shared;
    using UI.Helpers;
    using UI.Tray;
    using UpdateCheck;
    using AppUpdates;
    class LoxToolsApplication : ApplicationContext, ISingleInstance {
		private static LoxToolsApplication _instance;
		#region Public Obejcts needed for the App
		//Component declarations

		public NotifyIcon TrayIcon;
        public ContextMenuStrip TrayIconContextMenu;

        FileSystemWatcher[] Watchers;

        Dictionary<string, string> ProjectFiles = new Dictionary<string, string>() { };
        public List<string> ConfigFolderPaths;
        public List<string> ProjectsFolderPaths;

        bool useLatestVersion = false;

        #region pipe

        VersionSelectorWindow mainForm;
        SettingsWindow settingsWindow;
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly Control uiInvokeControl;

        private Task ServerTask;
        #endregion
        private readonly object refreshLock = new object();
        private System.Threading.Timer refreshDebounceTimer;
        private CancellationTokenSource refreshCts;
        private readonly SemaphoreSlim refreshSemaphore = new SemaphoreSlim(1, 1);
        private const int RefreshDebounceMs = 1000;
        private UpdateCheckService updateCheckService;
        private AppUpdateService appUpdateService;
        private bool simulateUpdateAvailable;
        private const int FirstStartupScanBudgetMs = 300;
        private const int FirstStartupScanEntryLimit = 500;

        public static class WatcherFilter {
            public const string
                LoxoneConfig = "LoxoneConfig*",
                loxoneFile = "*.Loxone",
                lxoneBackupFile = "*.backup";

            public static List<string> getAllFileEndings() {
                List<string> results = new List<string>();

                FieldInfo[] fields = typeof(WatcherFilter).GetFields();

                foreach (var field in fields) {
                    if (field.FieldType == typeof(string)) {
                        string value = (string)field.GetValue(null);
                        if (value != null && value.StartsWith("*.")) {
                            results.Add(value.Remove(0,1));
                        }
                    }
                }

                return results;
            }
        }
#if DEBUG
        enum SettingsManipulation {
            setDefault,
            clearAllPaths,
            incorrectDefaultConfig,
            incorrectProjectFolderPath,
            incorrectFilePath,
            clearDefaultConfig,
            setAutostartup,
            unsetAutostartup,
            useLatestVersion,
            notUseLatestVersion,
            alwaysShowSelectionDialog,
            notalwaysShowSelectionDialog,
            setMiddleMouseButtonAction,
            unsetMiddleMouseButtonAction,
        }
        public static class SettingsManipulationStrings {
            public const string
                incorrectDefaultConfig = "C:";
        }
#endif

        private bool closeConfigWithMouseWheelClick = false;
        #endregion

        public LoxToolsApplication(string[] args, bool shiftPressed) {
			_instance = this;
            uiInvokeControl = new Control();
            uiInvokeControl.CreateControl();

            Application.ApplicationExit += new EventHandler(this.onApplicationExit);
            SettingsGuard.EnsureReadable();
            simulateUpdateAvailable =
                HasArgument(args, "simulate-update")
                || HasArgument(args, "simulateupdateavailable")
                || HasArgument(Environment.GetCommandLineArgs(), "simulate-update")
                || HasArgument(Environment.GetCommandLineArgs(), "simulateupdateavailable")
                || IsEnvironmentFlagSet("LOXTOOLS_SIMULATE_UPDATE")
                || IsEnvironmentFlagSet("CVS_SIMULATE_UPDATE")
                || IsEnvironmentFlagSet("CONFIGVERSIONSELECTOR_SIMULATE_UPDATE");

            EnsureSettingsUpgraded();
            bool isFirstStartup = InitialSetup.CheckRunInitialSetup();
            InitializeComponent(isFirstStartup);

            processArgs(args, shiftPressed);
		}

        private void InitializeComponent(bool isFirstStartup) {

            TrayIconContextMenu = new ContextMenuStrip();
            TrayIcon = setupNotifyIcon(TrayIcon, TrayIconContextMenu);
            TrayIcon.ContextMenuStrip = TrayIconContextMenu;

            ContextMenuManager.LoxToolsInstance = this;

            #region loadData
#if DEBUG
            //debugChangeSettings(SettingsManipulation.incorrectDefaultConfig);
#endif
            loadData();

            if (isFirstStartup) {
                TryRunBoundedFirstStartupScan();
            } else {
                findAllProjectFiles();
                findAllApplications();
            }
            Watchers = new FileSystemWatcher[ConfigFolderPaths.Count() + ProjectsFolderPaths.Count()];
            createPathWatchers(ConfigFolderPaths, WatcherFilter.LoxoneConfig);
            createFileWatchers(ProjectsFolderPaths, WatcherFilter.loxoneFile);
            setupContextMenu();
            InitializeUpdateCheckService();
            InitializeAppUpdateService();
            #endregion

            #region
            EnsureMainWindow();
            #endregion

            if (isFirstStartup) {
                ShowSettingsWindow(false);
            }
        }

        private void TryRunBoundedFirstStartupScan() {
            var stopwatch = Stopwatch.StartNew();
            var applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var projects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int entriesVisited = 0;

            try {
                foreach (string path in ConfigFolderPaths.Where(Directory.Exists)) {
                    foreach (string directory in Directory.EnumerateDirectories(path)) {
                        if (ScanBudgetExceeded(stopwatch, entriesVisited)) {
                            return;
                        }

                        entriesVisited++;
                        string versionName = Path.GetFileName(directory);
                        if (!string.IsNullOrWhiteSpace(versionName)) {
                            applications[versionName] = directory;
                        }
                    }
                }

                var enumerationOptions = new EnumerationOptions {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false
                };

                foreach (string path in ProjectsFolderPaths.Where(Directory.Exists)) {
                    foreach (string file in Directory.EnumerateFiles(path, WatcherFilter.loxoneFile, enumerationOptions)) {
                        if (ScanBudgetExceeded(stopwatch, entriesVisited)) {
                            ContextMenuManager.AllConfigVersions = applications
                                .OrderBy(entry => entry.Key)
                                .ToDictionary(entry => entry.Key, entry => entry.Value);
                            return;
                        }

                        entriesVisited++;
                        projects[file.Substring(path.Length).TrimStart(Path.DirectorySeparatorChar)] = path;
                    }
                }

                ContextMenuManager.AllConfigVersions = applications
                    .OrderBy(entry => entry.Key)
                    .ToDictionary(entry => entry.Key, entry => entry.Value);
                ProjectFiles = projects;
            } catch (Exception ex) {
                Debug.WriteLine(ex);
            }
        }

        private static bool ScanBudgetExceeded(Stopwatch stopwatch, int entriesVisited) {
            return stopwatch.ElapsedMilliseconds >= FirstStartupScanBudgetMs
                || entriesVisited >= FirstStartupScanEntryLimit;
        }

        private void EnsureSettingsUpgraded() {
            SettingsGuard.Execute(() => {
                if (Properties.Settings.Default.settingsUpgraded) {
                    return;
                }

                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.settingsUpgraded = true;
                Properties.Settings.Default.Save();
            });
        }

        private void processArgs(string[] args, bool shiftPressed) {
            foreach (string arg in args) {
                if (arg != string.Empty && IsSubstringInArray(arg, WatcherFilter.getAllFileEndings().ToArray())) {
                    EnsureMainWindow();

                    if (!mainForm.IsVisible && (shiftPressed || Properties.Settings.Default.alwaysShowSelectionDialog)) {
                        mainForm.FilePath = arg;
                        mainForm.Show();
                        mainForm.Activate();
                    } else {
                        mainForm.ExecuteArgs(arg, useLatestVersion);
                    }
                }
            }
        }

        public static bool ShiftPressed() {
            if ((Control.ModifierKeys & Keys.Shift) != 0) {
                return true;
            }
            return false;
        }

        private static bool IsSubstringInArray(string searchString, string[] indexEntries) {
            foreach (string str in indexEntries) {
                if (searchString.Contains(str)) {
                    return true;
                }
            }
            return false;
        }

        private string? filePathFromArgs(string[] args) {
            string? filePath = null;

            if (args != null) {
                foreach (string argument in args) {
                    if (Path.IsPathRooted(argument) && IsSubstringInArray(argument, WatcherFilter.getAllFileEndings().ToArray())) {
                        filePath = argument;
                    }
                }
            }

            return filePath;
        }

        public void OnInstanceInvoked(string[] args) {
            RunOnUiThread(() => {
                bool shiftPressed = ShiftPressed();

                string? pathFromAgrument = filePathFromArgs(args);
                if (pathFromAgrument != null) {
                    openConfigFileWithSelectionDialog((string)pathFromAgrument, shiftPressed);
                }
            });
        }

        private void RunOnUiThread(Action action) {
            if (action == null)
                return;

            if (mainForm != null) {
                if (mainForm.Dispatcher.CheckAccess()) {
                    action();
                } else {
                    mainForm.Dispatcher.BeginInvoke(action);
                }
                return;
            }

            if (uiInvokeControl != null && !uiInvokeControl.IsDisposed) {
                if (uiInvokeControl.InvokeRequired) {
                    uiInvokeControl.BeginInvoke(action);
                } else {
                    action();
                }
                return;
            }

            action();
        }

#if DEBUG

        private void debugChangeSettings(SettingsManipulation manipulationCommand) {
            switch (manipulationCommand) {
                case SettingsManipulation.setDefault:
                    Properties.Settings.Default.Autostartup = false;
                    Properties.Settings.Default.middleMouseButtonEvent = false;
                    Properties.Settings.Default.useLatestVersion = false;
                    Properties.Settings.Default.alwaysShowSelectionDialog = false;
                    break;
                case SettingsManipulation.clearAllPaths:
                    Properties.Settings.Default.SavedPaths = "";
                    Properties.Settings.Default.projectsFolderPath = "";
                    Properties.Settings.Default.defaultVersion = "";
                    break;
                case SettingsManipulation.incorrectDefaultConfig:
                    Properties.Settings.Default.defaultVersion = "C:";
                    break;
                case SettingsManipulation.incorrectProjectFolderPath:
                    Properties.Settings.Default.defaultVersion = "C:";
                    break;
                case SettingsManipulation.incorrectFilePath:
                    Properties.Settings.Default.defaultVersion = "C:";
                    break;
                case SettingsManipulation.setAutostartup:
                    Properties.Settings.Default.Autostartup = true;
                    break;
                case SettingsManipulation.unsetAutostartup:
                    Properties.Settings.Default.Autostartup = false;
                    break;
                case SettingsManipulation.useLatestVersion:
                    Properties.Settings.Default.useLatestVersion = true;
                    break;
                case SettingsManipulation.notUseLatestVersion:
                    Properties.Settings.Default.useLatestVersion = false;
                    break;
                case SettingsManipulation.alwaysShowSelectionDialog:
                    Properties.Settings.Default.alwaysShowSelectionDialog = true;
                    break;
                case SettingsManipulation.notalwaysShowSelectionDialog:
                    Properties.Settings.Default.alwaysShowSelectionDialog = false;
                    break;
                case SettingsManipulation.setMiddleMouseButtonAction:
                    Properties.Settings.Default.middleMouseButtonEvent = true;
                    break;
                case SettingsManipulation.unsetMiddleMouseButtonAction:
                    Properties.Settings.Default.middleMouseButtonEvent = false;
                    break;
                default:
                    break;
            }
            Properties.Settings.Default.Save();
        }
#endif

        private void loadData() {
            EditSettings editSettings = new EditSettings();
            SettingsGuard.Execute(() => {
                ConfigFolderPaths = editSettings.deserializeString(Properties.Settings.Default.SavedPaths);
                ProjectsFolderPaths = editSettings.deserializeString(Properties.Settings.Default.projectsFolderPath);
                closeConfigWithMouseWheelClick = Properties.Settings.Default.middleMouseButtonEvent;
                useLatestVersion = Properties.Settings.Default.useLatestVersion;
                AutoStartHelper.RegisterInStartup(Properties.Settings.Default.Autostartup);
            }, () => {
                ConfigFolderPaths = new List<string>();
                ProjectsFolderPaths = new List<string>();
                closeConfigWithMouseWheelClick = false;
                useLatestVersion = false;
                AutoStartHelper.RegisterInStartup(true);
            });
        }

        public void UpdateData() {
            loadData();
            findAllProjectFiles();
            findAllApplications();
            Watchers = new FileSystemWatcher[ConfigFolderPaths.Count() + ProjectsFolderPaths.Count()];
            createPathWatchers(ConfigFolderPaths, WatcherFilter.LoxoneConfig);
            createFileWatchers(ProjectsFolderPaths, WatcherFilter.loxoneFile);
            ContextMenuManager.ProjectsFolderPaths = ProjectsFolderPaths;
            ContextMenuManager.ProjectFiles = ProjectFiles;
            refreshContextMenu();
            updateCheckService?.NotifyLocalVersionsChanged(GetLocalBuildIds());
        }

        private NotifyIcon setupNotifyIcon(NotifyIcon icon, ContextMenuStrip contextMenu) {
            icon = new NotifyIcon();
            icon.Icon = Properties.Resources.LoxTools;

            icon.MouseDoubleClick += TrayIcon_DoubleClick;
            icon.MouseDown += TrayIcon_MouseDown;
            icon.BalloonTipClicked += (sender, args) => appUpdateService?.HandlePrimaryAction();
            icon.Visible = true;

            return icon;
        }

        private void findAllApplications() {
            Dictionary<string, string> searchresult = new Dictionary<string, string>() { };
            try {
                List<string> subdirectoryEntries = new List<string>();
                foreach (string str in ConfigFolderPaths) {
                    subdirectoryEntries.AddRange(Directory.GetDirectories(str));
                }

                foreach (string subdirectory in subdirectoryEntries) {
                    string configVersion = "";

                    try {
                        string subFolderName = subdirectory.Remove(0, subdirectory.LastIndexOf("\\") + 1);
                        configVersion = subFolderName;
                    } catch (Exception ex) {
                        MessageBox.Show(ex.ToString());
                    } finally {
                        if (!searchresult.ContainsKey(configVersion) && configVersion != "") {
                            searchresult.Add(configVersion, subdirectory);
                        }
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            } finally {
                searchresult = searchresult.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);
                ContextMenuManager.AllConfigVersions = searchresult;
            }
        }

        private void findAllProjectFiles() {
            Dictionary<string, string> searchresult = new Dictionary<string, string>() { };

            try {
                List<string> subdirectoryEntries = new List<string>();
                foreach (string path in ProjectsFolderPaths) {
                    subdirectoryEntries.AddRange(Directory.GetFileSystemEntries(path, WatcherFilter.loxoneFile, SearchOption.AllDirectories));
                    foreach (string FileName in subdirectoryEntries) {
                        searchresult.Add(FileName.Remove(0, path.Count() + 1), path);
                    }
                }
            } catch (Exception) { } finally {
                ProjectFiles = searchresult;
            }
        }

        #region Contextmenu Manager
        private void setupContextMenu() {
            // set data
            ContextMenuManager.ProjectsFolderPaths = ProjectsFolderPaths;
            ContextMenuManager.ProjectFiles = ProjectFiles;

            refreshContextMenu();
        }
        private void refreshContextMenu() {
            RunOnUiThread(() => {
                ContextMenuStrip contextMenuStrip = ContextMenuManager.generateContextMenu(ContextMenuManager.AllConfigVersions);
                TrayIcon.ContextMenuStrip = contextMenuStrip;
            });
        }
        #endregion

        #region File/Path Wathchers
        private void createFileWatchers(List<string> files, string? filter = default) => createPathWatchers(files, filter);
        private void createPathWatchers(List<string> paths, string filter) {
            paths = checkPathAccess(paths);
            if (paths != null) {
                foreach (string singlePath in paths) {
                    FileSystemWatcher watcher = new FileSystemWatcher();

                    watcher.Path = singlePath;
                    watcher.IncludeSubdirectories = true;
                    watcher.NotifyFilter = NotifyFilters.Attributes |
                    NotifyFilters.CreationTime |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.FileName |
                    NotifyFilters.LastAccess |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Security |
                    NotifyFilters.Size;

                    watcher.Changed += new FileSystemEventHandler(FolderChanged);
                    watcher.Created += new FileSystemEventHandler(FolderCreated);
                    watcher.Deleted += new FileSystemEventHandler(FolderRemoved);
                    watcher.Renamed += new RenamedEventHandler(FolderRenamed);

                    watcher.EnableRaisingEvents = true;

                    for (int j = 0; j < Watchers.Count(); j++) {
                        if (Watchers[j] == null) {
                            Watchers[j] = watcher;
                            break;
                        }
                    }
                }
            }
        }

        private List<string> checkPathAccess(List<string> pathsToCheck) {
            List<string> existingPaths = new List<string>();
            foreach (string checkPath in pathsToCheck) {
                if (Directory.Exists(checkPath)) {
                    existingPaths.Add(checkPath);
                }
            }
            return existingPaths;
        }

        private void FolderCreated(object sender, FileSystemEventArgs e) {
            directoryFolderEvents(sender, e);
        }

        private void FolderChanged(object sender, FileSystemEventArgs e) {
            directoryFolderEvents(sender, e);
        }

        private void FolderRemoved(object sender, FileSystemEventArgs e) {
            directoryFolderEvents(sender, e);
        }
        private void FolderRenamed(object sender, RenamedEventArgs e) {
            directoryFolderEvents(sender, e);
        }

        private void directoryFolderEvents(object sender, FileSystemEventArgs e) {
            ScheduleRefreshFromWatcher(e?.FullPath);
        }

        private static bool IsUnderAnyPath(string path, IEnumerable<string> roots) {
            if (string.IsNullOrWhiteSpace(path) || roots == null) {
                return false;
            }

            foreach (var root in roots) {
                if (string.IsNullOrWhiteSpace(root)) {
                    continue;
                }

                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }
        private void ScheduleRefreshFromWatcher(string path) {
            if ((ConfigFolderPaths == null || ProjectsFolderPaths == null) && string.IsNullOrWhiteSpace(path)) {
                return;
            }

            bool isKnownPath =
                IsUnderAnyPath(path, ConfigFolderPaths) ||
                IsUnderAnyPath(path, ProjectsFolderPaths);

            if (!isKnownPath) {
                return;
            }

            lock (refreshLock) {
                if (refreshDebounceTimer == null) {
                    refreshDebounceTimer = new System.Threading.Timer(_ => BeginRefreshScan(), null, Timeout.Infinite, Timeout.Infinite);
                }

                refreshCts?.Cancel();
                refreshDebounceTimer.Change(RefreshDebounceMs, Timeout.Infinite);
            }
        }

        private void BeginRefreshScan() {
            CancellationTokenSource cts = new CancellationTokenSource();
            lock (refreshLock) {
                refreshCts = cts;
            }

            _ = Task.Run(() => RefreshScanAsync(cts.Token), cts.Token);
        }

        private async Task RefreshScanAsync(CancellationToken token) {
            await refreshSemaphore.WaitAsync(token);
            try {
                if (token.IsCancellationRequested) {
                    return;
                }

                Dictionary<string, string> tempAllConfigVersions = ContextMenuManager.AllConfigVersions;
                Dictionary<string, string> projectFiles = ProjectFiles;
                List<string> configFolderPaths = ConfigFolderPaths;

                findAllProjectFiles();
                if (token.IsCancellationRequested) {
                    return;
                }
                findAllApplications();
                IReadOnlyList<string> localBuildIds = GetLocalBuildIds();

                ObjectComparisons Compare = new ObjectComparisons();
                bool configVersionsChanged = !Compare.DictionaryIsEqual(tempAllConfigVersions, ContextMenuManager.AllConfigVersions);
                bool projectFilesChanged = !Compare.DictionaryIsEqual(projectFiles, ProjectFiles);
                bool configPathsChanged = !Compare.ListIsEqual(ConfigFolderPaths, configFolderPaths);

                if (configVersionsChanged || projectFilesChanged || configPathsChanged) {
                    ContextMenuManager.ProjectsFolderPaths = ProjectsFolderPaths;
                    ContextMenuManager.ProjectFiles = ProjectFiles;
                    refreshContextMenu();
                }

                updateCheckService?.NotifyLocalVersionsChanged(localBuildIds);
            } catch (OperationCanceledException) {
            } finally {
                refreshSemaphore.Release();
            }
        }
        #endregion

        #region All Events
        private void onApplicationExit(object sender, EventArgs e) {
            try {
                updateCheckService?.Dispose();
                appUpdateService?.Dispose();
                TrayIcon.Visible = false;
                TrayIcon.Dispose();
            } catch (Exception ex) { MessageBox.Show(ex.ToString()); }
        }

        private void TrayIcon_MouseDown(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Middle) {
                if (closeConfigWithMouseWheelClick) {
                    closeAllRunningConfigApplication();
                }
            }
        }
        private void TrayIcon_DoubleClick(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                ShowSettingsWindow(modal: true);
            } else if (e.Button == MouseButtons.Middle) {
                if (closeConfigWithMouseWheelClick) {
                    closeAllRunningConfigApplication();
                }
            }
        }

        public void ShowSettingsWindow(bool modal) {
            if (settingsWindow != null) {
                try {
                    if (settingsWindow.WindowState == System.Windows.WindowState.Minimized) {
                        settingsWindow.WindowState = System.Windows.WindowState.Normal;
                    }
                    settingsWindow.Activate();
                } catch {
                }
                return;
            }

            var configPaths = ConfigFolderPaths ?? new List<string>();
            var projectPaths = ProjectsFolderPaths ?? new List<string>();

            settingsWindow = new SettingsWindow(configPaths, projectPaths);
            settingsWindow.SettingsSaved += SettingsWindow_SettingsSaved;
            settingsWindow.Closed += SettingsWindow_Closed;

            // Only set an owner if the main window is already created/shown; otherwise WPF throws.
            if (mainForm != null && mainForm.IsLoaded) {
                settingsWindow.Owner = mainForm;
            }

            if (modal) {
                settingsWindow.ShowDialog();
            } else {
                settingsWindow.Show();
            }
        }

        private void SettingsWindow_SettingsSaved(object sender, EventArgs e) {
            UpdateData();
        }

        private void SettingsWindow_Closed(object sender, EventArgs e) {
            if (settingsWindow == null) {
                return;
            }

            settingsWindow.SettingsSaved -= SettingsWindow_SettingsSaved;
            settingsWindow.Closed -= SettingsWindow_Closed;
            settingsWindow = null;
        }

        #endregion


        #region Command functions
        private void closeAllRunningConfigApplication() {
            Task.Run(() => {
                try {
                    var processes = Process.GetProcessesByName("LoxoneConfig");
                    if (processes == null || processes.Length == 0) {
                        return;
                    }

                    foreach (var proc in processes) {
                        try {
                            if (!proc.HasExited) {
                                proc.CloseMainWindow();
                            }
                        } catch { }
                    }

                    Task.Delay(1500).Wait();

                    foreach (var proc in processes) {
                        try {
                            if (!proc.HasExited) {
                                proc.Kill(true);
                            }
                        } catch { }
                    }

                    try {
                        TrayIcon?.ShowBalloonTip(
                            3000,
                            Lang.TerminateAllLoxoneConfigInstances_Title,
                            Lang.TerminateAllLoxoneConfigInstances_Description,
                            ToolTipIcon.Info);
                    } catch { }
                } catch (Exception ex) {
                    try { Debug.WriteLine(ex); } catch { }
                }
            });
        }

        public void CloseThisApplication() {
            Application.Exit();
        }
        #endregion

        public void openConfigFileWithSelectionDialog(string arg, bool shiftPressed) {
            EnsureMainWindow();
            mainForm.ConfigVersions = ContextMenuManager.AllConfigVersions;
            try {
                if (shiftPressed || Properties.Settings.Default.alwaysShowSelectionDialog) {
                    if (!mainForm.IsVisible) {
                        mainForm.FilePath = arg;
                        mainForm.Show();
                        mainForm.Activate();
                    } else if (mainForm != null) {
                        mainForm.Activate();
                    }
                } else {
                    mainForm.ExecuteArgs(arg, useLatestVersion);
                }
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }
		public static LoxToolsApplication GetInstance() {
            if (_instance != null)
                return _instance;
            return default;
        }

        private void EnsureMainWindow() {
            if (mainForm == null) {
                mainForm = new VersionSelectorWindow(this, ContextMenuManager.AllConfigVersions);
                mainForm.Closed += (s, e) => { mainForm = null; };
            }
        }

        private void InitializeUpdateCheckService() {
            if (updateCheckService != null) {
                return;
            }

            updateCheckService = new UpdateCheckService(
                uiInvokeControl,
                GetLocalBuildIds,
                CloseThisApplication,
                simulateUpdateAvailable);
            ContextMenuManager.UpdateCheckService = updateCheckService;
            updateCheckService.Start();
        }

        private void InitializeAppUpdateService() {
            if (appUpdateService != null) return;

            appUpdateService = new AppUpdateService(uiInvokeControl, CloseThisApplication);
            appUpdateService.StateChanged += (sender, snapshot) => ContextMenuManager.UpdateAppUpdateMenuItem(snapshot);
            appUpdateService.UpdateNotificationRequested += (sender, snapshot) => {
                if (TrayIcon == null || snapshot?.Release == null) return;
                TrayIcon.BalloonTipTitle = Lang.AppUpdate_NotificationTitle;
                TrayIcon.BalloonTipText = string.Format(Lang.AppUpdate_NotificationText, snapshot.Release.Version);
                TrayIcon.BalloonTipIcon = ToolTipIcon.Info;
                TrayIcon.ShowBalloonTip(8000);
            };
            ContextMenuManager.AppUpdateService = appUpdateService;
            ContextMenuManager.UpdateAppUpdateMenuItem(appUpdateService.CurrentSnapshot);
            appUpdateService.Start();
        }

        private IReadOnlyList<string> GetLocalBuildIds() {
            var results = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string exePath in GetInstalledConfigExePaths()) {
                if (VersionDisplayHelper.TryGetBuildIdFromExe(exePath, out string buildId)) {
                    if (unique.Add(buildId)) {
                        results.Add(buildId);
                    }
                }

                var versions = VersionDisplayHelper.ReadExeVersions(exePath);
                string displayVersion = VersionDisplayHelper.GetDisplayVersion(versions.Item1, versions.Item2);
                if (!string.IsNullOrWhiteSpace(displayVersion) && unique.Add(displayVersion)) {
                    results.Add(displayVersion);
                }

                if (!string.IsNullOrWhiteSpace(versions.Item1) && unique.Add(versions.Item1)) {
                    results.Add(versions.Item1);
                }
            }

            return results;
        }

        private IReadOnlyList<string> GetInstalledConfigExePaths() {
            if (ConfigFolderPaths == null || ConfigFolderPaths.Count == 0) {
                return Array.Empty<string>();
            }

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in ConfigFolderPaths) {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
                    continue;
                }

                string[] subDirectories;
                try {
                    subDirectories = Directory.GetDirectories(root);
                } catch {
                    continue;
                }

                foreach (string subDirectory in subDirectories) {
                    string exePath = Path.Combine(subDirectory, "LoxoneConfig.exe");
                    if (File.Exists(exePath)) {
                        result.Add(exePath);
                    }
                }
            }

            return result
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private string GetLatestInstalledConfigExePath() {
            var versions = ContextMenuManager.AllConfigVersions;
            if (versions == null || versions.Count == 0) {
                return null;
            }

            Version bestVersion = null;
            string bestPath = null;

            foreach (var entry in versions) {
                string exePath = Path.Combine(entry.Value ?? string.Empty, "LoxoneConfig.exe");
                var versionsInfo = VersionDisplayHelper.ReadExeVersions(exePath);
                Version parsed = VersionDisplayHelper.GetSortVersion(versionsInfo.Item1, versionsInfo.Item2, true);
                if (parsed == null) {
                    continue;
                }

                if (bestVersion == null || parsed > bestVersion) {
                    bestVersion = parsed;
                    bestPath = exePath;
                }
            }

            if (string.IsNullOrWhiteSpace(bestPath)) {
                var first = versions.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first.Value)) {
                    return Path.Combine(first.Value, "LoxoneConfig.exe");
                }
            }

            return bestPath;
        }

        private static bool HasArgument(string[] args, string expected) {
            if (args == null || string.IsNullOrWhiteSpace(expected)) {
                return false;
            }

            string expectedNormalized = NormalizeArgument(expected);
            foreach (string arg in args) {
                string normalized = NormalizeArgument(arg);
                if (!string.IsNullOrEmpty(normalized) &&
                    string.Equals(normalized, expectedNormalized, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeArgument(string arg) {
            if (string.IsNullOrWhiteSpace(arg)) {
                return string.Empty;
            }

            string value = arg.Trim().Trim('"');
            if (value.StartsWith("--", StringComparison.OrdinalIgnoreCase)) {
                value = value.Substring(2);
            } else if (value.StartsWith("/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("-", StringComparison.OrdinalIgnoreCase)) {
                value = value.Substring(1);
            }

            return value.Replace("-", string.Empty).Trim();
        }

        private static bool IsEnvironmentFlagSet(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return false;
            }

            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            return value == "1"
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
	}

    static class InvokeContextMenu {
        public static void InvokeIfRequired(this System.ComponentModel.ISynchronizeInvoke obj, System.Windows.Forms.MethodInvoker action) {
            if (obj.InvokeRequired) {
                obj.Invoke(action, null);
            } else {
                action();
            }

        }
        public static void InvokeIfRequired<T>(this T obj, Action<T> action) where T : System.ComponentModel.ISynchronizeInvoke {
            InvokeIfRequired(obj, () => action(obj));
        }

        public static void Invoke(this SynchronizationContext context, Action action) {
            if (action != null) {
                if (context != null) {
                    context.Send((state) => action(), null);
                } else {
                    action();
                }
            }
        }
    }
}
