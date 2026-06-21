using System;
using System.Text.RegularExpressions;

namespace LoxTools.AppUpdates {
    internal enum AppUpdateChannel {
        Stable = 0,
        Beta = 1,
        Alpha = 2
    }

    internal enum AppReleaseStage {
        Alpha = 0,
        Beta = 1,
        ReleaseCandidate = 2,
        Stable = 3
    }

    internal readonly struct AppSemanticVersion : IComparable<AppSemanticVersion>, IEquatable<AppSemanticVersion> {
        private static readonly Regex Pattern = new Regex(
            @"^v?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<stage>alpha|beta|rc)\.(?<number>0|[1-9]\d*))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public AppReleaseStage Stage { get; }
        public int PrereleaseNumber { get; }

        public AppSemanticVersion(int major, int minor, int patch, AppReleaseStage stage, int prereleaseNumber) {
            Major = major;
            Minor = minor;
            Patch = patch;
            Stage = stage;
            PrereleaseNumber = prereleaseNumber;
        }

        public bool IsEligibleFor(AppUpdateChannel channel) {
            if (channel == AppUpdateChannel.Stable) {
                return Stage == AppReleaseStage.Stable;
            }

            if (channel == AppUpdateChannel.Beta) {
                return Stage != AppReleaseStage.Alpha;
            }

            return true;
        }

        public int CompareTo(AppSemanticVersion other) {
            int result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;
            result = Stage.CompareTo(other.Stage);
            if (result != 0) return result;
            return Stage == AppReleaseStage.Stable ? 0 : PrereleaseNumber.CompareTo(other.PrereleaseNumber);
        }

        public bool Equals(AppSemanticVersion other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is AppSemanticVersion other && Equals(other);
        public override int GetHashCode() {
            unchecked {
                int hash = Major;
                hash = (hash * 397) ^ Minor;
                hash = (hash * 397) ^ Patch;
                hash = (hash * 397) ^ (int)Stage;
                hash = (hash * 397) ^ PrereleaseNumber;
                return hash;
            }
        }

        public override string ToString() {
            string core = $"{Major}.{Minor}.{Patch}";
            if (Stage == AppReleaseStage.Stable) return core;
            string label = Stage == AppReleaseStage.Alpha ? "alpha" : Stage == AppReleaseStage.Beta ? "beta" : "rc";
            return $"{core}-{label}.{PrereleaseNumber}";
        }

        public static bool TryParse(string value, out AppSemanticVersion version) {
            version = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string normalized = value.Trim();
            int metadataIndex = normalized.IndexOf('+');
            if (metadataIndex >= 0) normalized = normalized.Substring(0, metadataIndex);

            Match match = Pattern.Match(normalized);
            if (!match.Success) return false;

            if (!int.TryParse(match.Groups["major"].Value, out int major)
                || !int.TryParse(match.Groups["minor"].Value, out int minor)
                || !int.TryParse(match.Groups["patch"].Value, out int patch)) {
                return false;
            }

            AppReleaseStage stage = AppReleaseStage.Stable;
            int number = 0;
            if (match.Groups["stage"].Success) {
                string label = match.Groups["stage"].Value.ToLowerInvariant();
                stage = label == "alpha" ? AppReleaseStage.Alpha
                    : label == "beta" ? AppReleaseStage.Beta
                    : AppReleaseStage.ReleaseCandidate;
                if (!int.TryParse(match.Groups["number"].Value, out number)) return false;
            }

            version = new AppSemanticVersion(major, minor, patch, stage, number);
            return true;
        }
    }
}
