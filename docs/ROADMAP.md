# Roadmap

## Vertical slice

- [x] Switchable allied fleet
- [x] Deterministic pilots and enemy director
- [x] Manual flight and weapons
- [x] Natural-language command console
- [x] Local Ollama adapter with offline fallback
- [x] Local whisper.cpp microphone adapter
- [x] Distinct flagship, carrier, frigate, and destroyer abilities
- [x] Victory, defeat, pause, help, restart, and tactical HUD
- [x] Headless core test suite

## Indiegogo demo

- [x] Replace procedural silhouettes with a coherent six-class scalable vector fleet set
- [x] Combat-readability pass with targeting, projectile, shield, thruster, impact, and destruction VFX
- [ ] Final art-direction, animation, VFX, and store-quality capture pass
- [x] Automated 1920×1080 real-gameplay capture validation for Steam store screenshots
- [x] 24 playable missions with escalating objectives and command complexity
- [x] Connected eight-act storyline with mission briefings, victory debriefs, and failure beats
- [x] Encode a 450-minute campaign pacing target across 15–23-minute missions
- [x] Record real mission attempts and active playtime, with an exportable campaign pacing report
- [x] One-keystroke playtest handoff that opens feedback and reveals the pacing report
- [ ] Validate and tune the complete campaign to 6–8 hours using external human playtest data
- [x] Procedural acknowledgements, weapons, abilities, tactical alerts, and victory cues
- [x] Original seamless background theme with a recognizable melodic hook
- [x] Layered procedural stereo combat feedback and mix pass
- [ ] Optional professional voice performances
- [x] Four-beat Captain's Drill for switching, manual flight, fleet orders, and abilities
- [ ] Validate a first satisfying spoken order within two minutes on supported hardware
- [x] In-game local-AI readiness diagnostics, Ollama pull, and speech-model download
- [x] Bundle verified whisper.cpp executables for one-click Windows/Linux voice setup
- [ ] Performance capture on Steam Deck and mid-range Windows hardware
- [x] Reproducible campaign-wide simulation benchmark harness
- [x] Automatic local crash reports
- [x] Structured in-game GitHub feedback and bug-report forms
- [ ] Trailer capture
- [x] Downloadable unsigned Windows/Linux demo artifacts with native launch validation

## Steam alpha

- [x] Optional runtime-detected GodotSteam achievement adapter
- [ ] Steam lobbies, invitations, Cloud synchronization, and Workshop
- [x] Deterministic input recording and final-state replay checksum validation
- [x] Authoritative command ownership, sequence, and tick-window validation core
- [x] Direct-IP ENet host/client transport, in-game lobbies, co-op vs bots, balanced PvP, and snapshot recovery
- [ ] Steam lobby/invitation/relay adapter, host migration, reconnection, and adversarial network soak testing
- [x] Persistent settings, gamepad flight/combat, color-vision palettes, captions, and reduced flashes
- [x] Persistent conflict-safe keyboard action rebinding with dynamic HUD and tutorial prompts
- [x] Persistent controller combat/navigation button remapping with controller-only navigation
- [ ] Manual ultrawide and subtitle-language validation
- [ ] Signed Windows and Linux packages
- [x] Tagged CI workflow for unsigned checksummed Windows/Linux packages
- [ ] Steam Deck verification work
- [x] Public bug-report and player-feedback issue templates
- [ ] Steam Playtest branch

See [CAMPAIGN_SCOPE.md](CAMPAIGN_SCOPE.md) for the promise-by-promise status.
