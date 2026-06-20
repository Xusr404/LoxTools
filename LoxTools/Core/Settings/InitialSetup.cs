using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Input;
using WindowsShortcutFactory;

namespace LoxTools.Core.Settings {
    internal class InitialSetup {
        private const string registryEntryPath = "SOFTWARE\\LoxTools\\Settings";
        private const string legacyRegistryEntryPath = "SOFTWARE\\ConfigVersionSelector\\Settings";
        private const string registryFirstRunKey = "FirstRunDone";
        private const string registryInstallDateKey = "InstallDate";
		internal const string RegisteredAppName = "LoxTools";
		private const string CapabilitiesPath = "Software\\LoxTools\\Capabilities";
		private const string LoxoneProgId = "LoxTools.LoxoneFile";
        internal const string LoxoneFolderName = "Loxone";
        internal const string LoxoneConfigFolderName = "Loxone Config";
        internal const string ProjectsFolderName = "Projects";


        public static bool CheckRunInitialSetup() {
#if DEBUG
            //FirstStartSetup();
#endif
			EnsureDefaultAppCapabilities();
			MigrateLegacySetupState();
			using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registryEntryPath)) {
                if (key == null) {
                    return false;
                }

                if (!IsFirstRun(key)) {
                    return false;
                }

                RunFirstRunSetup(key);
                return true;
            }
        }

        private static void MigrateLegacySetupState() {
            using (RegistryKey currentKey = Registry.CurrentUser.OpenSubKey(registryEntryPath, false)) {
                if (currentKey?.GetValue(registryFirstRunKey) != null) {
                    return;
                }
            }

            using (RegistryKey legacyKey = Registry.CurrentUser.OpenSubKey(legacyRegistryEntryPath, false)) {
                object firstRunDone = legacyKey?.GetValue(registryFirstRunKey);
                if (firstRunDone == null) {
                    return;
                }

                using (RegistryKey currentKey = Registry.CurrentUser.CreateSubKey(registryEntryPath)) {
                    currentKey?.SetValue(registryFirstRunKey, firstRunDone, RegistryValueKind.DWord);
                    object installDate = legacyKey.GetValue(registryInstallDateKey);
                    if (installDate != null) {
                        currentKey?.SetValue(registryInstallDateKey, installDate, RegistryValueKind.String);
                    }
                }
            }
        }

        private static bool IsFirstRun(RegistryKey key) {
            return key.GetValue(registryFirstRunKey) == null;
        }

        private static void RunFirstRunSetup(RegistryKey key) {
            setRegistryEntries(key);
            setDefaultPaths();
            setDefaultAdvancedSettings();
            addToStartFolder();
        }

		private static void addToStartFolder() {
			string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
			string startMenuFolder = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
			string programsFolder = Path.Combine(startMenuFolder, "Programs");
			string shortcutPath = Path.Combine(programsFolder, "LoxTools.lnk");

			if (File.Exists(shortcutPath))
				return;

			Type shellType = Type.GetTypeFromProgID("WScript.Shell");
			dynamic shell = Activator.CreateInstance(shellType);
			dynamic shortcut = shell.CreateShortcut(shortcutPath);

			shortcut.TargetPath = exePath;
			shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
			shortcut.WindowStyle = 1;
			shortcut.Description = Language.Lang.ShortcutDescription;
			shortcut.Save();
		}

        private static void setDefaultPaths() {
            string projectsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                LoxoneFolderName,
                LoxoneConfigFolderName,
                ProjectsFolderName);
            string installPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                LoxoneFolderName);

            Properties.Settings.Default.projectsFolderPath = EnsurePathOrEmpty(projectsPath);
            Properties.Settings.Default.SavedPaths = EnsurePathOrEmpty(installPath);
            Properties.Settings.Default.Save();
        }

        private static void setDefaultAdvancedSettings() {
            Properties.Settings.Default.Autostartup = true;
            Properties.Settings.Default.middleMouseButtonEvent = false;
            Properties.Settings.Default.Save();
        }

        private static string EnsurePathOrEmpty(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return string.Empty;
            }

            if (Directory.Exists(path)) {
                return path;
            }

            Debug.WriteLine(string.Format(Language.Lang.InitialSetup_DefaultPathMissing, path));
            return string.Empty;
        }

		private static void setRegistryEntries(RegistryKey key) {
            key.SetValue(registryFirstRunKey, 1, RegistryValueKind.DWord);
            key.SetValue(registryInstallDateKey, DateTime.UtcNow.ToString("o"), RegistryValueKind.String);
        }

        private static void EnsureDefaultAppCapabilities() {
            if (IsAppCapabilitiesRegistered()) {
                return;
            }

            RegisterDefaultAppCapabilities();
        }

        public static void RefreshDefaultAppCapabilities() {
            RegisterDefaultAppCapabilities();
        }

        private static bool IsAppCapabilitiesRegistered() {
            try {
                using (RegistryKey registeredApps = Registry.CurrentUser.OpenSubKey("Software\\RegisteredApplications")) {
                    if (registeredApps == null) {
                        return false;
                    }

                    string existingPath = registeredApps.GetValue(RegisteredAppName) as string;
                    return string.Equals(existingPath, CapabilitiesPath, StringComparison.OrdinalIgnoreCase);
                }
            } catch (Exception ex) {
                Debug.WriteLine(string.Format(Language.Lang.InitialSetup_ReadRegisteredAppsFailed, ex));
                return false;
            }
        }

		private static void RegisterDefaultAppCapabilities() {
			try {
				string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
				if (string.IsNullOrWhiteSpace(exePath))
					return;

				string appIcon = $"{exePath},0";
				string openCommand = $"\"{exePath}\" \"%1\"";

				using (RegistryKey classesRoot = Registry.CurrentUser.CreateSubKey("Software\\Classes")) {
					if (classesRoot == null)
						return;

					using (RegistryKey progIdKey = classesRoot.CreateSubKey(LoxoneProgId)) {
						progIdKey?.SetValue(null, "Loxone File", RegistryValueKind.String);
						using (RegistryKey defaultIconKey = progIdKey?.CreateSubKey("DefaultIcon")) {
							defaultIconKey?.SetValue(null, appIcon, RegistryValueKind.String);
						}
						using (RegistryKey shellKey = progIdKey?.CreateSubKey("shell\\open\\command")) {
							shellKey?.SetValue(null, openCommand, RegistryValueKind.String);
						}
					}

					using (RegistryKey loxoneExtKey = classesRoot.CreateSubKey(".loxone")) {
						using (RegistryKey openWith = loxoneExtKey?.CreateSubKey("OpenWithProgids")) {
							openWith?.SetValue(LoxoneProgId, string.Empty, RegistryValueKind.String);
						}
					}

					using (RegistryKey loxoneBackupExtKey = classesRoot.CreateSubKey(".loxone.backup")) {
						using (RegistryKey openWith = loxoneBackupExtKey?.CreateSubKey("OpenWithProgids")) {
							openWith?.SetValue(LoxoneProgId, string.Empty, RegistryValueKind.String);
						}
					}
				}

				using (RegistryKey capabilitiesKey = Registry.CurrentUser.CreateSubKey(CapabilitiesPath)) {
					if (capabilitiesKey == null)
						return;

					capabilitiesKey.SetValue("ApplicationName", RegisteredAppName, RegistryValueKind.String);
					capabilitiesKey.SetValue("ApplicationDescription", RegisteredAppName, RegistryValueKind.String);
					capabilitiesKey.SetValue("ApplicationIcon", appIcon, RegistryValueKind.String);

					using (RegistryKey fileAssociations = capabilitiesKey.CreateSubKey("FileAssociations")) {
						fileAssociations?.SetValue(".loxone", LoxoneProgId, RegistryValueKind.String);
						fileAssociations?.SetValue(".loxone.backup", LoxoneProgId, RegistryValueKind.String);
					}
				}

				using (RegistryKey registeredApps = Registry.CurrentUser.CreateSubKey("Software\\RegisteredApplications")) {
					registeredApps?.SetValue(RegisteredAppName, CapabilitiesPath, RegistryValueKind.String);
				}
			}
			catch (Exception ex) {
                Debug.WriteLine(string.Format(Language.Lang.InitialSetup_RegisterCapabilitiesFailed, ex));
			}
		}

#if DELETEREGISTRYENTRIES || DEBUG
        public static void FirstStartSetup() {
            DeleteRegistryEntries();
			Properties.Settings.Default.projectsFolderPath = $"";
			Properties.Settings.Default.SavedPaths = $"";
			Properties.Settings.Default.Save();

		}
        public static void DeleteRegistryEntries() {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registryEntryPath)) {

                try {
                    Registry.CurrentUser.DeleteSubKeyTree(registryEntryPath, throwOnMissingSubKey: false);
                } finally {

                }
            }
		}
#endif
    }
}
