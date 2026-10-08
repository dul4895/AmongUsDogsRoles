# Development testing

Run `./scripts/build.ps1` for the production build and pure gameplay checks.
These cover targeting, cooldowns, win conditions, Coroner tracking, meeting
guess eligibility, allocation probabilities, and Hacker pairing/Admin rules.

## Optional game clients

Run `./scripts/setup-testing.ps1` from PowerShell after building. It downloads
the pinned Reactor 2.5.1 debugger source, builds the test tools, and creates
two separate game copies under `.tools/` using the default Steam installation.
For a different Steam path, first run `scripts/install.ps1` with `-GamePath`
and `-Destination .tools/playtest`.

Use **Test Roles.cmd** for one game or **Test Multiplayer.cmd** for two.
F2 opens the role testing panel; F1 opens Reactor.Debugger. Use Practice for
solo testing or Local for multiplayer. Never distribute these test plugins
to ordinary players.

## Scripted multiplayer checks

Use PowerShell 7 for the scripted test driver. After setup:

```powershell
./scripts/launch-hardening.ps1 -Clients 4
./scripts/qa.ps1 1 host
2..4 | ForEach-Object { ./scripts/qa.ps1 $_ join }
./scripts/playtest-fixes-checks.ps1
```

Run a separate nine-client session for the Kidnapper scenarios:

```powershell
./scripts/launch-hardening.ps1 -Clients 9
./scripts/qa.ps1 1 host
2..9 | ForEach-Object { ./scripts/qa.ps1 $_ join }
./scripts/kidnapper-start-checks.ps1 -Clients 9
```

Close the first session before preparing another. Use the same mod and helper
build on every client. The launch script waits for each client's startup;
the Kidnapper driver waits for all nine rosters before starting the match.
The scenarios expect Polus and test-controlled roles and settings.

Results are written to `artifacts/test-results/`. The test copies, dependency
caches, raw results, and logs are ignored by Git. Historical development results
are summarized in [the change notes](CHANGES_0.2.1.md); they are not a claim
that every scenario has been rerun on a newly built release.

## Version 19 / Influencer checks

Use fresh 64-bit v19 game copies. Existing pre-v19 copies under `.tools/` must
be replaced with fresh installations before using the testing scripts.
In a fresh four-client lobby, run `./scripts/version19-checks.ps1` before
`./scripts/playtest-fixes-checks.ps1`. This configures guaranteed custom roles
and Influencer for the test, verifies natural allocation, kills a real custom
Crewmate, sends a native Influencer image to another client, and checks Faker.
Afterwards, disconnect the clients, host a fresh lobby in the same process,
and run `./scripts/qa.ps1 1 role-settings-restore` before closing the host to
restore the role rates saved at the start of the scenario.

`./scripts/qa.ps1 1 v19-name-color-null` checks the guarded Mira name-color
fallback in a connected test session. The October 4 results and current dogs
server limitation are summarized in [the v19 notes](UPDATE_0.3.0.md).
