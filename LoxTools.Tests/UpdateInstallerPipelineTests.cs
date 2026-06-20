using LoxTools.Models;
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
    public sealed class UpdateInstallerPipelineTests {
        [TestMethod]
        public async Task PipelineReturnsInstallerOnlyAfterVerifierAcceptsIt() {
            byte[] archiveBytes = CreateArchiveBytes();
            UpdateInfo target = CreateTarget(archiveBytes);
            var verifier = new FakeSignatureVerifier(UpdateValidationFailure.None);

            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(archiveBytes)) {
                var pipeline = new UpdateInstallerPipeline(client, verifier);

                string installerPath = await pipeline.PrepareAsync(target, temp.Path, CancellationToken.None);

                Assert.IsTrue(verifier.WasCalled);
                Assert.IsTrue(File.Exists(installerPath));
            }
        }

        [TestMethod]
        public async Task PipelineFailsClosedWhenVerifierRejectsInstaller() {
            byte[] archiveBytes = CreateArchiveBytes();
            UpdateInfo target = CreateTarget(archiveBytes);
            var verifier = new FakeSignatureVerifier(UpdateValidationFailure.Signature);

            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(archiveBytes)) {
                var pipeline = new UpdateInstallerPipeline(client, verifier);

                UpdateSecurityException exception = await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => pipeline.PrepareAsync(target, temp.Path, CancellationToken.None));

                Assert.AreEqual(UpdateValidationFailure.Signature, exception.Failure);
                Assert.IsTrue(verifier.WasCalled);
            }
        }

        [TestMethod]
        public async Task WorkflowInvokesShortcutAndLauncherOnlyAfterVerificationSucceeds() {
            byte[] archiveBytes = CreateArchiveBytes();
            UpdateInfo target = CreateTarget(archiveBytes);
            var verifier = new FakeSignatureVerifier(UpdateValidationFailure.None);
            int shortcutCalls = 0;
            int launchCalls = 0;

            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(archiveBytes)) {
                var pipeline = new UpdateInstallerPipeline(client, verifier);
                var workflow = new UpdateInstallerWorkflow(
                    pipeline,
                    _ => shortcutCalls++,
                    _ => { launchCalls++; return null; });

                await workflow.ExecuteAsync(target, temp.Path, CancellationToken.None);

                Assert.AreEqual(1, shortcutCalls);
                Assert.AreEqual(1, launchCalls);
            }
        }

        [TestMethod]
        public async Task WorkflowDoesNotInvokeShortcutOrLauncherAfterVerificationFailure() {
            byte[] archiveBytes = CreateArchiveBytes();
            UpdateInfo target = CreateTarget(archiveBytes);
            var verifier = new FakeSignatureVerifier(UpdateValidationFailure.Publisher);
            int shortcutCalls = 0;
            int launchCalls = 0;

            using (var temp = new TemporaryDirectory())
            using (HttpClient client = CreateClient(archiveBytes)) {
                var pipeline = new UpdateInstallerPipeline(client, verifier);
                var workflow = new UpdateInstallerWorkflow(
                    pipeline,
                    _ => shortcutCalls++,
                    _ => { launchCalls++; return null; });

                await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => workflow.ExecuteAsync(target, temp.Path, CancellationToken.None));

                Assert.AreEqual(0, shortcutCalls);
                Assert.AreEqual(0, launchCalls);
            }
        }

        private static byte[] CreateArchiveBytes() {
            using (var temp = new TemporaryDirectory()) {
                string archivePath = SecureInstallerExtractorTests.CreateArchive(
                    temp.Path,
                    (UpdateSecurityPolicy.InstallerFileName, new byte[] { 1, 2, 3, 4 }));
                return File.ReadAllBytes(archivePath);
            }
        }

        private static UpdateInfo CreateTarget(byte[] archiveBytes) {
            string crc;
            using (var stream = new MemoryStream(archiveBytes)) {
                crc = Crc32Helper.ComputeCrc32(stream).ToString("x8");
            }

            return new UpdateInfo(
                UpdateChannel.Release,
                "17.0.03.31",
                new UpdateVersion(17, 0, 3, 31),
                new Uri("https://updatefiles.loxone.com/LoxConfig/package.zip"),
                crc,
                archiveBytes.Length);
        }

        private static HttpClient CreateClient(byte[] archiveBytes) {
            return new HttpClient(new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(archiveBytes)
            })) { Timeout = Timeout.InfiniteTimeSpan };
        }

        private sealed class FakeSignatureVerifier : IInstallerSignatureVerifier {
            private readonly UpdateValidationFailure result;

            public bool WasCalled { get; private set; }

            public FakeSignatureVerifier(UpdateValidationFailure result) {
                this.result = result;
            }

            public UpdateValidationFailure Verify(string installerPath) {
                WasCalled = true;
                return result;
            }
        }
    }
}
