using LoxTools.AppUpdates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Text.Json;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class GitHubReleaseClientTests {
        [TestMethod]
        public void ParseIgnoresDraftsAndMalformedTags() {
            const string json = @"[
              {""tag_name"":""v1.2.0"",""draft"":false,""html_url"":""https://github.com/Xusr404/LoxTools/releases/tag/v1.2.0"",""assets"":[]},
              {""tag_name"":""v1.3.0"",""draft"":true,""html_url"":""https://github.com/Xusr404/LoxTools/releases/tag/v1.3.0"",""assets"":[]},
              {""tag_name"":""latest"",""draft"":false,""html_url"":""https://github.com/Xusr404/LoxTools/releases/latest"",""assets"":[]}
            ]";
            using (JsonDocument document = JsonDocument.Parse(json)) {
                var releases = GitHubReleaseClient.Parse(document.RootElement);
                Assert.AreEqual(1, releases.Count);
                Assert.AreEqual("1.2.0", releases[0].Version.ToString());
            }
        }

        [TestMethod]
        public void ParsePreservesExactReleaseAssets() {
            const string json = @"[{""tag_name"":""v1.2.0-beta.2"",""draft"":false,""html_url"":""https://github.com/Xusr404/LoxTools/releases/tag/v1.2.0-beta.2"",""assets"":[{""name"":""LoxTools-Setup-1.2.0-beta.2.exe"",""browser_download_url"":""https://github.com/Xusr404/LoxTools/releases/download/v1.2.0-beta.2/setup.exe"",""size"":1234}]}]";
            using (JsonDocument document = JsonDocument.Parse(json)) {
                AppReleaseInfo release = GitHubReleaseClient.Parse(document.RootElement).Single();
                AppReleaseAsset asset = release.FindAsset("LoxTools-Setup-1.2.0-beta.2.exe");
                Assert.IsNotNull(asset);
                Assert.AreEqual(1234, asset.Size);
            }
        }

        [TestMethod]
        public void SelectReleaseChoosesHighestEligibleNewerVersion() {
            AppSemanticVersion.TryParse("1.0.0", out AppSemanticVersion installed);
            var releases = new[] {
                Create("1.1.0-alpha.2"), Create("1.1.0-beta.1"), Create("1.0.1"), Create("0.9.0")
            };

            Assert.AreEqual("1.0.1", AppUpdateService.SelectRelease(releases, installed, AppUpdateChannel.Stable).Version.ToString());
            Assert.AreEqual("1.1.0-beta.1", AppUpdateService.SelectRelease(releases, installed, AppUpdateChannel.Beta).Version.ToString());
            Assert.AreEqual("1.1.0-beta.1", AppUpdateService.SelectRelease(releases, installed, AppUpdateChannel.Alpha).Version.ToString());
        }

        [TestMethod]
        public void NotificationIsRequestedOnlyOncePerVersion() {
            Assert.IsTrue(AppUpdateService.ShouldNotify("1.0.0", "1.0.1"));
            Assert.IsFalse(AppUpdateService.ShouldNotify("1.0.1", "1.0.1"));
            Assert.IsFalse(AppUpdateService.ShouldNotify("1.0.0", string.Empty));
        }

        [TestMethod]
        public void AssetValidationRejectsMissingAndOversizedAssets() {
            Assert.IsFalse(AppUpdateService.IsValidAsset(null, 100));
            Assert.IsFalse(AppUpdateService.IsValidAsset(new AppReleaseAsset("setup.exe", new System.Uri("https://github.com/setup.exe"), 0), 100));
            Assert.IsFalse(AppUpdateService.IsValidAsset(new AppReleaseAsset("setup.exe", new System.Uri("https://github.com/setup.exe"), 101), 100));
            Assert.IsTrue(AppUpdateService.IsValidAsset(new AppReleaseAsset("setup.exe", new System.Uri("https://github.com/setup.exe"), 100), 100));
        }

        [TestMethod]
        public void DownloadHostMustBeGitHubHttps() {
            Assert.IsTrue(AppUpdateService.IsGitHubDownloadUri(new System.Uri("https://github.com/Xusr404/LoxTools/releases/download/v1/setup.exe")));
            Assert.IsFalse(AppUpdateService.IsGitHubDownloadUri(new System.Uri("http://github.com/setup.exe")));
            Assert.IsFalse(AppUpdateService.IsGitHubDownloadUri(new System.Uri("https://example.com/setup.exe")));
        }

        private static AppReleaseInfo Create(string version) {
            AppSemanticVersion.TryParse(version, out AppSemanticVersion parsed);
            return new AppReleaseInfo("v" + version, parsed, new System.Uri("https://github.com/Xusr404/LoxTools/releases/tag/v" + version), null);
        }
    }
}
