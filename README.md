# AmongUsDogsRoles

Among Us with ten extra roles and familiar vanilla gameplay.

The source is now **0.2.1**, with Kidnapper and Faker fixes, optional meeting
guesses, configurable post-meeting kill cooldowns, and the Hacker role.
See [the changes and validation notes](docs/CHANGES_0.2.1.md).

## Setup

Requires **Among Us 2026.8.18 on Windows/Steam**. Everyone in the lobby must use the same game and mod versions.

1. Download the ZIP from the [latest published release](https://github.com/dul4895/AmongUsDogsRoles/releases/latest). Check its version: source updates do not automatically publish a new release ZIP. To install 0.2.1 from source, use the build instructions below.
2. In Steam, open **Among Us → Manage → Browse local files**. Copy the clean, unmodded game folder somewhere else and name the copy **AmongUsDogsRoles**.
3. Extract the ZIP's contents into that copy. The **BepInEx** and **dotnet** folders must sit directly beside **Among Us.exe**.
4. Keep Steam running and launch **Among Us.exe** from the copied folder. The first launch can take several minutes.

The **dogs** server is added and selected automatically. Use **Online** to host a lobby or join with a friend's code. Open **New roles** in the lobby or during a match for the full role guide.

## New roles

| Role | Team | Ability |
| --- | --- | --- |
| **Kidnapper** | Impostor | Capture and drag someone, then execute or release them. Captives escape when the timer expires. You cannot vent while dragging. |
| **Kamikaze** | Impostor | Explode to kill everyone nearby, including yourself and teammates. If nobody survives, the game is a draw. |
| **Consigliere** | Impostor | Investigate nearby players to privately learn their exact roles. |
| **Escapist** | Impostor | Mark a location and recall to it later. Recalling consumes the mark; meetings clear it. |
| **Faker** | Impostor | Fake a reportable death once per game. While faking, you count as dead and cannot act or vote. Unfake outside meetings to return alive and regain normal abilities. **CAUTION:** if the last remaining impostor is faking when a meeting starts, impostors lose immediately. |
| **Hacker** | Impostor | Freeze Vitals for 15 seconds, distort occupied-room Admin counts, and swap paired players' names and colors in cameras. Meetings cancel the hack. |
| **Veteran** | Crewmate | Activate a limited-use alert to kill players who directly target you. It does not protect against a Kamikaze explosion. |
| **Sheriff** | Crewmate | Shoot an impostor or the Jester. Shooting a crewmate kills only you. |
| **Coroner** | Crewmate | Sniff bodies to get an arrow toward the killer for up to 10 seconds, ending within 1.5 screen heights. Sniff again to renew it. You cannot report bodies. Sniffing a Faker's body points to that body. |
| **Jester** | Neutral | Get voted out to win alone. Your tasks are fake and cannot be completed. You count as a crewmate for the normal impostor win condition. |

The host can enable **Impostors can guess roles in meetings** (off by default).
Living impostors can guess a living non-impostor's exact role: a correct guess
kills the target, and a wrong guess kills the guesser. Faking Fakers cannot guess
or see other players' hidden roles. **Kill cooldown after meetings** defaults
to 30 seconds and applies to impostors and Sheriff independently of ability tuning.

## Build and test

Install the .NET 8 SDK and run `./scripts/build.ps1` from PowerShell. It builds
`src/AmongUsDogsRoles/bin/Release/net6.0/AmongUsDogsRoles.dll` and runs the
gameplay rule checks. Package dependencies remain pinned in the lock file.

To create a separate modded copy of your clean Steam game, run:

```powershell
./scripts/install.ps1 -Destination "$env:USERPROFILE/Games/AmongUsDogsRoles"
```

For a non-default Steam library, also pass `-GamePath` with the clean game
folder. The installer downloads and verifies the pinned loader. Everyone
must install the same mod version.

See [testing instructions](docs/TESTING.md) for the optional local test helper
and multiplayer regression scenarios. Generated test results remain under
ignored `artifacts/`; test plugins must not be included in player distributions.
