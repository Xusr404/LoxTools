using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace LoxTools.UpdateCheck {
    internal interface IInstallerSignatureVerifier {
        UpdateValidationFailure Verify(string installerPath);
    }

    internal sealed class WindowsInstallerSignatureVerifier : IInstallerSignatureVerifier {
        private const string ExpectedOrganization = "Loxone Electronics GmbH";
        private const string ExpectedCountry = "AT";
        private static readonly Guid GenericVerifyV2Action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public UpdateValidationFailure Verify(string installerPath) {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath) || !HasValidAuthenticodeSignature(installerPath)) {
                return UpdateValidationFailure.Signature;
            }

            try {
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(installerPath))) {
                    return HasExpectedPublisher(certificate)
                        ? UpdateValidationFailure.None
                        : UpdateValidationFailure.Publisher;
                }
            } catch {
                return UpdateValidationFailure.Signature;
            }
        }

        internal static bool HasExpectedPublisher(X509Certificate2 certificate) {
            if (certificate == null) {
                return false;
            }

            string decodedSubject = certificate.SubjectName.Decode(
                X500DistinguishedNameFlags.UseNewLines | X500DistinguishedNameFlags.DoNotUseQuotes);
            string organization = null;
            string country = null;

            foreach (string rawLine in decodedSubject.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                int separatorIndex = rawLine.IndexOf('=');
                if (separatorIndex <= 0) {
                    continue;
                }

                string key = rawLine.Substring(0, separatorIndex).Trim();
                string value = rawLine.Substring(separatorIndex + 1).Trim();
                if (string.Equals(key, "O", StringComparison.OrdinalIgnoreCase)) {
                    organization = value;
                } else if (string.Equals(key, "C", StringComparison.OrdinalIgnoreCase)) {
                    country = value;
                }
            }

            return string.Equals(organization, ExpectedOrganization, StringComparison.Ordinal)
                && string.Equals(country, ExpectedCountry, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasValidAuthenticodeSignature(string filePath) {
            IntPtr filePathPointer = IntPtr.Zero;
            IntPtr fileInfoPointer = IntPtr.Zero;
            IntPtr trustDataPointer = IntPtr.Zero;

            try {
                filePathPointer = Marshal.StringToCoTaskMemUni(filePath);
                var fileInfo = new WinTrustFileInfo {
                    StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                    FilePath = filePathPointer
                };
                fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

                var trustData = new WinTrustData {
                    StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                    UiChoice = 2,
                    RevocationChecks = 1,
                    UnionChoice = 1,
                    FileInfo = fileInfoPointer,
                    StateAction = 0,
                    ProviderFlags = 0x00000080,
                    UiContext = 0
                };
                trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
                Marshal.StructureToPtr(trustData, trustDataPointer, fDeleteOld: false);

                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2Action, trustDataPointer) == 0;
            } catch {
                return false;
            } finally {
                if (trustDataPointer != IntPtr.Zero) {
                    Marshal.FreeHGlobal(trustDataPointer);
                }
                if (fileInfoPointer != IntPtr.Zero) {
                    Marshal.FreeHGlobal(fileInfoPointer);
                }
                if (filePathPointer != IntPtr.Zero) {
                    Marshal.FreeCoTaskMem(filePathPointer);
                }
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
        private static extern uint WinVerifyTrust(
            IntPtr windowHandle,
            [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
            IntPtr trustData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo {
            public uint StructSize;
            public IntPtr FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }
    }
}
