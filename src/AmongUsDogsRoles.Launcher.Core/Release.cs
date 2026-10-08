using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmongUsDogsRoles.Launcher;

public sealed record ReleaseManifest(int SchemaVersion, string Version, string GameVersion,
    string Platform, string Architecture, string AssetName, string Sha256, long Size)
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("This release needs a newer launcher.");
        if (!Regex.IsMatch(Version ?? "", @"^\d+\.\d+\.\d+$") || !System.Version.TryParse(Version, out _))
            throw new InvalidDataException("Invalid release version.");
        if (!Regex.IsMatch(GameVersion ?? "", @"^\d{4}\.\d{1,2}\.\d{1,2}$"))
            throw new InvalidDataException("Invalid game version.");
        if (Platform != "steam" || Architecture != "x64")
            throw new InvalidDataException("This launcher supports Windows Steam x64 releases only.");
        if (AssetName != $"AmongUsDogsRoles-{Version}.zip") throw new InvalidDataException("Unexpected package name.");
        if (!Regex.IsMatch(Sha256 ?? "", "^[a-fA-F0-9]{64}$") || Size <= 0 || Size > 1024L * 1024 * 1024)
            throw new InvalidDataException("Invalid package checksum or size.");
    }
}

public sealed record AvailableRelease(ReleaseManifest Manifest, Uri DownloadUrl);

public sealed class ReleaseClient : IDisposable
{
    public const string RepositoryUrl = "https://github.com/dul4895/AmongUsDogsRoles";
    public const string ManifestName = "launcher-release.json";
    private readonly HttpClient http;

    public ReleaseClient(HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromMinutes(15);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AmongUsDogsRoles-Launcher/1.0.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<AvailableRelease> LatestAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync("https://api.github.com/repos/dul4895/AmongUsDogsRoles/releases/latest", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("No stable release is available yet. Use a test package or try again after a release is published.");
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            throw new InvalidOperationException("GitHub temporarily limited update checks. Try later, or play your installed version.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Automatic updates require a stable published release.");
        var tag = root.GetProperty("tag_name").GetString()!;
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var manifestAssets = assets.Where(a => a.GetProperty("name").GetString() == ManifestName).ToArray();
        if (manifestAssets.Length != 1)
            throw new InvalidOperationException("The latest release predates launcher support. Its publisher must include launcher-release.json. You can still play an installed version or install a test package.");
        var manifestUrl = AssetUrl(manifestAssets[0], tag);
        var manifest = await http.GetFromJsonAsync<ReleaseManifest>(manifestUrl, ReleaseManifest.Json, timeout.Token)
            ?? throw new InvalidDataException("Empty release manifest.");
        manifest.Validate();
        if (tag != manifest.Version && tag != "v" + manifest.Version)
            throw new InvalidDataException("Release tag and manifest version disagree.");
        var packages = assets.Where(a => a.GetProperty("name").GetString() == manifest.AssetName).ToArray();
        if (packages.Length != 1 || packages[0].GetProperty("size").GetInt64() != manifest.Size)
            throw new InvalidDataException("The release package is missing or its size disagrees with the manifest.");
        return new AvailableRelease(manifest, AssetUrl(packages[0], tag));
    }

    private static Uri AssetUrl(JsonElement asset, string tag)
    {
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        var prefix = RepositoryUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/";
        if (!uri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal) || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Unexpected release download location.");
        return uri;
    }

    public async Task DownloadAsync(AvailableRelease release, string destination, IProgress<string>? progress, CancellationToken token)
    {
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long total = 0;
        var lastPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > release.Manifest.Size) throw new InvalidDataException("Download exceeds the expected package size.");
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            var percent = (int)(total * 100 / release.Manifest.Size);
            if (percent != lastPercent) progress?.Report($"Downloading mod… {percent}%");
            lastPercent = percent;
        }
        if (total != release.Manifest.Size) throw new InvalidDataException("Download was incomplete. Try again.");
    }

    public void Dispose() => http.Dispose();
}
