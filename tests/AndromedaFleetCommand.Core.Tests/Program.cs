using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Simulation;

var tests = new (string Name, Action Body)[]
{
    ("Vector operations remain finite", VectorOperationsRemainFinite),
    ("Parser handles fleet attack", ParserHandlesFleetAttack),
    ("Parser handles intercept, defend, and movement", ParserHandlesOrders),
    ("Parser safely rejects unknown input", ParserRejectsUnknownInput),
    ("Simulation is deterministic", SimulationIsDeterministic),
    ("Manual control moves selected ship", ManualControlMovesShip),
    ("Dispatcher assigns bounded orders", DispatcherAssignsOrders),
    ("Ship abilities are bounded by cooldowns", ShipAbilitiesUseCooldowns),
    ("Damage and victory rules work", DamageAndVictoryRules),
    ("Long battle maintains invariants", LongBattleMaintainsInvariants),
    ("Mission catalog is internally valid", MissionCatalogIsValid),
    ("Mission objectives determine victory and defeat", MissionObjectivesDetermineStatus),
    ("Every campaign mission survives integration play", EveryMissionSurvivesIntegrationPlay),
    ("Campaign progression unlocks missions sequentially", CampaignProgressionUnlocksMissions),
    ("Campaign progress persists and recovers safely", CampaignProgressPersistsSafely),
    ("Tutorial advances only through intended actions", TutorialAdvancesInOrder)
};

var failures = 0;
var started = DateTime.UtcNow;
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {name}\n{error}");
    }
}

Console.WriteLine($"\n{tests.Length} tests, {failures} failures, {(DateTime.UtcNow - started).TotalSeconds:F2}s");
return failures == 0 ? 0 : 1;

static void VectorOperationsRemainFinite()
{
    var vector = new Vector2D(3, 4);
    Near(5, vector.Length, 1e-9, "Vector length");
    Near(1, vector.Normalized.Length, 1e-9, "Normalized vector");
    Equal(new Vector2D(5, 7), vector + new Vector2D(2, 3), "Vector addition");
    Near(2, vector.Limit(2).Length, 1e-9, "Vector limit");
    True(Vector2D.Zero.Normalized.IsFinite, "Normalizing zero remains finite");
}

static void ParserHandlesFleetAttack()
{
    var result = new RuleBasedCommandInterpreter().Parse("All ships, focus fire on the enemy flagship.");
    True(result.Success && result.Command is not null, "Fleet attack should parse");
    Equal("all", result.Command!.ShipSelector, "Fleet selector");
    Equal(OrderType.Attack, result.Command.Action, "Attack action");
    Equal("enemy flagship", result.Command.TargetSelector, "Flagship target");
}

static void ParserHandlesOrders()
{
    var parser = new RuleBasedCommandInterpreter();
    var intercept = parser.Parse("Frigate Two, intercept the bomber wing");
    Equal("frigate two", intercept.Command!.ShipSelector, "Frigate subject");
    Equal(OrderType.Intercept, intercept.Command.Action, "Intercept action");
    Equal("nearest bomber", intercept.Command.TargetSelector, "Bomber target");

    var defend = parser.Parse("Destroyer Three, protect Carrier One");
    Equal(OrderType.Defend, defend.Command!.Action, "Defend action");
    Equal("carrier one", defend.Command.TargetSelector, "Carrier target");

    var move = parser.Parse("Flagship, move north");
    Equal(OrderType.Move, move.Command!.Action, "Move action");
    True(move.Command.Destination?.Y < 300, "North destination");
}

static void ParserRejectsUnknownInput() =>
    True(!new RuleBasedCommandInterpreter().Parse("Make it so").Success, "Unknown command should fail safely");

static void SimulationIsDeterministic()
{
    var left = new BattleSimulation(42);
    var right = new BattleSimulation(42);
    for (var tick = 0; tick < 900; tick++)
    {
        left.Update(BattleSimulation.FixedStep);
        right.Update(BattleSimulation.FixedStep);
    }
    for (var index = 0; index < left.Ships.Count; index++)
    {
        var a = left.Ships[index];
        var b = right.Ships[index];
        Near(a.Position.X, b.Position.X, 1e-9, "Deterministic x");
        Near(a.Position.Y, b.Position.Y, 1e-9, "Deterministic y");
        Near(a.Hull, b.Hull, 1e-9, "Deterministic hull");
        Near(a.Shield, b.Shield, 1e-9, "Deterministic shield");
    }
}

