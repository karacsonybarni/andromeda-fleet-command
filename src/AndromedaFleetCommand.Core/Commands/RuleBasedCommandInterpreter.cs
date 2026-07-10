using System.Text.RegularExpressions;
using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Commands;

public sealed partial class RuleBasedCommandInterpreter : ICommandInterpreter
{
    public const double WorldWidth = 1600;
    public const double WorldHeight = 900;

    public Task<CommandParseResult> InterpretAsync(string input, CancellationToken cancellationToken = default) =>
        Task.FromResult(Parse(input));

    public CommandParseResult Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return CommandParseResult.Failed("Say or type an order first");

        var text = Normalize(input);
        var action = FindAction(text);
        if (action is null)
        {
            return CommandParseResult.Failed("Try attack, intercept, defend, move, hold, form up, or retreat");
        }

        var subject = FindSubject(text);
        var target = FindTarget(text, action.Value);
        var destination = action is OrderType.Move or OrderType.Retreat or OrderType.FormUp
            ? FindDestination(text, action.Value)
            : null;
        if (RequiresTarget(action.Value) && target is null)
        {
            target = action == OrderType.Defend ? "flagship" : "nearest enemy";
        }

        return CommandParseResult.Succeeded(new FleetCommand(subject, action.Value, target, destination));
    }

    private static OrderType? FindAction(string text)
    {
        if (ContainsAny(text, "intercept", "cut off", "engage bombers")) return OrderType.Intercept;
        if (ContainsAny(text, "defend", "protect", "guard", "cover")) return OrderType.Defend;
        if (ContainsAny(text, "retreat", "withdraw", "fall back", "disengage")) return OrderType.Retreat;
        if (ContainsAny(text, "form up", "regroup", "formation", "rally")) return OrderType.FormUp;
        if (ContainsAny(text, "hold position", "hold", "stop", "stand by")) return OrderType.Hold;
        if (ContainsAny(text, "move", "go to", "advance", "navigate", "head to")) return OrderType.Move;
        if (ContainsAny(text, "attack", "focus fire", "destroy", "fire on", "engage")) return OrderType.Attack;
        return null;
    }

    private static string FindSubject(string text)
    {
        if (ContainsAny(text, "all ships", "entire fleet", "everyone", "all units")) return "all";
        var actionIndex = FirstActionIndex(text);
        foreach (var name in new[] { "flagship", "carrier one", "frigate two", "destroyer three" })
        {
            var index = text.IndexOf(name, StringComparison.Ordinal);
            if (index >= 0 && (actionIndex < 0 || index < actionIndex)) return name;
        }
        return "selected";
    }

    private static string? FindTarget(string text, OrderType action)
    {
        if (action == OrderType.Defend)
        {
            if (text.Contains("carrier", StringComparison.Ordinal)) return "carrier one";
            if (text.Contains("frigate", StringComparison.Ordinal)) return "frigate two";
            if (text.Contains("destroyer", StringComparison.Ordinal)) return "destroyer three";
            return "flagship";
        }
        if (ContainsAny(text, "enemy flagship", "hostile flagship", "their flagship")) return "enemy flagship";
        if (ContainsAny(text, "bomber wing", "bombers", "bomber")) return "nearest bomber";
        if (text.Contains("enemy carrier", StringComparison.Ordinal)) return "enemy carrier";
        if (ContainsAny(text, "nearest enemy", "closest enemy")) return "nearest enemy";

        var actionIndex = FirstActionIndex(text);
        if (actionIndex < 0) return null;
        var tail = ActionPrefixRegex().Replace(text[actionIndex..], string.Empty).Trim();
        return tail.Length is > 0 and < 45 ? tail : null;
    }

    private static Vector2D? FindDestination(string text, OrderType action)
    {
        if (action == OrderType.Retreat) return new(170, WorldHeight / 2);
        if (ContainsAny(text, "north", "top")) return new(WorldWidth / 2, 170);
        if (ContainsAny(text, "south", "bottom")) return new(WorldWidth / 2, WorldHeight - 170);
        if (ContainsAny(text, "left", "west")) return new(260, WorldHeight / 2);
        if (ContainsAny(text, "right", "east")) return new(WorldWidth - 260, WorldHeight / 2);
        if (text.Contains("carrier", StringComparison.Ordinal)) return null;
        return new(WorldWidth / 2, WorldHeight / 2);
    }

    private static int FirstActionIndex(string text)
    {
        var best = int.MaxValue;
        foreach (var word in new[]
                 {
                     "intercept", "defend", "protect", "retreat", "withdraw", "form up", "regroup",
                     "hold", "stop", "move", "advance", "attack", "engage", "destroy", "focus fire"
                 })
        {
            var index = text.IndexOf(word, StringComparison.Ordinal);
            if (index >= 0) best = Math.Min(best, index);
        }
        return best == int.MaxValue ? -1 : best;
    }

    private static bool RequiresTarget(OrderType action) =>
        action is OrderType.Attack or OrderType.Intercept or OrderType.Defend;

    private static string Normalize(string input) =>
        WhitespaceRegex().Replace(NonCommandCharactersRegex().Replace(input.ToLowerInvariant(), " "), " ").Trim();

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonCommandCharactersRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("^(intercept|attack|engage|destroy|focus fire|fire on|defend|protect|guard|cover)\\s+")]
    private static partial Regex ActionPrefixRegex();
}
