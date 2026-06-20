using LoxTools.Language;
using LoxTools.Core.Settings;
using LoxTools.Models;
using LoxTools.Properties;
using LoxTools.UI.Helpers;
using LoxTools.UI.Tray.ContextMenuItems;
using LoxTools.UI.Tray.ContextMenuItems.ToolStripItems;
using LoxTools.UpdateCheck;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xaml.Schema;

namespace LoxTools.UI.Tray {
    static class ContextMenuManager {
        private const bool SortConfigVersionsByFileVersion = VersionDisplayHelper.SortByFileVersionOnly;
        private const string UpdateChannelValueName = "UpdateChannel";
        private const string IncludeAlphaValueName = "IncludeAlpha";
        public static ApplicationContext context;
        private const string standardLoxoneAppSubfolder = @"\Programs\kerberos\Loxone.exe";
        private const string loxoneExecConfigFileName = "LoxoneConfig.exe";
        public const string ExplorerCmdName = "explorer.exe";
        public static bool AllowContextMenuClosing { get; set; } = false;
        private static ContextMenuStrip contextMenu { get; set; }
        public static LoxToolsApplication LoxToolsInstance = null;

        private static Dictionary<string, string> allConfigVersions = new Dictionary<string, string>() { };
        public static Dictionary<string, string> AllConfigVersions {
            get
            {
                return allConfigVersions;
            }
            set
            {
                allConfigVersions = value;
            }
        }
        public static List<string> ProjectsFolderPaths { get; set; }
        private static Dictionary<string, string> projectFiles = new Dictionary<string, string>() { };

        public static Dictionary<string, string> ProjectFiles {
            get => projectFiles;
            set => projectFiles = value;
        }
        private static ConfigToolStripItem currentDefaultVersion { get; set; }
        private static ConfigToolStripItem latestDefaultVersion { get; set; }
        private static ProcessToolStripItem updateCheckMenuItem { get; set; }
        private static ProcessToolStripItem updateChannelMenuItem { get; set; }
        public static UpdateCheckService UpdateCheckService { get; set; }
        private static System.Windows.Forms.Timer updateCheckDotsTimer;
        private static int updateCheckDotsIndex;
        private static UpdateCheckState updateCheckState;
        private static string updateCheckVersionLabel;
        private static UpdateChannel updateCheckChannel;
        private static string updateCheckDotsFormat;
        private static Icon warningTrayIcon;
        private static Icon defaultTrayIcon;
        private static bool? trayIconWarningShown;
        private static Icon lastTrayIcon;
        private static bool trayIconRefreshQueued;

		public static readonly List<ProcessToolStripItem> BaseToolStripItems = new List<ProcessToolStripItem>() {
            CreateUpdateCheckMenuItem(),
            CreateUpdateChannelMenuItem(),
			new ProcessToolStripItem (
			    Lang.ProjectFolder,
                null,
				() => createProjectFolderDropDown(ProjectsFolderPaths)
		    ),
			new ProcessToolStripItem (
				Lang.Projects,
                null,
				() => createProjectFilesDropDown(ProjectFiles.Values.ToList(), projectFiles.Keys.ToList())
		    ),
			new ProcessToolStripItem (
				Lang.OpenLxApp,
			    OpenLoxoneApp
			),
			new ProcessToolStripItem (
				Lang.CloseAllConfigs,
			    closeAllRunningConfigApplication
		    ),
		    new ProcessToolStripItem (
				Lang.Settings,
                openSettingsForm
			),
		    new ProcessToolStripItem (
			    Lang.CloseApplications,
			    closeApplication
		    )
		};

		#region events
		public static MouseEventHandler CloseDropDownMenuItem_Click;
        private static void closeDropDownMenuItem_Click(object sender, MouseEventArgs a) {
            if (CloseDropDownMenuItem_Click != null)
                CloseDropDownMenuItem_Click.Invoke(sender, a);
        }

        private static void closingContextMenu(object sender, ToolStripDropDownClosingEventArgs e) {
			e.Cancel = !AllowContextMenuClosing;
			AllowContextMenuClosing = true;
		}
        #endregion