static void ManualControlMovesShip()
{
    var simulation = new BattleSimulation(1);
    var flagship = simulation.SelectedShip;
    var startX = flagship.Position.X;
    simulation.SetManualInput(new(true, false, false, false, false));
    for (var tick = 0; tick < 120; tick++) simulation.Update(BattleSimulation.FixedStep);
    True(flagship.Position.X > startX + 20, "Thrust moves the selected ship");
    True(flagship.Velocity.Length <= flagship.Stats.MaxSpeed + 1e-9, "Speed is capped");
}

static void DispatcherAssignsOrders()
{
    var simulation = new BattleSimulation(2);
    var command = new RuleBasedCommandInterpreter().Parse("All ships, attack the enemy flagship").Command!;
    new CommandDispatcher().Dispatch(command, simulation);
    foreach (var ship in simulation.Ships.Where(ship => ship.Team == Team.Player))
    {
        Equal(OrderType.Attack, ship.Order.Type, "Every player ship attacks");
        Equal("enemy-flagship", ship.Order.TargetId, "Target id is validated");
    }
}

static void ShipAbilitiesUseCooldowns()
{
    var simulation = new BattleSimulation(6);
    var first = simulation.TryActivateSelectedAbility();
    var second = simulation.TryActivateSelectedAbility();
    True(first.Contains("command pulse", StringComparison.OrdinalIgnoreCase), "Flagship ability activates");
    True(second.Contains("ready in", StringComparison.OrdinalIgnoreCase), "Ability cannot be spammed");
    True(simulation.SelectedShip.AbilityCooldown > 0, "Ability cooldown is recorded");
}

static void DamageAndVictoryRules()
{
    var simulation = new BattleSimulation(3);
    var flagship = simulation.FindShip("enemy-flagship")!;
    var hull = flagship.Hull;
    flagship.ApplyDamage(50);
    Near(hull, flagship.Hull, 1e-9, "Shield absorbs initial damage");
    flagship.ApplyDamage(10_000);
    simulation.Update(BattleSimulation.FixedStep);
    Equal(BattleStatus.PlayerVictory, simulation.Status, "Destroying enemy flagship wins");
}

static void LongBattleMaintainsInvariants()
{
    var simulation = new BattleSimulation(4);
    var command = new RuleBasedCommandInterpreter().Parse("All ships, attack the enemy flagship").Command!;
    new CommandDispatcher().Dispatch(command, simulation);
    for (var tick = 0; tick < 60 * 150 && simulation.Status == BattleStatus.Active; tick++)
        simulation.Update(BattleSimulation.FixedStep);

    foreach (var ship in simulation.Ships)
    {
        True(ship.Position.IsFinite && ship.Velocity.IsFinite, "Physics remains finite");
        True(ship.Position.X is >= 0 and <= BattleSimulation.WorldWidth, "Ship x remains in world");
        True(ship.Position.Y is >= 0 and <= BattleSimulation.WorldHeight, "Ship y remains in world");
        True(ship.Hull >= 0 && ship.Shield >= 0, "Damage values remain non-negative");
    }
    True(simulation.ElapsedSeconds > 5, "Integration battle advances through meaningful combat");
}

static void MissionCatalogIsValid()
{
    Equal(3, MissionCatalog.All.Count, "Indiegogo demo mission count");
    Equal(3, MissionCatalog.All.Select(mission => mission.Id).Distinct().Count(), "Mission IDs are unique");
    foreach (var mission in MissionCatalog.All)
    {
        True(mission.Ships.Any(ship => ship.Team == Team.Player), $"{mission.Title} has a player fleet");
        True(mission.Ships.Any(ship => ship.Team == Team.Enemy), $"{mission.Title} has enemies");
        var ids = mission.Ships.Select(ship => ship.Id).ToHashSet(StringComparer.Ordinal);
        Equal(mission.Ships.Count, ids.Count, $"{mission.Title} ship IDs are unique");
        foreach (var order in mission.InitialOrders)
        {
            True(ids.Contains(order.ShipId), $"{mission.Title} order subject exists");
            True(order.TargetId is null || ids.Contains(order.TargetId), $"{mission.Title} order target exists");
        }
        True(mission.Objective.TargetId is null || ids.Contains(mission.Objective.TargetId),
            $"{mission.Title} objective target exists");
        True(mission.Objective.ProtectedShipId is null || ids.Contains(mission.Objective.ProtectedShipId),
            $"{mission.Title} protected ship exists");
    }
}

