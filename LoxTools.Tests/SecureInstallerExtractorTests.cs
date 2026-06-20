using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.IO.Compression;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class SecureInstallerExtractorTests {
        [TestMethod]
        public void ExtractAcceptsTheSingleExpectedRootInstaller() {
            using (var temp = new TemporaryDirectory()) {
                string archivePath = CreateArchive(temp.Path, (UpdateSecurityPolicy.InstallerFileName, new byte[] { 1, 2, 3 }));
                var extractor = new SecureInstallerExtractor();

                string result = extractor.Extract(archivePath, Path.Combine(temp.Path, "out"));

                Assert.AreEqual(UpdateSecurityPolicy.InstallerFileName, Path.GetFileName(result));
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(result));
            }
        }

        [DataTestMethod]
        [DataRow("../LoxoneConfigSetup.exe")]
        [DataRow("folder/LoxoneConfigSetup.exe")]
        [DataRow("OtherSetup.exe")]
        public void ExtractRejectsUnexpectedEntryNames(string entryName) {
            using (var temp = new TemporaryDirectory()) {
                string archivePath = CreateArchive(temp.Path, (entryName, new byte[] { 1 }));
                var extractor = new SecureInstallerExtractor();

                UpdateSecurityException exception = Assert.ThrowsException<UpdateSecurityException>(
                    () => extractor.Extract(archivePath, Path.Combine(temp.Path, "out")));

                Assert.AreEqual(UpdateValidationFailure.Archive, exception.Failure);
            }
        }

        [TestMethod]
        public void ExtractRejectsAdditionalFiles() {
            using (var temp = new TemporaryDirectory()) {
                string archivePath = CreateArchive(
                    temp.Path,
                    (UpdateSecurityPolicy.InstallerFileName, new byte[] { 1 }),
                    ("extra.dll", new byte[] { 2 }));
                var extractor = new SecureInstallerExtractor();

                UpdateSecurityException exception = Assert.ThrowsException<UpdateSecurityException>(
                    () => extractor.Extract(archivePath, Path.Combine(temp.Path, "out")));

                Assert.AreEqual(UpdateValidationFailure.Archive, exception.Failure);
            }
        }

        [TestMethod]
        public void ExtractRejectsEmptyInstaller() {
            using (var temp = new TemporaryDirectory()) {
                string archivePath = CreateArchive(temp.Path, (UpdateSecurityPolicy.InstallerFileName, new byte[0]));
                var extractor = new SecureInstallerExtractor();

                Assert.ThrowsException<UpdateSecurityException>(
                    () => extractor.Extract(archivePath, Path.Combine(temp.Path, "out")));
            }
        }

        internal static string CreateArchive(string folder, params (string Name, byte[] Content)[] entries) {
            string archivePath = Path.Combine(folder, "package.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create)) {
                foreach (var item in entries) {
                    ZipArchiveEntry entry = archive.CreateEntry(item.Name, CompressionLevel.NoCompression);
                    using (Stream stream = entry.Open()) {
                        stream.Write(item.Content, 0, item.Content.Length);
                    }
                }
            }

            return archivePath;
        }
    }
}