        public static ContextMenuStrip generateContextMenu() => generateContextMenu(allConfigVersions);
        public static ContextMenuStrip generateContextMenu(Dictionary<string, string> allConfigExecFiles) {
            contextMenu = setupContextMenu(contextMenu);
            contextMenu.Items.AddRange(generateMainToolStripItems(allConfigExecFiles));
            contextMenu.Items.Insert(allConfigExecFiles.Keys.Count, new ExtendedToolStripSeparator() { });
            InsertUpdateCheckSeparator();

            return contextMenu;
        }

        private static LxToolStripitem[] generateMainToolStripItems() => generateMainToolStripItems(allConfigVersions);
        private static LxToolStripitem[] generateMainToolStripItems(Dictionary<string, string> tempAllConfigVersions) {
            List<LxToolStripitem>  completeContextMenuItems = new List<LxToolStripitem>();

			completeContextMenuItems.AddRange(CreateConfigItems(tempAllConfigVersions));
			completeContextMenuItems.AddRange(CreateBaseToolStripItems());

			return completeContextMenuItems.ToArray();
        }

		private static IEnumerable<LxToolStripitem> CreateConfigItems(Dictionary<string, string> configVersions) {
			var result = new List<LxToolStripitem>();
			string defaultPath = Properties.Settings.Default.defaultVersion ?? string.Empty;
			var entries = BuildConfigVersionEntries(configVersions);
            var orderedEntries = SortConfigVersionsByFileVersion
                ? entries.OrderBy(e => e.ParsedVersion == null)
                    .ThenBy(e => e.ParsedVersion ?? new Version(0, 0))
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

			bool defaultApplied = false;
			foreach (var entry in orderedEntries) {
				var item = new ConfigToolStripItem(entry.Path, entry.Name) {
					ImageScaling = ToolStripItemImageScaling.SizeToFit
				};
				StoreExeVersionInfo(item, entry.Versions);
				item.DisplayName = entry.DisplayName;

				if (!string.IsNullOrEmpty(defaultPath)
					&& string.Equals(defaultPath, entry.Path, StringComparison.OrdinalIgnoreCase)) {
					setDefaultVersion(item);
					defaultApplied = true;
				}

				result.Add(item);
			}

			latestDefaultVersion = GetLatestVersionItem(result, entries);
			if (!defaultApplied) {
				if (latestDefaultVersion != null) {
					setDefaultVersion(latestDefaultVersion);
				} else if (result.Count > 0) {
					setDefaultVersion(result[0] as ConfigToolStripItem);
				}
			}

			if (Properties.Settings.Default.useLatestVersion && latestDefaultVersion != null) {
				setLatestDefaultVersion();
			}

			return result;
		}

		private static void StoreExeVersionInfo(ConfigToolStripItem item, Tuple<string, string> versions) {
			if (item != null) {
				item.Tag = versions;
			}
		}

		private static List<ConfigVersionEntry> BuildConfigVersionEntries(Dictionary<string, string> configVersions) {
			var entries = new List<ConfigVersionEntry>();
			if (configVersions == null) {
				return entries;
			}

			foreach (var pair in configVersions) {
				string name = pair.Key;
				string path = pair.Value;
				string exePath = Path.Combine(path ?? string.Empty, loxoneExecConfigFileName);
				var versions = VersionDisplayHelper.ReadExeVersions(exePath);
				string displayName = VersionDisplayHelper.BuildDisplayName(name, versions.Item1, versions.Item2);
				Version parsed = VersionDisplayHelper.GetSortVersion(versions.Item1, versions.Item2, SortConfigVersionsByFileVersion);

				entries.Add(new ConfigVersionEntry {
					Name = name,
					Path = path,
					DisplayName = displayName,
					ParsedVersion = parsed,
					Versions = versions
				});
			}

			return entries;
		}

		private static ConfigToolStripItem GetLatestVersionItem(List<LxToolStripitem> items, List<ConfigVersionEntry> entries) {
			var latest = entries
				.Where(e => e.ParsedVersion != null)
				.OrderByDescending(e => e.ParsedVersion)
				.ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault();

			if (latest == null) {
				return null;
			}

			foreach (var item in items.OfType<ConfigToolStripItem>()) {
				if (string.Equals(item.FileDirectory, latest.Path, StringComparison.OrdinalIgnoreCase)) {
					return item;
				}
			}

			return null;
		}

		private sealed class ConfigVersionEntry {
			public string Name { get; set; }
			public string Path { get; set; }
			public string DisplayName { get; set; }
			public Version ParsedVersion { get; set; }
			public Tuple<string, string> Versions { get; set; }
		}

