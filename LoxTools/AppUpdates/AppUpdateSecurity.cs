using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LoxTools.AppUpdates {
    internal interface IAppInstallerVerifier {
        bool IsExecutionConfigured { get; }
        bool Verify(string installerPath, string checksumFilePath, string expectedFileName);
    }

    internal sealed class AppInstallerVerifier : IAppInstallerVerifier {
        private static readonly Guid GenericVerifyV2Action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private readonly string expectedPublisher;

        public AppInstallerVerifier() : this(ReadExpectedPublisher()) { }
        internal AppInstallerVerifier(string expectedPublisher) {
            this.expectedPublisher = expectedPublisher?.Trim() ?? string.Empty;
        }

        public bool IsExecutionConfigured => !string.IsNullOrWhiteSpace(expectedPublisher);

        public bool Verify(string installerPath, string checksumFilePath, string expectedFileName) {
            if (!IsExecutionConfigured || !File.Exists(installerPath) || !File.Exists(checksumFilePath)) return false;
            if (!string.Equals(Path.GetFileName(installerPath), expectedFileName, StringComparison.Ordinal)) return false;
            if (!TryReadExpectedHash(checksumFilePath, expectedFileName, out string expectedHash)) return false;
            if (!string.Equals(ComputeSha256(installerPath), expectedHash, StringComparison.OrdinalIgnoreCase)) return false;
            if (!HasValidAuthenticodeSignature(installerPath)) return false;

            try {
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(installerPath))) {
                    string simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                    return string.Equals(simpleName, expectedPublisher, StringComparison.Ordinal);
                }
            } catch {
                return false;
            }
        }

        internal static bool TryReadExpectedHash(string checksumFilePath, string expectedFileName, out string hash) {
            hash = null;
            var pattern = new Regex(@"^(?<hash>[a-fA-F0-9]{64})\s{2}(?<name>[^\\/]+)$", RegexOptions.CultureInvariant);
            string[] lines;
            try { lines = File.ReadAllLines(checksumFilePath); } catch { return false; }

            var matches = lines.Select(line => pattern.Match(line.Trim()))
                .Where(match => match.Success && string.Equals(match.Groups["name"].Value, expectedFileName, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1) return false;
            hash = matches[0].Groups["hash"].Value.ToLowerInvariant();
            return true;
        }

        internal static string ComputeSha256(string filePath) {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create()) {
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
            }
        }

        private static string ReadExpectedPublisher() {
            return Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, "LoxToolsUpdatePublisher", StringComparison.Ordinal))?.Value;
        }

        private static bool HasValidAuthenticodeSignature(string filePath) {
            IntPtr filePathPointer = IntPtr.Zero;
            IntPtr fileInfoPointer = IntPtr.Zero;
            IntPtr trustDataPointer = IntPtr.Zero;
            try {
                filePathPointer = Marshal.StringToCoTaskMemUni(filePath);
                var fileInfo = new WinTrustFileInfo { StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = filePathPointer };
                fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
                var trustData = new WinTrustData {
                    StructSize = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = 2, RevocationChecks = 1,
                    UnionChoice = 1, FileInfo = fileInfoPointer, ProviderFlags = 0x00000080
                };
                trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
                Marshal.StructureToPtr(trustData, trustDataPointer, false);
                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2Action, trustDataPointer) == 0;
            } catch {
                return false;
            } finally {
                if (trustDataPointer != IntPtr.Zero) Marshal.FreeHGlobal(trustDataPointer);
                if (fileInfoPointer != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPointer);
                if (filePathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(filePathPointer);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern uint WinVerifyTrust(IntPtr windowHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo { public uint StructSize; public IntPtr FilePath; public IntPtr FileHandle; public IntPtr KnownSubject; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData {
            public uint StructSize; public IntPtr PolicyCallbackData; public IntPtr SipClientData; public uint UiChoice;
            public uint RevocationChecks; public uint UnionChoice; public IntPtr FileInfo; public uint StateAction;
            public IntPtr StateData; public IntPtr UrlReference; public uint ProviderFlags; public uint UiContext;
        }
    }

#if DEBUG
    internal sealed class DebugAppInstallerVerifier : IAppInstallerVerifier {
        public bool IsExecutionConfigured => true;

        public bool Verify(string installerPath, string checksumFilePath, string expectedFileName) {
            if (!File.Exists(installerPath) || !File.Exists(checksumFilePath)) return false;
            if (!string.Equals(Path.GetFileName(installerPath), expectedFileName, StringComparison.Ordinal)) return false;
            if (!AppInstallerVerifier.TryReadExpectedHash(checksumFilePath, expectedFileName, out string expectedHash)) return false;

            return string.Equals(
                AppInstallerVerifier.ComputeSha256(installerPath),
                expectedHash,
                StringComparison.OrdinalIgnoreCase);
        }
    }
#endif

    internal static class AppInstallationDetector {
        public static bool IsInstalledCopy() {
            string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LoxTools");
            string executableDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(executableDirectory, localPrograms, StringComparison.OrdinalIgnoreCase)) return true;

            try {
                using (RegistryKey uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")) {
                    if (uninstall == null) return false;
                    foreach (string keyName in uninstall.GetSubKeyNames()) {
                        using (RegistryKey entry = uninstall.OpenSubKey(keyName)) {
                            string displayName = entry?.GetValue("DisplayName") as string;
                            string installLocation = entry?.GetValue("InstallLocation") as string;
                            if (string.Equals(displayName, "LoxTools", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(installLocation)
                                && string.Equals(executableDirectory, installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) {
                                return true;
                            }
                        }
                    }
                }
            } catch { }
            return false;
        }
    }
}
