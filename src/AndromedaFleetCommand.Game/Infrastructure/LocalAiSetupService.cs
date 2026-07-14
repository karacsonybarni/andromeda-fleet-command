using System.Net.Http.Json;
using System.Text.Json;
using AndromedaFleetCommand.Core.Configuration;

namespace AndromedaFleetCommand.Game.Infrastructure;

public sealed record LocalAiReadiness(
    bool OllamaReachable,
    bool OllamaModelInstalled,
    bool WhisperCliFound,
    bool WhisperModelFound,
    string Detail)
{
    public bool VoiceReady => WhisperCliFound && WhisperModelFound;
}

public sealed class LocalAiSetupService : IDisposable
{
    public const string WhisperModelFileName = "ggml-base.en.bin";
    public const string WhisperModelUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<LocalAiReadiness> CheckAsync(LocalAiConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var ollamaReachable = false;
        var modelInstalled = false;
        try
        {
            using var response = await _client.GetAsync(new Uri(new Uri(configuration.OllamaUrl), "api/tags"),
                cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            ollamaReachable = true;
            modelInstalled = document.RootElement.GetProperty("models").EnumerateArray()
                .Any(model => ModelMatches(model.GetProperty("name").GetString(), configuration.OllamaModel));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Readiness is reported below; the trusted offline parser remains available.
        }

        var cliFound = configuration.WhisperCli is { } cli && File.Exists(cli);
        var whisperModelFound = configuration.WhisperModel is { } model && File.Exists(model);
        var detail = ollamaReachable
            ? modelInstalled ? "Ollama and the command model are ready" : "Ollama found; command model not installed"
            : "Ollama is not running; offline commands remain ready";
        return new(ollamaReachable, modelInstalled, cliFound, whisperModelFound, detail);
    }

    public async Task PullOllamaModelAsync(LocalAiConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await client.PostAsJsonAsync(
            new Uri(new Uri(configuration.OllamaUrl), "api/pull"),
            new { name = configuration.OllamaModel, stream = false }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DownloadWhisperModelAsync(string destination,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = destination + ".download";
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await client.GetAsync(WhisperModelUrl,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(temporaryPath))
            await source.CopyToAsync(target, cancellationToken);
        File.Move(temporaryPath, destination, true);
    }

    public static string? FindWhisperCli()
    {
        var configured = Environment.GetEnvironmentVariable("AFC_WHISPER_CLI");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var executableNames = OperatingSystem.IsWindows()
            ? new[] { "whisper-cli.exe", "whisper.exe" }
            : new[] { "whisper-cli", "whisper" };
        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        foreach (var executable in executableNames)
        {
            var bundled = Path.Combine(processDirectory, "tools", "whisper", executable);
            if (File.Exists(bundled)) return Path.GetFullPath(bundled);
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var executable in executableNames)
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public void Dispose() => _client.Dispose();

    private static bool ModelMatches(string? installed, string requested) =>
        !string.IsNullOrWhiteSpace(installed) &&
        (installed.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
         installed.StartsWith(requested + ":", StringComparison.OrdinalIgnoreCase) ||
         requested.StartsWith(installed + ":", StringComparison.OrdinalIgnoreCase));
}
