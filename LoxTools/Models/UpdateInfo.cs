using System;

namespace LoxTools.Models {
    public enum UpdateChannel {
        Release,
        Beta,
        Alpha
    }

    internal readonly struct UpdateVersion : IComparable<UpdateVersion>, IEquatable<UpdateVersion> {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public int Build { get; }

        public UpdateVersion(int major, int minor, int patch, int build) {
            Major = major;
            Minor = minor;
            Patch = patch;
            Build = build;
        }

        public int CompareTo(UpdateVersion other) {
            int result = Major.CompareTo(other.Major);
            if (result != 0) {
                return result;
            }

            result = Minor.CompareTo(other.Minor);
            if (result != 0) {
                return result;
            }

            result = Patch.CompareTo(other.Patch);
            if (result != 0) {
                return result;
            }

            return Build.CompareTo(other.Build);
        }

        public bool Equals(UpdateVersion other) {
            return Major == other.Major
                && Minor == other.Minor
                && Patch == other.Patch
                && Build == other.Build;
        }

        public override bool Equals(object obj) {
            return obj is UpdateVersion other && Equals(other);
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = (hash * 31) + Major.GetHashCode();
                hash = (hash * 31) + Minor.GetHashCode();
                hash = (hash * 31) + Patch.GetHashCode();
                hash = (hash * 31) + Build.GetHashCode();
                return hash;
            }
        }

        public override string ToString() {
            return $"{Major}.{Minor}.{Patch}.{Build}";
        }

        public static bool TryParse(string versionString, out UpdateVersion version) {
            version = default;
            if (string.IsNullOrWhiteSpace(versionString)) {
                return false;
            }

            string[] parts = versionString.Trim().Split('.');
            if (parts.Length != 4) {
                return false;
            }

            if (!int.TryParse(parts[0], out int major)) {
                return false;
            }

            if (!int.TryParse(parts[1], out int minor)) {
                return false;
            }

            if (!int.TryParse(parts[2], out int patch)) {
                return false;
            }

            if (!int.TryParse(parts[3], out int build)) {
                return false;
            }

            version = new UpdateVersion(major, minor, patch, build);
            return true;
        }

        public static bool TryParseBuildId(string buildId, out UpdateVersion version) {
            version = default;
            if (string.IsNullOrWhiteSpace(buildId) || buildId.Length != 8) {
                return false;
            }

            string normalized = buildId.Trim();
            if (!int.TryParse(normalized.Substring(0, 2), out int major)) {
                return false;
            }

            if (!int.TryParse(normalized.Substring(2, 2), out int minor)) {
                return false;
            }

            if (!int.TryParse(normalized.Substring(4, 2), out int patch)) {
                return false;
            }

            if (!int.TryParse(normalized.Substring(6, 2), out int build)) {
                return false;
            }

            version = new UpdateVersion(major, minor, patch, build);
            return true;
        }
    }

    internal sealed class UpdateInfo {
        public UpdateChannel Channel { get; }
        public string VersionString { get; }
        public UpdateVersion Version { get; }
        public Uri DownloadUri { get; }
        public string Crc32Hex { get; }
        public long FileSize { get; }

        public UpdateInfo(UpdateChannel channel, string versionString, UpdateVersion version, Uri downloadUri, string crc32Hex, long fileSize) {
            Channel = channel;
            VersionString = versionString;
            Version = version;
            DownloadUri = downloadUri ?? throw new ArgumentNullException(nameof(downloadUri));
            Crc32Hex = crc32Hex;
            FileSize = fileSize;
        }
    }
}
