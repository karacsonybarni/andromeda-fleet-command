namespace AndromedaFleetCommand.Core.Model;

public sealed class Ship
{
    public Ship(string id, string name, ShipClass shipClass, Team team, Vector2D position, double angle)
    {
        Id = id;
        Name = name;
        Class = shipClass;
        Team = team;
        Position = position;
        Angle = angle;
        Hull = Stats.MaxHull;
        Shield = Stats.MaxShield;
    }

    public string Id { get; }
    public string Name { get; }
    public ShipClass Class { get; }
    public Team Team { get; }
    public ShipStats Stats => ShipCatalog.StatsFor(Class);
    public Vector2D Position { get; internal set; }
    public Vector2D Velocity { get; internal set; }
    public double Angle { get; internal set; }
    public double Hull { get; private set; }
    public double Shield { get; private set; }
    public double Energy { get; private set; } = 100;
    public double WeaponCooldown { get; internal set; }
    public double AbilityCooldown { get; internal set; }
    public double OverdriveRemaining { get; internal set; }
    public ShipOrder Order { get; internal set; } = ShipOrder.Hold;
    public bool IsManuallyControlled { get; internal set; }
    public bool IsAlive => Hull > 0;
    public double HullRatio => Math.Max(0, Hull / Stats.MaxHull);
    public double ShieldRatio => Math.Max(0, Shield / Stats.MaxShield);
    public double EnergyRatio => Math.Max(0, Energy / 100);
    public double EffectiveMaxSpeed => Stats.MaxSpeed * (OverdriveRemaining > 0 ? 1.65 : 1);

    public void ApplyDamage(double amount)
    {
        if (amount <= 0 || !IsAlive) return;
        var absorbed = Math.Min(Shield, amount);
        Shield -= absorbed;
        Hull = Math.Max(0, Hull - (amount - absorbed));
    }

    internal void RegenerateShield(double amount)
    {
        if (IsAlive) Shield = Math.Min(Stats.MaxShield, Shield + amount);
    }

    internal void RecoverEnergy(double amount) => Energy = Math.Min(100, Energy + amount);
    internal void DrainEnergy(double amount) => Energy = Math.Max(0, Energy - amount);
    internal void ActivateOverdrive(double seconds) => OverdriveRemaining = Math.Max(OverdriveRemaining, seconds);

    internal void NormalizeAngle()
    {
        while (Angle > Math.PI) Angle -= Math.PI * 2;
        while (Angle < -Math.PI) Angle += Math.PI * 2;
    }
}
