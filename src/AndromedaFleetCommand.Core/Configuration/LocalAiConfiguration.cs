using System.Text.Json;

namespace AndromedaFleetCommand.Core.Configuration;

public sealed record LocalAiConfiguration(
    bool OllamaEnabled,
    string OllamaUrl,
    string OllamaModel,
    string? WhisperCli,
    string? WhisperModel,
    bool? PreferGpu = true)
{
    public const int MaximumGpuLayers = 999;

    public static LocalAiConfiguration Default =>
        new(false, "http://127.0.0.1:11434/", "qwen3:4b", null, null, true);

    public int OllamaGpuLayers => PreferGpu == false ? 0 : MaximumGpuLayers;

    public LocalAiConfiguration Normalize()
    {
        var endpoint = Uri.TryCreate(OllamaUrl, UriKind.Absolute, out var uri) && uri.IsLoopback &&
                       uri.Scheme is "http" or "https"
            ? uri.ToString()
            : Default.OllamaUrl;
        var model = string.IsNullOrWhiteSpace(OllamaModel) ? Default.OllamaModel : OllamaModel.Trim();
        return this with
        {
            OllamaUrl = endpoint,
            OllamaModel = model,
            WhisperCli = CleanPath(WhisperCli),
            WhisperModel = CleanPath(WhisperModel),
            PreferGpu = PreferGpu ?? true
        };
    }

    public static LocalAiConfiguration ApplyEnvironment(LocalAiConfiguration persisted)
    {
        var enabled = bool.TryParse(Environment.GetEnvironmentVariable("AFC_OLLAMA"), out var value)
            ? value
            : persisted.OllamaEnabled;
        var preferGpu = bool.TryParse(Environment.GetEnvironmentVariable("AFC_OLLAMA_GPU"), out var gpuValue)
            ? gpuValue
            : persisted.PreferGpu;
        return (persisted with
        {
            OllamaEnabled = enabled,
            PreferGpu = preferGpu,
            OllamaUrl = Environment.GetEnvironmentVariable("AFC_OLLAMA_URL") ?? persisted.OllamaUrl,
            OllamaModel = Environment.GetEnvironmentVariable("AFC_OLLAMA_MODEL") ?? persisted.OllamaModel,
            WhisperCli = Environment.GetEnvironmentVariable("AFC_WHISPER_CLI") ?? persisted.WhisperCli,
            WhisperModel = Environment.GetEnvironmentVariable("AFC_WHISPER_MODEL") ?? persisted.WhisperModel
        }).Normalize();
    }

    private static string? CleanPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
}

public sealed class LocalAiConfigurationStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalAiConfiguration Load()
    {
        if (!File.Exists(path)) return LocalAiConfiguration.Default;
        try
        {
            return (JsonSerializer.Deserialize<LocalAiConfiguration>(File.ReadAllText(path), JsonOptions) ??
                    LocalAiConfiguration.Default).Normalize();
        }
        catch (JsonException)
        {
            return LocalAiConfiguration.Default;
        }
    }

    public void Save(LocalAiConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration.Normalize(), JsonOptions));
        File.Move(temporaryPath, path, true);
    }
}