static void MissionObjectivesDetermineStatus()
{
    var first = new BattleSimulation(MissionId.FirstCommand);
    first.FindShip("enemy-raider-leader")!.ApplyDamage(10_000);
    first.Update(BattleSimulation.FixedStep);
    Equal(BattleStatus.PlayerVictory, first.Status, "Destroy-target mission victory");

    var defence = new BattleSimulation(MissionId.BrokenShield);
    foreach (var bomber in defence.Ships.Where(ship => ship.Team == Team.Enemy && ship.Class == ShipClass.Bomber))
        bomber.ApplyDamage(10_000);
    defence.Update(BattleSimulation.FixedStep);
    Equal(BattleStatus.PlayerVictory, defence.Status, "Class-elimination mission victory");
    Equal(0, defence.ObjectiveProgress.Remaining, "No bombers remain");

    var failed = new BattleSimulation(MissionId.BrokenShield);
    failed.FindShip("player-carrier")!.ApplyDamage(10_000);
    failed.Update(BattleSimulation.FixedStep);
    Equal(BattleStatus.EnemyVictory, failed.Status, "Losing protected carrier fails mission");
}

static void EveryMissionSurvivesIntegrationPlay()
{
    var parser = new RuleBasedCommandInterpreter();
    var dispatcher = new CommandDispatcher();
    foreach (var mission in MissionCatalog.All)
    {
        var simulation = new BattleSimulation(mission.Id);
        var command = parser.Parse("All ships, attack the nearest enemy").Command!;
        dispatcher.Dispatch(command, simulation);
        for (var tick = 0; tick < 60 * 30 && simulation.Status == BattleStatus.Active; tick++)
            simulation.Update(BattleSimulation.FixedStep);
        foreach (var ship in simulation.Ships)
        {
            True(ship.Position.IsFinite && ship.Velocity.IsFinite, $"{mission.Title} remains finite");
            True(ship.Hull >= 0 && ship.Shield >= 0, $"{mission.Title} damage remains bounded");
        }
        True(simulation.ElapsedSeconds > 2, $"{mission.Title} advances under integration load");
    }
}

static void CampaignProgressionUnlocksMissions()
{
    var progress = CampaignProgress.New;
    True(progress.IsUnlocked(0), "First mission starts unlocked");
    True(!progress.IsUnlocked(1), "Second mission starts locked");
    progress = progress.Complete(MissionId.FirstCommand);
    True(progress.IsUnlocked(1), "Completing first unlocks second");
    progress = progress.Complete(MissionId.BrokenShield);
    True(progress.IsUnlocked(2), "Completing second unlocks third");
    progress = progress.Complete(MissionId.BlackSun);
    Equal(2, progress.HighestUnlockedMission, "Progress clamps at final mission");
    Equal(3, progress.CompletedMissions.Count, "Every mission can be completed");
}

static void CampaignProgressPersistsSafely()
{
    var directory = Path.Combine(Path.GetTempPath(), $"afc-tests-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "campaign.json");
    try
    {
        var store = new CampaignProgressStore(path);
        Equal(0, store.Load().HighestUnlockedMission, "Missing save starts a new campaign");
        var expected = CampaignProgress.New.Complete(MissionId.FirstCommand);
        store.Save(expected);
        var actual = store.Load();
        Equal(1, actual.HighestUnlockedMission, "Unlocked mission round-trips");
        True(actual.IsCompleted(MissionId.FirstCommand), "Completed mission round-trips");
        File.WriteAllText(path, "{not valid json");
        Equal(0, store.Load().HighestUnlockedMission, "Corrupt save falls back safely");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void TutorialAdvancesInOrder()
{
    var tutorial = new TutorialTracker();
    True(!tutorial.Notify(TutorialAction.IssueOrder), "Cannot skip switch-ship step");
    True(tutorial.Notify(TutorialAction.SwitchShip), "Switch step advances");
    True(tutorial.Notify(TutorialAction.IssueOrder), "Order step advances");
    True(tutorial.Notify(TutorialAction.ActivateAbility), "Ability step advances");
    True(tutorial.IsComplete, "Tutorial completes");
    Equal(3, tutorial.CompletedSteps, "Tutorial reports completed steps");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}

static void Near(double expected, double actual, double tolerance, string message)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{message}: expected {expected} ± {tolerance}, got {actual}");
}
