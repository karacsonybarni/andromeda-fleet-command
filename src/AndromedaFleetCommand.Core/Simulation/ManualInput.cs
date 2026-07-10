namespace AndromedaFleetCommand.Core.Simulation;

public readonly record struct ManualInput(bool Thrust, bool Reverse, bool TurnLeft, bool TurnRight, bool Fire)
{
    public static readonly ManualInput None = new(false, false, false, false, false);
}
