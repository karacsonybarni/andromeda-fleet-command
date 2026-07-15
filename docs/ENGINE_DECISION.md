# Engine decision: keep Godot 4.7 .NET

**Status:** accepted

**Last reviewed:** 2026-07-15

## Context

Andromeda Fleet Command is an open-source, Steam-first desktop fleet game. The
engine must support a responsive 2D battlefield, deterministic simulation,
local native AI adapters, automated headless testing, and contributions from
people who may not own commercial game-development tools.

Unity and Unreal have larger commercial user bases than Godot. Raw popularity
is not the only useful measure for this project: contributor access, licensing,
reviewable Git diffs, setup size, and the cost of replacing working code matter
more once a playable game exists.

## Decision

Keep **Godot 4.7 .NET with C# and .NET 8**.

| Criterion | Godot 4.7 .NET | Unity | Unreal Engine |
| --- | --- | --- | --- |
| Engine terms | MIT; fully open source | Proprietary; source access is not open source | Source available under the Unreal EULA |
| New-contributor access | Download the editor and .NET SDK; no vendor account | Unity Hub/editor and Unity terms | Epic account, EULA, and a substantially larger toolchain |
| Main gameplay language | C# | C# | C++ and/or Blueprints |
| Git collaboration | Designed to produce mostly readable, mergeable files | Mixed text and serialized editor assets | Many large/binary editor assets |
| Migration cost | None | Full presentation, build, input, audio, and QA rewrite | Full presentation, build, input, audio, and QA rewrite |
| Fit for this project | **Best fit** | Capable, but worse contributor freedom and rewrite cost | Excellent high-end 3D, but unnecessary weight and contributor friction here |

Godot is already delivering the required desktop rendering, audio, input,
headless validation, and export pipeline. The pure .NET simulation core also
keeps most gameplay changes testable without launching the editor. Migrating
would pause feature and polish work for months while providing little benefit
to this primarily 2D game.

We will not rewrite the tested C# core in GDScript. C# is familiar to a broad
developer pool, keeps the simulation strongly typed, and supports fast
headless tests. GDScript remains welcome for isolated Godot editor tools when
it clearly lowers contributor effort and does not duplicate game rules.

## How this attracts contributors

- No purchase, royalty, Steam App ID, hosted AI key, or vendor account is
  required to build and test the game.
- Gameplay rules live in a small engine-independent .NET project.
- Godot presentation files and C# source are reviewable in ordinary pull
  requests.
- Native C++ is reserved for measured performance hotspots or established
  libraries, so contributors do not need a C++ toolchain by default.
- Mission, balance, accessibility, audio, art, documentation, and QA changes
  all have explicit entry points in `CONTRIBUTING.md`.

## Reconsideration triggers

Re-evaluate the engine only if at least one of these becomes true and a
prototype demonstrates the replacement is better:

1. A required Steam desktop feature cannot be shipped or maintained in Godot.
2. Measured renderer or platform limits block the agreed visual design after
   profiling and reasonable optimization.
3. Contributor data over two release cycles shows the engine—not onboarding,
   documentation, issue quality, or project scope—is the dominant barrier.
4. A funded migration includes feature parity, automated test parity, asset
   conversion, packaging, and a realistic maintenance plan.

Popularity by itself is not a migration trigger.

## Primary references

- [Godot license](https://godotengine.org/license/)
- [Godot C#/.NET documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/)
- [Godot version-control guidance](https://docs.godotengine.org/en/stable/tutorials/best_practices/version_control_systems.html)
- [Unity source-code licensing](https://unity.com/products/source-code)
- [Unreal Engine source access and EULA requirements](https://www.unrealengine.com/ue-on-github)
