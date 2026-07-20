# Validation

Last verified: 20 July 2026.

## Automated core suite

Command:

~~~bash
dotnet run --project tests/AndromedaFleetCommand.Core.Tests
~~~

Result: **43 tests, 0 failures**.

Coverage includes:

- vector and physics invariants
- natural-language attack, intercept, defend, move, and retreat parsing
- safe rejection of unknown commands
- deterministic replay-equivalent simulation
- all 24 mission definitions, eight-act narrative continuity, pacing metadata, and objective outcomes
- a complete representative 24-mission campaign playthrough through normal
  parsed orders, manual controls, abilities, tutorial steps, and sequential unlocks
- sequential campaign unlocks and corrupt-save recovery
- bounded campaign pacing telemetry, corrupt-data recovery, full-coverage detection,
  duration/variance aggregation, and deterministic Markdown report export
- concise four-beat tutorial ordering, progress, purpose text, and dual-input prompts
- loopback-only local-AI configuration and corrupt-settings recovery
- GPU-preferred local inference defaults and legacy-settings migration
- accessibility-setting normalization and corrupt-settings recovery
- deterministic replay checksums, persistence, and corruption rejection
- authoritative multiplayer ownership, sequencing, and deterministic snapshot validation
- cooperative and PvP lobby assignment, malformed-payload rejection, disconnect
  recovery, and deterministic authoritative sessions
- manual control and speed limits
- validated command dispatch
- tactical-ability cooldowns
- shields, hull damage, and victory conditions
- a 150-second maximum endurance battle

## Godot API build

Verified against the official Godot 4.7 stable .NET SDK:

- Debug build: passed
- Release build: passed
- C# warnings: none
- C# errors: none

The temporary local verification environment emitted an SDK-resolution warning
because the official SDK package was mounted directly from Godot’s distribution
instead of restored from NuGet. This is not a source warning and does not occur
in a normal Godot .NET installation.

## Runtime checks

- Scene import and initialization: passed
- 120-frame real-engine headless run: passed
- Object/resource leak check: passed
- Automated in-engine smoke battle: passed
- Six SVG ship assets imported and loaded through Godot's resource pipeline: passed
- Headless resource leak check after presentation load: passed
- Live two-process ENet cooperative match: passed
- Live two-process ENet PvP match: passed
- Packaged native Windows headless launch and bundled whisper.cpp startup: passed

Both multiplayer modes are exercised with separate host and client Godot
processes. The check requires matching modes, advancing authoritative snapshots,
clean process exits, and no oversized-packet/MTU warnings.

The release workflow also performs a clean launch of the exported Linux build
and requires the smoke marker before it uploads either desktop package. This
guards against native-only exports that are missing their managed .NET payload.

Expected smoke marker:

~~~text
AFC_SMOKE_PASS ships=5 projectiles=...
~~~

Benchmark marker from this environment:

~~~text
AFC_BENCHMARK_PASS ticks=32400 ticks_per_second=37360
~~~

## Release-package checks

The `Build desktop demo` workflow runs on every `main` update that changes
release inputs, and on every version tag.
It uses the official Godot 4.7 .NET editor and export templates, exports both
desktop targets, packages both platforms, and launch-tests each packaged game
on a native GitHub-hosted operating system before the workflow can pass. The
Windows launch also verifies the bundled whisper.cpp executable.

Release workflow run
[29721897182](https://github.com/karacsonybarni/andromeda-fleet-command/actions/runs/29721897182)
passed for commit `651531084245c3682d0706c1e6fd78b9571b0663`.
The downloaded artifact was independently checked after upload:

~~~text
GitHub artifact  6d343aa63dea7e8df1a67f15170a09855ed7ea561564a22e220d102452538eb6
Linux archive    a63547a32bb9ee2f9d067b45d4737b13c7c3ff611fca0b1dd830a2987452d5ae
Windows archive  e70b22a8458798ee0fe4d85108a7be2a0ce524a083dce168446268660fd9a9bb
~~~

Both portable checksum entries passed, compressed-file tests passed, and no
`.pdb` debug symbols were present in either player package.

## Remaining manual checks

These require a graphical Windows environment and should be performed before
publishing a public demo:

- visual spacing at 16:9, ultrawide, and Steam Deck resolutions
- keyboard focus while opening and cancelling the command line
- microphone permissions and whisper.cpp setup on Windows
- controller rebinding and accessibility review
- native graphical Windows play with keyboard and controller

## Store capture validation

The pull-request visual workflow renders ten unedited gameplay and interface
screens at 1920×1080. `scripts/verify-visual-captures.py` rejects missing,
unexpected, duplicate, undersized, non-PNG, or non-16:9 captures before the
artifact can pass. The three curated README images are selected from that same
automated in-engine capture set.
