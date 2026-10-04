# Changes in 0.2.1

This update brings the 0.2.0 gameplay changes and the 0.2.1 Kidnapper follow-up
into the existing AmongUsDogsRoles project. The plugin ID remains
`dogs.amongusdogsroles`; assembly names, resource paths, and test helpers use
the same project identity.

- Kidnapper: separate actual HUD visibility from MiraAPI's role filtering so
  an earlier hide callback cannot strand Drag. Wait for the host's capture
  decision before consuming cooldown, reject same-frame Release/Execute, and
  log capture eligibility and rejection reasons.
- Faker: fake death no longer grants spectator role visibility.
- Meeting guesses: an off-by-default host option lets living impostors guess
  living non-impostors' exact roles. Correct guesses kill the target; incorrect
  guesses kill the guesser. Faking Fakers and ghosts cannot submit guesses.
- Coroner: tracking expires after 10 seconds or within 1.5 gameplay screen
  heights of the tracked target. Another sniff can renew it.
- Meeting cooldown: impostor Kill and Sheriff Shoot use the host's separate
  post-meeting cooldown, default 30 seconds, adjustable from 0 to 60 seconds.
- Role allocation: each configured copy rolls its displayed percentage;
  guaranteed roles take priority when there are fewer available team slots.
- Hacker: freeze Vitals, redistribute Admin counts between occupied rooms,
  and swap camera names/colors for randomly paired players. The default hack
  lasts 15 seconds; meetings cancel it.

## Validation

The development version passed 69 pure gameplay checks plus 201,000
allocation/count invariant trials. Its integration testing covered 31 checks
on four local clients and 43 focused Kidnapper checks on nine local clients
on Polus. The Kidnapper reproduction used an injected stale role-filter state;
it does not prove that this state caused the original human playtest failures.

Those live-game results predate this transfer. The transferred production mod
builds with zero warnings/errors, and all 69 gameplay checks plus 201,000
allocation/count trials pass. The renamed test helper also builds, with its
existing nullable warning in HardeningBridge. The integration scenarios are
included for reproduction; raw snapshots, logs, and recordings are not tracked.
Nine human Internet clients, arbitrary latency, and every map remain unverified.
