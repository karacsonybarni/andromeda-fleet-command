# Contributing

Thanks for helping build Andromeda Fleet Command.

## Principles

- Protect the fantasy: one commander, many capable ships.
- Keep runtime AI local and optional.
- Put deterministic game rules in AndromedaFleetCommand.Core.
- Keep Godot code focused on presentation and platform input.
- Profile before adding C++ or clever concurrency.
- Prefer a small playable improvement over a large speculative subsystem.

## Setup

Install .NET 8 and Godot 4.7 .NET, then:

~~~bash
./scripts/test.sh
./scripts/check.sh
./scripts/run.sh
~~~

Ollama, whisper.cpp, Steam, and a Steam App ID are optional. The offline parser,
simulation, tests, and local platform adapter work without them.

For a code-only first pass, install just .NET 8 and run:

~~~bash
./scripts/test.sh
~~~

## Pick a contribution path

- **Simulation and combat:** start in `src/AndromedaFleetCommand.Core` and add
  coverage under `tests/AndromedaFleetCommand.Core.Tests`.
- **Missions, story, and balance:** start with `MissionCatalog.cs`,
  `docs/CAMPAIGN_STORY.md`, and the campaign playthrough tests.
- **UI and visual effects:** start in `src/AndromedaFleetCommand.Game/Main.cs`;
  run the visual QA capture before and after visible changes.
- **Audio and music:** start in `TacticalAudio.cs`; keep assets original or
  compatibly licensed and document their provenance.
- **Accessibility and input:** test keyboard and controller paths, saved
  settings, reduced-flash mode, captions, and color-vision modes.
- **Local AI and voice:** preserve the trusted offline fallback and never let
  model output bypass command validation.
- **Art, writing, docs, and QA:** these are first-class contributions. Include
  source files or reproducible steps and record exactly what you tested.

If you are unsure where to begin, choose a small issue labelled `good first
issue` or propose one focused, player-visible improvement.

## Pull requests

1. Open an issue for large design or architecture changes.
2. Add or update core tests for gameplay changes.
3. Keep commits focused.
4. Run the full check script.
5. Include a short clip or screenshot for visible changes.

Good first contributions include command synonyms, accessibility improvements,
new mission objectives, HUD polish, balance tests, sound hooks, and ship ability
feedback.

Engine changes are exceptional because they affect every contributor and build
target. Read [the engine decision record](docs/ENGINE_DECISION.md) before
proposing one; proposals must include a working prototype and a feature-, test-,
and packaging-parity migration plan.

AI-assisted contributions are welcome when the contributor understands,
reviews, tests, and can maintain the submitted code.
