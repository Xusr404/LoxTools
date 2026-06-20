using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class UpdateMetadataTests {
        private const string ValidRelease = "<Root><Release Version=\"17.0.03.31\" Path=\"http://updatefiles.loxone.com/LoxConfig/LoxoneConfigSetup_17000331.zip\" crc32=\"deadbeef\" Filesize=\"531271265\" /></Root>";

        [TestMethod]
        public void ParserValidatesAndNormalizesReleaseMetadata() {
            var parser = new UpdateCheckXmlParser();

            var result = parser.ParseRelease(ValidRelease);

            Assert.AreEqual("https://updatefiles.loxone.com/LoxConfig/LoxoneConfigSetup_17000331.zip", result.DownloadUri.AbsoluteUri);
            Assert.AreEqual(531271265L, result.FileSize);
            Assert.AreEqual("deadbeef", result.Crc32Hex);
        }

        [DataTestMethod]
        [DataRow("zzzzzzzz", "531271265")]
        [DataRow("deadbee", "531271265")]
        [DataRow("deadbeef", "")]
        [DataRow("deadbeef", "0")]
        [DataRow("deadbeef", "2147483649")]
        public void ParserRejectsInvalidIntegrityMetadata(string crc32, string fileSize) {
            string xml = $"<Root><Release Version=\"17.0.03.31\" Path=\"http://updatefiles.loxone.com/LoxConfig/update.zip\" crc32=\"{crc32}\" Filesize=\"{fileSize}\" /></Root>";
            var parser = new UpdateCheckXmlParser();

            Assert.ThrowsException<InvalidOperationException>(() => parser.ParseRelease(xml));
        }

        [TestMethod]
        public void ParserRejectsDtds() {
            string xml = "<!DOCTYPE Root [<!ENTITY value '17.0.03.31'>]><Root><Release Version=\"&value;\" Path=\"http://updatefiles.loxone.com/LoxConfig/update.zip\" crc32=\"deadbeef\" Filesize=\"1\" /></Root>";
            var parser = new UpdateCheckXmlParser();

            Assert.ThrowsException<XmlException>(() => parser.ParseRelease(xml));
        }

        [TestMethod]
        public async Task MetadataClientRejectsRedirects() {
            using (var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Redirect) {
                Headers = { Location = new Uri("https://example.invalid/updatecheck.xml") }
            })) {
                var xmlClient = new UpdateCheckXmlClient(client);

                UpdateSecurityException exception = await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => xmlClient.DownloadUpdateCheckXmlAsync(CancellationToken.None));
                Assert.AreEqual(UpdateValidationFailure.Redirect, exception.Failure);
            }
        }

        [TestMethod]
        public async Task MetadataClientRejectsOversizedResponses() {
            byte[] bytes = new byte[UpdateSecurityPolicy.MaxMetadataBytes + 1];
            using (var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(bytes)
            })) {
                var xmlClient = new UpdateCheckXmlClient(client);

                UpdateSecurityException exception = await Assert.ThrowsExceptionAsync<UpdateSecurityException>(
                    () => xmlClient.DownloadUpdateCheckXmlAsync(CancellationToken.None));
                Assert.AreEqual(UpdateValidationFailure.Metadata, exception.Failure);
            }
        }

        [TestMethod]
        public async Task MetadataClientReturnsBoundedUtf8Content() {
            byte[] bytes = Encoding.UTF8.GetBytes(ValidRelease);
            using (var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new UnknownLengthContent(bytes)
            })) {
                var xmlClient = new UpdateCheckXmlClient(client);

                string result = await xmlClient.DownloadUpdateCheckXmlAsync(CancellationToken.None);

                Assert.AreEqual(ValidRelease, result);
            }
        }

        private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> factory) {
            return new HttpClient(new TestHttpMessageHandler(factory)) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }
}
