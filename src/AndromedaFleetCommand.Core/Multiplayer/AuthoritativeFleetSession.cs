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

public sealed record CommandAdmission(bool Accepted, string Message);

public sealed record AuthoritativeSnapshot(
    int ServerTick,
    BattleStatus Status,
    string Checksum,
    IReadOnlyList<ShipSnapshot> Ships);

public sealed record ShipSnapshot(
    string Id,
    Vector2D Position,
    Vector2D Velocity,
    double Hull,
    double Shield,
    OrderType Order);

public sealed class AuthoritativeFleetSession
{
    private const int MaximumFutureTicks = 120;
    private const int MaximumPastTicks = 30;
    private readonly Dictionary<string, HashSet<string>> _assignments = new(StringComparer.Ordinal);
    private readonly HashSet<(string PlayerId, long Sequence)> _sequences = [];
    private readonly List<NetworkFleetCommand> _pending = [];
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
        var validShips = shipIds.Where(id => Simulation.FindShip(id)?.Team == Team.Player)
            .ToHashSet(StringComparer.Ordinal);
        if (validShips.Count == 0) throw new ArgumentException("At least one player ship is required", nameof(shipIds));
        _assignments[playerId] = validShips;
    }

    public CommandAdmission Submit(NetworkFleetCommand command)
    {
        if (!_assignments.TryGetValue(command.PlayerId, out var ships))
            return new(false, "Unknown player");
        if (!ships.Contains(command.ShipId)) return new(false, "Player does not control that ship");
        if (_sequences.Contains((command.PlayerId, command.Sequence))) return new(false, "Duplicate command sequence");
        if (command.ClientTick < ServerTick - MaximumPastTicks) return new(false, "Command arrived too late");
        if (command.ClientTick > ServerTick + MaximumFutureTicks) return new(false, "Command is too far in the future");
        if (!Enum.IsDefined(command.Action)) return new(false, "Unknown order type");
        _sequences.Add((command.PlayerId, command.Sequence));
        _pending.Add(command);
        return new(true, "Command accepted");
    }

    public AuthoritativeSnapshot Step()
    {
        foreach (var command in _pending.Where(item => item.ClientTick <= ServerTick)
                     .OrderBy(item => item.ClientTick).ThenBy(item => item.PlayerId, StringComparer.Ordinal)
                     .ThenBy(item => item.Sequence).ToArray())
        {
            var ship = Simulation.FindShip(command.ShipId);
            if (ship is { IsAlive: true })
                _dispatcher.Dispatch(new(ship.Name, command.Action, command.TargetSelector, command.Destination),
                    Simulation);
            _pending.Remove(command);
        }
        Simulation.Update(BattleSimulation.FixedStep);
        ServerTick++;
        return Snapshot();
    }

    public AuthoritativeSnapshot Snapshot() => new(ServerTick, Simulation.Status,
        SimulationChecksum.Compute(Simulation), Simulation.Ships.Select(ship => new ShipSnapshot(
            ship.Id, ship.Position, ship.Velocity, ship.Hull, ship.Shield, ship.Order.Type)).ToArray());
}
