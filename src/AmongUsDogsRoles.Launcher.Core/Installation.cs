using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmongUsDogsRoles.Launcher;

public sealed record InstalledRelease(ReleaseManifest Release);

// Root is the player's existing, independent mod folder. Stage from that folder only,
// then swap directories, keeping the previous folder as a backup. Never locate Steam.
public sealed class Installation
{
    public string Root { get; }
    private const string StateName = ".dogs-launcher-installed.json";
    private string StatePath => Path.Combine(Root, StateName);
    private string JournalPath => Root + ".dogs-update.json";
    public Installation(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        ValidateSelectionPath(Root);
    }

    public static void ValidateSelectionPath(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Choose your existing copied mod folder first.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (Directory.GetParent(path) is null) throw new InvalidOperationException("Choose the copied game folder, not a drive root.");
        // Steam installs games beneath steamapps on every library drive. Reject the
        // path without opening Steam's registry entries, manifests or game files.
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("steamapps", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose your separate copied mod folder outside Steam's steamapps libraries. The launcher will not use the original Steam installation.");
        RejectLinks(path);
    }

    public InstalledRelease? ReadInstalled()
    {
        RecoverInterruptedUpdate();
        return ReadState();
    }

    public void ValidateExistingCopy()
    {
        ValidateSelectionPath(Root);
        foreach (var file in new[] { "Among Us.exe", "GameAssembly.dll", "Among Us_Data/globalgamemanagers" })
            if (!File.Exists(Path.Combine(Root, file)))
                throw new InvalidOperationException("Choose your complete copied Among Us folder containing Among Us.exe.");
    }

    public static void EnsureGameClosed()
    {
        var processes = Process.GetProcessesByName("Among Us");
        try { if (processes.Length != 0) throw new InvalidOperationException("Close Among Us before updating or starting another copy."); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static void ValidateGame(string folder, ReleaseManifest release)
    {
        release.Validate();
        ValidateSelectionPath(folder);
        var exe = Path.Combine(folder, "Among Us.exe");
        var versionFile = Path.Combine(folder, "Among Us_Data", "globalgamemanagers");
        if (!File.Exists(exe) || !File.Exists(versionFile) || !File.Exists(Path.Combine(folder, "GameAssembly.dll")))
            throw new InvalidOperationException("Select your complete copied game folder containing Among Us.exe.");
        var data = Encoding.UTF8.GetString(File.ReadAllBytes(versionFile));
        if (!Regex.IsMatch(data, @"(?<![0-9.])" + Regex.Escape(release.GameVersion) + @"(?![0-9.])"))
            throw new InvalidOperationException($"Mod {release.Version} needs Among Us {release.GameVersion} (64-bit). Your copied game version does not match. No game files have changed. You can still use Play installed; upgrading the base game is a separate manual step.");
        using var stream = File.OpenRead(exe);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Invalid game executable.");
        stream.Position = 60;
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 6) throw new InvalidDataException("Invalid game executable header.");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664)
            throw new InvalidOperationException("The launcher requires the 64-bit Windows game. Old 32-bit installations cannot be upgraded in place.");
    }

    public async Task<InstalledRelease> InstallAsync(ReleaseManifest release,
        Func<string, CancellationToken, Task> acquirePackage, IProgress<string>? progress, CancellationToken token)
    {
        release.Validate();
        EnsureGameClosed();
        ValidateSelectionPath(Root);
        if (Environment.ProcessPath is { } executable && Inside(executable, Root))
            throw new InvalidOperationException("Keep the launcher EXE outside your mod folder (for example on your Desktop), then reopen it to update.");
        using var installLock = AcquireLock();
        RecoverUnderLock();
        var previous = ReadState();
        if (previous is not null && System.Version.Parse(previous.Release.Version) > System.Version.Parse(release.Version))
            throw new InvalidOperationException("The available release is older than your installed version. Automatic downgrades are disabled.");
        ValidateGame(Root, release);
        var id = Guid.NewGuid().ToString("N");
        var work = Root + ".dogs-work-" + id;
        var backup = Root + ".dogs-backup-" + id;
        var destination = Path.Combine(work, "game");
        Directory.CreateDirectory(work);
        try
        {
            var archive = Path.Combine(work, "package.zip");
            await acquirePackage(archive, token);
            progress?.Report("Verifying the download…");
            await VerifyPackageAsync(archive, release, token);
            var payload = Path.Combine(work, "payload");
            await Task.Run(() => ExtractPackage(archive, payload, token), token);
            RequirePayload(payload);
            progress?.Report("Preparing an update from your existing mod folder…");
            await Task.Run(() => CopyTree(Root, destination, token, Root), token);
            ValidateGame(destination, release);
            progress?.Report("Installing the mod…");
            await Task.Run(() => CopyTree(payload, destination, token), token);
            var installed = new InstalledRelease(release);
            File.WriteAllText(Path.Combine(destination, StateName), JsonSerializer.Serialize(installed, ReleaseManifest.Json));
            token.ThrowIfCancellationRequested();
            EnsureGameClosed();
            // The journal is durable before the first rename. A crash between the
            // two moves restores the backup on the next launcher operation.
            using (var journal = new FileStream(JournalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                journal.Write(Encoding.UTF8.GetBytes(id));
                journal.Flush(true);
            }
            try
            {
                Directory.Move(Root, backup);
                Directory.Move(destination, Root);
            }
            catch
            {
                RecoverUnderLock();
                throw;
            }
            // The new directory is committed. Failure to remove the journal is
            // harmless: recovery sees Root and leaves the committed version intact.
            try { File.Delete(JournalPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            progress?.Report($"Mod {release.Version} is ready.");
            return installed;
        }
        finally
        {
            TryDeleteWorkDirectory(work);
        }
    }

    private InstalledRelease? ReadState()
    {
        if (!File.Exists(StatePath)) return null;
        var state = JsonSerializer.Deserialize<InstalledRelease>(File.ReadAllText(StatePath), ReleaseManifest.Json)
            ?? throw new InvalidDataException("Empty launcher installation record.");
        state.Release.Validate();
        return state;
    }

    private FileStream AcquireLock() => new(Root + ".dogs-update.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    public void RecoverInterruptedUpdate()
    {
        ValidateSelectionPath(Root);
        if (!File.Exists(JournalPath)) return;
        EnsureGameClosed();
        using var installLock = AcquireLock();
        RecoverUnderLock();
    }

    private void RecoverUnderLock()
    {
        if (!File.Exists(JournalPath)) return;
        var id = File.ReadAllText(JournalPath);
        if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new InvalidDataException("Invalid update recovery record. No folders were changed.");
        if (!Directory.Exists(Root))
        {
            var backup = Root + ".dogs-backup-" + id;
            RejectLinks(backup);
            if (!Directory.Exists(backup)) throw new IOException($"Cannot recover the copied game. Its backup is missing: {backup}");
            Directory.Move(backup, Root);
        }
        File.Delete(JournalPath);
    }

    public static async Task VerifyPackageAsync(string archive, ReleaseManifest release, CancellationToken token)
    {
        release.Validate();
        if (new FileInfo(archive).Length != release.Size) throw new InvalidDataException("Package size mismatch. Nothing was installed.");
        await using var stream = File.OpenRead(archive);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package checksum mismatch. Nothing was installed.");
    }

    private static bool AllowedFile(string name) =>
        name.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("dotnet/", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("licenses-and-source/", StringComparison.OrdinalIgnoreCase) ||
        new[] { "BepInEx/plugins/AmongUsDogsRoles.dll", "BepInEx/plugins/Reactor.dll", "BepInEx/plugins/MiraAPI.dll",
            "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "LICENSE", "NOTICE", "START_HERE.md" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    public static void ExtractPackage(string archive, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        if (zip.Entries.Count > 20000) throw new InvalidDataException("Too many files in package.");
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            var directory = name.EndsWith('/');
            var parts = name.TrimEnd('/').Split('/');
            if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                    p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) ||
                ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Unsafe path in release package.");
            if (directory) continue;
            if (!AllowedFile(name) || !names.Add(name)) throw new InvalidDataException($"Unexpected or duplicate package file: {name}");
            expanded = checked(expanded + entry.Length);
            if (expanded > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Expanded package is too large.");
            var target = Path.GetFullPath(Path.Combine(destination, name));
            if (!Inside(target, destination)) throw new InvalidDataException("Package path escapes installation.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }
    }

    private static void RequirePayload(string payload)
    {
        foreach (var relative in new[] { "BepInEx/plugins/AmongUsDogsRoles.dll", "BepInEx/plugins/Reactor.dll",
                     "BepInEx/plugins/MiraAPI.dll", "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "winhttp.dll", "doorstop_config.ini" })
            if (!File.Exists(Path.Combine(payload, relative))) throw new InvalidDataException($"Incomplete package: {relative} is missing.");
        if (!Directory.Exists(Path.Combine(payload, "dotnet")) || !Directory.EnumerateFiles(Path.Combine(payload, "dotnet"), "*", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("The package is missing the bundled runtime.");
    }

    public void Launch()
    {
        EnsureGameClosed();
        var installed = ReadInstalled();
        ValidateExistingCopy();
        if (installed is not null) { ValidateGame(Root, installed.Release); RequirePayload(Root); }
        else if (!File.Exists(Path.Combine(Root, "BepInEx/plugins/AmongUsDogsRoles.dll")))
            throw new InvalidOperationException("This copy has no AmongUsDogsRoles mod yet. Install a package first.");
        // Existing manually installed copies can launch without release metadata,
        // even if an older game architecture prevents installing the latest mod.
        Process.Start(new ProcessStartInfo(Path.Combine(Root, "Among Us.exe")) { WorkingDirectory = Root, UseShellExecute = true });
    }

    private static bool Inside(string path, string parent) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RejectLinks(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked game/data folders are not supported. Choose a regular folder.");
    }

    private static void CopyTree(string source, string destination, CancellationToken token, string? gameRoot = null)
    {
        token.ThrowIfCancellationRequested();
        RejectLinks(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (gameRoot is not null)
            {
                var relative = Path.GetRelativePath(gameRoot, entry.FullName).Replace('\\', '/');
                if (new[] { "BepInEx/core", "BepInEx/interop", "BepInEx/cache", "dotnet", "licenses-and-source", StateName }
                    .Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
            }
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files are not supported in game folders.");
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo) CopyTree(entry.FullName, target, token, gameRoot);
            else File.Copy(entry.FullName, target, true);
        }
    }

    private void TryDeleteWorkDirectory(string path)
    {
        // Only delete this installation's generated sibling staging directory.
        if (!Regex.IsMatch(path, "^" + Regex.Escape(Root + ".dogs-work-") + "[a-f0-9]{32}$")) return;
        try
        {
            RejectLinks(path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException) { /* Interrupted staging is harmless and can be removed later. */ }
        catch (UnauthorizedAccessException) { }
    }
}
