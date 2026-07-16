using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Simulation;

public sealed record SimulationFrame(
    MissionId MissionId,
    long Seed,
    BattleStatus Status,
    double ElapsedSeconds,
    double InitialPlayerStrength,
    double InitialEnemyStrength,
    IReadOnlyList<ShipFrame> Ships,
    IReadOnlyList<ProjectileFrame> Projectiles,
    IReadOnlyList<CombatEventFrame> Events);

public sealed record ShipFrame(
    string Id,
    Vector2D Position,
    Vector2D Velocity,
    double Angle,
    double Hull,
    double Shield,
    double Energy,
    double WeaponCooldown,
    double AbilityCooldown,
    double OverdriveRemaining,
    OrderType Order,
    string? TargetId,
    Vector2D? Destination,
    bool IsManuallyControlled);

public sealed record ProjectileFrame(
    string SourceId,
    Team Team,
    double Damage,
    Vector2D Position,
    Vector2D Velocity,
    double RemainingLife);

public sealed record CombatEventFrame(
    CombatEventType Type,
    Vector2D Position,
    string? Message,
    double RemainingLife,
    double InitialLife);
