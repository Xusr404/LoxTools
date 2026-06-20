using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class BoundedFileDownloaderTests {
        private static readonly Uri PackageUri = new Uri("https://updatefiles.loxone.com/LoxConfig/update.zip");

        [TestMethod]
        public async Task DownloadWritesOnlyTheDeclaredNumberOfBytes() {
            byte[] content = { 1, 2, 3, 4 };
            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(new ByteArrayContent(content))) {
                string path = Path.Combine(temp.Path, "update.zip");
                var downloader = new BoundedFileDownloader(client);

                await downloader.DownloadAsync(PackageUri, path, content.Length, CancellationToken.None);

                CollectionAssert.AreEqual(content, File.ReadAllBytes(path));
            }
        }

        [TestMethod]
        public async Task DownloadRejectsContentLengthMismatchAndDeletesPartialFile() {
            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(new ByteArrayContent(new byte[] { 1, 2, 3 }))) {
                string path = Path.Combine(temp.Path, "update.zip");
                var downloader = new BoundedFileDownloader(client);

                UpdateSecurityException exception = await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => downloader.DownloadAsync(PackageUri, path, 4, CancellationToken.None));

                Assert.AreEqual(UpdateValidationFailure.Size, exception.Failure);
                Assert.IsFalse(File.Exists(path));
            }
        }

        [TestMethod]
        public async Task DownloadRejectsUnknownLengthOverrunAndDeletesPartialFile() {
            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(new UnknownLengthContent(new byte[] { 1, 2, 3, 4, 5 }))) {
                string path = Path.Combine(temp.Path, "update.zip");
                var downloader = new BoundedFileDownloader(client);

                UpdateSecurityException exception = await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => downloader.DownloadAsync(PackageUri, path, 4, CancellationToken.None));

                Assert.AreEqual(UpdateValidationFailure.Size, exception.Failure);
                Assert.IsFalse(File.Exists(path));
            }
        }

        [TestMethod]
        public async Task DownloadRejectsUnknownLengthTruncationAndDeletesPartialFile() {
            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(new UnknownLengthContent(new byte[] { 1, 2, 3 }))) {
                string path = Path.Combine(temp.Path, "update.zip");
                var downloader = new BoundedFileDownloader(client);

                UpdateSecurityException exception = await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => downloader.DownloadAsync(PackageUri, path, 4, CancellationToken.None));

                Assert.AreEqual(UpdateValidationFailure.Size, exception.Failure);
                Assert.IsFalse(File.Exists(path));
            }
        }

        [TestMethod]
        public async Task DownloadCancellationDoesNotLeaveAPartialFile() {
            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(new UnknownLengthContent(new byte[] { 1, 2, 3 })))
            using (var cancellation = new CancellationTokenSource()) {
                string path = Path.Combine(temp.Path, "update.zip");
                var downloader = new BoundedFileDownloader(client);
                cancellation.Cancel();

                await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                    () => downloader.DownloadAsync(PackageUri, path, 3, cancellation.Token));

                Assert.IsFalse(File.Exists(path));
            }
        }

        [TestMethod]
        public async Task DownloadRejectsRedirects() {
            using (var temp = new TemporaryDirectory())
            using (var client = new HttpClient(new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)))) {
                var downloader = new BoundedFileDownloader(client);

                UpdateSecurityException exception = await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => downloader.DownloadAsync(PackageUri, Path.Combine(temp.Path, "update.zip"), 1, CancellationToken.None));

                Assert.AreEqual(UpdateValidationFailure.Redirect, exception.Failure);
            }
        }

        private static HttpClient CreateClient(HttpContent content) {
            return new HttpClient(new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = content
            })) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }
}
