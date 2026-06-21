using LoxTools.AppUpdates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class AppInstallerVerifierTests {
        [TestMethod]
        public void ChecksumParserRequiresExactlyOneExactFileName() {
            using (var temp = new TemporaryDirectory()) {
                string path = Path.Combine(temp.Path, "SHA256SUMS.txt");
                string hash = new string('a', 64);
                File.WriteAllText(path, hash + "  LoxTools-Setup-1.2.3.exe");

                Assert.IsTrue(AppInstallerVerifier.TryReadExpectedHash(path, "LoxTools-Setup-1.2.3.exe", out string parsed));
                Assert.AreEqual(hash, parsed);
                Assert.IsFalse(AppInstallerVerifier.TryReadExpectedHash(path, "LoxTools-Setup-1.2.4.exe", out _));
            }
        }

        [TestMethod]
        public void ChecksumParserRejectsDuplicateEntries() {
            using (var temp = new TemporaryDirectory()) {
                string path = Path.Combine(temp.Path, "SHA256SUMS.txt");
                string line = new string('b', 64) + "  LoxTools-Setup-1.2.3.exe";
                File.WriteAllText(path, line + System.Environment.NewLine + line);
                Assert.IsFalse(AppInstallerVerifier.TryReadExpectedHash(path, "LoxTools-Setup-1.2.3.exe", out _));
            }
        }

        [TestMethod]
        public void EmptyPublisherDisablesAutomaticExecution() {
            var verifier = new AppInstallerVerifier(string.Empty);
            Assert.IsFalse(verifier.IsExecutionConfigured);
        }

#if DEBUG
        [TestMethod]
        public void DebugVerifierRequiresMatchingSha256() {
            using (var temp = new TemporaryDirectory()) {
                const string installerName = "LoxTools-Setup-1.2.3.exe";
                string installerPath = Path.Combine(temp.Path, installerName);
                string checksumPath = Path.Combine(temp.Path, "SHA256SUMS.txt");
                File.WriteAllBytes(installerPath, new byte[] { 1, 2, 3, 4 });
                File.WriteAllText(checksumPath, AppInstallerVerifier.ComputeSha256(installerPath) + "  " + installerName);
                var verifier = new DebugAppInstallerVerifier();

                Assert.IsTrue(verifier.Verify(installerPath, checksumPath, installerName));

                File.AppendAllText(installerPath, "tampered");
                Assert.IsFalse(verifier.Verify(installerPath, checksumPath, installerName));
            }
        }
#endif
    }
}
