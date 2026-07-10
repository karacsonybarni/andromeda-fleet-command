# Validation

Last verified: 10 July 2026.

## Automated core suite

Command:

~~~bash
dotnet run --project tests/AndromedaFleetCommand.Core.Tests
~~~

Result: **23 tests, 0 failures**.

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

Expected smoke marker:

~~~text
AFC_SMOKE_PASS ships=5 projectiles=...
~~~

Benchmark marker from this environment:

~~~text
AFC_BENCHMARK_PASS ticks=32400 ticks_per_second=37360
~~~

## Remaining manual checks

These require a graphical Windows environment and should be performed before
publishing a public demo:

- visual spacing at 16:9, ultrawide, and Steam Deck resolutions
- keyboard focus while opening and cancelling the command line
- microphone permissions and whisper.cpp setup on Windows
- controller rebinding and accessibility review
- Windows export-template build and launch outside the Godot editor
