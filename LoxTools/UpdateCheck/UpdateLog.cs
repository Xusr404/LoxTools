using System;
using System.IO;

namespace LoxTools.UpdateCheck {
    internal static class UpdateLog {
        private static readonly object Gate = new object();
        internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoxTools", "Updates");
        internal static void Write(string message) {
            lock (Gate) {
                try {
                    Directory.CreateDirectory(Folder);
                    File.AppendAllText(Path.Combine(Folder, "update.log"), DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
