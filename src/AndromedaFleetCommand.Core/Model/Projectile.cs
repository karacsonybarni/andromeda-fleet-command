namespace AndromedaFleetCommand.Core.Model;

public sealed class Projectile
{
    public Projectile(string sourceId, Team team, double damage, Vector2D position, Vector2D velocity, double life)
    {
        SourceId = sourceId;
        Team = team;
        Damage = damage;
        Position = position;
        Velocity = velocity;
        RemainingLife = life;
    }

    public string SourceId { get; }
    public Team Team { get; }
    public double Damage { get; }
    public Vector2D Position { get; private set; }
    public Vector2D Velocity { get; }
    public double RemainingLife { get; private set; }
    public bool IsAlive => RemainingLife > 0;

    internal void Update(double delta)
    {
        Position += Velocity * delta;
        RemainingLife -= delta;
    }
}
