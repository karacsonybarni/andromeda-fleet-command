using System.Text.Json;

namespace AndromedaFleetCommand.Core.Configuration;

public static class GamepadActionIds
{
    public const string Fire = "fire";
    public const string Ability = "ability";
    public const string Voice = "voice";
    public const string Missions = "missions";
    public const string Pause = "pause";
    public const string SwitchShip = "switch-ship";
    public const string Restart = "restart";
    public const string NextMission = "next-mission";
}

public sealed record GamepadActionDescriptor(string Id, string Label, string DefaultButton);

public static class GamepadActions
{
    public static IReadOnlyList<GamepadActionDescriptor> All { get; } =
    [
        new(GamepadActionIds.Fire, "Fire weapons", "A"),
        new(GamepadActionIds.Ability, "Tactical ability", "B"),
        new(GamepadActionIds.Voice, "Voice command", "Y"),
        new(GamepadActionIds.Missions, "Mission selection", "X"),
        new(GamepadActionIds.Pause, "Pause", "Start"),
        new(GamepadActionIds.SwitchShip, "Cycle controlled ship", "RightShoulder"),
        new(GamepadActionIds.Restart, "Restart mission", "DpadDown"),
        new(GamepadActionIds.NextMission, "Next mission", "DpadUp")
    ];

    public static GamepadActionDescriptor? Find(string id) =>
        All.FirstOrDefault(action => action.Id.Equals(id, StringComparison.Ordinal));
}

public sealed record GamepadBindings
{
    public Dictionary<string, string> Buttons { get; init; } = [];

    public static GamepadBindings Default => new()
    {
        Buttons = GamepadActions.All.ToDictionary(action => action.Id, action => action.DefaultButton,
            StringComparer.Ordinal)
    };

    public string Get(string actionId)
    {
        var action = GamepadActions.Find(actionId) ??
                     throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unknown gamepad action");
        return Buttons.TryGetValue(action.Id, out var button) && IsValidButtonName(button)
            ? button.Trim()
            : action.DefaultButton;
    }

    public GamepadBindings Normalize() => new()
    {
        Buttons = GamepadActions.All.ToDictionary(action => action.Id, action => Get(action.Id),
            StringComparer.Ordinal)
    };

    public GamepadBindings Rebind(string actionId, string buttonName)
    {
        var action = GamepadActions.Find(actionId) ??
                     throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unknown gamepad action");
        if (!IsValidButtonName(buttonName))
            throw new ArgumentException("A button name is required", nameof(buttonName));

        var normalized = Normalize();
        var newButton = buttonName.Trim();
        var previousButton = normalized.Get(action.Id);
        var conflict = GamepadActions.All.FirstOrDefault(candidate =>
            !candidate.Id.Equals(action.Id, StringComparison.Ordinal) &&
            normalized.Get(candidate.Id).Equals(newButton, StringComparison.OrdinalIgnoreCase));
        normalized.Buttons[action.Id] = newButton;
        if (conflict is not null) normalized.Buttons[conflict.Id] = previousButton;
        return normalized;
    }

    public GamepadBindings Reset(string actionId)
    {
        var action = GamepadActions.Find(actionId) ??
                     throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unknown gamepad action");
        return Rebind(action.Id, action.DefaultButton);
    }

    private static bool IsValidButtonName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 32;
}

public sealed class GamepadBindingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GamepadBindings Load()
    {
        if (!File.Exists(path)) return GamepadBindings.Default;
        try
        {
            return (JsonSerializer.Deserialize<GamepadBindings>(File.ReadAllText(path), JsonOptions) ??
                    GamepadBindings.Default).Normalize();
        }
        catch (JsonException)
        {
            return GamepadBindings.Default;
        }
    }

    public void Save(GamepadBindings bindings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(bindings.Normalize(), JsonOptions));
        File.Move(temporaryPath, path, true);
    }
}
