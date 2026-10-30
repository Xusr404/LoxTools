using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;

namespace LoxTools.Tests {
    [TestClass]
    public class UpdateWorkingFolderTests {
        [TestMethod]
        public void StartupCleanupProtectsActiveOperationAndRemovesAbandonedFolder() {
            using (var temp = new TemporaryDirectory()) {
                var operation = new UpdateWorkingFolder(temp.Path);
                string path = operation.Path;
                File.WriteAllText(Path.Combine(path, "installer.exe"), "test");
                UpdateWorkingFolder.Cleanup(temp.Path, TimeSpan.Zero);
                Assert.IsTrue(Directory.Exists(path));
                operation.Dispose(); // Simulate an interrupted download.
                UpdateWorkingFolder.Cleanup(temp.Path, TimeSpan.Zero);
                Assert.IsFalse(Directory.Exists(path));
            }
        }

        [TestMethod]
        public void StartupCleanupProtectsInstallerAfterApplicationLeaseEnds() {
            using (var temp = new TemporaryDirectory())
            using (Process current = Process.GetCurrentProcess()) {
                var operation = new UpdateWorkingFolder(temp.Path);
                operation.TrackInstaller(current);
                operation.Dispose();
                UpdateWorkingFolder.Cleanup(temp.Path, TimeSpan.Zero);
                Assert.IsTrue(Directory.Exists(operation.Path));
                operation.DeleteAfterExit(); // Test fixture cleanup only.
            }
        }

        [TestMethod]
        public void CompletedOperationRemovesPayloadButRetainsLogsAndOtherDirectories() {
            using (var temp = new TemporaryDirectory()) {
                string log = Path.Combine(temp.Path, "installer-example.log");
                File.WriteAllText(log, "test log");
                string unrelated = Path.Combine(temp.Path, "other-data");
                Directory.CreateDirectory(unrelated);
                using (var operation = new UpdateWorkingFolder(temp.Path)) {
                    File.WriteAllText(Path.Combine(operation.Path, "setup.exe"), "test");
                    operation.DeleteAfterExit();
                    Assert.IsFalse(Directory.Exists(operation.Path));
                }
                UpdateWorkingFolder.Cleanup(temp.Path, TimeSpan.Zero);
                Assert.IsTrue(File.Exists(log));
                Assert.IsTrue(Directory.Exists(unrelated));
            }
        }

        [TestMethod]
        public void CleanupRemovesCompletedInstallerLeftByCrash() {
            using (var temp = new TemporaryDirectory()) {
                string path = Path.Combine(temp.Path, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, ".active"), int.MaxValue + "|0");
                UpdateWorkingFolder.Cleanup(temp.Path, TimeSpan.Zero);
                Assert.IsFalse(Directory.Exists(path));
            }
        }
    }
}
