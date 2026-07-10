namespace AndromedaFleetCommand.Core.Model;

public enum ShipClass
{
    Flagship,
    Carrier,
    Frigate,
    Destroyer,
    Bomber,
    Escort
}

public readonly record struct ShipStats(
    double MaxHull,
    double MaxShield,
    double MaxSpeed,
    double Acceleration,
    double TurnRate,
    double WeaponRange,
    double FireCooldown,
    double WeaponDamage,
    double Radius);

public static class ShipCatalog
{
    public static ShipStats StatsFor(ShipClass shipClass) => shipClass switch
    {
        ShipClass.Flagship => new(420, 230, 112, 58, 1.45, 470, 0.72, 25, 34),
        ShipClass.Carrier => new(340, 190, 100, 52, 1.25, 430, 0.88, 19, 31),
        ShipClass.Frigate => new(210, 120, 165, 88, 2.35, 360, 0.42, 13, 22),
        ShipClass.Destroyer => new(285, 145, 132, 68, 1.72, 520, 0.92, 34, 27),
        ShipClass.Bomber => new(95, 35, 150, 92, 2.2, 250, 1.18, 31, 14),
        ShipClass.Escort => new(125, 55, 175, 105, 2.65, 290, 0.48, 11, 16),
        _ => throw new ArgumentOutOfRangeException(nameof(shipClass))
    };
}
