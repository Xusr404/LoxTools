using IWshRuntimeLibrary; // Add reference to Windows Script Host Object Model
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;

namespace LoxTools.Core.Startup {
    static class AutoStartHelper {
        private const string regestryEntryName = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string registryValue = "LoxTools";
        private const string legacyRegistryValue = "ConfigVersionSelector";
        private static RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(regestryEntryName, true);

        public static void RegisterInStartup(bool isChecked) {
            bool entryExists = registryKey.GetValueNames().Contains(registryValue);
            RegistryKey key = Registry.CurrentUser.OpenSubKey(regestryEntryName, true);

            if (isChecked != entryExists) {
                if(isChecked) {
                    AddToAutostart(key, registryValue);
                }else {
                    RemoveFromAutostart(key, registryValue);
                }
            }

            RemoveFromAutostart(key, legacyRegistryValue);
        }

        private static void AddToAutostart(RegistryKey key, string appName) {
            string exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) {
                return;
            }

            key.SetValue(appName, $"\"{exePath}\"");
        }

        private static void RemoveFromAutostart(RegistryKey key, string appName) {
            if (key.GetValue(appName) != null)
                key.DeleteValue(appName);
        }
    }
}
