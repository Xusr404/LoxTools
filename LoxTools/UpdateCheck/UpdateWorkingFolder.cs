using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LoxTools.UpdateCheck {
    internal sealed class UpdateWorkingFolder : IDisposable {
        private const string MarkerName = ".active";
        private FileStream lease;
        internal string Path { get; }

        internal UpdateWorkingFolder(string root) {
            Path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            lease = new FileStream(System.IO.Path.Combine(Path, MarkerName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        }

        internal void TrackInstaller(Process process) {
            // Persist identity as well as holding a lock: the installer may survive an app crash.
            long started = -1;
            try { started = process.StartTime.ToUniversalTime().Ticks; }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            using (var writer = new StreamWriter(lease, System.Text.Encoding.UTF8, 1024, leaveOpen: true)) {
                writer.Write(process.Id.ToString(CultureInfo.InvariantCulture) + "|" + started.ToString(CultureInfo.InvariantCulture));
                writer.Flush();
                lease.Flush(true);
            }
        }

        internal void DeleteAfterExit() {
            Dispose();
            TryDelete(Path);
        }

        public void Dispose() {
            lease?.Dispose();
            lease = null;
        }

        internal static void Cleanup(string root, TimeSpan minimumAge) {
            if (!Directory.Exists(root)) return;
            try {
                foreach (string folder in Directory.GetDirectories(root)) {
                    // Never delete unrelated folders or follow redirected working folders.
                    if (!Guid.TryParseExact(System.IO.Path.GetFileName(folder), "N", out _)) continue;
                    try {
                        var info = new DirectoryInfo(folder);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                            || DateTime.UtcNow - info.LastWriteTimeUtc < minimumAge) continue;
                        string marker = System.IO.Path.Combine(folder, MarkerName);
                        if (File.Exists(marker)) {
                            using (var exclusive = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.None))
                            using (var reader = new StreamReader(exclusive)) {
                                string identity = reader.ReadToEnd();
                                if (!string.IsNullOrEmpty(identity) && MayBeRunning(identity)) continue;
                            }
                        }
                        TryDelete(folder);
                    } catch (IOException) { /* An active instance holds the lease. */ }
                    catch (UnauthorizedAccessException) { /* Leave inaccessible files intact. */ }
                }
            } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool MayBeRunning(string identity) {
            string[] parts = identity.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int pid) || !long.TryParse(parts[1], out long started)) return true;
            try {
                using (Process process = Process.GetProcessById(pid)) {
                    return !process.HasExited && (started == -1 || process.StartTime.ToUniversalTime().Ticks == started);
                }
            } catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return true; }
        }

        private static void TryDelete(string folder) {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { UpdateLog.Write("Working folder cleanup deferred: files are still in use."); }
            catch (UnauthorizedAccessException) { UpdateLog.Write("Working folder cleanup deferred: access denied."); }
        }
    }
}
