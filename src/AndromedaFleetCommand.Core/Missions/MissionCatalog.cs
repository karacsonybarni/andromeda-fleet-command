using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Missions;

public static class MissionCatalog
{
    public static IReadOnlyList<MissionDefinition> All { get; } =
    [
        new(
            MissionId.FirstCommand,
            "First Command",
            "Training patrol, Nysa Reach",
            "Raiders have trapped a civilian convoy. Take direct control, switch ships, then order the frigate to intercept their bomber before the raider leader escapes.",
            "Frigate Two, intercept the bomber",
            0xAFC1001,
            [
                Player("player-flagship", "Flagship", ShipClass.Flagship, 250, 460),
                Player("player-frigate", "Frigate Two", ShipClass.Frigate, 300, 330),
                Enemy("enemy-raider-leader", "Raider Leader", ShipClass.Destroyer, 1240, 460),
                Enemy("enemy-bomber-1", "Raider Bomber", ShipClass.Bomber, 1090, 290),
                Enemy("enemy-escort-1", "Raider Escort", ShipClass.Escort, 1150, 610)
            ],
            [
                new("player-frigate", OrderType.Defend, "player-flagship"),
                new("enemy-raider-leader", OrderType.Attack, "player-flagship"),
                new("enemy-bomber-1", OrderType.Attack, "player-flagship"),
                new("enemy-escort-1", OrderType.Attack, "player-frigate")
            ],
            new(MissionObjectiveKind.DestroyTarget,
                "Disable the raider leader",
                "Destroy the raider command ship while keeping your flagship alive.",
                TargetId: "enemy-raider-leader",
                ProtectedShipId: "player-flagship")),
        new(
            MissionId.BrokenShield,
            "Broken Shield",
            "Convoy defence, Pelagos Corridor",
            "A Ketzal bomber wing is diving on Carrier One. Split the fleet: intercept the bombers, defend the carrier, and use each ship's tactical role.",
            "Frigate Two, intercept the bomber wing",
            0xAFC2002,
            [
                Player("player-flagship", "Flagship", ShipClass.Flagship, 245, 450),
                Player("player-carrier", "Carrier One", ShipClass.Carrier, 205, 270),
                Player("player-frigate", "Frigate Two", ShipClass.Frigate, 330, 345),
                Player("player-destroyer", "Destroyer Three", ShipClass.Destroyer, 270, 625),
                Enemy("enemy-bomber-1", "Bomber One", ShipClass.Bomber, 1180, 190),
                Enemy("enemy-bomber-2", "Bomber Two", ShipClass.Bomber, 1240, 270),
                Enemy("enemy-bomber-3", "Bomber Three", ShipClass.Bomber, 1200, 690),
                Enemy("enemy-bomber-4", "Bomber Four", ShipClass.Bomber, 1280, 610),
                Enemy("enemy-escort-1", "Escort One", ShipClass.Escort, 1320, 390),
                Enemy("enemy-escort-2", "Escort Two", ShipClass.Escort, 1350, 520)
            ],
            [
                new("player-carrier", OrderType.Defend, "player-flagship"),
                new("player-frigate", OrderType.Intercept, "enemy-bomber-1"),
                new("player-destroyer", OrderType.Attack, "enemy-bomber-3"),
                new("enemy-bomber-1", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-2", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-3", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-4", OrderType.Attack, "player-carrier"),
                new("enemy-escort-1", OrderType.Attack, "player-flagship"),
                new("enemy-escort-2", OrderType.Attack, "player-destroyer")
            ],
            new(MissionObjectiveKind.EliminateClass,
                "Break the bomber wing",
                "Destroy every bomber before Carrier One is lost.",
                TargetClass: ShipClass.Bomber,
                ProtectedShipId: "player-carrier")),
        new(
            MissionId.BlackSun,
            "Black Sun",
            "Fleet engagement, Andromeda frontier",
            "The Ketzal flagship is exposed, but its carrier and escorts form a layered defence. Command the entire fleet, protect your flagship, and end the engagement.",
            "All ships, attack the enemy flagship",
            0xAFC3003,
            [
                Player("player-flagship", "Flagship", ShipClass.Flagship, 260, 450),
                Player("player-carrier", "Carrier One", ShipClass.Carrier, 205, 265),
                Player("player-frigate", "Frigate Two", ShipClass.Frigate, 330, 335),
                Player("player-destroyer", "Destroyer Three", ShipClass.Destroyer, 260, 620),
                Enemy("enemy-flagship", "Enemy Flagship", ShipClass.Flagship, 1340, 450),
                Enemy("enemy-carrier", "Enemy Carrier", ShipClass.Carrier, 1390, 235),
                Enemy("enemy-destroyer-1", "Enemy Destroyer One", ShipClass.Destroyer, 1250, 300),
                Enemy("enemy-destroyer-2", "Enemy Destroyer Two", ShipClass.Destroyer, 1270, 650),
                Enemy("enemy-bomber-1", "Bomber One", ShipClass.Bomber, 1170, 220),
                Enemy("enemy-bomber-2", "Bomber Two", ShipClass.Bomber, 1210, 185),
                Enemy("enemy-bomber-3", "Bomber Three", ShipClass.Bomber, 1205, 730),
                Enemy("enemy-escort-1", "Enemy Escort One", ShipClass.Escort, 1300, 540),
                Enemy("enemy-escort-2", "Enemy Escort Two", ShipClass.Escort, 1320, 580)
            ],
            [
                new("player-carrier", OrderType.Defend, "player-flagship"),
                new("player-frigate", OrderType.Intercept, "enemy-bomber-1"),
                new("player-destroyer", OrderType.Attack, "enemy-flagship"),
                new("enemy-flagship", OrderType.Attack, "player-flagship"),
                new("enemy-carrier", OrderType.Attack, "player-flagship"),
                new("enemy-destroyer-1", OrderType.Attack, "player-flagship"),
                new("enemy-destroyer-2", OrderType.Attack, "player-flagship"),
                new("enemy-bomber-1", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-2", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-3", OrderType.Attack, "player-carrier"),
                new("enemy-escort-1", OrderType.Attack, "player-flagship"),
                new("enemy-escort-2", OrderType.Attack, "player-flagship")
            ],
            new(MissionObjectiveKind.DestroyTarget,
                "Destroy the enemy flagship",
                "Break the Ketzal command ship while your flagship survives.",
                TargetId: "enemy-flagship",
                ProtectedShipId: "player-flagship"))
    ];

    public static MissionDefinition Get(MissionId id) =>
        All.First(mission => mission.Id == id);

    public static int IndexOf(MissionId id) =>
        All.Select((mission, index) => (mission, index))
            .First(entry => entry.mission.Id == id).index;

    private static ShipSpawn Player(string id, string name, ShipClass shipClass, double x, double y) =>
        new(id, name, shipClass, Team.Player, new(x, y), 0);

    private static ShipSpawn Enemy(string id, string name, ShipClass shipClass, double x, double y) =>
        new(id, name, shipClass, Team.Enemy, new(x, y), Math.PI);
}
