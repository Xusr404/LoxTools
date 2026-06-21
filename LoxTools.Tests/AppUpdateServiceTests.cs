using System.Diagnostics;
using LoxTools.AppUpdates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class AppUpdateServiceTests {
        [TestMethod]
        public void InstallerStartsAsSilentBackgroundUpgrade() {
            ProcessStartInfo startInfo = AppUpdateService.CreateInstallerStartInfo(@"C:\Temp\LoxTools-Setup-1.2.3.exe", 1234);

            Assert.AreEqual(@"C:\Temp\LoxTools-Setup-1.2.3.exe", startInfo.FileName);
            Assert.AreEqual("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LoxToolsPid=1234", startInfo.Arguments);
            Assert.IsTrue(startInfo.UseShellExecute);
            Assert.AreEqual(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        }
    }
}
