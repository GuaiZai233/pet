using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuaiMiao.Services;

internal sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string ReleasePageUrl,
    string? DownloadUrl,
    string? Sha256)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

internal sealed class GitHubUpdateService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private const long MaxDownloadBytes = 200L * 1024 * 1024;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = Timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GuaiMiao", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.GetAsync(AppInfo.LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(content,
            cancellationToken: cancellationToken) ?? throw new InvalidOperationException("GitHub 返回了空的发布信息。");

        var latestVersion = ParseVersion(release.TagName);
        var releasePageUrl = IsTrustedReleasePage(release.HtmlUrl) ? release.HtmlUrl : AppInfo.HomepageUrl + "/releases";
        var asset = release.Assets.FirstOrDefault(candidate =>
            candidate.Name.Equals(AppInfo.ReleaseAssetName, StringComparison.OrdinalIgnoreCase) &&
            IsTrustedDownload(candidate.BrowserDownloadUrl));
        return new UpdateCheckResult(CurrentVersion, latestVersion, releasePageUrl,
            asset?.BrowserDownloadUrl, ParseSha256(asset?.Digest));
    }

    public async Task<string> DownloadAsync(UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        if (!update.IsUpdateAvailable || update.DownloadUrl is null)
            throw new InvalidOperationException("没有可下载的新版本。");
        if (update.Sha256 is null)
            throw new InvalidOperationException("GitHub 发布资源缺少 SHA-256 摘要，已拒绝下载。");

        Directory.CreateDirectory(Infrastructure.AppPaths.CacheDirectory);
        var finalPath = Path.Combine(Infrastructure.AppPaths.CacheDirectory,
            $"乖喵-update-{update.LatestVersion.ToString(3)}.exe");
        var temporaryPath = finalPath + ".download";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GuaiMiao", CurrentVersion.ToString()));
            using var response = await client.GetAsync(update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length > MaxDownloadBytes)
                throw new InvalidOperationException("更新文件超过 200 MB 安全限制。");

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new InvalidOperationException("更新文件超过 200 MB 安全限制。");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            await using (var stream = File.OpenRead(temporaryPath))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("更新文件 SHA-256 校验失败。");
            }

            var downloadedVersionText = FileVersionInfo.GetVersionInfo(temporaryPath).FileVersion;
            if (!Version.TryParse(downloadedVersionText, out var downloadedVersion) ||
                downloadedVersion < update.LatestVersion)
                throw new InvalidOperationException("更新文件版本与 GitHub 发布版本不匹配。");
            File.Move(temporaryPath, finalPath, true);
            return finalPath;
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    public static void LaunchInstaller(string path)
    {
        var full = Path.GetFullPath(path);
        var cache = Path.GetFullPath(Infrastructure.AppPaths.CacheDirectory) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(cache, StringComparison.OrdinalIgnoreCase) ||
            !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新文件不在受信任的缓存目录中。");
        Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新地址不是受信任的 GitHub 链接。");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static Version ParseVersion(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        var suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            value = value[..suffix];
        return Version.TryParse(value, out var version)
            ? version
            : throw new InvalidOperationException($"无法识别发布版本号：{tag}");
    }

    private static bool IsTrustedReleasePage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/GuaiZai233/pet/releases/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedDownload(string url) =>
        url.StartsWith(AppInfo.ReleaseDownloadPrefix, StringComparison.OrdinalIgnoreCase);

    private static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var value = digest[prefix.Length..];
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
