# Steam integration and release checklist

The game runs without Steam. When the GodotSteam GDExtension is installed, the
runtime detects its `Steam` singleton dynamically and enables achievements. No
Steam library is linked into the pure simulation or required for contributors.

## Owner-provided release inputs

These cannot be committed to the public repository:

- the real Steam App ID and depot/build configuration
- Steamworks partner access and store-page permissions
- Windows code-signing credentials
- final achievement IDs configured in Steamworks

The implemented achievement IDs are:

- `ACH_FIRST_COMMAND`
- `ACH_BROKEN_SHIELD`
- `ACH_BLACK_SUN`
- `ACH_CAMPAIGN_COMPLETE`

## Packaging

The `Build desktop demo` GitHub Actions workflow downloads the official Godot
4.7 .NET editor and export templates, runs tests, exports Windows and Linux,
packages both builds, and publishes checksummed workflow artifacts. It can run
manually or from a `v*` tag.

Before uploading to Steam:

1. Install the GodotSteam GDExtension version compatible with Godot 4.7.
2. Add the App ID only through the private build environment.
3. Map the four achievement IDs in Steamworks.
4. Sign the Windows executable and verify both packages on clean machines.
5. Configure Steam Cloud for Godot's user-data save, replay, settings, and log paths.
6. Create depots and upload through SteamPipe.

The playable direct-IP ENet multiplayer mode lets one player's machine host an
authoritative co-op or PvP match. It validates ownership, duplicate sequences,
tick windows, payload and queue limits, and recovers clients with complete
checksummed snapshots. Steam lobbies, invitations, relay hosting, host
migration, reconnection, and adversarial Internet soak testing remain separate
release work. The direct transport is documented in [MULTIPLAYER.md](MULTIPLAYER.md).
