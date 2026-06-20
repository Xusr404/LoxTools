using LoxTools.Models;
using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class UpdateVersionTests {
        [TestMethod]
        public void VersionComparisonPreservesBuildOrdering() {
            var older = new UpdateVersion(17, 0, 3, 30);
            var newer = new UpdateVersion(17, 0, 3, 31);

            Assert.IsTrue(older.CompareTo(newer) < 0);
            Assert.IsTrue(newer.CompareTo(older) > 0);
            Assert.AreEqual(0, newer.CompareTo(new UpdateVersion(17, 0, 3, 31)));
        }

        [TestMethod]
        public void AlphaParserPreservesTestEntryCompatibility() {
            const string xml = "<Root><Test Version=\"17.1.06.19\" Path=\"http://updatefiles.loxone.com/LoxConfig/LoxoneConfigSetup_17010619.zip\" crc32=\"deadbeef\" Filesize=\"551314535\" /></Root>";
            var parser = new UpdateCheckXmlParser();

            UpdateInfo result = parser.ParseAlpha(xml);

            Assert.AreEqual(UpdateChannel.Alpha, result.Channel);
            Assert.AreEqual("17.1.06.19", result.VersionString);
        }
    }
}