		private static IEnumerable<LxToolStripitem> CreateBaseToolStripItems() {
			foreach (var item in BaseToolStripItems)
				item.BuildDropDownIfNecessary();

			return BaseToolStripItems;
		}
		private static ToolStripDropDownMenu createProjectFolderDropDown(List<string> allProjectFolderPaths) => createToolStripDropDownMenu(allProjectFolderPaths, null, typeof(FolderToolStripItem));
        private static ToolStripDropDownMenu createProjectFilesDropDown(List<string> values, List<string> Keys) => createToolStripDropDownMenu(values, Keys, typeof(FileToolStripItem));

        private static ToolStripDropDownMenu createToolStripDropDownMenu(List<string> itemList, List<string> dataList, Type toolStripitemType) {
            ToolStripDropDownMenu tempToolStripDropDownMenu = new ToolStripDropDownMenu();
            tempToolStripDropDownMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            tempToolStripDropDownMenu.ForeColor = Color.GhostWhite;

            if (itemList != null) {

                for (int i = 0; i < itemList.Count; i++) {
                    if (toolStripitemType == typeof(FolderToolStripItem)) {
                        tempToolStripDropDownMenu.Items.Add(new FolderToolStripItem(itemList[i]));
                    } else if (toolStripitemType == typeof(FileToolStripItem)) {
                        tempToolStripDropDownMenu.Items.Add(new FileToolStripItem(itemList[i], dataList[i]));
                    }
                }

                tempToolStripDropDownMenu.ShowImageMargin = false;
            }

            return tempToolStripDropDownMenu;
        }

        #region UI
        private static ContextMenuStrip setupContextMenu(ContextMenuStrip tempContextMenu) {
            tempContextMenu = new ContextMenuStrip();
            tempContextMenu.SuspendLayout();
            tempContextMenu.Opening += (sender, args) => {
                RefreshConfigItemDisplayNames(tempContextMenu);
                RefreshUpdateChannelMenuItem(GetConfiguredUpdateChannel());
                AllowContextMenuClosing = true;
            };
            tempContextMenu.Closing += closingContextMenu;
            //tempContextMenu.LostFocus += contextMenu_LostFocus;

            tempContextMenu.Renderer = new DarkToolStripRenderer();
            tempContextMenu.ImageScalingSize = new Size(16, 16);

            tempContextMenu.ResumeLayout(false);

            return tempContextMenu;
        }

        private static void RefreshConfigItemDisplayNames(ContextMenuStrip menu) {
            if (menu == null) {
                return;
            }

            var itemsWithVersions = new List<(ConfigToolStripItem Item, Version ParsedVersion)>();
            foreach (var item in menu.Items.OfType<ConfigToolStripItem>()) {
                string exePath = Path.Combine(item.FileDirectory ?? string.Empty, loxoneExecConfigFileName);
                var versions = VersionDisplayHelper.ReadExeVersions(exePath);
                item.Tag = versions;
                item.DisplayName = VersionDisplayHelper.BuildDisplayName(item.FileName, versions.Item1, versions.Item2);
                Version parsedVersion = VersionDisplayHelper.GetSortVersion(versions.Item1, versions.Item2, SortConfigVersionsByFileVersion);
                itemsWithVersions.Add((item, parsedVersion));
            }

            if (itemsWithVersions.Count == 0) {
                return;
            }

            var orderedItems = SortConfigVersionsByFileVersion
                ? itemsWithVersions
                    .OrderBy(x => x.ParsedVersion == null)
                    .ThenBy(x => x.ParsedVersion ?? new Version(0, 0))
                    .ThenBy(x => x.Item.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Item)
                    .ToList()
                : itemsWithVersions
                    .OrderBy(x => x.Item.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Item)
                    .ToList();

            foreach (var item in itemsWithVersions.Select(x => x.Item).ToList()) {
                menu.Items.Remove(item);
            }

            for (int i = 0; i < orderedItems.Count; i++) {
                menu.Items.Insert(i, orderedItems[i]);
            }

            latestDefaultVersion = itemsWithVersions
                .Where(x => x.ParsedVersion != null)
                .OrderByDescending(x => x.ParsedVersion)
                .ThenByDescending(x => x.Item.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Item)
                .FirstOrDefault();

            if (Properties.Settings.Default.useLatestVersion && latestDefaultVersion != null) {
                setLatestDefaultVersion();
            }
        }

