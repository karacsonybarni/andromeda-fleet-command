using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Missions;

public enum MissionId
{
    FirstCommand = 0,
    BrokenShield = 1,
    BlackSun = 2,
    Afterglow = 4,
    SilentRelay = 5,
    Crownfall = 6,
    HollowMoon = 7,
    ThiefOfSuns = 8,
    FoundryZero = 9,
    FalseOrders = 10,
    MutinyAtLyra = 11,
    SerasChoice = 12,
    PrisonerSignal = 13,
    GardenOfStone = 14,
    EnemyOfMyEnemy = 15,
    DarkNursery = 16,
    GravitysChoir = 17,
    TheFirstSeed = 18,
    HomewardFire = 19,
    TheLongRetreat = 20,
    GateOfKnives = 21,
    CrownFleet = 22,
    ChoiceAtNysa = 23,
    OneCommand = 24,

    // Keep the original wire/replay value for this non-campaign scenario.
    FleetDuel = 3
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

public sealed record MissionNarrative(
    string Chapter,
    string Speaker,
    IReadOnlyList<string> BriefingLines,
    IReadOnlyList<string> VictoryLines,
    IReadOnlyList<string> FailureLines);

public sealed record MissionComplexity(
    int Rating,
    string Tier,
    int SimultaneousThreatGroups,
    string TacticalFocus);

public sealed record MissionDefinition(
    MissionId Id,
    string Title,
    string Subtitle,
    string Briefing,
    string RecommendedOrder,
    long Seed,
    IReadOnlyList<ShipSpawn> Ships,
    IReadOnlyList<InitialOrder> InitialOrders,
    MissionObjective Objective,
    MissionNarrative Narrative,
    MissionComplexity Complexity,
    int EstimatedMinutes);

public readonly record struct MissionObjectiveProgress(
    string Label,
    double Ratio,
    int Remaining,
    int Total)
{
    public double ClampedRatio => Math.Clamp(Ratio, 0, 1);
}
