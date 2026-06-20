using System;
using System.Configuration;

namespace LoxTools.Core.Settings {
    internal static class SettingsGuard {
        public static void EnsureReadable() {
            try {
                _ = Properties.Settings.Default;
            } catch (ConfigurationErrorsException ex) {
                TryReset(ex);
            }
        }

        public static void Execute(Action action, Action onReset = null) {
            try {
                action?.Invoke();
            } catch (ConfigurationErrorsException ex) {
                TryReset(ex);
                onReset?.Invoke();
            }
        }

        public static T Execute<T>(Func<T> func, T fallback = default) {
            try {
                return func != null ? func() : fallback;
            } catch (ConfigurationErrorsException ex) {
                TryReset(ex);
                return fallback;
            }
        }

        private static void TryReset(ConfigurationErrorsException ex) {
            try {
                Properties.Settings.Default.Reset();
                Properties.Settings.Default.Save();
            } catch {
            }
        }
    }
}