		#endregion

        private static ProcessToolStripItem CreateUpdateCheckMenuItem() {
            if (updateCheckMenuItem != null) {
                return updateCheckMenuItem;
            }

            updateCheckMenuItem = new ProcessToolStripItem(
                Lang.UpdateCheck_Checking,
                () => UpdateCheckService?.HandleMenuAction(),
                onMiddleClickAction: TryOpenUpdateDownloadFolder);
            updateCheckMenuItem.ImageScaling = ToolStripItemImageScaling.SizeToFit;
            updateCheckMenuItem.Enabled = false;
            updateCheckMenuItem.MouseEnter += UpdateCheckMenuItem_MouseEnter;
            updateCheckMenuItem.MouseLeave += UpdateCheckMenuItem_MouseLeave;
            return updateCheckMenuItem;
        }

        private static ProcessToolStripItem CreateUpdateChannelMenuItem() {
            if (updateChannelMenuItem != null) {
                return updateChannelMenuItem;
            }

            updateChannelMenuItem = new ProcessToolStripItem();
            updateChannelMenuItem.Enabled = false;
            RefreshUpdateChannelMenuItem(GetConfiguredUpdateChannel());
            return updateChannelMenuItem;
        }

        public static void RefreshUpdateChannelMenuItem(UpdateChannel channel) {
            if (updateChannelMenuItem == null) {
                return;
            }

            updateChannelMenuItem.Text = $"{Lang.SettingsUpdateChannelTitle}: {GetUpdateChannelDisplayName(channel)}";
        }

        private static string GetUpdateChannelDisplayName(UpdateChannel channel) {
            if (channel == UpdateChannel.Beta) {
                return Lang.SettingsUpdateChannel_Beta;
            }

            if (channel == UpdateChannel.Alpha) {
                return Lang.SettingsUpdateChannel_Alpha;
            }

            return Lang.SettingsUpdateChannel_Release;
        }

        private static UpdateChannel GetConfiguredUpdateChannel() {
            int storedValue = RegistryFlagReader.GetDwordValue(UpdateChannelValueName, defaultValue: int.MinValue);
            if (Enum.IsDefined(typeof(UpdateChannel), storedValue)) {
                return (UpdateChannel)storedValue;
            }

            bool includeAlpha = RegistryFlagReader.GetDwordFlag(IncludeAlphaValueName);
            return includeAlpha ? UpdateChannel.Alpha : UpdateChannel.Release;
        }

        public static void UpdateUpdateCheckMenuItem(UpdateCheckState state, bool enabled, string versionLabel, UpdateChannel channel) {
            if (updateCheckMenuItem == null) {
                return;
            }

            updateCheckState = state;
            updateCheckVersionLabel = versionLabel;
            updateCheckChannel = channel;

            switch (state) {
                case UpdateCheckState.Checking:
                    updateCheckMenuItem.Enabled = false;
                    updateCheckMenuItem.Image = GetUpdateCheckIcon(UpdateCheckState.Checking);
                    StartUpdateCheckAnimation(Lang.UpdateCheck_Checking_WithDots);
                    SetTrayIconWarning(false);
                    break;
                case UpdateCheckState.Downloading:
                    updateCheckMenuItem.Enabled = false;
                    updateCheckMenuItem.Image = GetUpdateCheckIcon(UpdateCheckState.Downloading);
                    StartUpdateCheckAnimation(Lang.UpdateCheck_Downloading_WithDots);
                    SetTrayIconWarning(false);
                    break;
                case UpdateCheckState.UpdateAvailable:
                    StopUpdateCheckAnimation();
                    updateCheckMenuItem.Image = GetUpdateCheckIcon(UpdateCheckState.UpdateAvailable);
                    updateCheckMenuItem.Text = BuildUpdateStateText(
                        versionLabel,
                        channel,
                        Lang.UpdateCheck_UpdateAvailable_Release,
                        Lang.UpdateCheck_UpdateAvailable_Release_WithVersion,
                        Lang.UpdateCheck_UpdateAvailable_Beta,
                        Lang.UpdateCheck_UpdateAvailable_Beta_WithVersion,
                        Lang.UpdateCheck_UpdateAvailable_Alpha,
                        Lang.UpdateCheck_UpdateAvailable_Alpha_WithVersion);
                    updateCheckMenuItem.Enabled = enabled;
                    SetTrayIconWarning(true);
                    break;
                case UpdateCheckState.UpToDate:
                    StopUpdateCheckAnimation();
                    updateCheckMenuItem.Image = GetUpdateCheckIcon(UpdateCheckState.UpToDate);
                    updateCheckMenuItem.Text = BuildUpdateStateText(
                        versionLabel,
                        channel,
                        Lang.UpdateCheck_UpToDate_Release,
                        Lang.UpdateCheck_UpToDate_Release_WithVersion,
                        Lang.UpdateCheck_UpToDate_Beta,
                        Lang.UpdateCheck_UpToDate_Beta_WithVersion,
                        Lang.UpdateCheck_UpToDate_Alpha,
                        Lang.UpdateCheck_UpToDate_Alpha_WithVersion);
                    updateCheckMenuItem.Enabled = enabled;
                    SetTrayIconWarning(false);
                    break;
                case UpdateCheckState.Failed:
                    StopUpdateCheckAnimation();
                    updateCheckMenuItem.Image = GetUpdateCheckIcon(UpdateCheckState.Failed);
                    updateCheckMenuItem.Text = Lang.UpdateCheck_Failed;
                    updateCheckMenuItem.Enabled = enabled;
                    SetTrayIconWarning(false);
                    break;
            }
        }

