using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Configuration;

namespace AndromedaFleetCommand.Game.Infrastructure;

public sealed class LocalCommandInterpreter : ICommandInterpreter, IDisposable
{
    private readonly RuleBasedCommandInterpreter _fallback;
    private readonly HttpClient _client;
    private readonly bool _enabled;
    private readonly string _model;
    private readonly int _gpuLayers;

    public LocalCommandInterpreter(RuleBasedCommandInterpreter fallback)
        : this(fallback, LocalAiConfiguration.ApplyEnvironment(LocalAiConfiguration.Default))
    {
    }

    public LocalCommandInterpreter(RuleBasedCommandInterpreter fallback, LocalAiConfiguration configuration)
    {
        _fallback = fallback;
        var normalized = configuration.Normalize();
        _enabled = normalized.OllamaEnabled;
        _model = normalized.OllamaModel;
        _gpuLayers = normalized.OllamaGpuLayers;
        _client = new() { BaseAddress = new(normalized.OllamaUrl), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<CommandParseResult> InterpretAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled) return _fallback.Parse(input);
        try
        {
            const string instructions = """
                Rewrite the player's fleet order as one concise canonical command.
                Subjects: selected ship, all ships, Flagship, Carrier One, Frigate Two, Destroyer Three.
                Actions: attack, intercept, defend, move, hold, form up, retreat.
                Targets: enemy flagship, enemy carrier, nearest enemy, nearest bomber, or a friendly ship.
                Output only the rewritten order, without analysis or JSON.
                """;
            var request = new OllamaRequest(_model, false, $"{instructions}\nPlayer order: {input}",
                new(_gpuLayers));
            using var response = await _client.PostAsJsonAsync("api/generate", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);
            if (string.IsNullOrWhiteSpace(result?.Response)) return _fallback.Parse(input);
            var parsed = _fallback.Parse(result.Response);
            return parsed.Success ? parsed : _fallback.Parse(input);
        }
        catch (Exception)
        {
            return _fallback.Parse(input);
        }
    }

    public void Dispose() => _client.Dispose();

    private sealed record OllamaRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("options")] OllamaOptions Options);

    private sealed record OllamaOptions([property: JsonPropertyName("num_gpu")] int NumGpu);

    private sealed record OllamaResponse([property: JsonPropertyName("response")] string Response);
}
