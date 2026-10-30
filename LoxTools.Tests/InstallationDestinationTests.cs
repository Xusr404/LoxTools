using LoxTools.Models;
using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.IO;

namespace LoxTools.Tests {
    [TestClass]
    public class InstallationDestinationTests {
        [TestMethod]
        public void SuccessfulInstallationRootCanBeRegisteredWithoutLosingConfiguredPaths() {
            string root = Path.Combine(Path.GetTempPath(), "LoxTools.Tests", "Loxone");
            string existing = Path.Combine(Path.GetTempPath(), "LoxTools.Tests", "Other");
            Assert.AreEqual(root, InstallationDestination.IncludeRoot(string.Empty, root));
            Assert.AreEqual(existing + "," + root, InstallationDestination.IncludeRoot(existing, root));
            Assert.AreEqual(root, InstallationDestination.IncludeRoot(root, root.ToUpperInvariant() + Path.DirectorySeparatorChar));
        }

        [TestMethod]
        public void RegisteringDriveRootPreservesAbsolutePath() {
            string root = Path.GetPathRoot(Path.GetTempPath());
            Assert.AreEqual(root, InstallationDestination.IncludeRoot(string.Empty, root));
        }

        [DataTestMethod]
        [DataRow(UpdateChannel.Release, false, "LoxoneConfig_Release")]
        [DataRow(UpdateChannel.Beta, false, "LoxoneConfig_Beta")]
        [DataRow(UpdateChannel.Alpha, false, "LoxoneConfig_Alpha")]
        [DataRow(UpdateChannel.Release, true, "LoxoneConfig_17031027_Release")]
        [DataRow(UpdateChannel.Beta, true, "LoxoneConfig_17031027_Beta")]
        [DataRow(UpdateChannel.Alpha, true, "LoxoneConfig_17031027_Alpha")]
        public void FolderModesRespectVersionAndChannel(UpdateChannel channel, bool versioned, string expected) {
            var target = new UpdateInfo(channel, "17.3.10.27", new UpdateVersion(17, 3, 10, 27),
                new Uri("https://updatefiles.loxone.com/LoxConfig/test.zip"), "00000000", 1);
            Assert.AreEqual(expected, InstallationDestination.GetFolderName(target, versioned));
            string root = Path.Combine(Path.GetTempPath(), "LoxTools.Tests", "Install Root");
            string path = InstallationDestination.GetPath(root, target, versioned);
            Assert.AreEqual(Path.Combine(root, expected), path);
            string logPath = Path.Combine(root, "Installer Logs", "install.log");
            var info = InstallationDestination.CreateStartInfo(Path.Combine(root, "Setup.exe"), path, logPath);
            StringAssert.Contains(info.Arguments, "/DIR=\"" + path + "\"");
            StringAssert.Contains(info.Arguments, "/GROUP=\"" + expected + "\"");
            StringAssert.Contains(info.Arguments, "/NORESTART");
            StringAssert.Contains(info.Arguments, "/VERYSILENT");
            StringAssert.Contains(info.Arguments, "/SUPPRESSMSGBOXES");
            StringAssert.Contains(info.Arguments, "/SP-");
            StringAssert.Contains(info.Arguments, "/NOCLOSEAPPLICATIONS");
            StringAssert.Contains(info.Arguments, "/NORESTARTAPPLICATIONS");
            StringAssert.Contains(info.Arguments, "/LOG=\"" + logPath + "\"");
            Assert.IsFalse(info.Arguments.Contains("/NOCANCEL"));
            Assert.AreEqual("runas", info.Verb);
            Assert.AreEqual(System.Diagnostics.ProcessWindowStyle.Hidden, info.WindowStyle);
            Assert.IsTrue(info.UseShellExecute);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("relative")]
        [DataRow("C:\\Install\" /DIR=bad")]
        public void InvalidInstallationRootsAreRejected(string root) {
            Assert.ThrowsException<ArgumentException>(() => InstallationDestination.GetPath(root, null, false));
        }

        [TestMethod]
        public void PreferenceDefaultsToSingleFolderAndSerializesBothModes() {
            var property = new LoxTools.Properties.Settings().Properties["VersionedInstallationFolders"];
            Assert.AreEqual("False", property.DefaultValue);
            foreach (bool mode in new[] { false, true }) {
                var original = new SettingsPropertyValue(property) { PropertyValue = mode };
                var restored = new SettingsPropertyValue(property) { SerializedValue = original.SerializedValue };
                Assert.AreEqual(mode, restored.PropertyValue);
            }
        }

        [DataTestMethod]
        [DataRow("relative.log")]
        [DataRow("C:\\Logs\\file\" /NORESTART.log")]
        public void UnsafeInstallerLogPathsAreRejected(string logPath) {
            string root = Path.Combine(Path.GetTempPath(), "LoxTools.Tests");
            Assert.ThrowsException<ArgumentException>(() => InstallationDestination.CreateStartInfo(
                Path.Combine(root, "Setup.exe"), Path.Combine(root, "LoxoneConfig_Release"), logPath));
        }
    }
}