        private static string BuildUpdateStateText(
            string versionLabel,
            UpdateChannel channel,
            string releaseText,
            string releaseWithVersionText,
            string betaText,
            string betaWithVersionText,
            string alphaText,
            string alphaWithVersionText) {
            string text = releaseText;
            string withVersionText = releaseWithVersionText;

            if (channel == UpdateChannel.Beta) {
                text = betaText;
                withVersionText = betaWithVersionText;
            } else if (channel == UpdateChannel.Alpha) {
                text = alphaText;
                withVersionText = alphaWithVersionText;
            }

            return string.IsNullOrWhiteSpace(versionLabel)
                ? text
                : string.Format(withVersionText, versionLabel);
        }

        private static Image GetUpdateCheckIcon(UpdateCheckState state) {
            return state switch {
                UpdateCheckState.Checking => Properties.Resources.IconChecking,
                UpdateCheckState.Downloading => Properties.Resources.IconDownloading,
                UpdateCheckState.UpdateAvailable => Properties.Resources.IconDownloading,
                UpdateCheckState.UpToDate => Properties.Resources.IconCheck,
                UpdateCheckState.Failed => Properties.Resources.IconFailed,
                _ => Properties.Resources.IconChecking
            };
        }

        private static void StartUpdateCheckAnimation(string formatText) {
            if (updateCheckMenuItem == null) {
                return;
            }

            if (updateCheckDotsTimer == null) {
                updateCheckDotsTimer = new System.Windows.Forms.Timer();
                updateCheckDotsTimer.Interval = 600;
                updateCheckDotsTimer.Tick += (s, e) => UpdateAnimatedDots();
            }

            updateCheckDotsFormat = formatText;
            updateCheckDotsIndex = 0;
            UpdateAnimatedDots();
            updateCheckDotsTimer.Start();
        }

        private static void StopUpdateCheckAnimation() {
            if (updateCheckDotsTimer == null) {
                return;
            }

            updateCheckDotsTimer.Stop();
        }

        private static void UpdateAnimatedDots() {
            if (updateCheckMenuItem == null) {
                return;
            }

            string dots = updateCheckDotsIndex == 0 ? "." : updateCheckDotsIndex == 1 ? ".." : "...";
            string formatText = string.IsNullOrWhiteSpace(updateCheckDotsFormat)
                ? Lang.UpdateCheck_Checking_WithDots
                : updateCheckDotsFormat;
            updateCheckMenuItem.Text = string.Format(formatText, dots);
            updateCheckDotsIndex = (updateCheckDotsIndex + 1) % 3;
        }

        private static void UpdateCheckMenuItem_MouseEnter(object sender, EventArgs e) {
            if (updateCheckMenuItem == null) {
                return;
            }

            if (updateCheckState == UpdateCheckState.UpToDate || updateCheckState == UpdateCheckState.Failed) {
                updateCheckMenuItem.Text = Lang.UpdateCheck_CheckAgain;
            }
        }

