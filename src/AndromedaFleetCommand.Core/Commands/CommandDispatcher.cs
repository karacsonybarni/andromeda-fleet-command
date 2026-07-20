using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Simulation;

namespace AndromedaFleetCommand.Core.Commands;

public sealed class CommandDispatcher
{
    public string Dispatch(FleetCommand command, BattleSimulation simulation)
    {
        var subjects = ResolveSubjects(command.ShipSelector, simulation);
        return DispatchToSubjects(command, subjects, simulation);
    }

    public string DispatchToShip(
        string shipId,
        OrderType action,
        string? targetSelector,
        Vector2D? destination,
        BattleSimulation simulation)
    {
        var ship = simulation.FindShip(shipId);
        var subjects = ship is { IsAlive: true } ? new List<Ship> { ship } : [];
        return DispatchToSubjects(new(shipId, action, targetSelector, destination), subjects, simulation);
    }

    private static string DispatchToSubjects(FleetCommand command, List<Ship> subjects,
        BattleSimulation simulation)
    {
        if (subjects.Count == 0) return $"No available ship matched “{command.ShipSelector}”";

        var target = ResolveTarget(command, subjects[0], simulation);
        if (NeedsTarget(command.Action) && target is null)
        {
            return $"No valid target matched “{command.TargetSelector}”";
        }

        foreach (var ship in subjects)
        {
            var destination = command.Destination;
            if (command.Action == OrderType.Defend && target is not null) destination = target.Position;
            if (command.Action == OrderType.FormUp && destination is null) destination = subjects[0].Position;
            ship.Order = new(command.Action, target?.Id, destination);
        }

        var subjectText = subjects.Count == 1 ? subjects[0].Name : "All ships";
        var targetText = target is null ? string.Empty : $" “{target.Name}”";
        var message = $"{subjectText}: {SplitPascalCase(command.Action.ToString()).ToLowerInvariant()}{targetText}";
        simulation.AddOrderEvent(message);
        return message;
    }

    private static List<Ship> ResolveSubjects(string selector, BattleSimulation simulation)
    {
        if (selector.Equals("selected", StringComparison.OrdinalIgnoreCase)) return [simulation.SelectedShip];
        var fleet = simulation.Ships.Where(ship => ship.IsAlive && ship.Team == Team.Player).ToList();
        if (selector.Equals("all", StringComparison.OrdinalIgnoreCase)) return fleet;
        return fleet.Where(ship =>
                ship.Name.Contains(selector, StringComparison.OrdinalIgnoreCase) ||
                ship.Class.ToString().Contains(selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static Ship? ResolveTarget(FleetCommand command, Ship subject, BattleSimulation simulation)
    {
        if (!NeedsTarget(command.Action)) return null;
        var desiredTeam = command.Action == OrderType.Defend
            ? subject.Team
            : subject.Team == Team.Player ? Team.Enemy : Team.Player;
        var candidates = simulation.Ships
            .Where(ship => ship.IsAlive && ship.Team == desiredTeam && ship.Id != subject.Id)
            .ToList();
        var selector = command.TargetSelector?.ToLowerInvariant() ?? "nearest";

        if (selector.Contains("flagship", StringComparison.Ordinal))
        {
            var flagship = candidates.FirstOrDefault(ship => ship.Class == ShipClass.Flagship);
            if (flagship is not null) return flagship;
        }
        if (selector.Contains("bomber", StringComparison.Ordinal))
        {
            var bomber = candidates.Where(ship => ship.Class == ShipClass.Bomber)
                .MinBy(ship => ship.Position.DistanceTo(subject.Position));
            if (bomber is not null) return bomber;
        }
        foreach (var shipClass in Enum.GetValues<ShipClass>())
        {
            if (!selector.Contains(shipClass.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            var classTarget = candidates.Where(ship => ship.Class == shipClass)
                .MinBy(ship => ship.Position.DistanceTo(subject.Position));
            if (classTarget is not null) return classTarget;
        }
        var named = candidates.FirstOrDefault(ship =>
            ship.Name.Contains(selector, StringComparison.OrdinalIgnoreCase) ||
            selector.Contains(ship.Name, StringComparison.OrdinalIgnoreCase));
        return named ?? candidates.MinBy(ship => ship.Position.DistanceTo(subject.Position));
    }

    private static bool NeedsTarget(OrderType action) =>
        action is OrderType.Attack or OrderType.Intercept or OrderType.Defend;

    private static string SplitPascalCase(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
}
