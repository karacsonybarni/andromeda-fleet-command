namespace AndromedaFleetCommand.Core.Model;

public sealed record ShipOrder(OrderType Type, string? TargetId = null, Vector2D? Destination = null)
{
    public static readonly ShipOrder Hold = new(OrderType.Hold);
    public static ShipOrder Attack(string targetId) => new(OrderType.Attack, targetId);
    public static ShipOrder Move(Vector2D destination) => new(OrderType.Move, Destination: destination);
}