        private static void UpdateCheckMenuItem_MouseLeave(object sender, EventArgs e) {
            if (updateCheckMenuItem == null) {
                return;
            }

            UpdateUpdateCheckMenuItem(updateCheckState, updateCheckMenuItem.Enabled, updateCheckVersionLabel, updateCheckChannel);
        }

        private static void SetTrayIconWarning(bool showWarning) {
            var instance = LoxToolsInstance ?? LoxToolsApplication.GetInstance();
            if (instance?.TrayIcon == null) {
                return;
            }

            if (trayIconWarningShown.HasValue && trayIconWarningShown.Value == showWarning) {
                return;
            }

            if (defaultTrayIcon == null) {
                defaultTrayIcon = (Icon)Properties.Resources.LoxTools.Clone();
            }

            if (showWarning) {
                if (warningTrayIcon == null) {
                    warningTrayIcon = (Icon)Properties.Resources.LoxToolsWarning.Clone();
                }

                if (warningTrayIcon != null) {
                    ForceTrayIconRefresh(instance.TrayIcon, warningTrayIcon);
                    trayIconWarningShown = true;
                    return;
                }
            }

            ForceTrayIconRefresh(instance.TrayIcon, defaultTrayIcon);
            trayIconWarningShown = false;
        }

        private static void RefreshTrayIcon(NotifyIcon trayIcon) {
            if (trayIcon == null || trayIconRefreshQueued) {
                return;
            }

            trayIconRefreshQueued = true;
            try {
                bool wasVisible = trayIcon.Visible;
                trayIcon.Visible = false;
                trayIcon.Visible = wasVisible || true;
            } finally {
                trayIconRefreshQueued = false;
            }
        }

        private static void ForceTrayIconRefresh(NotifyIcon trayIcon, Icon icon) {
            if (trayIcon == null) {
                return;
            }

            if (ReferenceEquals(trayIcon.Icon, icon) && ReferenceEquals(lastTrayIcon, icon)) {
                return;
            }

            trayIcon.Icon = icon;
            lastTrayIcon = icon;
            RefreshTrayIcon(trayIcon);
        }

        private static void TryOpenUpdateDownloadFolder() {
            if (updateCheckState != UpdateCheckState.Downloading) {
                return;
            }

            string path = UpdateCheckService.GetTempRoot();
            OpenFolderWithShell(path);
        }

		#region EventActions

		public static void openSettingsForm() {
			var instance = LoxToolsInstance ?? LoxToolsApplication.GetInstance();
			instance?.ShowSettingsWindow(false);
		}
        public static void tryOpenConfig(string execConfigPath, bool showSelectionDialog) {
            if (execConfigPath != null) {
                try {
                    startProcess(@$"{execConfigPath}\\{loxoneExecConfigFileName}");
                } catch (Exception ex) {
                    MessageBox.Show(ex.ToString());
                }
            } else {
                MessageBox.Show("Couldn't find Folder of Loxone Config");
            }
        }
        public static void tryOpenFile(string execConfigPath, bool showSelectionDialog) {
            if (execConfigPath != null) {
                try {
                    if (showSelectionDialog || Properties.Settings.Default.alwaysShowSelectionDialog) {
                        LoxToolsInstance.openConfigFileWithSelectionDialog(execConfigPath, true);
                    } else if (Properties.Settings.Default.useLatestVersion) {
                        executeWithHiddenCmdWindow(latestDefaultVersion.getFullExePath(), "\"" + execConfigPath);
                    } else {
                        executeWithHiddenCmdWindow(currentDefaultVersion.getFullExePath(), "\"" + execConfigPath);
                    }
                } catch (Exception ex) {
                    MessageBox.Show(ex.ToString());
                }
            } else {
                MessageBox.Show("Couldn't find Folder of Loxone Config");
            }
        }

        public static void closeAllRunningConfigApplication() {
            foreach (Process proc in Process.GetProcessesByName("LoxoneConfig")) {
                proc.Kill();
            }
        }

        // Application Methods
        public static void closeApplication() {
            if (LoxToolsInstance != null)
                LoxToolsInstance.CloseThisApplication();
        }

