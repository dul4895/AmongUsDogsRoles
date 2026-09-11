# AmongUsDogsRoles

Among Us with nine extra roles and familiar vanilla gameplay.

## Setup

Requires **Among Us 2026.8.18 on Windows/Steam**. Everyone in the lobby must use the same game and mod versions.

1. Download **AmongUsDogsRoles-0.1.21.zip** from the [latest release](https://github.com/dul4895/AmongUsDogsRoles/releases/latest).
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
| **Veteran** | Crewmate | Activate a limited-use alert to kill players who directly target you. It does not protect against a Kamikaze explosion. |
| **Sheriff** | Crewmate | Shoot an impostor or the Jester. Shooting a crewmate kills only you. |
| **Coroner** | Crewmate | Sniff bodies to get an arrow toward the killer. You cannot report bodies. Sniffing a Faker's body points to that body. |
| **Jester** | Neutral | Get voted out to win alone. Your tasks are fake and cannot be completed. You count as a crewmate for the normal impostor win condition. |
