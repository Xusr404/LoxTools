using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class WindowsInstallerSignatureVerifierTests {
        [TestMethod]
        public void VerifierRejectsUnsignedFiles() {
            using (var temp = new TemporaryDirectory()) {
                string path = Path.Combine(temp.Path, UpdateSecurityPolicy.InstallerFileName);
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                var verifier = new WindowsInstallerSignatureVerifier();

                Assert.AreEqual(UpdateValidationFailure.Signature, verifier.Verify(path));
            }
        }

        [TestMethod]
        public void PublisherPolicyAcceptsExpectedOrganizationAndCountry() {
            using (X509Certificate2 certificate = CreateCertificate("CN=Loxone Electronics GmbH, O=Loxone Electronics GmbH, C=AT")) {
                Assert.IsTrue(WindowsInstallerSignatureVerifier.HasExpectedPublisher(certificate));
            }
        }

        [TestMethod]
        public void PublisherPolicyRejectsOtherOrganizations() {
            using (X509Certificate2 certificate = CreateCertificate("CN=Example, O=Example GmbH, C=AT")) {
                Assert.IsFalse(WindowsInstallerSignatureVerifier.HasExpectedPublisher(certificate));
            }
        }

        private static X509Certificate2 CreateCertificate(string subject) {
            using (RSA rsa = RSA.Create(2048)) {
                var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return request.CreateSelfSigned(
                    System.DateTimeOffset.UtcNow.AddDays(-1),
                    System.DateTimeOffset.UtcNow.AddDays(1));
            }
        }
    }
}
