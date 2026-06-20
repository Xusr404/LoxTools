using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.Tests {
    internal sealed class TestHttpMessageHandler : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFactory;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) {
            this.responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }

    internal sealed class UnknownLengthContent : HttpContent {
        private readonly byte[] content;

        public UnknownLengthContent(byte[] content) {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context) {
            return stream.WriteAsync(content, 0, content.Length);
        }

        protected override bool TryComputeLength(out long length) {
            length = 0;
            return false;
        }
    }

    internal sealed class TemporaryDirectory : IDisposable {
        public string Path { get; }

        public TemporaryDirectory() {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LoxTools.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose() {
            try {
                if (Directory.Exists(Path)) {
                    Directory.Delete(Path, recursive: true);
                }
            } catch {
            }
        }
    }
}
