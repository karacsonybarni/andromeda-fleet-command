# Validation

Last verified: 10 July 2026.

## Automated core suite

Command:

~~~bash
dotnet run --project tests/AndromedaFleetCommand.Core.Tests
~~~

Result: **26 tests, 0 failures**.

Coverage includes:

- vector and physics invariants
- natural-language attack, intercept, defend, move, and retreat parsing
- safe rejection of unknown commands
- deterministic replay-equivalent simulation
- all three mission definitions and objective outcomes
- sequential campaign unlocks and corrupt-save recovery
- first-command tutorial step ordering
- loopback-only local-AI configuration and corrupt-settings recovery
- accessibility-setting normalization and corrupt-settings recovery
- deterministic replay checksums, persistence, and corruption rejection
- authoritative multiplayer ownership, sequencing, and deterministic snapshot validation
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
desktop targets, launch-tests Linux, packages both platforms, and uploads
checksummed artifacts. Windows remains cross-exported and therefore requires a
clean Windows launch check before public distribution.

Release workflow run
[29088052845](https://github.com/karacsonybarni/andromeda-fleet-command/actions/runs/29088052845)
passed for commit `f054a9dc3c3810b8870e8cd388b16810183a2361`.
The downloaded artifact was independently checked after upload:

~~~text
GitHub artifact  fc7cc7ee161d2ae8e0cf9a7513df552f1dfbddbdde5cd3cb6a638829695f3244
Linux archive    0368ddd53b48a50475c57da1b381d300a66a1c13ac2fc68450c42391923b85ab
Windows archive  47a77e010fbd7ba361d2a58e43f7868c2856ba4a6dafa91d9cad4c4a4ccb0716
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
- Windows export-template build and launch outside the Godot editor
