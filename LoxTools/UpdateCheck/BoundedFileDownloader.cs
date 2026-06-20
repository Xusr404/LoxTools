using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.UpdateCheck {
    internal sealed class BoundedFileDownloader {
        private const int BufferSize = 128 * 1024;
        private readonly HttpClient httpClient;

        public BoundedFileDownloader(HttpClient httpClient) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task DownloadAsync(Uri uri, string destinationPath, long expectedSize, CancellationToken cancellationToken) {
            if (uri == null) {
                throw new ArgumentNullException(nameof(uri));
            }
            if (string.IsNullOrWhiteSpace(destinationPath)) {
                throw new ArgumentException("A destination path is required.", nameof(destinationPath));
            }
            if (expectedSize <= 0 || expectedSize > UpdateSecurityPolicy.MaxPackageBytes) {
                throw new UpdateSecurityException(UpdateValidationFailure.Size, "The declared package size is outside the allowed range.");
            }

            try {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)) {
                    UpdateSecurityPolicy.EnsureSuccessfulResponse(response);

                    long? contentLength = response.Content.Headers.ContentLength;
                    if (contentLength.HasValue && contentLength.Value != expectedSize) {
                        throw new UpdateSecurityException(UpdateValidationFailure.Size, "The package content length does not match the declared size.");
                    }
                    if (response.Content.Headers.ContentEncoding.Any()) {
                        throw new UpdateSecurityException(UpdateValidationFailure.Size, "Encoded update package responses are not allowed.");
                    }

                    string parentDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrWhiteSpace(parentDirectory)) {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    using (var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true)) {
                        var buffer = new byte[BufferSize];
                        long totalBytes = 0;
                        int bytesRead;
                        while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0) {
                            totalBytes += bytesRead;
                            if (totalBytes > expectedSize || totalBytes > UpdateSecurityPolicy.MaxPackageBytes) {
                                throw new UpdateSecurityException(UpdateValidationFailure.Size, "The package exceeded its declared or maximum size.");
                            }

                            await output.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                        }

                        if (totalBytes != expectedSize) {
                            throw new UpdateSecurityException(UpdateValidationFailure.Size, "The downloaded package size does not match the declared size.");
                        }
                    }
                }
            } catch {
                TryDelete(destinationPath);
                throw;
            }
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
