using LoxTools.AppUpdates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class AppSemanticVersionTests {
        [DataTestMethod]
        [DataRow("v1.2.3", "1.2.3")]
        [DataRow("1.2.3-alpha.1", "1.2.3-alpha.1")]
        [DataRow("v1.2.3-beta.4", "1.2.3-beta.4")]
        [DataRow("1.2.3-rc.2+commit", "1.2.3-rc.2")]
        public void ParsesSupportedVersions(string input, string expected) {
            Assert.IsTrue(AppSemanticVersion.TryParse(input, out AppSemanticVersion version));
            Assert.AreEqual(expected, version.ToString());
        }

        [DataTestMethod]
        [DataRow("1.2")]
        [DataRow("1.2.3-dev.20260621.1")]
        [DataRow("1.2.3-alpha")]
        [DataRow("release-1.2.3")]
        public void RejectsUnsupportedVersions(string input) {
            Assert.IsFalse(AppSemanticVersion.TryParse(input, out _));
        }

        [TestMethod]
        public void OrdersPrereleasesBeforeStable() {
            AssertVersionLess("1.2.3-alpha.2", "1.2.3-beta.1");
            AssertVersionLess("1.2.3-beta.1", "1.2.3-rc.1");
            AssertVersionLess("1.2.3-rc.1", "1.2.3");
            AssertVersionLess("1.2.3", "1.2.4-alpha.1");
        }

        [TestMethod]
        public void AppliesChannelInclusionRules() {
            AppSemanticVersion.TryParse("1.0.0-alpha.1", out AppSemanticVersion alpha);
            AppSemanticVersion.TryParse("1.0.0-beta.1", out AppSemanticVersion beta);
            AppSemanticVersion.TryParse("1.0.0-rc.1", out AppSemanticVersion rc);
            AppSemanticVersion.TryParse("1.0.0", out AppSemanticVersion stable);

            Assert.IsFalse(alpha.IsEligibleFor(AppUpdateChannel.Stable));
            Assert.IsFalse(beta.IsEligibleFor(AppUpdateChannel.Stable));
            Assert.IsTrue(stable.IsEligibleFor(AppUpdateChannel.Stable));
            Assert.IsFalse(alpha.IsEligibleFor(AppUpdateChannel.Beta));
            Assert.IsTrue(beta.IsEligibleFor(AppUpdateChannel.Beta));
            Assert.IsTrue(rc.IsEligibleFor(AppUpdateChannel.Beta));
            Assert.IsTrue(stable.IsEligibleFor(AppUpdateChannel.Beta));
            Assert.IsTrue(alpha.IsEligibleFor(AppUpdateChannel.Alpha));
        }

        private static void AssertVersionLess(string lower, string higher) {
            AppSemanticVersion.TryParse(lower, out AppSemanticVersion lowerVersion);
            AppSemanticVersion.TryParse(higher, out AppSemanticVersion higherVersion);
            Assert.IsTrue(lowerVersion.CompareTo(higherVersion) < 0, $"Expected {lower} to be lower than {higher}.");
        }
    }
}
