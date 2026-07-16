using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Multiplayer;

public enum MultiplayerMode
{
    Cooperative,
    Versus
}

public sealed record LobbyActionResult(bool Accepted, string Message);

public sealed record LobbyPlayerSnapshot(
    string PlayerId,
    string DisplayName,
    Team Team,
    IReadOnlyList<string> ShipIds,
    bool IsHost);

public sealed record FleetLobbySnapshot(
    MultiplayerMode Mode,
    MissionId MissionId,
    bool MatchStarted,
    int MaximumPlayers,
    IReadOnlyList<LobbyPlayerSnapshot> Players);

public sealed class FleetLobby
{
    public const int MaximumPlayers = 4;
    private readonly List<LobbyPlayerState> _players = [];

    public FleetLobby(string hostPlayerId, string hostDisplayName, MultiplayerMode mode)
    {
        if (string.IsNullOrWhiteSpace(hostPlayerId))
            throw new ArgumentException("Host player ID is required", nameof(hostPlayerId));
        HostPlayerId = hostPlayerId;
        Mode = mode;
        MissionId = mode == MultiplayerMode.Versus ? MissionId.FleetDuel : MissionId.BlackSun;
        _players.Add(new(hostPlayerId, NormalizeName(hostDisplayName), Team.Player, []));
        RebalanceAssignments();
    }

    public string HostPlayerId { get; }
    public MultiplayerMode Mode { get; private set; }
    public MissionId MissionId { get; private set; }
    public bool MatchStarted { get; private set; }
    public bool IsStartable => _players.All(player => player.ShipIds.Count > 0) && (Mode == MultiplayerMode.Cooperative
        ? _players.Count > 0
        : _players.Count >= 2 && _players.Any(player => player.Team == Team.Player) &&
          _players.Any(player => player.Team == Team.Enemy));

    public LobbyActionResult TryAddPlayer(string playerId, string displayName)
    {
        if (MatchStarted) return new(false, "Match already started");
        if (string.IsNullOrWhiteSpace(playerId)) return new(false, "Player ID is required");
        if (_players.Any(player => player.PlayerId.Equals(playerId, StringComparison.Ordinal)))
            return new(false, "Player already joined");
        if (_players.Count >= MaximumPlayers) return new(false, "Lobby is full");
        if (Mode == MultiplayerMode.Cooperative && _players.Count >=
            MissionCatalog.Get(MissionId).Ships.Count(ship => ship.Team == Team.Player))
            return new(false, "The selected mission has no unassigned allied ships");
        _players.Add(new(playerId, NormalizeName(displayName), Team.Player, []));
        RebalanceAssignments();
        return new(true, "Player joined");
    }

    public LobbyActionResult RemovePlayer(string playerId)
    {
        if (playerId.Equals(HostPlayerId, StringComparison.Ordinal))
            return new(false, "The host cannot leave without closing the lobby");
        var removed = _players.RemoveAll(player => player.PlayerId.Equals(playerId, StringComparison.Ordinal));
        if (removed == 0) return new(false, "Unknown player");
        if (!MatchStarted) RebalanceAssignments();
        return new(true, "Player left");
    }

    public LobbyActionResult SetMode(MultiplayerMode mode)
    {
        if (MatchStarted) return new(false, "Cannot change mode during a match");
        Mode = mode;
        MissionId = mode == MultiplayerMode.Versus ? MissionId.FleetDuel : MissionId.BlackSun;
        RebalanceAssignments();
        return new(true, mode == MultiplayerMode.Cooperative ? "Co-op mode selected" : "Versus mode selected");
    }

    public LobbyActionResult SetCooperativeMission(MissionId missionId)
    {
        if (MatchStarted) return new(false, "Cannot change mission during a match");
        if (Mode != MultiplayerMode.Cooperative) return new(false, "Versus mode uses Fleet Duel");
        if (!MissionCatalog.All.Any(mission => mission.Id == missionId))
            return new(false, "Unknown cooperative mission");
        if (_players.Count > MissionCatalog.Get(missionId).Ships.Count(ship => ship.Team == Team.Player))
            return new(false, "That mission has fewer allied ships than connected players");
        MissionId = missionId;
        RebalanceAssignments();
        return new(true, $"{MissionCatalog.Get(missionId).Title} selected");
    }

    public (LobbyActionResult Result, AuthoritativeFleetSession? Session) StartMatch()
    {
        if (MatchStarted) return (new(false, "Match already started"), null);
        if (!IsStartable) return (new(false, "Versus requires players on both teams"), null);
        MatchStarted = true;
        return (new(true, "Match started"), CreateSession());
    }

    public AuthoritativeFleetSession CreateRematch()
    {
        if (!MatchStarted) throw new InvalidOperationException("The match has not started");
        return CreateSession();
    }

    public FleetLobbySnapshot Snapshot() => new(
        Mode,
        MissionId,
        MatchStarted,
        MaximumPlayers,
        _players.Select(player => new LobbyPlayerSnapshot(
            player.PlayerId,
            player.DisplayName,
            player.Team,
            player.ShipIds.ToArray(),
            player.PlayerId.Equals(HostPlayerId, StringComparison.Ordinal))).ToArray());

    private AuthoritativeFleetSession CreateSession()
    {
        var session = new AuthoritativeFleetSession(MissionId, MissionCatalog.Get(MissionId).Seed);
        foreach (var player in _players)
            session.AssignPlayer(player.PlayerId, player.ShipIds.ToArray());
        return session;
    }

    private void RebalanceAssignments()
    {
        for (var index = 0; index < _players.Count; index++)
        {
            var team = Mode == MultiplayerMode.Cooperative || index % 2 == 0 ? Team.Player : Team.Enemy;
            _players[index] = _players[index] with { Team = team, ShipIds = [] };
        }

        var mission = MissionCatalog.Get(MissionId);
        foreach (var team in Enum.GetValues<Team>())
        {
            var captains = _players.Where(player => player.Team == team).ToArray();
            if (captains.Length == 0) continue;
            var ships = mission.Ships.Where(ship => ship.Team == team).ToArray();
            for (var index = 0; index < ships.Length; index++)
            {
                var captain = captains[index % captains.Length];
                var playerIndex = _players.FindIndex(player =>
                    player.PlayerId.Equals(captain.PlayerId, StringComparison.Ordinal));
                var assigned = _players[playerIndex].ShipIds.Append(ships[index].Id).ToArray();
                _players[playerIndex] = _players[playerIndex] with { ShipIds = assigned };
            }
        }
    }

    private static string NormalizeName(string displayName)
    {
        var cleaned = new string((displayName ?? string.Empty)
            .Where(character => !char.IsControl(character))
            .ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Captain";
        return cleaned.Length <= 24 ? cleaned : cleaned[..24];
    }

    private sealed record LobbyPlayerState(
        string PlayerId,
        string DisplayName,
        Team Team,
        IReadOnlyList<string> ShipIds);
}
