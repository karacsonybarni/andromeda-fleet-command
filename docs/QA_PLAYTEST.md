# Campaign QA and pacing playtest

## Full-campaign human pacing protocol

The game now records every completed single-player campaign attempt automatically.
Victory and defeat debriefs show active battle time, the mission target, variance,
and how many of the 24 missions have a measured victory. Multiplayer results are
excluded. Pauses, mission selection, briefings, and debriefs are not counted, so
the report measures encounter time rather than a player's reading or break time.

After each terminal result, two files are atomically updated in Godot's user-data directory:

- `campaign-pacing.json` contains the bounded machine-readable attempt history;
- `campaign-pacing-report.md` is the shareable human-readable report.

Typical directories are `%APPDATA%\Godot\app_userdata\Andromeda Fleet Command\`
on Windows and `~/.local/share/godot/app_userdata/Andromeda Fleet Command/` on Linux.
The report withholds a full-campaign duration until all 24 missions have a recorded
victory, preventing partial runs from being presented as evidence for the 6–8-hour promise.

For a useful external pacing pass:

1. Move any existing `campaign-pacing.json` aside so the pass starts at 0/24 measured missions.
2. Play on default settings without using debug or benchmark modes. Read briefings normally;
   do not rush simply to improve the timer.
3. Finish every mission in order. Keep defeats: retry data is part of difficulty evidence.
4. After Mission 24, attach `campaign-pacing-report.md` to the playtest feedback together
   with hardware, input method, prior strategy-game experience, and perceived difficulty.
5. Repeat with several players before changing targets. Tune missions whose latest times,
   retry counts, or qualitative feedback consistently disagree with their authored budget.

The 450-minute total remains an authored target until this protocol has external human
results. Automated simulation victories prove objective reachability and regression safety;
they are intentionally not substituted for human pacing evidence.

## Historical Act I automated QA — 2026-07-15

## Scope and method

This historical pass covered the original three-mission Black Sun incident, now Act I
of the 24-mission *Crown of Andromeda* campaign. It used the real fixed-step battle simulation, mission catalog, tutorial tracker,
manual helm/fire inputs, rule-based command parser, command dispatcher, ship switching,
tactical abilities, mission objectives, protected-ship failure rules, and sequential
campaign unlocks. It ran from clean Linux CI processes at accelerated execution speed.

The `simulated` time below is the in-game battle clock. The `wall` time is how long the
accelerated integration run took on the CI worker; it is not representative of player
frame rate. The same final run was executed independently by the normal CI job and the
renderer-driven QA job to verify cross-process determinism.

This was not a native Windows GUI playtest. Renderer captures and packaged startup smoke
were validated separately, but they are not counted as completed gameplay.

## Final successful campaign run

| Mission | Outcome | Simulated | CI wall | Orders | Switches | Abilities | Protected hull | Allied survivors |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| First Command | Victory | 15.1s | 25ms | 3 | 1 | 2 | Flagship 4% | 2/2 |
| Broken Shield | Victory | 17.0s | 41ms | 1 | 1 | 2 | Carrier One 6% | 4/4 |
| Black Sun | Victory | 15.1s | 63ms | 1 | 5 | 4 | Flagship 85% | 3/4 |

Total in-game campaign battle time: **47.2 seconds**. The Captain's Drill completed in
order during First Command before the mission victory. All three missions unlocked and
completed sequentially.

The independent renderer job produced the same simulated times, protected hull values,
outcomes, and survivor counts. Its wall times were 27ms, 52ms, and 79ms.

## Tactics used

- **First Command:** switched to Frigate Two, supplied manual helm/fire input, issued the
  recommended intercept order, activated overdrive, then ordered the fleet to focus the
  raider leader.
- **Broken Shield:** took direct control of Carrier One and kited vertically along the
  left side while firing and launching drone volleys. The other three ships focused the
  bomber wing.
- **Black Sun:** manually piloted the flagship while the other ships executed the
  advertised enemy-flagship focus order. At eight seconds, switched across the fleet for
  one coordinated ability salvo, then returned to the flagship.

## Failed attempts that informed fixes

- Broken Shield lost at 12.8s when Carrier One was left to retreat under four-bomber
  focus; all four bombers remained and the carrier reached 0% hull.
- Direct Black Sun focus attempts lost between 10.8s and 19.6s. The original enemy order
  layout concentrated too much fire on the protected flagship before player orders could
  affect the objective.
- A frantic 21-switch ability loop left the enemy flagship at 7% hull but was rejected as
  an unreasonable balance requirement.
- Before the stable-hash fix, identical clean processes disagreed: one won Black Sun at
  17.5s with 75% flagship hull, while another lost at 30.2s. This exposed process-randomized
  defensive AI phases.

## Defects found and fixed

1. **Total fleet-loss crash:** recording the defeat event accessed `SelectedShip` after
   no player ship remained. Event anchoring now safely falls back to destroyed/player/any
   ship positions, with a dedicated regression test.
2. **Cross-process nondeterminism:** defensive orbit phases used .NET's randomized string
   hash. They now use a stable FNV-1a hash, producing identical results in independent jobs.
3. **Black Sun threat concentration:** the enemy flagship now holds formation, its carrier
   and one destroyer defend it, and escorts pressure separate allied ships. This matches the
   layered-defence briefing and leaves room for player tactics.
4. **Shallow campaign coverage:** the previous 30-second stability loop did not require a
   win. CI now requires tutorial completion, normal inputs and commands, protected-ship
   survival, sequential unlocks, and victory in all three missions.

## Remaining manual QA

- Play the full eight-act campaign at human speed and tune the current 450-minute
  encounter budget into a measured 6–8-hour completion window.
- Run the Windows package on a native Windows machine with keyboard and controller.
- Confirm perceived difficulty at human input speed; the final hull margins in First
  Command and Broken Shield are intentionally narrow and may need accessibility tuning.
- Test audio mix, voice recording permissions, focus loss, display scaling, and Steam
  overlay behavior on representative player hardware.
