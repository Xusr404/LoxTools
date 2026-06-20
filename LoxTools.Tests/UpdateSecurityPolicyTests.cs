using LoxTools.UpdateCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LoxTools.Tests {
    [TestClass]
    public sealed class UpdateSecurityPolicyTests {
        [TestMethod]
        public void MetadataEndpointUsesHttps() {
            Assert.AreEqual(Uri.UriSchemeHttps, UpdateSecurityPolicy.MetadataUri.Scheme);
            Assert.AreEqual("update.loxone.com", UpdateSecurityPolicy.MetadataUri.Host);
        }

        [DataTestMethod]
        [DataRow("http://updatefiles.loxone.com/LoxConfig/LoxoneConfigSetup_17000331.zip")]
        [DataRow("https://updatefiles.loxone.com/LoxConfig/LoxoneConfigSetup_17010616.zip")]
        [DataRow("http://updatefiles.loxone.com/LoxConfig/LoxoneConfigSetup_17010619.zip")]
        public void ApprovedPackageUrlsNormalizeToHttps(string source) {
            Uri result = UpdateSecurityPolicy.NormalizePackageUri(source);

            Assert.AreEqual(Uri.UriSchemeHttps, result.Scheme);
            Assert.AreEqual(UpdateSecurityPolicy.PackageHost, result.Host);
            Assert.AreEqual(443, result.Port);
            Assert.IsTrue(result.IsDefaultPort);
        }

        [DataTestMethod]
        [DataRow("http://evil.example/LoxConfig/update.zip")]
        [DataRow("https://updatefiles.loxone.com.evil.example/LoxConfig/update.zip")]
        [DataRow("https://user@updatefiles.loxone.com/LoxConfig/update.zip")]
        [DataRow("https://updatefiles.loxone.com:444/LoxConfig/update.zip")]
        [DataRow("ftp://updatefiles.loxone.com/LoxConfig/update.zip")]
        [DataRow("https://updatefiles.loxone.com/Other/update.zip")]
        [DataRow("https://updatefiles.loxone.com/LoxConfig/update.exe")]
        public void UnapprovedPackageUrlsAreRejected(string source) {
            UpdateSecurityException exception = Assert.ThrowsException<UpdateSecurityException>(
                () => UpdateSecurityPolicy.NormalizePackageUri(source));

            Assert.AreEqual(UpdateValidationFailure.Endpoint, exception.Failure);
        }
    }
}
