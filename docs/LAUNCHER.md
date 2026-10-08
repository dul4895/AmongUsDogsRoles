# AmongUsDogsRoles launcher

The Windows launcher gives players one **Update & Play** button. It checks the
latest stable GitHub release each time, downloads and verifies its mod package,
updates the player's existing copied mod folder, then launches that folder's
executable. It does not discover, read, copy from, or modify the original Steam
installation. Launching Among Us through Steam continues to launch vanilla.
Players need their own legitimate copied game and Steam running for authentication.
No game files are distributed.

## For friends

1. Download **AmongUsDogsRoles-Launcher.exe** once and keep it somewhere convenient,
   such as your Desktop, outside the mod folder. It includes its runtime;
   no .NET installation is needed.
2. Open it and use **Browse** to select the copied folder your group already uses
   for the mod, containing **Among Us.exe**. The launcher remembers that selection.
   Folders inside any `steamapps` directory are rejected to protect vanilla installs.
3. Keep Steam running and click **Update & Play**. It updates this mod copy in the
   same location, preserving settings and keeping a backup. The first launch after
   an update can take several minutes while bindings regenerate.
4. Use that same launcher whenever you play. Updates are applied before launch.

Everyone in a lobby still needs the same mod and game versions. Close Among Us
before updating. The launcher verifies the exact game version and x64 executable;
it cannot turn an old 32-bit game into the new 64-bit game. If the copied game is
incompatible, the update stops without changing it. Refreshing the copied base game
is a separate manual step; the launcher never consults or synchronizes it with
Steam's installed game. Steam updates to vanilla do not alter the mod copy.

**Play installed** skips the online check, including when GitHub is unavailable.
It does not guarantee multiplayer compatibility with friends on another version.
Existing manually extracted mod copies work with **Play installed** immediately,
without any launcher release record or download. Older copies can still be played
even when their architecture is incompatible with a newer mod release.
`BepInEx/config`, other plugins and user files are preserved across updates.
The packaged loader/runtime directories are replaced, and generated `BepInEx/interop`
and `BepInEx/cache` are regenerated on the next game launch. Backups retain the complete
previous folder. The game executable and other base game files come only from the
selected copy and are preserved byte-for-byte.

## Build and test locally

Version 0.3.0 is released as stable without an additional friend-group playtest.
Automated updater checks and the earlier local multiplayer checks are documented;
this does not establish that every friend's PC or antivirus will accept the launcher.
The existing server compatibility caveat in [the 0.3.0 notes](UPDATE_0.3.0.md)
also applies; a launcher does not update the multiplayer server.

Build from PowerShell 7 with the .NET 8 SDK:

```powershell
./scripts/build-launcher.ps1
```

The standalone executable is written to
`artifacts/launcher/AmongUsDogsRoles-Launcher.exe`. The build runs updater checks
and renders the Windows UI before publishing. The output folder also includes
licenses, runtime notices and an archive of the exact launcher source.
GitHub Actions also builds and
uploads the launcher as a workflow artifact.

`scripts/package.ps1` now creates `artifacts/launcher-release.json` beside the mod
ZIP. To generate a manifest for an existing package without rebuilding it:

```powershell
./scripts/new-launcher-manifest.ps1 -Package artifacts/AmongUsDogsRoles-0.3.0.zip
```

Keep the manifest and its matching ZIP together. In the launcher, click
**Install test package…**, choose that manifest, then **Play installed**.
Local test packages are not uploaded or offered as automatic updates.
Only use manifests and packages from the project publisher.

## Publish future updates

After testing a release:

1. Bump the mod version and update the version, game version and filenames in the
   packaging scripts and installation notes. Build with `scripts/package.ps1`.
2. Build the launcher with `scripts/build-launcher.ps1` if distributing a new
   launcher. Include LICENSE, NOTICE and matching source when distributing it;
   GitHub source archives are sufficient only when the release commit contains
   that exact launcher source. Self-contained .NET runtime notices are included
   in the launcher output folder.
3. Prepare a draft GitHub release tagged `v0.3.0` or `0.3.0` (using the actual
   version). Upload `AmongUsDogsRoles-<version>.zip`, `launcher-release.json`, and
   the launcher EXE for first-time players. Attach matching source for any changes
   not in the release commit. Finish uploading all assets before publication.
4. Publish it as a stable release and mark it **latest** when it is ready for
   everyone. Drafts and prereleases are not automatically installed. Keep release
   assets immutable; use a new version for changes.

The manifest records the package name, byte size, SHA-256 hash, supported Steam
game version and architecture. The launcher accepts downloads only from this
repository's release URLs. Checksums catch corruption and mismatched assets;
GitHub repository access controls remain the trust boundary. No account/token is
needed for public releases. API limits and missing manifests produce an error
while leaving the installed copy available.

The launcher deliberately reports an older release without `launcher-release.json`
as unsupported. It never guesses compatibility from a ZIP filename. New releases
with a different manifest schema require a new launcher. The launcher itself is
updated by downloading a replacement EXE; mod updates are automatic on **Update & Play**.

## Storage and recovery

Only the remembered folder selection lives under `%LOCALAPPDATA%\AmongUsDogsRoles`
in `selected-copy.txt`. The game remains at the path you selected. The launcher
records the installed release in `.dogs-launcher-installed.json` inside that copy.

For a selected folder named `AmongUsDogsRoles`, an update prepares a sibling
`AmongUsDogsRoles.dogs-work-<id>` folder using only the existing mod copy and the
downloaded package. Once verification and preparation finish, it renames the old
folder to `AmongUsDogsRoles.dogs-backup-<id>` and moves the prepared copy to the
original path. Existing game shortcuts therefore still point at the updated copy.
A sibling `.dogs-update.json` journal allows automatic recovery if the launcher
is interrupted between the two renames. A `.dogs-update.lock` file prevents
concurrent updates; the empty file can remain after an update.

Backups are retained. Use **Open mod folder**, then browse to its parent to inspect
them. Updates need space for a full additional game copy, the downloaded archive
and extracted mod files. The launcher must be outside the mod folder so Windows
can rename the folder while the launcher is running.

After a successful update, with both launcher and game closed and no pending
`.dogs-update.json`, older `.dogs-backup-*` and leftover `.dogs-work-*` folders may
be removed. To roll back manually, close both programs, rename the current mod folder
to keep it safe, then rename a backup to the original mod-folder name. Use
**Play installed** to launch it without immediately updating again. The backup
contains settings as they were before that update.

If you used the first experimental launcher build, browse explicitly to the copy
you want to keep, including an `install-*` folder created by that build if desired.
The launcher does not reuse that build's saved Steam source path.

## Validation

```powershell
./scripts/build-launcher.ps1 -SkipPublish
# Also exercise a real package against a disposable game fixture:
dotnet run --project tests/AmongUsDogsRoles.Launcher.Checks -c Release -- `
  artifacts/AmongUsDogsRoles-0.3.0.zip artifacts/launcher-release.json
```

Checks cover release discovery, prerelease exclusion, missing manifests, unsafe
download URLs, SHA-256/size verification, malformed archive paths, extra plugins,
game version/architecture, cancellation, failed-update recovery, config carryover,
refusing downgrades, rejecting Steam library paths, and updating with the original
Steam installation absent. Tests compare vanilla fixture files before and after
updates and simulate interrupted directory swaps. Fixture checks do not run the
actual game. Before sharing, test adopting an existing copy, Update & Play,
Play installed without internet, an update
with settings carried over, and a multiplayer session on real Steam installations.
