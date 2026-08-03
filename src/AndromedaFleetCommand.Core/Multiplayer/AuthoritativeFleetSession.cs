using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Replay;
using AndromedaFleetCommand.Core.Simulation;

namespace AndromedaFleetCommand.Core.Multiplayer;

public sealed record NetworkFleetCommand(
    long Sequence,
    int ClientTurn,
    string PlayerId,
    string ShipId,
    OrderType Action,
    string? TargetSelector = null,
    Vector2D? Destination = null);

public sealed record NetworkControlFrame(
    long Sequence,
    int ClientTurn,
    string PlayerId,
    string ShipId,
    ManualInput Input,
    bool ActivateAbility = false);

public sealed record NetworkTurnCommit(long Sequence, int ClientTurn, string PlayerId);

public sealed record CommandAdmission(bool Accepted, string Message);

public sealed record TurnCommitResult(
    bool Accepted,
    bool Resolved,
    string Message,
    AuthoritativeSnapshot? Snapshot = null);

public sealed record AuthoritativeSnapshot(
    int ServerTurn,
    long Revision,
    BattleStatus Status,
    string Checksum,
    SimulationFrame Frame);

/// <summary>
/// Host-authoritative simultaneous-turn session. Captains submit plans for the current turn,
/// then independently commit. The host resolves exactly once after every connected captain is ready.
/// </summary>
public sealed class AuthoritativeFleetSession
{
    private const int MaximumPendingCommandsPerPlayer = 16;
    private const int MaximumPendingActionsPerPlayer = 8;
    private const int SequenceWindowSize = 4096;
    private readonly Dictionary<string, HashSet<string>> _assignments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SequenceWindow> _sequences = new(StringComparer.Ordinal);
    private readonly List<NetworkFleetCommand> _pendingCommands = [];
    private readonly List<NetworkControlFrame> _pendingActions = [];
    private readonly HashSet<string> _readyPlayers = new(StringComparer.Ordinal);
    private readonly CommandDispatcher _dispatcher = new();
    private long _revision;

    public AuthoritativeFleetSession(MissionId missionId, long? seed = null)
    {
        Simulation = new(missionId, seed);
    }

    public BattleSimulation Simulation { get; }
    public int ServerTurn => Simulation.TurnNumber;
    public IReadOnlySet<string> ReadyPlayers => _readyPlayers;

    public void AssignPlayer(string playerId, params string[] shipIds)
    {
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player ID is required", nameof(playerId));
        var requested = shipIds.Distinct(StringComparer.Ordinal).ToArray();
        var validShips = requested.Where(id => Simulation.FindShip(id) is not null)
            .ToHashSet(StringComparer.Ordinal);
        if (validShips.Count == 0 || validShips.Count != requested.Length)
            throw new ArgumentException("Every assigned ship must exist", nameof(shipIds));
        var teams = validShips.Select(id => Simulation.FindShip(id)!.Team).Distinct().ToArray();
        if (teams.Length != 1) throw new ArgumentException("A player must control ships from one team", nameof(shipIds));
        var ownedByAnother = _assignments
            .Where(pair => !pair.Key.Equals(playerId, StringComparison.Ordinal))
            .SelectMany(pair => pair.Value)
            .Any(validShips.Contains);
        if (ownedByAnother) throw new ArgumentException("A ship cannot be assigned to multiple players", nameof(shipIds));
        _assignments[playerId] = validShips;
    }

    public bool UnassignPlayer(string playerId)
    {
        _readyPlayers.Remove(playerId);
        _sequences.Remove(playerId);
        _pendingCommands.RemoveAll(command => command.PlayerId.Equals(playerId, StringComparison.Ordinal));
        _pendingActions.RemoveAll(action => action.PlayerId.Equals(playerId, StringComparison.Ordinal));
        var removed = _assignments.Remove(playerId);
        if (removed && _assignments.Count > 0 && _assignments.Keys.All(_readyPlayers.Contains))
            ResolveCommittedPlans();
        return removed;
    }

    public IReadOnlyList<string> AssignedShips(string playerId) =>
        _assignments.TryGetValue(playerId, out var ships)
            ? ships.OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : [];

    public Team? AssignedTeam(string playerId)
    {
        var first = AssignedShips(playerId).FirstOrDefault();
        return first is null ? null : Simulation.FindShip(first)?.Team;
    }

    public CommandAdmission Submit(NetworkFleetCommand command)
    {
        var admission = ValidatePlan(command.PlayerId, command.ShipId, command.Sequence, command.ClientTurn);
        if (!admission.Accepted) return admission;
        if (!Enum.IsDefined(command.Action)) return new(false, "Unknown order type");
        if (_pendingCommands.Count(item => item.PlayerId.Equals(command.PlayerId, StringComparison.Ordinal)) >=
            MaximumPendingCommandsPerPlayer)
            return new(false, "Too many orders planned this turn");
        ReserveSequence(command.PlayerId, command.Sequence);
        _pendingCommands.Add(command);
        _revision++;
        return new(true, $"Order planned for turn {ServerTurn}");
    }

