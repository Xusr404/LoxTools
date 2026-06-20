using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace LoxTools.UpdateCheck {
    internal enum UpdateValidationFailure {
        None,
        Metadata,
        Endpoint,
        Redirect,
        Size,
        Crc32,
        Archive,
        Signature,
        Publisher
    }

    internal sealed class UpdateSecurityException : Exception {
        public UpdateValidationFailure Failure { get; }

        public UpdateSecurityException(UpdateValidationFailure failure, string message)
            : base(message) {
            Failure = failure;
        }

        public UpdateSecurityException(UpdateValidationFailure failure, string message, Exception innerException)
            : base(message, innerException) {
            Failure = failure;
        }
    }

    internal static class UpdateSecurityPolicy {
        internal const long MaxMetadataBytes = 1024L * 1024L;
        internal const long MaxPackageBytes = 2L * 1024L * 1024L * 1024L;
        internal const string PackageHost = "updatefiles.loxone.com";
        internal const string PackagePathPrefix = "/LoxConfig/";
        internal const string InstallerFileName = "LoxoneConfigSetup.exe";

        internal static readonly Uri MetadataUri = new Uri("https://update.loxone.com/updatecheck.xml");

        internal static HttpClient CreateHttpClient() {
            var handler = new HttpClientHandler {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                CheckCertificateRevocationList = true,
                UseCookies = false
            };

            return new HttpClient(handler) {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        internal static Uri NormalizePackageUri(string value) {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri uri)) {
                throw new UpdateSecurityException(UpdateValidationFailure.Endpoint, "The update package URI is invalid.");
            }

            bool supportedScheme = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            if (!supportedScheme
                || !string.Equals(uri.Host, PackageHost, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment)
                || !uri.IsDefaultPort
                || !uri.AbsolutePath.StartsWith(PackagePathPrefix, StringComparison.Ordinal)
                || !uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                throw new UpdateSecurityException(UpdateValidationFailure.Endpoint, "The update package URI is not approved.");
            }

            var secureUri = new UriBuilder(uri) {
                Scheme = Uri.UriSchemeHttps,
                Port = -1
            };
            return secureUri.Uri;
        }

        internal static void EnsureSuccessfulResponse(HttpResponseMessage response) {
            if (response == null) {
                throw new ArgumentNullException(nameof(response));
            }

            int statusCode = (int)response.StatusCode;
            if (statusCode >= 300 && statusCode < 400) {
                throw new UpdateSecurityException(UpdateValidationFailure.Redirect, "Update redirects are not allowed.");
            }

            response.EnsureSuccessStatusCode();
        }
    }
}
