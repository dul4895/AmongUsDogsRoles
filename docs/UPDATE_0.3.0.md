# AmongUsDogsRoles 0.3.0 — Among Us v19

Requires **Steam 2026.9.29 / v19.0.0, Windows x64**. This is the September 29
Influencer update. Every player needs the same game and mod versions.

## Install

The release includes **AmongUsDogsRoles-Launcher.exe**. Keep it outside the game
folder, select your existing compatible copied mod folder, and use **Update & Play**.
It updates only that copy and never uses the original Steam game folder. See
[launcher instructions](https://github.com/dul4895/AmongUsDogsRoles/blob/v0.3.0/docs/LAUNCHER.md). Copies from the old 32-bit game need a manual
base-game upgrade before they can use this release. The launcher is unsigned and
may trigger Windows reputation warnings.

For manual installation:

1. Update your clean Among Us installation in Steam.
2. Copy that clean game folder to a new folder, such as `AmongUsDogsRoles-0.3.0`.
3. Extract **AmongUsDogsRoles-0.3.0.zip** into the new game copy. `BepInEx`,
   `dotnet`, and `winhttp.dll` must sit beside `Among Us.exe`.
4. Keep Steam running and launch the game from that new folder. The first launch
   generates new bindings and may take several minutes.

Do not copy this over a previous 32-bit mod installation, and do not reuse its
generated `BepInEx/interop` files. The ZIP does not contain the game itself.
The testing helper and Reactor.Debugger are development-only and are not bundled.

Alternatively, build the source with `scripts/build.ps1` and use
`scripts/install.ps1 -Destination <new-empty-folder>`. For a different Steam
library, pass `-GamePath <clean-game-folder>` as well.

## Compatibility changes

- Rebuilt against the 2026.9.29 game bindings and updated the dependency lock.
- Replaced the 32-bit loader with the verified 64-bit BepInEx/runtime bundle.
- Pinned Reactor 2.5.1-ci.400, including its string-ID compatibility fix.
- Adapted MiraAPI custom role counts and chances to the new V12 role-options format. This restores natural role allocation, synchronized settings, and meeting guesses.
- Added a guard for early name-color lookups before player/game initialization.
- Updated the installer to check both game version and executable architecture.
- Influencer uses the vanilla implementation (internally `SpiritGuide`). Ghost
  roles remain excluded from living-role allocation and meeting guesses.

The loader archive and Reactor package are SHA-256 checked. The package includes
the mod source, dependency source/reference archives, and license notices.

## Verification

Published as stable without an additional friend-group playtest. The launcher has
automated update/recovery checks and window-render checks; antivirus acceptance
has not been verified across players' computers. The checks below describe the
earlier local multiplayer verification, not a new Internet-server test.

Verified on October 4, 2026 with Steam 2026.9.29 / v19.0.0:

- Release build: zero warnings/errors; 69 gameplay checks and 201,000 allocation/count trials passed.
- Four real game clients on Polus: all 7 v19/Influencer checks and all 28 gameplay regression checks passed.
- Custom settings synchronized and custom roles were assigned naturally. A killed custom Crewmate became an Influencer, opened the native image panel, and sent an image received by another client. Faker's fake death did not grant a ghost role.
- Regression coverage included Kidnapper capture/release/execute, Coroner tracking, Hacker Vitals/cameras/Admin, Faker behavior, meeting cooldowns, and host/remote meeting guesses.
- The final name-color initialization guard passed in two restarted clients. The earlier name-color null exception did not recur.
- The installer accepted the installed game's version/architecture and refused a nonempty destination without overwriting it.

The test environment still logs upstream MiraAPI initialization messages and an Innersloth FileIO null-path exception during scene transitions. These did not block the checks above; this is not a claim of an error-free runtime or every-map coverage.

**Online limitation:** the dogs server rejected v19 with "Your client is too new, please update your Impostor server to play." Its Impostor server needs v19 support before private Internet play can be verified. Local multiplayer passes; the client update cannot resolve that server rejection.