    public CommandAdmission SubmitControl(NetworkControlFrame frame)
    {
        var admission = ValidatePlan(frame.PlayerId, frame.ShipId, frame.Sequence, frame.ClientTurn);
        if (!admission.Accepted) return admission;
        if (_pendingActions.Count(item => item.PlayerId.Equals(frame.PlayerId, StringComparison.Ordinal)) >=
            MaximumPendingActionsPerPlayer)
            return new(false, "Too many tactical actions planned this turn");
        ReserveSequence(frame.PlayerId, frame.Sequence);
        _pendingActions.RemoveAll(item => item.PlayerId.Equals(frame.PlayerId, StringComparison.Ordinal) &&
                                          item.ShipId.Equals(frame.ShipId, StringComparison.Ordinal) &&
                                          item.ActivateAbility == frame.ActivateAbility);
        _pendingActions.Add(frame);
        _revision++;
        return new(true, frame.ActivateAbility ? "Ability queued" : "Maneuver plotted");
    }

    public TurnCommitResult CommitTurn(NetworkTurnCommit commit)
    {
        if (!Simulation.CanPlan) return new(false, false, "The battle is complete");
        if (!_assignments.ContainsKey(commit.PlayerId)) return new(false, false, "Unknown player");
        if (_sequences.TryGetValue(commit.PlayerId, out var sequences) && sequences.Contains(commit.Sequence))
            return new(false, false, "Duplicate command sequence");
        if (commit.ClientTurn != ServerTurn)
            return new(false, false, commit.ClientTurn < ServerTurn ? "That turn has already resolved" : "Cannot ready a future turn");
        if (_readyPlayers.Contains(commit.PlayerId)) return new(false, false, "Captain is already ready");
        ReserveSequence(commit.PlayerId, commit.Sequence);
        _readyPlayers.Add(commit.PlayerId);
        _revision++;

        if (_assignments.Keys.Any(playerId => !_readyPlayers.Contains(playerId)))
            return new(true, false, $"Ready for turn {ServerTurn}; waiting for {_assignments.Count - _readyPlayers.Count} captain(s)");

        var snapshot = ResolveCommittedPlans();
        return new(true, true, snapshot.Status == BattleStatus.Active
            ? $"Turn {commit.ClientTurn} resolved"
            : "Battle resolved", snapshot);
    }

    public AuthoritativeSnapshot Snapshot() => new(
        ServerTurn,
        _revision,
        Simulation.Status,
        SimulationChecksum.Compute(Simulation),
        Simulation.CaptureFrame());

    private void ApplyPlans()
    {
        foreach (var command in _pendingCommands
                     .OrderBy(item => item.ClientTurn)
                     .ThenBy(item => item.PlayerId, StringComparer.Ordinal)
                     .ThenBy(item => item.Sequence))
        {
            if (Simulation.FindShip(command.ShipId) is { IsAlive: true })
                _dispatcher.DispatchToShip(command.ShipId, command.Action, command.TargetSelector,
                    command.Destination, Simulation);
        }

        foreach (var action in _pendingActions
                     .OrderBy(item => item.PlayerId, StringComparer.Ordinal)
                     .ThenBy(item => item.Sequence))
        {
            if (Simulation.FindShip(action.ShipId) is not { IsAlive: true }) continue;
            if (action.ActivateAbility) Simulation.TryActivateAbility(action.ShipId);
            else Simulation.PlanManeuver(action.ShipId, action.Input);
        }
    }

    private AuthoritativeSnapshot ResolveCommittedPlans()
    {
        ApplyPlans();
        Simulation.ResolveTurn();
        _pendingCommands.Clear();
        _pendingActions.Clear();
        _readyPlayers.Clear();
        _revision++;
        return Snapshot();
    }

    private CommandAdmission ValidatePlan(string playerId, string shipId, long sequence, int clientTurn)
    {
        if (!Simulation.CanPlan) return new(false, "The turn is resolving");
        if (!_assignments.TryGetValue(playerId, out var ships)) return new(false, "Unknown player");
        if (_readyPlayers.Contains(playerId)) return new(false, "Captain already committed this turn");
        if (!ships.Contains(shipId)) return new(false, "Player does not control that ship");
        if (_sequences.TryGetValue(playerId, out var sequences) && sequences.Contains(sequence))
            return new(false, "Duplicate command sequence");
        if (clientTurn != ServerTurn)
            return new(false, clientTurn < ServerTurn ? "Command arrived too late" : "Command is for a future turn");
        return new(true, "Command accepted");
    }

    private void ReserveSequence(string playerId, long sequence)
    {
        if (!_sequences.TryGetValue(playerId, out var window))
        {
            window = new(SequenceWindowSize);
            _sequences[playerId] = window;
        }
        window.Add(sequence);
    }

    private sealed class SequenceWindow(int capacity)
    {
        private readonly Queue<long> _order = [];
        private readonly HashSet<long> _values = [];

        public bool Contains(long sequence) => _values.Contains(sequence);

        public void Add(long sequence)
        {
            if (!_values.Add(sequence)) return;
            _order.Enqueue(sequence);
            while (_order.Count > capacity) _values.Remove(_order.Dequeue());
        }
    }
}
