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

## Pull requests

1. Open an issue for large design or architecture changes.
2. Add or update core tests for gameplay changes.
3. Keep commits focused.
4. Run the full check script.
5. Include a short clip or screenshot for visible changes.

Good first contributions include command synonyms, accessibility improvements,
new mission objectives, HUD polish, balance tests, sound hooks, and ship ability
feedback.

AI-assisted contributions are welcome when the contributor understands,
reviews, tests, and can maintain the submitted code.
