using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LoxTools.UI.Helpers {
	internal static class VersionDisplayHelper {
		public const bool SortByFileVersionOnly = true;

		public static Tuple<string, string> ReadExeVersions(string exePath) {
			string fileVersion = string.Empty;
			string productVersion = string.Empty;

			try {
				if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath)) {
					FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
					fileVersion = info.FileVersion ?? string.Empty;
					productVersion = info.ProductVersion ?? string.Empty;
				}
			} catch {
				fileVersion = string.Empty;
				productVersion = string.Empty;
			}

			return Tuple.Create(fileVersion, productVersion);
		}

		public static string BuildDisplayName(string baseName, string fileVersion, string productVersion) {
			string name = baseName ?? string.Empty;
			string version = GetDisplayVersion(fileVersion, productVersion);
			if (string.IsNullOrWhiteSpace(version)) {
				return name;
			}

			if (NameContainsVersion(name, version)) {
				return name;
			}

			return $"{name}  ({version})";
		}

		public static string GetDisplayVersion(string fileVersion, string productVersion) {
			return string.IsNullOrWhiteSpace(productVersion) ? (fileVersion ?? string.Empty) : productVersion;
		}

		public static Version GetSortVersion(string fileVersion, string productVersion, bool useFileVersionOnly) {
			string source = useFileVersionOnly ? (fileVersion ?? string.Empty) : GetDisplayVersion(fileVersion, productVersion);
			return ParseDisplayVersion(source);
		}

		public static Version ParseDisplayVersion(string version) {
			if (string.IsNullOrWhiteSpace(version)) {
				return null;
			}

			Match match = Regex.Match(version, @"\d+(\.\d+){1,3}");
			if (!match.Success) {
				return null;
			}

			string normalized = NormalizeVersion(match.Value);
			if (string.IsNullOrWhiteSpace(normalized)) {
				return null;
			}

			if (Version.TryParse(normalized, out Version parsed)) {
				return parsed;
			}

			return null;
		}

		private static bool NameContainsVersion(string name, string version) {
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) {
				return false;
			}

			string normalizedTarget = NormalizeVersion(version);
			if (string.IsNullOrWhiteSpace(normalizedTarget)) {
				return false;
			}

			foreach (Match match in Regex.Matches(name, @"\d+(\.\d+){1,3}")) {
				string normalizedNameVersion = NormalizeVersion(match.Value);
				if (string.Equals(normalizedNameVersion, normalizedTarget, StringComparison.OrdinalIgnoreCase)) {
					return true;
				}
			}

			if (TryParseVersionParts(normalizedTarget, out int[] parts)) {
				string digitsOnlyName = Regex.Replace(name, @"\D", string.Empty);
				if (!string.IsNullOrEmpty(digitsOnlyName)) {
					string concatNoPad = string.Concat(parts.Select(p => p.ToString()));
					string concatPad2 = string.Concat(parts.Select(p => p.ToString("00")));
					if (digitsOnlyName.Contains(concatNoPad, StringComparison.OrdinalIgnoreCase)
						|| digitsOnlyName.Contains(concatPad2, StringComparison.OrdinalIgnoreCase)) {
						return true;
					}
				}
			}

			return false;
		}

		private static string NormalizeVersion(string version) {
			if (string.IsNullOrWhiteSpace(version)) {
				return string.Empty;
			}

			string[] parts = version.Trim().Split('.');
			for (int i = 0; i < parts.Length; i++) {
				if (int.TryParse(parts[i], out int value)) {
					parts[i] = value.ToString();
				} else {
					return version.Trim();
				}
			}

			return string.Join(".", parts);
		}

		private static bool TryParseVersionParts(string version, out int[] parts) {
			parts = Array.Empty<int>();
			if (string.IsNullOrWhiteSpace(version)) {
				return false;
			}

			string[] tokens = version.Split('.');
			var parsed = new int[tokens.Length];
			for (int i = 0; i < tokens.Length; i++) {
				if (!int.TryParse(tokens[i], out int value)) {
					parts = Array.Empty<int>();
					return false;
				}
				parsed[i] = value;
			}

			parts = parsed;
			return parts.Length > 0;
		}

		public static bool TryGetBuildIdFromExe(string exePath, out string buildId) {
			buildId = null;
			var versions = ReadExeVersions(exePath);
			return TryGetBuildId(versions.Item1, versions.Item2, out buildId);
		}

		public static bool TryGetBuildId(string fileVersion, string productVersion, out string buildId) {
			buildId = null;
			string display = GetDisplayVersion(fileVersion, productVersion);
			if (string.IsNullOrWhiteSpace(display)) {
				return false;
			}

			Match match = Regex.Match(display, @"\d+(\.\d+){1,3}");
			if (!match.Success) {
				return false;
			}

			string[] parts = match.Value.Split('.');
			var numbers = new int[4];
			for (int i = 0; i < numbers.Length; i++) {
				if (i >= parts.Length) {
					numbers[i] = 0;
					continue;
				}

				if (!int.TryParse(parts[i], out int value) || value < 0 || value > 99) {
					return false;
				}

				numbers[i] = value;
			}

			buildId = string.Concat(numbers.Select(n => n.ToString("00")));
			return buildId.Length == 8;
		}
	}
}
