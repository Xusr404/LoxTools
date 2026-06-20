using LoxTools.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.UpdateCheck {
    internal sealed class UpdateInstallerPipeline {
        private readonly BoundedFileDownloader downloader;
        private readonly SecureInstallerExtractor extractor;
        private readonly IInstallerSignatureVerifier signatureVerifier;

        public UpdateInstallerPipeline(HttpClient httpClient, IInstallerSignatureVerifier signatureVerifier) {
            downloader = new BoundedFileDownloader(httpClient);
            extractor = new SecureInstallerExtractor();
            this.signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        }

        public async Task<string> PrepareAsync(UpdateInfo target, string workingFolder, CancellationToken cancellationToken) {
            if (target == null) {
                throw new ArgumentNullException(nameof(target));
            }
            if (string.IsNullOrWhiteSpace(workingFolder)) {
                throw new ArgumentException("A working folder is required.", nameof(workingFolder));
            }

            Directory.CreateDirectory(workingFolder);
            string archivePath = Path.Combine(workingFolder, "LoxoneConfigUpdate.zip");
            await downloader.DownloadAsync(target.DownloadUri, archivePath, target.FileSize, cancellationToken).ConfigureAwait(false);

            string actualCrc;
            using (var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                actualCrc = Crc32Helper.ComputeCrc32(fileStream).ToString("x8");
            }

            if (!string.Equals(target.Crc32Hex, actualCrc, StringComparison.OrdinalIgnoreCase)) {
                throw new UpdateSecurityException(UpdateValidationFailure.Crc32, "The update package CRC32 check failed.");
            }

            string extractFolder = Path.Combine(workingFolder, "extracted");
            string installerPath = extractor.Extract(archivePath, extractFolder);
            UpdateValidationFailure signatureResult = signatureVerifier.Verify(installerPath);
            if (signatureResult != UpdateValidationFailure.None) {
                throw new UpdateSecurityException(signatureResult, "The update installer signature is not trusted.");
            }

            return installerPath;
        }
    }
}
