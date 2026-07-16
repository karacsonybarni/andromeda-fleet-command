using System.Text.Json;

namespace AndromedaFleetCommand.Core.Multiplayer;

public sealed record JoinRequest(string DisplayName, int ProtocolVersion);
public sealed record MatchStartMessage(FleetLobbySnapshot Lobby, AuthoritativeSnapshot Snapshot);

public static class MultiplayerWire
{
    public const int ProtocolVersion = 1;
    public const int MaximumPayloadCharacters = 262_144;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        IgnoreReadOnlyProperties = true
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static bool TryDeserialize<T>(string payload, out T? message)
    {
        message = default;
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumPayloadCharacters) return false;
        try
        {
            message = JsonSerializer.Deserialize<T>(payload, Options);
            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
