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
    ManualInput,
    FleetCommand,
    SelectShip,
    CycleShip,
    Ability
}

public sealed record ReplayEvent(
    int Tick,
    ReplayEventType Type,
    ManualInput Input,
    FleetCommand? Command = null,
    int ShipIndex = 0);

public sealed record BattleReplay(
    int FormatVersion,
    MissionId MissionId,
    long Seed,
    IReadOnlyList<ReplayEvent> Events,
    int FinalTick,
    string ExpectedChecksum);

public sealed class ReplayRecorder(MissionId missionId, long seed)
{
    private readonly List<ReplayEvent> _events = [];
    private ManualInput _lastInput;
    private bool _hasInput;

    public void RecordInput(int tick, ManualInput input)
    {
        if (_hasInput && input == _lastInput) return;
        _events.Add(new(tick, ReplayEventType.ManualInput, input));
        _lastInput = input;
        _hasInput = true;
    }

    public void RecordCommand(int tick, FleetCommand command) =>
        _events.Add(new(tick, ReplayEventType.FleetCommand, ManualInput.None, command));

    public void RecordShipSelection(int tick, int shipIndex) =>
        _events.Add(new(tick, ReplayEventType.SelectShip, ManualInput.None, ShipIndex: shipIndex));

    public void RecordShipCycle(int tick) =>
        _events.Add(new(tick, ReplayEventType.CycleShip, ManualInput.None));

    public void RecordAbility(int tick) =>
        _events.Add(new(tick, ReplayEventType.Ability, ManualInput.None));

    public BattleReplay Complete(int finalTick, BattleSimulation simulation) =>
        new(1, missionId, seed, _events.ToArray(), finalTick, SimulationChecksum.Compute(simulation));
}

public static class ReplayRunner
{
    public static (BattleSimulation Simulation, string Checksum) Run(BattleReplay replay)
    {
        if (replay.FormatVersion != 1) throw new InvalidOperationException(
            $"Unsupported replay format {replay.FormatVersion}");
        var simulation = new BattleSimulation(replay.MissionId, replay.Seed);
        var dispatcher = new CommandDispatcher();
        var events = replay.Events.OrderBy(item => item.Tick).ToArray();
        var eventIndex = 0;
        var input = ManualInput.None;
        for (var tick = 0; tick < replay.FinalTick && simulation.Status == BattleStatus.Active; tick++)
        {
            while (eventIndex < events.Length && events[eventIndex].Tick == tick)
            {
                var item = events[eventIndex++];
                switch (item.Type)
                {
                    case ReplayEventType.ManualInput:
                        input = item.Input;
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
                }
            }
            simulation.SetManualInput(input);
            simulation.Update(BattleSimulation.FixedStep);
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
                .Append((int)ship.Order.Type).Append(',').Append(ship.Order.TargetId).Append('|');
        }
        foreach (var projectile in simulation.Projectiles)
        {
            text.Append(projectile.SourceId).Append(':')
                .Append(projectile.Position.X.ToString("R", invariant)).Append(',')
                .Append(projectile.Position.Y.ToString("R", invariant)).Append(',')
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