        // ConfigFile Methods
        public static void tryOpenFolder(string directoryPath) {
            try {
                OpenFolderWithShell(directoryPath);
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        // Loxone App Methods
        public static void OpenLoxoneApp() {
            try {
                string loxoneAppPath = getLoxoneAppInstalledPath();
                if (loxoneAppPath != null) {
                    Process.Start(loxoneAppPath);
                } else {
                    MessageBox.Show("Loxone App nicht installiert.");
                }
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        private static string getLoxoneAppInstalledPath() {
            try {
                if (checkIfLoxoneAppIsInstalled()) {
                    return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + standardLoxoneAppSubfolder;
                } else {
                    return null;
                }
            } catch {
                return null;
            }
        }

        private static bool checkIfLoxoneAppIsInstalled() {
            string defaultInstallationPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + standardLoxoneAppSubfolder;
            if (File.Exists(defaultInstallationPath)) {
                return true;
            } else {
                return false;
            }
        }

        public static void KillLoxoneApp() {
            try {
                foreach (Process proc in Process.GetProcessesByName("Loxone")) {
                    proc.Kill();
                }
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        // Process Stuff
        private static void startProcess(string path) {
            executeWithHiddenCmdWindow(path);
        }

        private static void executeWithHiddenCmdWindow(string filePath = null, string? Args = null) {
            Process cmd = new Process();
            cmd.StartInfo.FileName = filePath;
            cmd.StartInfo.Arguments = Args;
            cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.Start();
        }

        private static void OpenFolderWithShell(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var startInfo = new ProcessStartInfo {
                FileName = path,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        #endregion

        private static void InsertUpdateCheckSeparator() {
            if (contextMenu == null || updateCheckMenuItem == null) {
                return;
            }

            int updateIndex = contextMenu.Items.IndexOf(updateCheckMenuItem);
            if (updateIndex < 0) {
                return;
            }

            int channelIndex = updateChannelMenuItem == null ? -1 : contextMenu.Items.IndexOf(updateChannelMenuItem);
            int separatorIndex = channelIndex > updateIndex ? channelIndex + 1 : updateIndex + 1;
            if (separatorIndex <= contextMenu.Items.Count - 1 && contextMenu.Items[separatorIndex] is ExtendedToolStripSeparator) {
                return;
            }

            contextMenu.Items.Insert(separatorIndex, new ExtendedToolStripSeparator());
        }

        public static void setLatestDefaultVersion() {
            unsetCurrentItemSelection();
            latestDefaultVersion.toolStripMenuItemImage = Properties.Resources.lightGrayDot;
            latestDefaultVersion.Image = latestDefaultVersion.toolStripMenuItemImage;


            Properties.Settings.Default.useLatestVersion = true;
            Properties.Settings.Default.Save();
        }

        public static void setDefaultVersion(ConfigToolStripItem execToolStripItem) => setDefaultVersion(execToolStripItem, Properties.Resources.greenDot);
        public static void setDefaultVersion(ConfigToolStripItem execToolStripItem, Image imageToSet) {
            Properties.Settings.Default.defaultVersion = execToolStripItem.FileDirectory;
            Properties.Settings.Default.Save();

            unsetCurrentItemSelection();
            execToolStripItem.toolStripMenuItemImage = imageToSet;
            execToolStripItem.DefaultVersion = true;
            currentDefaultVersion = execToolStripItem;
        }

        public static void ApplyDefaultVersionSelection(string defaultVersionPath) {
            if (string.IsNullOrEmpty(defaultVersionPath))
                return;

            var instance = LoxToolsInstance;
            if (instance == null || instance.TrayIcon == null || instance.TrayIcon.ContextMenuStrip == null)
                return;

            string fileName = Path.GetFileName(defaultVersionPath);
            ConfigToolStripItem item = getItemByName(fileName) ?? getItemByName(defaultVersionPath);
            if (item != null)
                setDefaultVersion(item);
        }

        private static void unsetCurrentItemSelection() {
            if (currentDefaultVersion != null) {
                currentDefaultVersion.DefaultVersion = false;
            }
        }

        public static void unsetLatestItemSelection() {
            if (latestDefaultVersion != null) {
                latestDefaultVersion.Image = null;
            }
            Properties.Settings.Default.useLatestVersion = false;
            Properties.Settings.Default.Save();
        }

        public static ConfigToolStripItem getItemByName(string fileName) {
            foreach (ConfigToolStripItem col in LoxToolsInstance.TrayIcon.ContextMenuStrip.Items.OfType<ConfigToolStripItem>()) {
                if (fileName == col.FileName || fileName == col.DisplayName) {
                    return col;
                }
            }
            return null;
        }
    }
}
