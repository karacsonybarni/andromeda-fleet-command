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

For LAN play, use the host's private address, commonly beginning with `192.168.` or `10.`. For Internet play,
the host normally needs to forward UDP 7777 through their router and firewall and give clients the public IP.
The current direct ENet protocol is not an encrypted chat or identity service; never transmit secrets through it.

## Authority model

Clients send manual-control frames, ability requests, and bounded fleet orders. They never send positions,
damage, cooldowns, victory state, or other authoritative game values. The host:

1. maps the network peer ID to its assigned ships;
2. validates ownership, tick windows, duplicate sequences, queue limits, order types, and payload size;
3. advances the deterministic simulation at 60 Hz; and
4. sends complete checksummed recovery snapshots at 30 Hz.

Clients replace their local render state from those snapshots. Disconnecting releases the player's assignments,
so the existing deterministic pilot resumes every abandoned ship without stopping the match.

## Current limitations

- Direct IP/LAN discovery only; there is no lobby browser, NAT traversal, UPnP, or relay yet.
- The host cannot migrate during a match.
- Rejoining a match in progress is not implemented.
- Steam lobbies, invitations, authentication, and relay transport are still planned behind a transport adapter.
- Internet-facing adversarial and high-latency soak testing remains release work.

The pure multiplayer core is engine-independent and covered by the executable test suite. Live two-process ENet
smoke tests run in the desktop CI/export environment.
