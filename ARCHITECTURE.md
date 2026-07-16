# Architecture

## Decision

The project uses **Godot 4.7 .NET and C#**.

C# gives contributors a familiar, typed, compiled language without requiring
everyone to manage native toolchains. Godot is MIT-licensed, has no royalty or
account requirement, and exports directly to Steam’s desktop targets.

Godot’s engine is C++. Performance-sensitive native libraries can be integrated
through GDExtension, but this project will not introduce C++ until profiling
identifies a real bottleneck. A single-language gameplay codebase is easier to
contribute to and maintain.

See [the engine decision record](docs/ENGINE_DECISION.md) for the comparison
with Unity and Unreal, contributor-focused criteria, and reconsideration
triggers.

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

The multiplayer model is host-authoritative. `FleetLobby` assigns each captain
one team and a disjoint set of ships; `AuthoritativeFleetSession` accepts input
only for those ships, bounds payloads and pending work, rejects duplicate
sequences and out-of-window ticks, advances the fixed-step simulation, and
emits complete checksummed recovery snapshots.

`MultiplayerManager` is the Godot-only ENet adapter. It maps untrusted network
peers to server-owned player IDs, sends intent to the host over separate
control/snapshot channels, and applies snapshots on clients. The pure core has
no ENet or Steam dependency. A future Steam transport can replace discovery and
connectivity while retaining the same lobby/session protocol. Steam
lobbies/relay, host migration, reconnection, and adversarial Internet soak
testing remain release work.

## Steam strategy

Steamworks belongs behind an IPlatformServices adapter:

- achievements
- lobbies and invitations
- rich presence
- cloud saves
- workshop content

The local implementation remains the default so contributors never need a
Steam account or App ID.
