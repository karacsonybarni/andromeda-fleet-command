using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Replay;
using AndromedaFleetCommand.Core.Simulation;

namespace AndromedaFleetCommand.Core.Multiplayer;

public sealed record NetworkFleetCommand(
    long Sequence,
    int ClientTick,
    string PlayerId,
    string ShipId,
    OrderType Action,
    string? TargetSelector = null,
    Vector2D? Destination = null);

public sealed record NetworkControlFrame(
    long Sequence,
    int ClientTick,
    string PlayerId,
    string ShipId,
    ManualInput Input,
    bool ActivateAbility = false);

public sealed record CommandAdmission(bool Accepted, string Message);

public sealed record AuthoritativeSnapshot(
    int ServerTick,
    BattleStatus Status,
    string Checksum,
    SimulationFrame Frame);

public sealed class AuthoritativeFleetSession
{
    private const int MaximumFutureTicks = 120;
    private const int MaximumPastTicks = 30;
    private const int ControlTimeoutTicks = 30;
    private const int MaximumPendingCommandsPerPlayer = 16;
    private const int MaximumPendingAbilitiesPerPlayer = 4;
    private const int SequenceWindowSize = 4096;
    private readonly Dictionary<string, HashSet<string>> _assignments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SequenceWindow> _sequences = new(StringComparer.Ordinal);
    private readonly List<NetworkFleetCommand> _pendingCommands = [];
    private readonly List<NetworkControlFrame> _pendingAbilities = [];
    private readonly Dictionary<string, ReceivedControl> _latestControls = new(StringComparer.Ordinal);
    private readonly CommandDispatcher _dispatcher = new();

    public AuthoritativeFleetSession(MissionId missionId, long? seed = null)
    {
        Simulation = new(missionId, seed);
    }

    public BattleSimulation Simulation { get; }
    public int ServerTick { get; private set; }

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
        _latestControls.Remove(playerId);
        _sequences.Remove(playerId);
        _pendingCommands.RemoveAll(command => command.PlayerId.Equals(playerId, StringComparison.Ordinal));
        _pendingAbilities.RemoveAll(command => command.PlayerId.Equals(playerId, StringComparison.Ordinal));
        return _assignments.Remove(playerId);
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
        var admission = Validate(command.PlayerId, command.ShipId, command.Sequence, command.ClientTick);
        if (!admission.Accepted) return admission;
        if (!Enum.IsDefined(command.Action)) return new(false, "Unknown order type");
        if (_pendingCommands.Count(item => item.PlayerId.Equals(command.PlayerId, StringComparison.Ordinal)) >=
            MaximumPendingCommandsPerPlayer)
            return new(false, "Too many pending commands");
        ReserveSequence(command.PlayerId, command.Sequence);
        _pendingCommands.Add(command);
        return new(true, "Command accepted");
    }

    public CommandAdmission SubmitControl(NetworkControlFrame frame)
    {
        var admission = Validate(frame.PlayerId, frame.ShipId, frame.Sequence, frame.ClientTick);
        if (!admission.Accepted) return admission;
        if (frame.ActivateAbility && _pendingAbilities.Count(item =>
                item.PlayerId.Equals(frame.PlayerId, StringComparison.Ordinal)) >= MaximumPendingAbilitiesPerPlayer)
            return new(false, "Too many pending ability requests");
        ReserveSequence(frame.PlayerId, frame.Sequence);
        _latestControls[frame.PlayerId] = new(frame, ServerTick);
        if (frame.ActivateAbility) _pendingAbilities.Add(frame);
        return new(true, "Control accepted");
    }

    public AuthoritativeSnapshot Step()
    {
        foreach (var command in _pendingCommands.Where(item => item.ClientTick <= ServerTick)
                     .OrderBy(item => item.ClientTick).ThenBy(item => item.PlayerId, StringComparer.Ordinal)
                     .ThenBy(item => item.Sequence).ToArray())
        {
            if (Simulation.FindShip(command.ShipId) is { IsAlive: true })
                _dispatcher.DispatchToShip(command.ShipId, command.Action, command.TargetSelector,
                    command.Destination, Simulation);
            _pendingCommands.Remove(command);
        }

        Simulation.ClearManualInputs();
        foreach (var received in _latestControls.Values
                     .Where(item => item.Frame.ClientTick <= ServerTick &&
                                    ServerTick - item.ReceivedAtServerTick <= ControlTimeoutTicks)
                     .OrderBy(item => item.Frame.PlayerId, StringComparer.Ordinal))
        {
            Simulation.SetManualInput(received.Frame.ShipId, received.Frame.Input);
        }

        foreach (var ability in _pendingAbilities.Where(item => item.ClientTick <= ServerTick)
                     .OrderBy(item => item.ClientTick).ThenBy(item => item.PlayerId, StringComparer.Ordinal)
                     .ThenBy(item => item.Sequence).ToArray())
        {
            if (Simulation.FindShip(ability.ShipId) is { IsAlive: true })
                Simulation.TryActivateAbility(ability.ShipId);
            _pendingAbilities.Remove(ability);
        }

        Simulation.Update(BattleSimulation.FixedStep);
        ServerTick++;
        return Snapshot();
    }

    public AuthoritativeSnapshot Snapshot() => new(
        ServerTick,
        Simulation.Status,
        SimulationChecksum.Compute(Simulation),
        Simulation.CaptureFrame());

    private CommandAdmission Validate(string playerId, string shipId, long sequence, int clientTick)
    {
        if (!_assignments.TryGetValue(playerId, out var ships)) return new(false, "Unknown player");
        if (!ships.Contains(shipId)) return new(false, "Player does not control that ship");
        if (_sequences.TryGetValue(playerId, out var sequences) && sequences.Contains(sequence))
            return new(false, "Duplicate command sequence");
        if (clientTick < ServerTick - MaximumPastTicks) return new(false, "Command arrived too late");
        if (clientTick > ServerTick + MaximumFutureTicks) return new(false, "Command is too far in the future");
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

    private sealed record ReceivedControl(NetworkControlFrame Frame, int ReceivedAtServerTick);

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
