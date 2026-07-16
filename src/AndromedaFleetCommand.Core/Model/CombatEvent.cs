namespace AndromedaFleetCommand.Core.Model;

public enum CombatEventType
{
    MuzzleFlash,
    Impact,
    Destroyed,
    Order
}

public sealed class CombatEvent
{
    public CombatEvent(CombatEventType type, Vector2D position, string? message, double life,
        double? initialLife = null)
    {
        Type = type;
        Position = position;
        Message = message;
        RemainingLife = life;
        InitialLife = initialLife ?? life;
    }

    public CombatEventType Type { get; }
    public Vector2D Position { get; }
    public string? Message { get; }
    public double RemainingLife { get; private set; }
    public double InitialLife { get; }
    public bool IsAlive => RemainingLife > 0;
    internal void Update(double delta) => RemainingLife -= delta;
}
