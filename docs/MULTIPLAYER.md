# Multiplayer

Andromeda Fleet Command supports direct host-on-your-PC multiplayer for two to four players. There is no
dedicated server process: the captain who hosts is both a player and the authoritative server.

## Modes

- **Cooperative:** every captain controls part of the allied fleet while the remaining allied ships and all
  opponents are deterministic bots. The host can choose any campaign mission. A mission must have at least
  one allied ship per connected captain.
- **Versus:** captains alternate between the Andromeda and Ketzal teams in a mirrored four-versus-four Fleet
  Duel. At least two captains are required. Ships without a human captain remain bot-controlled.

## Start a match

1. Launch the game and press **F6**.
2. Enter a captain name.
3. Host with **H** for co-op or **V** for versus. The default port is UDP 7777.
4. Other captains enter the host address as `address:port` and press **J**.
5. The host can press **M** to change mode, use **Left/Right** to cycle all campaign missions
   (**1–3** are quick shortcuts), and press **Enter** to start.
6. During a completed match, the host can press **R** for a synchronized rematch. Press **F6**, then **D**
   to disconnect.

If a guest loses their connection during a match, their seat remains reserved. On the same computer, reopen the
multiplayer panel, enter the host address, and press **J**. The host restores that captain's display name, team,
ship assignments, and latest authoritative turn. Until they return, deterministic pilots continue their ships'
standing orders. An ordinary new join cannot claim the reserved seat; recovery requires that installation's token.

For LAN play, use the host's private address, commonly beginning with `192.168.` or `10.`. For Internet play,
the host normally needs to forward UDP 7777 through their router and firewall and give clients the public IP.
The current direct ENet protocol is not an encrypted chat or identity service; never transmit secrets through it.

## Authority model

Clients send plotted maneuvers, ability requests, bounded fleet orders, and a ready signal. They never send positions,
damage, cooldowns, victory state, or other authoritative game values. The host:

1. maps the network peer ID to its assigned ships;
2. validates ownership, turn number, duplicate sequences, queue limits, order types, and payload size;
3. waits until every connected captain has committed the current plan;
4. resolves one deterministic simultaneous turn; and
5. sends a complete revisioned, checksummed recovery snapshot.

Clients replace their local render state from those snapshots. No network traffic is required while captains think.
Disconnecting discards that captain's uncommitted actions, so standing deterministic orders remain safe. Each
installation stores a random reconnect identity in the Godot user-data directory; it is used only to reclaim the
same reserved seat from the same running host and is never treated as a Steam or account identity.

## Current limitations

- Direct IP/LAN discovery only; there is no lobby browser, NAT traversal, UPnP, or relay yet.
- The host cannot migrate during a match.
- A guest can rejoin only while the original host process and match are still running; reconnect identity does not
  survive host migration or a host restart.
- Steam lobbies, invitations, authentication, and relay transport are still planned behind a transport adapter.
- Internet-facing adversarial and high-latency soak testing remains release work.

The pure multiplayer core is engine-independent and covered by the executable test suite. Live two-process ENet
co-op/PvP smoke tests and a three-process disconnect/rejoin test run in the desktop CI/export environment.
