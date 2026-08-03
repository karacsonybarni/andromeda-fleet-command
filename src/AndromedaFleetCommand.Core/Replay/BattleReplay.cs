using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Simulation;

namespace AndromedaFleetCommand.Core.Replay;

public enum ReplayEventType
{
    Maneuver,
    FleetCommand,
    SelectShip,
    CycleShip,
    Ability,
    EndTurn
}

public sealed record ReplayEvent(
    int Turn,
    ReplayEventType Type,
    ManualInput Input,
    FleetCommand? Command = null,
    int ShipIndex = 0);

public sealed record BattleReplay(
    int FormatVersion,
    MissionId MissionId,
    long Seed,
    IReadOnlyList<ReplayEvent> Events,
    int FinalTurn,
    string ExpectedChecksum);

public sealed class ReplayRecorder(MissionId missionId, long seed)
{
    private readonly List<ReplayEvent> _events = [];

    public void RecordManeuver(int turn, ManualInput input)
    {
        _events.Add(new(turn, ReplayEventType.Maneuver, input));
    }

    public void RecordCommand(int turn, FleetCommand command) =>
        _events.Add(new(turn, ReplayEventType.FleetCommand, ManualInput.None, command));

    public void RecordShipSelection(int turn, int shipIndex) =>
        _events.Add(new(turn, ReplayEventType.SelectShip, ManualInput.None, ShipIndex: shipIndex));

    public void RecordShipCycle(int turn) =>
        _events.Add(new(turn, ReplayEventType.CycleShip, ManualInput.None));

    public void RecordAbility(int turn) =>
        _events.Add(new(turn, ReplayEventType.Ability, ManualInput.None));

    public void RecordEndTurn(int turn) =>
        _events.Add(new(turn, ReplayEventType.EndTurn, ManualInput.None));

    public BattleReplay Complete(int finalTurn, BattleSimulation simulation) =>
        new(2, missionId, seed, _events.ToArray(), finalTurn, SimulationChecksum.Compute(simulation));
}

public static class ReplayRunner
{
    public static (BattleSimulation Simulation, string Checksum) Run(BattleReplay replay)
    {
        if (replay.FormatVersion != 2) throw new InvalidOperationException(
            $"Unsupported replay format {replay.FormatVersion}");
        var simulation = new BattleSimulation(replay.MissionId, replay.Seed);
        var dispatcher = new CommandDispatcher();
        var events = replay.Events.OrderBy(item => item.Turn).ToArray();
        foreach (var item in events)
        {
            if (simulation.Status != BattleStatus.Active) break;
            switch (item.Type)
            {
                case ReplayEventType.Maneuver:
                    simulation.PlanManeuver(item.Input);
                    break;
                case ReplayEventType.FleetCommand when item.Command is not null:
                    dispatcher.Dispatch(item.Command, simulation);
                    break;
                case ReplayEventType.SelectShip:
                    simulation.SelectPlayerShip(item.ShipIndex);
                    break;
                case ReplayEventType.CycleShip:
                    simulation.CycleSelectedShip();
                    break;
                case ReplayEventType.Ability:
                    simulation.TryActivateSelectedAbility();
                    break;
                case ReplayEventType.EndTurn:
                    simulation.ResolveTurn();
                    break;
            }
        }
        return (simulation, SimulationChecksum.Compute(simulation));
    }

    public static bool Validate(BattleReplay replay) =>
        string.Equals(Run(replay).Checksum, replay.ExpectedChecksum, StringComparison.Ordinal);
}

public static class SimulationChecksum
{
    public static string Compute(BattleSimulation simulation)
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder()
            .Append((int)simulation.Mission.Id).Append('|')
            .Append((int)simulation.Status).Append('|')
            .Append(simulation.TurnNumber).Append('|')
            .Append((int)simulation.Phase).Append('|')
            .Append(simulation.ResolutionSecondsRemaining.ToString("R", invariant)).Append('|')
            .Append(simulation.ElapsedSeconds.ToString("R", invariant)).Append('|');
        foreach (var ship in simulation.Ships.OrderBy(ship => ship.Id, StringComparer.Ordinal))
        {
            text.Append(ship.Id).Append(':')
                .Append(ship.Position.X.ToString("R", invariant)).Append(',')
                .Append(ship.Position.Y.ToString("R", invariant)).Append(',')
                .Append(ship.Velocity.X.ToString("R", invariant)).Append(',')
                .Append(ship.Velocity.Y.ToString("R", invariant)).Append(',')
                .Append(ship.Angle.ToString("R", invariant)).Append(',')
                .Append(ship.Hull.ToString("R", invariant)).Append(',')
                .Append(ship.Shield.ToString("R", invariant)).Append(',')
                .Append(ship.Energy.ToString("R", invariant)).Append(',')
                .Append(ship.WeaponCooldown.ToString("R", invariant)).Append(',')
                .Append(ship.AbilityCooldown.ToString("R", invariant)).Append(',')
                .Append(ship.OverdriveRemaining.ToString("R", invariant)).Append(',')
                .Append((int)ship.Order.Type).Append(',').Append(ship.Order.TargetId).Append(',')
                .Append(ship.Order.Destination?.X.ToString("R", invariant)).Append(',')
                .Append(ship.Order.Destination?.Y.ToString("R", invariant)).Append(',')
                .Append(ship.IsManuallyControlled).Append('|');
        }
        foreach (var projectile in simulation.Projectiles)
        {
            text.Append(projectile.SourceId).Append(':')
                .Append((int)projectile.Team).Append(',')
                .Append(projectile.Damage.ToString("R", invariant)).Append(',')
                .Append(projectile.Position.X.ToString("R", invariant)).Append(',')
                .Append(projectile.Position.Y.ToString("R", invariant)).Append(',')
                .Append(projectile.Velocity.X.ToString("R", invariant)).Append(',')
                .Append(projectile.Velocity.Y.ToString("R", invariant)).Append(',')
                .Append(projectile.RemainingLife.ToString("R", invariant)).Append('|');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public sealed class BattleReplayStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Save(BattleReplay replay)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"{replay.MissionId}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.afcreplay.json");
        File.WriteAllText(path, JsonSerializer.Serialize(replay, JsonOptions));
        return path;
    }

    public BattleReplay? LoadLatest()
    {
        if (!Directory.Exists(directory)) return null;
        var path = Directory.EnumerateFiles(directory, "*.afcreplay.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (path is null) return null;
        try
        {
            return JsonSerializer.Deserialize<BattleReplay>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
