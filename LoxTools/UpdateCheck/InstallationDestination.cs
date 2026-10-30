using LoxTools.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LoxTools.UpdateCheck {
    internal static class InstallationDestination {
        internal static string IncludeRoot(string savedPaths, string root) {
            string[] paths = (savedPaths ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            bool exists = paths.Any(path => string.Equals(
                Path.TrimEndingDirectorySeparator(path.Trim()),
                fullRoot, StringComparison.OrdinalIgnoreCase));
            return exists ? savedPaths : string.Join(",", paths.Concat(new[] { fullRoot }));
        }

        internal static string GetFolderName(UpdateInfo target, bool versioned) {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!Enum.IsDefined(typeof(UpdateChannel), target.Channel)) throw new ArgumentException("Invalid installation channel.");
            var v = target.Version;
            if (v.Major < 0 || v.Minor < 0 || v.Patch < 0 || v.Build < 0)
                throw new ArgumentException("Invalid installation version.");
            string build = string.Format(CultureInfo.InvariantCulture, "{0:D2}{1:D2}{2:D2}{3:D2}", v.Major, v.Minor, v.Patch, v.Build);
            return versioned ? $"LoxoneConfig_{build}_{target.Channel}" : $"LoxoneConfig_{target.Channel}";
        }

        internal static string GetPath(string root, UpdateInfo target, bool versioned) {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
                || root.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new ArgumentException("Installation root must be an absolute directory path.", nameof(root));
            string destination = Path.Combine(Path.GetFullPath(root), GetFolderName(target, versioned));
            if (File.Exists(destination)) throw new IOException("Installation destination is an existing file.");
            return destination;
        }

        internal static ProcessStartInfo CreateStartInfo(string installer, string destination, string logPath) {
            if (!Path.IsPathFullyQualified(installer) || !Path.IsPathFullyQualified(destination)
                || !Path.IsPathFullyQualified(logPath)
                || (installer + destination + logPath).IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new ArgumentException("Installer, destination and log paths must be absolute and safely quoted.");
            return new ProcessStartInfo {
                FileName = installer,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /DIR=\"{destination}\" /GROUP=\"{Path.GetFileName(destination)}\" /LOG=\"{logPath}\""
            };
        }
    }
}
