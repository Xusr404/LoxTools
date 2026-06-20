using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.UpdateCheck {
    internal sealed class UpdateCheckXmlClient {
        private const int BufferSize = 16 * 1024;
        private readonly HttpClient httpClient;

        public UpdateCheckXmlClient(HttpClient httpClient) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> DownloadUpdateCheckXmlAsync(CancellationToken ct) {
            using (var request = new HttpRequestMessage(HttpMethod.Get, UpdateSecurityPolicy.MetadataUri))
            using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false)) {
                UpdateSecurityPolicy.EnsureSuccessfulResponse(response);

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > UpdateSecurityPolicy.MaxMetadataBytes) {
                    throw new UpdateSecurityException(UpdateValidationFailure.Metadata, "The update metadata response is too large.");
                }

                using (Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                using (var output = new MemoryStream()) {
                    var buffer = new byte[BufferSize];
                    long totalBytes = 0;
                    int bytesRead;
                    while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0) {
                        totalBytes += bytesRead;
                        if (totalBytes > UpdateSecurityPolicy.MaxMetadataBytes) {
                            throw new UpdateSecurityException(UpdateValidationFailure.Metadata, "The update metadata response exceeded the maximum size.");
                        }

                        output.Write(buffer, 0, bytesRead);
                    }

                    return Encoding.UTF8.GetString(output.ToArray());
                }
            }
        }
    }
}
