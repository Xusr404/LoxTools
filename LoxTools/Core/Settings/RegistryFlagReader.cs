using Microsoft.Win32;
using System;

namespace LoxTools.Core.Settings {
    internal static class RegistryFlagReader {
        private const string SettingsRegistryPath = "SOFTWARE\\LoxConfigDownloader";
        private const string LegacySettingsRegistryPath = "SOFTWARE\\ConfigVersionSelector\\Settings";

        public static bool GetDwordFlag(string valueName, bool defaultValue = false) {
            if (string.IsNullOrWhiteSpace(valueName)) {
                return defaultValue;
            }

            try {
                if (TryGetDwordFlag(SettingsRegistryPath, valueName, out bool flag)) {
                    return flag;
                }

                if (TryGetDwordFlag(LegacySettingsRegistryPath, valueName, out flag)) {
                    return flag;
                }

                return defaultValue;
            } catch {
                return defaultValue;
            }
        }

        private static bool TryGetDwordFlag(string registryPath, string valueName, out bool flag) {
            flag = false;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, false)) {
                if (key == null) {
                    return false;
                }

                object value = key.GetValue(valueName);
                if (value == null) {
                    return false;
                }

                if (value is int i) {
                    flag = i == 1;
                    return true;
                }

                if (value is string s && int.TryParse(s, out int parsed)) {
                    flag = parsed == 1;
                    return true;
                }

                return false;
            }
        }

        public static bool SetDwordFlag(string valueName, bool value) {
            if (string.IsNullOrWhiteSpace(valueName)) {
                return false;
            }

            try {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath)) {
                    if (key == null) {
                        return false;
                    }

                    key.SetValue(valueName, value ? 1 : 0, RegistryValueKind.DWord);
                    return true;
                }
            } catch {
                return false;
            }
        }

        public static int GetDwordValue(string valueName, int defaultValue = 0) {
            if (string.IsNullOrWhiteSpace(valueName)) {
                return defaultValue;
            }

            try {
                if (TryGetDwordValue(SettingsRegistryPath, valueName, out int value)) {
                    return value;
                }

                if (TryGetDwordValue(LegacySettingsRegistryPath, valueName, out value)) {
                    return value;
                }

                return defaultValue;
            } catch {
                return defaultValue;
            }
        }

        private static bool TryGetDwordValue(string registryPath, string valueName, out int value) {
            value = 0;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, false)) {
                if (key == null) {
                    return false;
                }

                object rawValue = key.GetValue(valueName);
                if (rawValue == null) {
                    return false;
                }

                if (rawValue is int intValue) {
                    value = intValue;
                    return true;
                }

                if (rawValue is string stringValue && int.TryParse(stringValue, out int parsedValue)) {
                    value = parsedValue;
                    return true;
                }

                return false;
            }
        }

        public static bool SetDwordValue(string valueName, int value) {
            if (string.IsNullOrWhiteSpace(valueName)) {
                return false;
            }

            try {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath)) {
                    if (key == null) {
                        return false;
                    }

                    key.SetValue(valueName, value, RegistryValueKind.DWord);
                    return true;
                }
            } catch {
                return false;
            }
        }
    }
}
