# Architecture

## Decision

The project uses **Godot 4.7 .NET and C#**.

C# ranks among GitHub’s most-used languages and has a large game-development
community. It gives contributors a familiar, typed, compiled language without
requiring everyone to manage native toolchains. Godot is MIT-licensed, has no
royalty or account requirement, and exports directly to Steam’s desktop
targets.

Godot’s engine is C++. Performance-sensitive native libraries can be integrated
through GDExtension, but this project will not introduce C++ until profiling
identifies a real bottleneck. A single-language gameplay codebase is easier to
contribute to and maintain.

## Boundaries

~~~text
Godot presentation and input
            |
            v
      application loop
            |
            v
 command interpreter ---> validated fleet command
            |                       |
            +-----------------------+
                                    v
                           deterministic C# core
                                    |
                                    v
                              render-only state

optional adapters: Ollama | whisper.cpp | Steamworks | persistence
~~~

### AndromedaFleetCommand.Core

Pure .NET with no Godot dependency. It owns ships, orders, projectiles,
abilities, command parsing, command validation, AI pilots, combat, and battle
outcomes. It runs headlessly in tests and can later run on a dedicated server.

### AndromedaFleetCommand.Game

Godot integration only: drawing, keyboard/microphone input, window lifecycle,
and environment-backed local integrations. Presentation reads core state but
does not implement combat rules.

### AndromedaFleetCommand.Core.Tests

Dependency-free executable test suite. It verifies deterministic behavior,
physics invariants, damage, victory conditions, natural-language parsing,
command validation, and tactical-ability cooldowns.

## Command safety

The local LLM never controls physics. It may only rewrite a player utterance
into a bounded vocabulary:

1. A local voice adapter transcribes speech.
2. The rule parser or optional local LLM interprets the text.
3. CommandDispatcher resolves and validates ships, targets, actions, and
   destinations.
4. Deterministic pilots execute the resulting ShipOrder.

If Ollama or whisper.cpp is absent or fails, typed offline commands keep
working.

## Performance strategy

- Fixed 60 Hz simulation with stable entity ordering
- Allocation-conscious value types in the hot simulation path
- Rendering decoupled from simulation updates
- No native extension before profiling
- Future entity partitioning and jobified weapon/steering evaluation
- C++ GDExtension reserved for measured hotspots or native AI libraries

## Multiplayer strategy

The future multiplayer model is server-authoritative. Clients send compact,
validated player commands; the server owns simulation state. Determinism also
supports command-log replays and desynchronization diagnostics.

## Steam strategy

Steamworks belongs behind an IPlatformServices adapter:

- achievements
- lobbies and invitations
- rich presence
- cloud saves
- workshop content

The local implementation remains the default so contributors never need a
Steam account or App ID.
