using System.Text.Json;

namespace AndromedaFleetCommand.Core.Configuration;

public static class GameActionIds
{
    public const string Thrust = "thrust";
    public const string Reverse = "reverse";
    public const string TurnLeft = "turn-left";
    public const string TurnRight = "turn-right";
    public const string Fire = "fire";
    public const string Ability = "ability";
    public const string Command = "command";
    public const string Voice = "voice";
    public const string Pause = "pause";
    public const string Restart = "restart";
    public const string Missions = "missions";
    public const string Help = "help";
    public const string SwitchShip = "switch-ship";
    public const string NextMission = "next-mission";
}

public sealed record GameActionDescriptor(string Id, string Label, string DefaultKey);

public static class GameActions
{
    public static IReadOnlyList<GameActionDescriptor> All { get; } =
    [
        new(GameActionIds.Thrust, "Thrust", "W"),
        new(GameActionIds.Reverse, "Reverse", "S"),
        new(GameActionIds.TurnLeft, "Turn left", "A"),
        new(GameActionIds.TurnRight, "Turn right", "D"),
        new(GameActionIds.Fire, "Fire weapons", "Space"),
        new(GameActionIds.Ability, "Tactical ability", "Q"),
        new(GameActionIds.Command, "Command channel", "Enter"),
        new(GameActionIds.Voice, "Voice command", "V"),
        new(GameActionIds.Pause, "Pause", "P"),
        new(GameActionIds.Restart, "Restart mission", "R"),
        new(GameActionIds.Missions, "Mission selection", "M"),
        new(GameActionIds.Help, "Help / briefing", "H"),
        new(GameActionIds.SwitchShip, "Cycle controlled ship", "Tab"),
        new(GameActionIds.NextMission, "Next mission", "N")
    ];

    public static GameActionDescriptor? Find(string id) =>
        All.FirstOrDefault(action => action.Id.Equals(id, StringComparison.Ordinal));
}

public sealed record InputBindings
{
    public Dictionary<string, string> Keys { get; init; } = [];

    public static InputBindings Default => new()
    {
        Keys = GameActions.All.ToDictionary(action => action.Id, action => action.DefaultKey,
            StringComparer.Ordinal)
    };

    public string Get(string actionId)
    {
        var action = GameActions.Find(actionId) ??
                     throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unknown game action");
        return Keys.TryGetValue(action.Id, out var key) && IsValidKeyName(key)
            ? key.Trim()
            : action.DefaultKey;
    }

    public InputBindings Normalize() => new()
    {
        Keys = GameActions.All.ToDictionary(action => action.Id, action => Get(action.Id),
            StringComparer.Ordinal)
    };

    public InputBindings Rebind(string actionId, string keyName)
    {
        var action = GameActions.Find(actionId) ??
                     throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unknown game action");
        if (!IsValidKeyName(keyName)) throw new ArgumentException("A key name is required", nameof(keyName));

        var normalized = Normalize();
        var newKey = keyName.Trim();
        var previousKey = normalized.Get(action.Id);
        var conflictingAction = GameActions.All.FirstOrDefault(candidate =>
            !candidate.Id.Equals(action.Id, StringComparison.Ordinal) &&
            normalized.Get(candidate.Id).Equals(newKey, StringComparison.OrdinalIgnoreCase));

        normalized.Keys[action.Id] = newKey;
        if (conflictingAction is not null) normalized.Keys[conflictingAction.Id] = previousKey;
        return normalized;
    }

    public InputBindings Reset(string actionId)
    {
        var action = GameActions.Find(actionId) ??
                     throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unknown game action");
        return Rebind(action.Id, action.DefaultKey);
    }

    private static bool IsValidKeyName(string? keyName) =>
        !string.IsNullOrWhiteSpace(keyName) && keyName.Trim().Length <= 32;
}

public sealed class InputBindingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public InputBindings Load()
    {
        if (!File.Exists(path)) return InputBindings.Default;
        try
        {
            return (JsonSerializer.Deserialize<InputBindings>(File.ReadAllText(path), JsonOptions) ??
                    InputBindings.Default).Normalize();
        }
        catch (JsonException)
        {
            return InputBindings.Default;
        }
    }

    public void Save(InputBindings bindings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(bindings.Normalize(), JsonOptions));
        File.Move(temporaryPath, path, true);
    }
}
