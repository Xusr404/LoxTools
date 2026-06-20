using System;
using System.IO;
using System.IO.Compression;

namespace LoxTools.UpdateCheck {
    internal sealed class SecureInstallerExtractor {
        private const int BufferSize = 128 * 1024;

        public string Extract(string archivePath, string destinationFolder) {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath)) {
                throw new UpdateSecurityException(UpdateValidationFailure.Archive, "The update archive is missing.");
            }
            if (string.IsNullOrWhiteSpace(destinationFolder)) {
                throw new ArgumentException("A destination folder is required.", nameof(destinationFolder));
            }

            Directory.CreateDirectory(destinationFolder);
            string installerPath = Path.Combine(destinationFolder, UpdateSecurityPolicy.InstallerFileName);

            try {
                using (ZipArchive archive = ZipFile.OpenRead(archivePath)) {
                    if (archive.Entries.Count != 1) {
                        throw new UpdateSecurityException(UpdateValidationFailure.Archive, "The update archive must contain exactly one file.");
                    }

                    ZipArchiveEntry entry = archive.Entries[0];
                    if (!string.Equals(entry.FullName, UpdateSecurityPolicy.InstallerFileName, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(entry.Name, UpdateSecurityPolicy.InstallerFileName, StringComparison.OrdinalIgnoreCase)
                        || entry.Length <= 0
                        || entry.Length > UpdateSecurityPolicy.MaxPackageBytes
                        || IsLinkOrReparsePoint(entry)) {
                        throw new UpdateSecurityException(UpdateValidationFailure.Archive, "The update archive has an unexpected installer entry.");
                    }

                    using (Stream input = entry.Open())
                    using (var output = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: false)) {
                        var buffer = new byte[BufferSize];
                        long totalBytes = 0;
                        int bytesRead;
                        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0) {
                            totalBytes += bytesRead;
                            if (totalBytes > entry.Length || totalBytes > UpdateSecurityPolicy.MaxPackageBytes) {
                                throw new UpdateSecurityException(UpdateValidationFailure.Archive, "The extracted installer exceeded its declared or maximum size.");
                            }

                            output.Write(buffer, 0, bytesRead);
                        }

                        if (totalBytes != entry.Length) {
                            throw new UpdateSecurityException(UpdateValidationFailure.Archive, "The extracted installer size is invalid.");
                        }
                    }
                }

                return installerPath;
            } catch (UpdateSecurityException) {
                TryDelete(installerPath);
                throw;
            } catch (Exception ex) {
                TryDelete(installerPath);
                throw new UpdateSecurityException(UpdateValidationFailure.Archive, "The update archive could not be validated.", ex);
            }
        }

        private static bool IsLinkOrReparsePoint(ZipArchiveEntry entry) {
            int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            int windowsAttributes = entry.ExternalAttributes & 0xFFFF;
            return unixFileType == 0xA000
                || (windowsAttributes & (int)FileAttributes.ReparsePoint) != 0;
        }

        private static void TryDelete(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch {
            }
        }
    }
}
