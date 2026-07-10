using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Commands;

public sealed record FleetCommand(
    string ShipSelector,
    OrderType Action,
    string? TargetSelector = null,
    Vector2D? Destination = null);

public sealed record CommandParseResult(bool Success, FleetCommand? Command, string Message)
{
    public static CommandParseResult Succeeded(FleetCommand command) => new(true, command, "Command understood");
    public static CommandParseResult Failed(string message) => new(false, null, message);
}

public interface ICommandInterpreter
{
    Task<CommandParseResult> InterpretAsync(string input, CancellationToken cancellationToken = default);
}
