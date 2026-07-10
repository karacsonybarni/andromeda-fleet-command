using System.Text.Json;

namespace AndromedaFleetCommand.Core.Configuration;

public enum ColorVisionMode
{
    Standard,
    Deuteranopia,
    Tritanopia,
    HighContrast
}

public sealed record GameSettings(
    double MasterVolume,
    ColorVisionMode ColorMode,
    bool Subtitles,
    bool ReduceFlashes,
    double GamepadDeadzone)
{
    public static GameSettings Default => new(0.8, ColorVisionMode.Standard, true, false, 0.22);

    public GameSettings Normalize() => this with
    {
        MasterVolume = Math.Clamp(MasterVolume, 0, 1),
        ColorMode = Enum.IsDefined(ColorMode) ? ColorMode : ColorVisionMode.Standard,
        GamepadDeadzone = Math.Clamp(GamepadDeadzone, 0.08, 0.65)
    };
}

public sealed class GameSettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GameSettings Load()
    {
        if (!File.Exists(path)) return GameSettings.Default;
        try
        {
            return (JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), JsonOptions) ??
                    GameSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            return GameSettings.Default;
        }
    }

    public void Save(GameSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        File.Move(temporaryPath, path, true);
    }
}
