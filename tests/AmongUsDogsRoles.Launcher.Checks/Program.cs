using AmongUsDogsRoles.Launcher;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "dogs-launcher-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
try
{
    var game = Path.Combine(root, "Copied Mod");
    Directory.CreateDirectory(Path.Combine(game, "Among Us_Data"));
    File.WriteAllText(Path.Combine(game, "Among Us_Data", "globalgamemanagers"), "\0build\02026.9.29\0");
    File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "game assembly sentinel");
    var exe = new byte[128];
    BitConverter.GetBytes((ushort)0x5a4d).CopyTo(exe, 0);
    BitConverter.GetBytes(64).CopyTo(exe, 60);
    BitConverter.GetBytes(0x4550).CopyTo(exe, 64);
    BitConverter.GetBytes((ushort)0x8664).CopyTo(exe, 68);
    File.WriteAllBytes(Path.Combine(game, "Among Us.exe"), exe);

    var zip = MakeZip("good.zip", ["BepInEx/plugins/AmongUsDogsRoles.dll", "BepInEx/plugins/Reactor.dll",
        "BepInEx/plugins/MiraAPI.dll", "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "dotnet/runtime.dll", "winhttp.dll", "doorstop_config.ini"]);
    var manifest = new ReleaseManifest(1, "0.3.0", "2026.9.29", "steam", "x64", "AmongUsDogsRoles-0.3.0.zip",
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))), new FileInfo(zip).Length);
    Check("valid manifest and game", () => { manifest.Validate(); Installation.ValidateGame(game, manifest); });
    Reject("unsupported manifest schema", () => (manifest with { SchemaVersion = 2 }).Validate());
    Reject("invalid checksum", () => (manifest with { Sha256 = "no" }).Validate());
    Reject("wrong platform", () => (manifest with { Platform = "epic" }).Validate());
    Reject("manifest filename traversal", () => (manifest with { AssetName = "../other.zip" }).Validate());
    Reject("wrong game version", () => Installation.ValidateGame(game, manifest with { GameVersion = "2026.9.2" }));
    exe[68] = 0x4c; exe[69] = 0x01;
    File.WriteAllBytes(Path.Combine(game, "Among Us.exe"), exe);
    Reject("32-bit game rejected", () => Installation.ValidateGame(game, manifest));
    exe[68] = 0x64; exe[69] = 0x86;
    File.WriteAllBytes(Path.Combine(game, "Among Us.exe"), exe);
    Directory.CreateDirectory(Path.Combine(game, "BepInEx"));
    Check("existing modded copy accepted", () => Installation.ValidateGame(game, manifest));
    Reject("original Steam folder rejected", () => new Installation(Path.Combine(root, "Steam", "steamapps", "common", "Among Us")));
    Reject("alternate Steam library rejected", () => new Installation(Path.Combine(root, "OtherLibrary", "STEAMAPPS", "common", "Among Us")));
    Reject("Steam descendant rejected", () => new Installation(Path.Combine(root, "steamapps", "common", "Among Us", "ModCopy")));

    foreach (var path in new[] { "../escape.dll", "BepInEx/core/../../escape.dll", "/root.dll", "C:/escape.dll",
                 "dotnet/bad:stream", "dotnet/NUL.dll", "dotnet/trailing. /file", "Among Us.exe", "BepInEx/plugins/TestHelper.dll" })
    {
        var bad = MakeZip(Guid.NewGuid() + ".zip", [path]);
        Reject("reject ZIP " + path, () => Installation.ExtractPackage(bad, Path.Combine(root, Guid.NewGuid().ToString()), default));
    }
    var duplicate = MakeZip("duplicate.zip", ["winhttp.dll", "WINHTTP.DLL"]);
    Reject("case-insensitive duplicate ZIP entry", () => Installation.ExtractPackage(duplicate, Path.Combine(root, "duplicate"), default));
    var symlink = Path.Combine(root, "link.zip");
    using (var file = ZipFile.Open(symlink, ZipArchiveMode.Create)) file.CreateEntry("dotnet/link").ExternalAttributes = unchecked((int)0xa1ff0000);
    Reject("ZIP symbolic link", () => Installation.ExtractPackage(symlink, Path.Combine(root, "link"), default));
    await RejectAsync("hash mismatch", () => Installation.VerifyPackageAsync(zip, manifest with { Sha256 = new string('0', 64) }, default));
    await RejectAsync("size mismatch", () => Installation.VerifyPackageAsync(zip, manifest with { Size = manifest.Size + 1 }, default));

    // A separate vanilla installation exists, but the updater only receives the copied folder.
    var vanilla = Path.Combine(root, "Steam", "steamapps", "common", "Among Us");
    Directory.CreateDirectory(vanilla);
    File.WriteAllBytes(Path.Combine(vanilla, "Among Us.exe"), exe);
    File.WriteAllText(Path.Combine(vanilla, "sentinel.txt"), "vanilla must never change");
    var vanillaBefore = Snapshot(vanilla);
    var installation = new Installation(game);
    Directory.CreateDirectory(Path.Combine(game, "BepInEx/plugins"));
    File.WriteAllText(Path.Combine(game, "BepInEx/plugins/AmongUsDogsRoles.dll"), "previous manually extracted mod");
    File.WriteAllText(Path.Combine(game, "BepInEx/plugins/FriendsOtherPlugin.dll"), "preserve this plugin");
    Directory.CreateDirectory(Path.Combine(game, "BepInEx/core"));
    File.WriteAllText(Path.Combine(game, "BepInEx/core/obsolete-loader.dll"), "obsolete");
    Directory.CreateDirectory(Path.Combine(game, "BepInEx/interop"));
    File.WriteAllText(Path.Combine(game, "BepInEx/interop/old-binding.dll"), "regenerate");
    Directory.CreateDirectory(Path.Combine(game, "BepInEx/config"));
    File.WriteAllText(Path.Combine(game, "BepInEx/config/settings.cfg"), "keep these settings");
    var originalCopy = Snapshot(game);
    Assert("manual copy needs no launcher record", installation.ReadInstalled() is null);
    Check("manual copy can be selected", installation.ValidateExistingCopy);
    Task Acquire(string target, CancellationToken token) { token.ThrowIfCancellationRequested(); File.Copy(zip, target); return Task.CompletedTask; }
    var first = await installation.InstallAsync(manifest, Acquire, null, default);
    Assert("first update committed in selected folder", installation.Root == game && installation.ReadInstalled()?.Release.Version == first.Release.Version);
    Assert("Steam folder byte-for-byte untouched", Snapshot(vanilla) == vanillaBefore);
    Assert("copied game executable unchanged", File.ReadAllBytes(Path.Combine(game, "Among Us.exe")).SequenceEqual(exe));
    Assert("manually installed settings preserved", File.ReadAllText(Path.Combine(game, "BepInEx/config/settings.cfg")) == "keep these settings");
    Assert("other plugins preserved", File.Exists(Path.Combine(game, "BepInEx/plugins/FriendsOtherPlugin.dll")));
    Assert("obsolete loader files removed from updated copy", !File.Exists(Path.Combine(game, "BepInEx/core/obsolete-loader.dll")));
    Assert("generated bindings rebuilt on next launch", !Directory.Exists(Path.Combine(game, "BepInEx/interop")));
    var backups = Directory.GetDirectories(root, "Copied Mod.dogs-backup-*");
    Assert("full original copy backed up", backups.Length == 1 && Snapshot(backups[0]) == originalCopy);
    var firstSnapshot = Snapshot(game);

    await RejectAsync("download failure", () => installation.InstallAsync(manifest,
        (_, _) => throw new IOException("simulated network failure"), null, default));
    Assert("download failure retains entire copy", Snapshot(game) == firstSnapshot);
    await RejectAsync("corrupt update", () => installation.InstallAsync(manifest with { Sha256 = new string('0', 64) }, Acquire, null, default));
    Assert("corrupt update retains entire copy", Snapshot(game) == firstSnapshot);
    using (var cancelled = new CancellationTokenSource())
        await RejectAsync("cancel before commit", () => installation.InstallAsync(manifest, (target, _) =>
        { File.Copy(zip, target); cancelled.Cancel(); return Task.CompletedTask; }, null, cancelled.Token));
    Assert("cancel retains entire copy", Snapshot(game) == firstSnapshot);
    using (var cancelled = new CancellationTokenSource())
        await RejectAsync("cancel after copying game", () => installation.InstallAsync(manifest, Acquire,
            new ImmediateProgress(message => { if (message == "Installing the mod…") cancelled.Cancel(); }), cancelled.Token));
    Assert("late cancel retains entire copy", Snapshot(game) == firstSnapshot);
    var incomplete = MakeZip("incomplete.zip", ["winhttp.dll"]);
    var incompleteManifest = manifest with { Size = new FileInfo(incomplete).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(incomplete))) };
    await RejectAsync("incomplete loader package", () => installation.InstallAsync(incompleteManifest,
        (target, _) => { File.Copy(incomplete, target); return Task.CompletedTask; }, null, default));
    Assert("incomplete package retains entire copy", Snapshot(game) == firstSnapshot);
    Assert("failed staging removed", Directory.GetDirectories(root, "Copied Mod.dogs-work-*").Length == 0);

    var newer = manifest with { Version = "0.3.1", AssetName = "AmongUsDogsRoles-0.3.1.zip" };
    Directory.Move(Path.Combine(root, "Steam"), Path.Combine(root, "Steam-is-unavailable"));
    var second = await installation.InstallAsync(newer, Acquire, null, default);
    Assert("update succeeds with original Steam installation absent", installation.ReadInstalled()?.Release.Version == second.Release.Version);
    Assert("game path stays unchanged after updates", installation.Root == game && File.Exists(Path.Combine(game, "Among Us.exe")));
    Assert("settings preserved on subsequent updates", File.ReadAllText(Path.Combine(game, "BepInEx", "config", "settings.cfg")) == "keep these settings");
    Assert("previous versions kept as backups", Directory.GetDirectories(root, "Copied Mod.dogs-backup-*").Length == 2);
    await RejectAsync("downgrade", () => installation.InstallAsync(manifest, Acquire, null, default));
    await RejectAsync("incompatible copied game left alone", () => installation.InstallAsync(newer with { GameVersion = "2027.1.1" }, Acquire, null, default));
    var secondSnapshot = Snapshot(game);
    var recoveryId = Guid.NewGuid().ToString("N");
    File.WriteAllText(game + ".dogs-update.json", recoveryId);
    Directory.Move(game, game + ".dogs-backup-" + recoveryId);
    installation.RecoverInterruptedUpdate();
    Assert("crash between folder moves restores complete backup", Snapshot(game) == secondSnapshot);
    File.WriteAllText(game + ".dogs-update.json", Guid.NewGuid().ToString("N"));
    installation.RecoverInterruptedUpdate();
    Assert("crash after commit leaves updated copy active", Snapshot(game) == secondSnapshot);
    File.WriteAllText(game + ".dogs-update.json", "../Steam");
    Reject("tampered recovery record rejected", installation.RecoverInterruptedUpdate);
    Assert("bad recovery record cannot change game", Snapshot(game) == secondSnapshot);
    File.Delete(game + ".dogs-update.json");
    Assert("vanilla bytes unchanged after all operations", Snapshot(Path.Combine(root, "Steam-is-unavailable", "steamapps", "common", "Among Us")) == vanillaBefore);

    var prefix = ReleaseClient.RepositoryUrl + "/releases/download/v0.3.0/";
    object[] Assets(string manifestUrl) => [
        new { name = ReleaseClient.ManifestName, browser_download_url = manifestUrl, size = 1L },
        new { name = manifest.AssetName, browser_download_url = prefix + manifest.AssetName, size = manifest.Size }];
    string ReleaseJson(bool prerelease, object[] assets) => JsonSerializer.Serialize(new { tag_name = "v0.3.0", draft = false, prerelease, assets });
    using (var client = new ReleaseClient(new FakeHttp(ReleaseJson(false, Assets(prefix + ReleaseClient.ManifestName)), manifest)))
        Assert("stable release discovery", (await client.LatestAsync(default)).Manifest.Version == manifest.Version);
    using (var client = new ReleaseClient(new FakeHttp(ReleaseJson(true, Assets(prefix + ReleaseClient.ManifestName)), manifest)))
        await RejectAsync("prerelease excluded", () => client.LatestAsync(default));
    using (var client = new ReleaseClient(new FakeHttp(ReleaseJson(false, []), manifest)))
        await RejectAsync("legacy release without manifest", () => client.LatestAsync(default));
    using (var client = new ReleaseClient(new FakeHttp(ReleaseJson(false, Assets("https://example.com/manifest")), manifest)))
        await RejectAsync("external manifest URL", () => client.LatestAsync(default));
    using (var client = new ReleaseClient(new FakeHttp(ReleaseJson(false, Assets(prefix + ReleaseClient.ManifestName)), manifest with { Version = "0.3.1", AssetName = "AmongUsDogsRoles-0.3.1.zip" })))
        await RejectAsync("tag/manifest mismatch", () => client.LatestAsync(default));
    using (var client = new ReleaseClient(new FakeHttp("{}", manifest, HttpStatusCode.Forbidden)))
        await RejectAsync("GitHub rate limit", () => client.LatestAsync(default));

    if (args.Length == 2)
    {
        var actual = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(args[1]), ReleaseManifest.Json)!;
        await Installation.VerifyPackageAsync(args[0], actual, default);
        // Remove only the fixture's metadata so the real package isn't seen as a downgrade.
        File.Delete(Path.Combine(game, ".dogs-launcher-installed.json"));
        await installation.InstallAsync(actual, (target, _) =>
        { File.Copy(args[0], target); return Task.CompletedTask; }, null, default);
        Assert("real release archive installs", true);
    }
    Console.WriteLine($"PASS: {passed} launcher checks.");
}
finally
{
    // Only the unique test root created above is removed.
    Directory.Delete(root, true);
}

string MakeZip(string filename, string[] entries)
{
    var path = Path.Combine(root, filename);
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var name in entries) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("fixture contents"); }
    return path;
}
string Snapshot(string directory) => string.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
    .OrderBy(path => path, StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
void Assert(string name, bool result) { if (!result) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Check(string name, Action action) { action(); Assert(name, true); }
void Reject(string name, Action action)
{
    try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or OperationCanceledException) { Assert(name, true); return; }
    throw new Exception("FAIL: accepted " + name);
}
async Task RejectAsync(string name, Func<Task> action)
{
    try { await action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or OperationCanceledException) { Assert(name, true); return; }
    throw new Exception("FAIL: accepted " + name);
}

sealed class FakeHttp(string releaseJson, ReleaseManifest manifest, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(request.RequestUri!.Host == "api.github.com"
            ? releaseJson : JsonSerializer.Serialize(manifest, ReleaseManifest.Json)) });
}

sealed class ImmediateProgress(Action<string> report) : IProgress<string>
{
    public void Report(string value) => report(value);
}
