using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Missions;

public enum MissionId
{
    FirstCommand,
    BrokenShield,
    BlackSun
}

public enum MissionObjectiveKind
{
    DestroyTarget,
    EliminateClass
}

public sealed record ShipSpawn(
    string Id,
    string Name,
    ShipClass Class,
    Team Team,
    Vector2D Position,
    double Angle);

public sealed record InitialOrder(
    string ShipId,
    OrderType Type,
    string? TargetId = null,
    Vector2D? Destination = null);

public sealed record MissionObjective(
    MissionObjectiveKind Kind,
    string Title,
    string Description,
    string? TargetId = null,
    ShipClass? TargetClass = null,
    string? ProtectedShipId = null);

public sealed record MissionDefinition(
    MissionId Id,
    string Title,
    string Subtitle,
    string Briefing,
    string RecommendedOrder,
    long Seed,
    IReadOnlyList<ShipSpawn> Ships,
    IReadOnlyList<InitialOrder> InitialOrders,
    MissionObjective Objective);

public readonly record struct MissionObjectiveProgress(
    string Label,
    double Ratio,
    int Remaining,
    int Total)
{
    public double ClampedRatio => Math.Clamp(Ratio, 0, 1);
}
