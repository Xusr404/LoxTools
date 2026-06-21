using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.AppUpdates {
    internal sealed class AppReleaseAsset {
        public string Name { get; }
        public Uri DownloadUri { get; }
        public long Size { get; }

        public AppReleaseAsset(string name, Uri downloadUri, long size) {
            Name = name ?? string.Empty;
            DownloadUri = downloadUri;
            Size = size;
        }
    }

    internal sealed class AppReleaseInfo {
        public string TagName { get; }
        public AppSemanticVersion Version { get; }
        public Uri ReleasePageUri { get; }
        public IReadOnlyList<AppReleaseAsset> Assets { get; }

        public AppReleaseInfo(string tagName, AppSemanticVersion version, Uri releasePageUri, IReadOnlyList<AppReleaseAsset> assets) {
            TagName = tagName;
            Version = version;
            ReleasePageUri = releasePageUri;
            Assets = assets ?? Array.Empty<AppReleaseAsset>();
        }

        public AppReleaseAsset FindAsset(string name) => Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal));
    }

    internal interface IAppReleaseSource {
        Task<IReadOnlyList<AppReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken);
    }

    internal sealed class GitHubReleaseClient : IAppReleaseSource {
        internal static readonly Uri ReleasesEndpoint = new Uri("https://api.github.com/repos/Xusr404/LoxTools/releases?per_page=20");
        private readonly HttpClient httpClient;

        public GitHubReleaseClient(HttpClient httpClient) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<IReadOnlyList<AppReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken) {
            using (var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint)) {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.UserAgent.ParseAdd("LoxTools-UpdateCheck/1.0");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

                using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)) {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false)) {
                        using (JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false)) {
                            return Parse(document.RootElement);
                        }
                    }
                }
            }
        }

        internal static IReadOnlyList<AppReleaseInfo> Parse(JsonElement root) {
            var releases = new List<AppReleaseInfo>();
            if (root.ValueKind != JsonValueKind.Array) return releases;

            foreach (JsonElement item in root.EnumerateArray()) {
                if (item.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True) continue;
                if (!item.TryGetProperty("tag_name", out JsonElement tagElement)) continue;
                string tag = tagElement.GetString();
                if (!AppSemanticVersion.TryParse(tag, out AppSemanticVersion version)) continue;
                if (!TryGetHttpsUri(item, "html_url", out Uri releasePage)) continue;

                var assets = new List<AppReleaseAsset>();
                if (item.TryGetProperty("assets", out JsonElement assetsElement) && assetsElement.ValueKind == JsonValueKind.Array) {
                    foreach (JsonElement asset in assetsElement.EnumerateArray()) {
                        if (!asset.TryGetProperty("name", out JsonElement nameElement)) continue;
                        string name = nameElement.GetString();
                        if (!TryGetHttpsUri(asset, "browser_download_url", out Uri downloadUri)) continue;
                        long size = asset.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long parsedSize) ? parsedSize : -1;
                        assets.Add(new AppReleaseAsset(name, downloadUri, size));
                    }
                }

                releases.Add(new AppReleaseInfo(tag, version, releasePage, assets));
            }

            return releases;
        }

        private static bool TryGetHttpsUri(JsonElement item, string propertyName, out Uri uri) {
            uri = null;
            if (!item.TryGetProperty(propertyName, out JsonElement element)) return false;
            return Uri.TryCreate(element.GetString(), UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        }
    }
}
