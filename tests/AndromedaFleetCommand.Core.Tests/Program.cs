using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Configuration;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Multiplayer;
using AndromedaFleetCommand.Core.Replay;
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
    ("Campaign narrative forms a connected eight-act arc", CampaignNarrativeFormsConnectedArc),
    ("Mission complexity escalates across the campaign", MissionComplexityEscalates),
    ("Mission objectives determine victory and defeat", MissionObjectivesDetermineStatus),
    ("Total fleet loss records defeat without crashing", TotalFleetLossRecordsDefeatSafely),
    ("Every campaign mission is winnable through representative play", EveryCampaignMissionIsWinnable),
    ("Campaign progression unlocks missions sequentially", CampaignProgressionUnlocksMissions),
    ("Campaign progress persists and recovers safely", CampaignProgressPersistsSafely),
    ("Campaign pacing telemetry aggregates completed attempts", CampaignPacingTelemetryAggregatesAttempts),
    ("Campaign pacing telemetry persists and exports a complete report", CampaignPacingTelemetryPersistsAndReports),
    ("Tutorial advances only through intended actions", TutorialAdvancesInOrder),
    ("Local AI configuration enforces local endpoints", LocalAiConfigurationEnforcesLocalEndpoints),
    ("Local AI configuration persists safely", LocalAiConfigurationPersistsSafely),
    ("Game settings normalize accessibility values", GameSettingsNormalizeValues),
    ("Game settings persist and recover safely", GameSettingsPersistSafely),
    ("Keyboard bindings cover actions and swap conflicts", InputBindingsCoverActionsAndSwapConflicts),
    ("Keyboard bindings persist and recover safely", InputBindingsPersistSafely),
    ("Gamepad bindings cover actions and swap conflicts", GamepadBindingsCoverActionsAndSwapConflicts),
    ("Gamepad bindings persist and recover safely", GamepadBindingsPersistSafely),
    ("Recorded battles replay to the same checksum", RecordedBattlesReplayDeterministically),
    ("Replay files persist and recover safely", ReplayFilesPersistSafely),
    ("Simulation checksum detects state changes", SimulationChecksumDetectsChanges),
    ("Fleet Duel is balanced for versus play", FleetDuelIsBalanced),
    ("Cooperative lobby partitions allied ships", CooperativeLobbyPartitionsAlliedShips),
    ("Versus lobby assigns opposing fleets", VersusLobbyAssignsOpposingFleets),
    ("Multiplayer wire codec rejects malformed payloads", MultiplayerWireCodecRejectsMalformedPayloads),
    ("Authoritative session rejects unauthorized commands", AuthoritativeSessionRejectsUnauthorizedCommands),
    ("Authoritative session applies owned commands", AuthoritativeSessionAppliesOwnedCommands),
    ("Authoritative session accepts enemy-team captains", AuthoritativeSessionAcceptsEnemyTeamCaptains),
    ("Authoritative session applies manual control and abilities", AuthoritativeSessionAppliesControlAndAbilities),
    ("Authoritative snapshots recover divergent clients", AuthoritativeSnapshotsRecoverDivergentClients),
    ("Disconnected captains release manual control", DisconnectedCaptainsReleaseManualControl),
    ("Authoritative sessions remain deterministic", AuthoritativeSessionsRemainDeterministic)
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
    var dispatcher = new CommandDispatcher();
    var command = new RuleBasedCommandInterpreter().Parse("All ships, attack the enemy flagship").Command!;
    dispatcher.Dispatch(command, simulation);
    foreach (var ship in simulation.Ships.Where(ship => ship.Team == Team.Player))
    {
        Equal(OrderType.Attack, ship.Order.Type, "Every player ship attacks");
        Equal("enemy-flagship", ship.Order.TargetId, "Target id is validated");
    }

    var classSimulation = new BattleSimulation(MissionId.MutinyAtLyra);
    var classCommand = new RuleBasedCommandInterpreter()
        .Parse("All ships, attack the nearest destroyer").Command!;
    dispatcher.Dispatch(classCommand, classSimulation);
    foreach (var ship in classSimulation.Ships.Where(ship => ship.Team == Team.Player))
        Equal(ShipClass.Destroyer, classSimulation.FindShip(ship.Order.TargetId!)!.Class,
            "Ship-class target selectors resolve to the requested class");
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
    Equal(24, MissionCatalog.All.Count, "Full campaign mission count");
    Equal(24, MissionCatalog.All.Select(mission => mission.Id).Distinct().Count(), "Mission IDs are unique");
    Equal(450, MissionCatalog.All.Sum(mission => mission.EstimatedMinutes),
        "Campaign targets seven and a half hours");
    True(MissionCatalog.All.All(mission => mission.EstimatedMinutes is >= 12 and <= 25),
        "Every mission has a credible playtime target");
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

static void CampaignNarrativeFormsConnectedArc()
{
    var missions = MissionCatalog.All;
    Equal(8, missions.Select(mission => mission.Narrative.Chapter).Distinct().Count(),
        "Campaign has eight acts");
    True(missions.GroupBy(mission => mission.Narrative.Chapter).All(act => act.Count() == 3),
        "Every act contains three missions");
    foreach (var mission in missions)
    {
        True(mission.Narrative.Speaker.Contains("SERA VEY", StringComparison.Ordinal),
            $"{mission.Title} retains the campaign's recurring commander");
        Equal(2, mission.Narrative.BriefingLines.Count, $"{mission.Title} has a concise briefing");
        Equal(2, mission.Narrative.VictoryLines.Count, $"{mission.Title} has a concise debrief");
        Equal(2, mission.Narrative.FailureLines.Count, $"{mission.Title} has a concise failure beat");
        True(mission.Narrative.BriefingLines.Concat(mission.Narrative.VictoryLines)
                .Concat(mission.Narrative.FailureLines).All(line => !string.IsNullOrWhiteSpace(line)),
            $"{mission.Title} narrative lines are populated");
    }

    True(missions[1].Narrative.VictoryLines.Any(line =>
            line.Contains("Black Sun", StringComparison.OrdinalIgnoreCase)),
        "Broken Shield reveals the Black Sun operation");
    True(missions[2].Narrative.VictoryLines.Any(line =>
            line.Contains("Crown Fleet", StringComparison.OrdinalIgnoreCase)),
        "Black Sun opens the Crown Fleet campaign");
    True(missions[^1].Narrative.VictoryLines.Any(line =>
            line.Contains("Andromeda Accord", StringComparison.OrdinalIgnoreCase)),
        "The final mission resolves the campaign with the Andromeda Accord");
}

static void MissionComplexityEscalates()
{
    var missions = MissionCatalog.All;
    True(missions.Take(3).Select(mission => mission.Complexity.Rating).SequenceEqual([1, 2, 3]),
        "Opening trilogy teaches command in three steps");
    True(missions.Skip(3).All(mission => mission.Complexity.Rating == 3),
        "The full campaign preserves complete fleet command after onboarding");
    True(missions.All(mission => mission.Complexity.SimultaneousThreatGroups is >= 1 and <= 4),
        "Threat groups remain readable");
    True(missions.Skip(3).All(mission => PlayerClassCount(mission) == 4),
        "Every post-tutorial mission keeps all four player roles");
    True(EnemyCount(missions[^1]) > EnemyCount(missions[0]),
        "The final operation fields a larger opposing force than the tutorial");

    Equal(2, PlayerCount(missions[0]), "Mission one starts with a readable two-ship detachment");
    Equal(4, PlayerCount(missions[1]), "Mission two introduces the full four-role fleet");
    Equal(4, PlayerCount(missions[2]), "Mission three preserves the full fleet for layered command");

    static int PlayerCount(MissionDefinition mission) =>
        mission.Ships.Count(ship => ship.Team == Team.Player);
    static int EnemyCount(MissionDefinition mission) =>
        mission.Ships.Count(ship => ship.Team == Team.Enemy);
    static int PlayerClassCount(MissionDefinition mission) =>
        mission.Ships.Where(ship => ship.Team == Team.Player).Select(ship => ship.Class).Distinct().Count();
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

static void TotalFleetLossRecordsDefeatSafely()
{
    var simulation = new BattleSimulation(MissionId.FirstCommand);
    foreach (var ship in simulation.Ships.Where(ship => ship.Team == Team.Player))
        ship.ApplyDamage(10_000);
    simulation.Update(BattleSimulation.FixedStep);
    Equal(BattleStatus.EnemyVictory, simulation.Status, "Total player fleet loss ends the mission");
    True(simulation.Events.Any(combatEvent =>
            combatEvent.Type == CombatEventType.Order &&
            combatEvent.Message?.Contains("failed", StringComparison.OrdinalIgnoreCase) == true),
        "Defeat event remains available when no player ship survives");
}

static void EveryCampaignMissionIsWinnable()
{
    var parser = new RuleBasedCommandInterpreter();
    var dispatcher = new CommandDispatcher();
    var progress = CampaignProgress.New;
    for (var missionIndex = 0; missionIndex < MissionCatalog.All.Count; missionIndex++)
    {
        var mission = MissionCatalog.All[missionIndex];
        True(progress.IsUnlocked(missionIndex), $"{mission.Title} is unlocked before deployment");
        var simulation = new BattleSimulation(mission.Id);
        var missionWallClock = System.Diagnostics.Stopwatch.StartNew();
        var tutorial = new TutorialTracker();
        var orders = 0;
        var abilities = 0;
        var shipSwitches = 0;

        if (mission.Id == MissionId.FirstCommand)
        {
            simulation.CycleSelectedShip();
            shipSwitches++;
            True(tutorial.Notify(TutorialAction.SwitchShip), "Tutorial records a ship switch");
            simulation.SetManualInput(new(true, false, true, false, true));
            simulation.Update(BattleSimulation.FixedStep);
            True(tutorial.Notify(TutorialAction.ManualControl), "Tutorial records direct helm input");
            DispatchOrder(mission.RecommendedOrder);
            True(tutorial.Notify(TutorialAction.IssueOrder), "Tutorial records the recommended fleet order");
            simulation.TryActivateSelectedAbility();
            abilities++;
            True(tutorial.Notify(TutorialAction.ActivateAbility), "Tutorial records tactical ability use");
            True(tutorial.IsComplete, "Captain's Drill completes before the first battle ends");
        }

        var combatOrders = new[]
        {
            mission.Id == MissionId.FirstCommand
                ? "All ships, attack the raider leader"
                : mission.Objective.Kind == MissionObjectiveKind.DestroyTarget
                    ? "All ships, attack the enemy flagship"
                    : $"All ships, attack the nearest {mission.Objective.TargetClass!.Value.ToString().ToLowerInvariant()}"
        };
        foreach (var order in combatOrders) DispatchOrder(order);
        if (mission.Objective.ProtectedShipId is not null)
        {
            var protectedIndex = simulation.PlayerFleet.ToList()
                .FindIndex(ship => ship.Id == mission.Objective.ProtectedShipId);
            if (protectedIndex >= 0)
            {
                simulation.SelectPlayerShip(protectedIndex);
                shipSwitches++;
            }
        }

        const int maximumTicks = 60 * 240;
        for (var tick = 0; tick < maximumTicks && simulation.Status == BattleStatus.Active; tick++)
        {
            if (mission.Id != MissionId.FirstCommand && tick % (60 * 8) == 0)
            {
                var fleetCount = simulation.PlayerFleet.Count;
                for (var shipIndex = 0; shipIndex < fleetCount; shipIndex++)
                {
                    simulation.SelectPlayerShip(shipIndex);
                    shipSwitches++;
                    if (simulation.SelectedShip.AbilityCooldown > 0) continue;
                    simulation.TryActivateSelectedAbility();
                    if (simulation.SelectedShip.AbilityCooldown > 0) abilities++;
                }
                if (mission.Objective.ProtectedShipId is not null &&
                    simulation.FindShip(mission.Objective.ProtectedShipId)?.IsAlive == true)
                {
                    simulation.SelectShip(mission.Objective.ProtectedShipId);
                    shipSwitches++;
                }
            }
            if (tick > 0 && tick % (60 * 12) == 0)
                foreach (var order in combatOrders) DispatchOrder(order);
            if (simulation.SelectedShip.AbilityCooldown <= 0)
            {
                simulation.TryActivateSelectedAbility();
                if (simulation.SelectedShip.AbilityCooldown > 0) abilities++;
            }

            simulation.SetManualInput(mission.Objective.ProtectedShipId is not null
                ? PlayerInputForProtectedShipEvasion(simulation)
                : PlayerInputForObjective(simulation));
            simulation.Update(BattleSimulation.FixedStep);
        }
        missionWallClock.Stop();

        foreach (var ship in simulation.Ships)
        {
            True(ship.Position.IsFinite && ship.Velocity.IsFinite, $"{mission.Title} remains finite");
            True(ship.Hull >= 0 && ship.Shield >= 0, $"{mission.Title} damage remains bounded");
        }
        var protectedShip = simulation.Mission.Objective.ProtectedShipId is null
            ? null
            : simulation.FindShip(simulation.Mission.Objective.ProtectedShipId);
        Console.WriteLine($"PLAY  mission={mission.Title} outcome={simulation.Status} " +
                          $"simulated={simulation.ElapsedSeconds:F1}s wall={missionWallClock.Elapsed.TotalMilliseconds:F0}ms " +
                          $"objective={simulation.ObjectiveProgress.Label.Replace(' ', '_')} " +
                          $"protected_hull={(protectedShip?.HullRatio * 100 ?? 100):F0}% " +
                          $"orders={orders} switches={shipSwitches} abilities={abilities} " +
                          $"player_survivors={simulation.PlayerFleet.Count}");
        Equal(BattleStatus.PlayerVictory, simulation.Status,
            $"{mission.Title} can be won with normal controls and parsed orders");
        True(simulation.ElapsedSeconds is > 2 and <= 240,
            $"{mission.Title} completes within the four-minute QA limit");
        True(simulation.Mission.Objective.ProtectedShipId is null ||
             simulation.FindShip(simulation.Mission.Objective.ProtectedShipId)?.IsAlive == true,
            $"{mission.Title} protected ship survives");
        progress = progress.Complete(mission.Id);

        void DispatchOrder(string text)
        {
            var parsed = parser.Parse(text);
            True(parsed.Success && parsed.Command is not null, $"Order parses: {text}");
            var acknowledgement = dispatcher.Dispatch(parsed.Command!, simulation);
            True(!acknowledgement.StartsWith("No ", StringComparison.Ordinal),
                $"Order dispatches: {text}");
            orders++;
        }
    }

    Equal(MissionCatalog.All.Count, progress.CompletedMissions.Count,
        "Representative play completes the campaign");
}

static ManualInput PlayerInputForObjective(BattleSimulation simulation)
{
    var selected = simulation.SelectedShip;
    var objective = simulation.Mission.Objective;
    var target = objective.Kind == MissionObjectiveKind.DestroyTarget
        ? simulation.FindShip(objective.TargetId!)
        : simulation.Ships
            .Where(ship => ship.IsAlive && ship.Team == Team.Enemy && ship.Class == objective.TargetClass)
            .MinBy(ship => ship.Position.DistanceTo(selected.Position));
    target ??= simulation.Ships
        .Where(ship => ship.IsAlive && ship.Team == Team.Enemy)
        .MinBy(ship => ship.Position.DistanceTo(selected.Position));
    if (target is null) return ManualInput.None;

    var difference = target.Position - selected.Position;
    var angleDifference = difference.Angle - selected.Angle;
    while (angleDifference > Math.PI) angleDifference -= Math.PI * 2;
    while (angleDifference < -Math.PI) angleDifference += Math.PI * 2;
    var desiredRange = selected.Stats.WeaponRange * 0.65;
    return new(
        Thrust: difference.Length > desiredRange,
        Reverse: difference.Length < desiredRange * 0.35,
        TurnLeft: angleDifference < -0.04,
        TurnRight: angleDifference > 0.04,
        Fire: Math.Abs(angleDifference) < 0.55);
}

static ManualInput PlayerInputForProtectedShipEvasion(BattleSimulation simulation)
{
    var carrier = simulation.SelectedShip;
    var destination = new Vector2D(90,
        (int)(simulation.ElapsedSeconds / 7) % 2 == 0 ? 760 : 140);
    var difference = destination - carrier.Position;
    var angleDifference = difference.Angle - carrier.Angle;
    while (angleDifference > Math.PI) angleDifference -= Math.PI * 2;
    while (angleDifference < -Math.PI) angleDifference += Math.PI * 2;
    return new(
        Thrust: difference.Length > 55,
        Reverse: false,
        TurnLeft: angleDifference < -0.04,
        TurnRight: angleDifference > 0.04,
        Fire: true);
}

static void CampaignProgressionUnlocksMissions()
{
    var progress = CampaignProgress.New;
    for (var index = 0; index < MissionCatalog.All.Count; index++)
    {
        True(progress.IsUnlocked(index), $"Mission {index + 1} is unlocked in sequence");
        if (index + 1 < MissionCatalog.All.Count)
            True(!progress.IsUnlocked(index + 1), $"Mission {index + 2} remains locked");
        progress = progress.Complete(MissionCatalog.All[index].Id);
    }
    Equal(MissionCatalog.All.Count - 1, progress.HighestUnlockedMission,
        "Progress clamps at final mission");
    Equal(MissionCatalog.All.Count, progress.CompletedMissions.Count,
        "Every mission can be completed");
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

static void CampaignPacingTelemetryAggregatesAttempts()
{
    var telemetry = CampaignPacingTelemetry.Empty
        .Record(MissionId.FirstCommand, BattleStatus.EnemyVictory, 1_050)
        .Record(MissionId.FirstCommand, BattleStatus.PlayerVictory, 930)
        .Record(MissionId.FirstCommand, BattleStatus.PlayerVictory, 840)
        .Record(MissionId.FleetDuel, BattleStatus.PlayerVictory, 120)
        .Record(MissionId.BrokenShield, BattleStatus.Active, 45)
        .Record(MissionId.BrokenShield, BattleStatus.PlayerVictory, double.NaN);

    var stats = telemetry.StatsFor(MissionId.FirstCommand);
    Equal(3, stats.Attempts, "Terminal campaign attempts are counted");
    Equal(2, stats.Victories, "Victories are counted");
    Equal(1, stats.Defeats, "Defeats are counted");
    Near(900, stats.TargetSeconds, 1e-9, "Mission target comes from the catalog");
    Near(840, stats.LatestVictorySeconds ?? -1, 1e-9, "Latest victory is exposed");
    Near(840, stats.FastestVictorySeconds ?? -1, 1e-9, "Fastest victory is exposed");
    Near(-60, stats.LatestVarianceSeconds ?? 0, 1e-9, "Variance compares measured and target time");
    Equal(1, telemetry.MeasuredMissionCount, "Only missions with victories count as measured");
    Equal(3, telemetry.Attempts.Count, "Non-campaign, active, and non-finite records are ignored");

    for (var attempt = 0; attempt < CampaignPacingTelemetry.MaximumAttemptsPerMission + 5; attempt++)
        telemetry = telemetry.Record(MissionId.BrokenShield, BattleStatus.EnemyVictory, attempt);
    Equal(CampaignPacingTelemetry.MaximumAttemptsPerMission,
        telemetry.StatsFor(MissionId.BrokenShield).Attempts,
        "Per-mission history remains bounded");
}

static void CampaignPacingTelemetryPersistsAndReports()
{
    var directory = Path.Combine(Path.GetTempPath(), $"afc-pacing-tests-{Guid.NewGuid():N}");
    var telemetryPath = Path.Combine(directory, "campaign-pacing.json");
    var reportPath = Path.Combine(directory, "campaign-pacing-report.md");
    try
    {
        var expected = CampaignPacingTelemetry.Empty;
        foreach (var mission in MissionCatalog.All)
            expected = expected.Record(mission.Id, BattleStatus.PlayerVictory, mission.EstimatedMinutes * 60);

        var store = new CampaignPacingTelemetryStore(telemetryPath);
        store.Save(expected);
        var actual = store.Load();
        Equal(MissionCatalog.All.Count, actual.MeasuredMissionCount,
            "Every successful mission round-trips");
        True(actual.HasCompleteCampaignMeasurement, "A complete pacing pass is recognized");
        Near(27_000, actual.MeasuredVictorySeconds, 1e-9,
            "Measured campaign total uses latest victories");

        CampaignPacingReport.Save(reportPath, actual);
        var report = File.ReadAllText(reportPath);
        True(report.Contains("24/24 missions", StringComparison.Ordinal),
            "Report states measurement coverage");
        True(report.Contains("Latest successful playthrough total: **7:30:00** (+00:00)",
                StringComparison.Ordinal),
            "Report states measured duration and variance");
        True(report.Contains("| 24 | One Command |", StringComparison.Ordinal),
            "Report includes the final mission");

        File.WriteAllText(telemetryPath, "{not valid json");
        Equal(0, store.Load().Attempts.Count, "Corrupt telemetry falls back safely");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void TutorialAdvancesInOrder()
{
    var tutorial = new TutorialTracker();
    Equal(4, TutorialTracker.Steps.Count, "Tutorial stays concise");
    True(TutorialTracker.Steps.All(step => !string.IsNullOrWhiteSpace(step.Title) &&
            !string.IsNullOrWhiteSpace(step.KeyboardPrompt) &&
            !string.IsNullOrWhiteSpace(step.ControllerPrompt) &&
            !string.IsNullOrWhiteSpace(step.Purpose)),
        "Every tutorial beat explains action and purpose for both input modes");
    True(!tutorial.Notify(TutorialAction.IssueOrder), "Cannot skip switch-ship step");
    True(tutorial.Notify(TutorialAction.SwitchShip), "Switch step advances");
    Near(0.25, tutorial.Progress, 1e-9, "Tutorial exposes progress");
    True(!tutorial.Notify(TutorialAction.IssueOrder), "Cannot skip manual-control step");
    True(tutorial.Notify(TutorialAction.ManualControl), "Manual-control step advances");
    True(tutorial.Notify(TutorialAction.IssueOrder), "Order step advances");
    True(tutorial.Notify(TutorialAction.ActivateAbility), "Ability step advances");
    True(tutorial.IsComplete, "Tutorial completes");
    Equal(4, tutorial.CompletedSteps, "Tutorial reports completed steps");
    True(tutorial.GetPrompt(true).Contains("CAPTAIN CERTIFIED", StringComparison.Ordinal),
        "Completion message is celebratory");
}

static void LocalAiConfigurationEnforcesLocalEndpoints()
{
    var remote = LocalAiConfiguration.Default with
    {
        OllamaEnabled = true,
        OllamaUrl = "https://example.com/hosted-model",
        OllamaModel = "  "
    };
    var normalized = remote.Normalize();
    Equal(LocalAiConfiguration.Default.OllamaUrl, normalized.OllamaUrl,
        "Hosted endpoints are replaced with loopback");
    Equal(LocalAiConfiguration.Default.OllamaModel, normalized.OllamaModel,
        "Blank models use the local default");
    True(normalized.OllamaEnabled, "Endpoint normalization does not silently change the user's toggle");
    True(normalized.PreferGpu == true, "GPU acceleration is preferred by default");
    Equal(LocalAiConfiguration.MaximumGpuLayers, normalized.OllamaGpuLayers,
        "GPU mode requests full model-layer offload");
    Equal(0, (normalized with { PreferGpu = false }).OllamaGpuLayers,
        "CPU-only mode disables GPU offload explicitly");
}

static void LocalAiConfigurationPersistsSafely()
{
    var directory = Path.Combine(Path.GetTempPath(), $"afc-ai-tests-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "local-ai.json");
    try
    {
        var store = new LocalAiConfigurationStore(path);
        var expected = LocalAiConfiguration.Default with
        {
            OllamaEnabled = true,
            OllamaModel = "qwen3:4b",
            WhisperCli = Path.Combine(directory, "whisper-cli"),
            WhisperModel = Path.Combine(directory, "ggml-base.en.bin")
        };
        store.Save(expected);
        Equal(expected.Normalize(), store.Load(), "Local AI configuration round-trips");
        File.WriteAllText(path, """
            {
              "OllamaEnabled": true,
              "OllamaUrl": "http://127.0.0.1:11434/",
              "OllamaModel": "qwen3:4b",
              "WhisperCli": null,
              "WhisperModel": null
            }
            """);
        True(store.Load().PreferGpu == true, "Older settings upgrade to GPU-preferred mode");
        File.WriteAllText(path, "not json");
        Equal(LocalAiConfiguration.Default, store.Load(), "Corrupt local AI settings recover safely");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void GameSettingsNormalizeValues()
{
    var settings = new GameSettings(4, (ColorVisionMode)999, false, true, -2).Normalize();
    Near(1, settings.MasterVolume, 1e-9, "Volume clamps high");
    Equal(ColorVisionMode.Standard, settings.ColorMode, "Unknown palette falls back");
    Near(0.08, settings.GamepadDeadzone, 1e-9, "Deadzone clamps low");
    True(settings.ReduceFlashes, "Accessibility toggles are preserved");
}

static void GameSettingsPersistSafely()
{
    var directory = Path.Combine(Path.GetTempPath(), $"afc-settings-tests-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "settings.json");
    try
    {
        var store = new GameSettingsStore(path);
        var expected = new GameSettings(0.5, ColorVisionMode.Deuteranopia, true, true, 0.32);
        store.Save(expected);
        Equal(expected, store.Load(), "Settings round-trip");
        File.WriteAllText(path, "[");
        Equal(GameSettings.Default, store.Load(), "Corrupt settings recover safely");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void InputBindingsCoverActionsAndSwapConflicts()
{
    var defaults = InputBindings.Default;
    Equal(GameActions.All.Count, defaults.Keys.Count, "Every action has a default binding");
    Equal(GameActions.All.Count, GameActions.All.Select(action => action.Id).Distinct().Count(),
        "Action identifiers are unique");
    Equal(GameActions.All.Count, defaults.Keys.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        "Default bindings do not conflict");

    var rebound = defaults.Rebind(GameActionIds.Thrust, "S");
    Equal("S", rebound.Get(GameActionIds.Thrust), "Requested key is assigned");
    Equal("W", rebound.Get(GameActionIds.Reverse), "Conflicting action receives the previous key");
    var restored = rebound.Reset(GameActionIds.Thrust);
    Equal("W", restored.Get(GameActionIds.Thrust), "Single action restores its default");
    Equal("S", restored.Get(GameActionIds.Reverse), "Reset preserves conflict-free bindings");
}

static void InputBindingsPersistSafely()
{
    var directory = Path.Combine(Path.GetTempPath(), $"afc-bindings-tests-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "input-bindings.json");
    try
    {
        var store = new InputBindingsStore(path);
        var expected = InputBindings.Default.Rebind(GameActionIds.Fire, "F");
        store.Save(expected);
        var loaded = store.Load();
        foreach (var action in GameActions.All)
            Equal(expected.Get(action.Id), loaded.Get(action.Id), $"{action.Label} round-trips");

        File.WriteAllText(path, "{ \"Keys\": { \"fire\": \"E\", \"unknown\": \"Z\" } }");
        var upgraded = store.Load();
        Equal("E", upgraded.Get(GameActionIds.Fire), "Known bindings survive partial older files");
        Equal(GameActions.All.Count, upgraded.Keys.Count, "Missing defaults are restored and unknown actions removed");

        File.WriteAllText(path, "not json");
        var recovered = store.Load();
        Equal(InputBindings.Default.Get(GameActionIds.Fire), recovered.Get(GameActionIds.Fire),
            "Corrupt bindings recover to defaults");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void GamepadBindingsCoverActionsAndSwapConflicts()
{
    var defaults = GamepadBindings.Default;
    Equal(GamepadActions.All.Count, defaults.Buttons.Count, "Every controller action has a default binding");
    Equal(GamepadActions.All.Count, defaults.Buttons.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        "Default controller bindings do not conflict");

    var rebound = defaults.Rebind(GamepadActionIds.Fire, "B");
    Equal("B", rebound.Get(GamepadActionIds.Fire), "Requested controller button is assigned");
    Equal("A", rebound.Get(GamepadActionIds.Ability), "Conflicting action receives the previous button");
    var restored = rebound.Reset(GamepadActionIds.Fire);
    Equal("A", restored.Get(GamepadActionIds.Fire), "Controller action restores its default");
    Equal("B", restored.Get(GamepadActionIds.Ability), "Controller reset remains conflict-free");
}

static void GamepadBindingsPersistSafely()
{
    var directory = Path.Combine(Path.GetTempPath(), $"afc-gamepad-tests-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "gamepad-bindings.json");
    try
    {
        var store = new GamepadBindingsStore(path);
        var expected = GamepadBindings.Default.Rebind(GamepadActionIds.Pause, "LeftStick");
        store.Save(expected);
        var loaded = store.Load();
        foreach (var action in GamepadActions.All)
            Equal(expected.Get(action.Id), loaded.Get(action.Id), $"{action.Label} round-trips");

        File.WriteAllText(path, "broken");
        var recovered = store.Load();
        Equal(GamepadBindings.Default.Get(GamepadActionIds.Pause), recovered.Get(GamepadActionIds.Pause),
            "Corrupt controller bindings recover to defaults");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void RecordedBattlesReplayDeterministically()
{
    var simulation = new BattleSimulation(MissionId.BlackSun, 77);
    var recorder = new ReplayRecorder(MissionId.BlackSun, 77);
    var dispatcher = new CommandDispatcher();
    var command = new FleetCommand("all", OrderType.Attack, "enemy flagship");
    recorder.RecordCommand(0, command);
    dispatcher.Dispatch(command, simulation);
    var finalTick = 0;
    for (var tick = 0; tick < 900 && simulation.Status == BattleStatus.Active; tick++)
    {
        var input = tick < 40 ? new ManualInput(true, false, false, false, tick % 8 == 0) : ManualInput.None;
        recorder.RecordInput(tick, input);
        if (tick == 60)
        {
            recorder.RecordShipSelection(tick, 1);
            simulation.SelectPlayerShip(1);
        }
        if (tick == 61)
        {
            recorder.RecordAbility(tick);
            simulation.TryActivateSelectedAbility();
        }
        simulation.SetManualInput(input);
        simulation.Update(BattleSimulation.FixedStep);
        finalTick++;
    }
    var replay = recorder.Complete(finalTick, simulation);
    True(ReplayRunner.Validate(replay), "Replay checksum matches recorded battle");
}

static void ReplayFilesPersistSafely()
{
    var directory = Path.Combine(Path.GetTempPath(), $"afc-replay-tests-{Guid.NewGuid():N}");
    try
    {
        var simulation = new BattleSimulation(MissionId.FirstCommand, 91);
        simulation.Update(BattleSimulation.FixedStep);
        var replay = new ReplayRecorder(MissionId.FirstCommand, 91).Complete(1, simulation);
        var store = new BattleReplayStore(directory);
        store.Save(replay);
        var loaded = store.LoadLatest();
        True(loaded is not null, "Replay loads");
        Equal(replay.MissionId, loaded!.MissionId, "Replay mission round-trips");
        Equal(replay.Seed, loaded.Seed, "Replay seed round-trips");
        Equal(replay.FinalTick, loaded.FinalTick, "Replay tick count round-trips");
        Equal(replay.ExpectedChecksum, loaded.ExpectedChecksum, "Replay checksum round-trips");
        True(replay.Events.SequenceEqual(loaded.Events), "Replay events round-trip");
        File.WriteAllText(Directory.GetFiles(directory).Single(), "broken");
        True(store.LoadLatest() is null, "Corrupt replay is rejected safely");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void SimulationChecksumDetectsChanges()
{
    var simulation = new BattleSimulation(MissionId.FirstCommand);
    var before = SimulationChecksum.Compute(simulation);
    simulation.SetManualInput(new(true, false, false, false, false));
    simulation.Update(BattleSimulation.FixedStep);
    var after = SimulationChecksum.Compute(simulation);
    True(!before.Equals(after, StringComparison.Ordinal), "Checksum changes with simulation state");
}

static void FleetDuelIsBalanced()
{
    var mission = MissionCatalog.FleetDuel;
    Equal(3, (int)MissionId.FleetDuel,
        "Fleet Duel preserves its original replay and multiplayer wire value");
    var player = mission.Ships.Where(ship => ship.Team == Team.Player).ToArray();
    var enemy = mission.Ships.Where(ship => ship.Team == Team.Enemy).ToArray();
    Equal(4, player.Length, "Fleet Duel has four Andromeda ships");
    Equal(4, enemy.Length, "Fleet Duel has four Ketzal ships");
    True(player.Select(ship => ship.Class).Order().SequenceEqual(enemy.Select(ship => ship.Class).Order()),
        "Fleet Duel mirrors ship classes");
    Equal(MissionId.FleetDuel, MissionCatalog.Get(MissionId.FleetDuel).Id,
        "Fleet Duel resolves outside the campaign catalog");
}

static void CooperativeLobbyPartitionsAlliedShips()
{
    var lobby = new FleetLobby("1", "Host", MultiplayerMode.Cooperative);
    True(lobby.TryAddPlayer("2", "Wing Captain").Accepted, "Second co-op captain joins");
    True(lobby.SetCooperativeMission(MissionId.BlackSun).Accepted, "Host chooses a campaign mission");
    var snapshot = lobby.Snapshot();
    True(snapshot.Players.All(player => player.Team == Team.Player), "Co-op captains share one team");
    var assigned = snapshot.Players.SelectMany(player => player.ShipIds).ToArray();
    var expected = MissionCatalog.Get(MissionId.BlackSun).Ships
        .Where(ship => ship.Team == Team.Player).Select(ship => ship.Id).ToArray();
    Equal(expected.Length, assigned.Length, "Every allied ship is assigned");
    Equal(expected.Length, assigned.Distinct(StringComparer.Ordinal).Count(), "Allied ownership is unique");
    True(expected.All(assigned.Contains), "Every mission ally belongs to a captain");
    var (result, session) = lobby.StartMatch();
    True(result.Accepted && session is not null, "Co-op lobby starts");
}

static void VersusLobbyAssignsOpposingFleets()
{
    var lobby = new FleetLobby("1", "Blue Captain", MultiplayerMode.Versus);
    True(!lobby.IsStartable, "Versus waits for an opponent");
    True(lobby.TryAddPlayer("2", "Red Captain").Accepted, "Opponent joins");
    True(lobby.IsStartable, "Versus starts with both teams represented");
    var snapshot = lobby.Snapshot();
    Equal(MissionId.FleetDuel, snapshot.MissionId, "Versus uses the balanced duel");
    Equal(Team.Player, snapshot.Players[0].Team, "Host leads Andromeda");
    Equal(Team.Enemy, snapshot.Players[1].Team, "Opponent leads Ketzal");
    Equal(4, snapshot.Players[0].ShipIds.Count, "Host receives a full fleet");
    Equal(4, snapshot.Players[1].ShipIds.Count, "Opponent receives a full fleet");
    var (result, session) = lobby.StartMatch();
    True(result.Accepted && session is not null, "Versus lobby starts");
    Equal(Team.Enemy, session!.AssignedTeam("2")!.Value, "Server records enemy-team ownership");
}

static void MultiplayerWireCodecRejectsMalformedPayloads()
{
    var lobby = new FleetLobby("1", "Host", MultiplayerMode.Cooperative).Snapshot();
    var payload = MultiplayerWire.Serialize(lobby);
    True(MultiplayerWire.TryDeserialize<FleetLobbySnapshot>(payload, out var decoded) && decoded is not null,
        "Lobby payload round-trips");
    Equal(lobby.MissionId, decoded!.MissionId, "Mission survives wire encoding");
    Equal(lobby.Players[0].DisplayName, decoded.Players[0].DisplayName, "Player survives wire encoding");
    True(!MultiplayerWire.TryDeserialize<FleetLobbySnapshot>("{broken", out _),
        "Malformed JSON is rejected");
    True(!MultiplayerWire.TryDeserialize<FleetLobbySnapshot>(
            new string('x', MultiplayerWire.MaximumPayloadCharacters + 1), out _),
        "Oversized payload is rejected");

    var session = new AuthoritativeFleetSession(MissionId.FleetDuel, 441);
    var authoritative = session.Snapshot();
    var snapshotPayload = MultiplayerWire.Serialize(authoritative);
    True(!snapshotPayload.Contains("Normalized", StringComparison.Ordinal),
        "Computed vector properties stay off the wire");
    True(MultiplayerWire.TryDeserialize<AuthoritativeSnapshot>(snapshotPayload, out var decodedSnapshot) &&
         decodedSnapshot is not null, "A complete authoritative snapshot round-trips through JSON");
    var recovered = new BattleSimulation(MissionId.FleetDuel, 441);
    recovered.ApplyFrame(decodedSnapshot!.Frame);
    Equal(authoritative.Checksum, SimulationChecksum.Compute(recovered),
        "The serialized snapshot recovers the authoritative checksum");
}

static void AuthoritativeSessionRejectsUnauthorizedCommands()
{
    var session = new AuthoritativeFleetSession(MissionId.BlackSun, 44);
    session.AssignPlayer("captain", "player-frigate");
    var enemy = session.Submit(new(1, 0, "captain", "enemy-flagship", OrderType.Attack, "player flagship"));
    True(!enemy.Accepted, "Players cannot command enemy ships");
    var unowned = session.Submit(new(2, 0, "captain", "player-carrier", OrderType.Attack, "enemy flagship"));
    True(!unowned.Accepted, "Players cannot command another player's ship");
    var unknown = session.Submit(new(3, 0, "intruder", "player-frigate", OrderType.Attack, "enemy flagship"));
    True(!unknown.Accepted, "Unknown players are rejected");
}

static void AuthoritativeSessionAppliesOwnedCommands()
{
    var session = new AuthoritativeFleetSession(MissionId.BlackSun, 45);
    session.AssignPlayer("captain", "player-frigate");
    var command = new NetworkFleetCommand(1, 0, "captain", "player-frigate",
        OrderType.Attack, "enemy flagship");
    True(session.Submit(command).Accepted, "Owned command is admitted");
    True(!session.Submit(command).Accepted, "Duplicate sequence is rejected");
    var snapshot = session.Step();
    Equal(1, snapshot.ServerTick, "Server tick advances");
    Equal(OrderType.Attack, session.Simulation.FindShip("player-frigate")!.Order.Type,
        "Accepted command reaches the simulation");
    Equal("enemy-flagship", session.Simulation.FindShip("player-frigate")!.Order.TargetId,
        "Server resolves the target");
}

static void AuthoritativeSessionAcceptsEnemyTeamCaptains()
{
    var session = new AuthoritativeFleetSession(MissionId.FleetDuel, 47);
    session.AssignPlayer("ketzal", "enemy-frigate");
    var command = new NetworkFleetCommand(1, 0, "ketzal", "enemy-frigate",
        OrderType.Attack, "flagship");
    True(session.Submit(command).Accepted, "Enemy-team order is admitted");
    session.Step();
    Equal("player-flagship", session.Simulation.FindShip("enemy-frigate")!.Order.TargetId,
        "Enemy-team target resolution points at Andromeda");
}

static void AuthoritativeSessionAppliesControlAndAbilities()
{
    var session = new AuthoritativeFleetSession(MissionId.FleetDuel, 48);
    session.AssignPlayer("captain", "player-frigate");
    var ship = session.Simulation.FindShip("player-frigate")!;
    var start = ship.Position;
    True(session.SubmitControl(new(1, 0, "captain", ship.Id,
        new(true, false, false, false, false))).Accepted, "Manual input is admitted");
    for (var tick = 0; tick < 20; tick++) session.Step();
    True(ship.Position.DistanceTo(start) > 0.1, "Manual thrust moves the owned ship");
    True(session.SubmitControl(new(2, session.ServerTick, "captain", ship.Id,
        ManualInput.None, ActivateAbility: true)).Accepted, "Ability input is admitted");
    session.Step();
    True(ship.AbilityCooldown > 0, "Owned ship ability activates on the server");
    True(!session.Submit(new(2, session.ServerTick, "captain", ship.Id, OrderType.Hold)).Accepted,
        "Control and order packets share duplicate protection");
}

static void AuthoritativeSnapshotsRecoverDivergentClients()
{
    var session = new AuthoritativeFleetSession(MissionId.FleetDuel, 49);
    session.AssignPlayer("captain", "player-destroyer");
    session.SubmitControl(new(1, 0, "captain", "player-destroyer",
        new(true, false, false, true, true)));
    AuthoritativeSnapshot snapshot = session.Snapshot();
    for (var tick = 0; tick < 25; tick++) snapshot = session.Step();

    var client = new BattleSimulation(MissionId.FirstCommand, 999);
    client.Update(BattleSimulation.FixedStep);
    client.ApplyFrame(snapshot.Frame);
    Equal(snapshot.Checksum, SimulationChecksum.Compute(client), "Snapshot restores the authoritative checksum");
    Equal(snapshot.Status, client.Status, "Snapshot restores battle status");
    Equal(snapshot.Frame.Projectiles.Count, client.Projectiles.Count, "Snapshot restores projectiles");
}

static void DisconnectedCaptainsReleaseManualControl()
{
    var session = new AuthoritativeFleetSession(MissionId.FleetDuel, 50);
    session.AssignPlayer("captain", "player-frigate");
    session.SubmitControl(new(1, 0, "captain", "player-frigate",
        new(true, false, false, false, false)));
    session.Step();
    True(session.Simulation.FindShip("player-frigate")!.IsManuallyControlled,
        "Connected captain has the helm");
    True(session.UnassignPlayer("captain"), "Captain is removed");
    session.Step();
    True(!session.Simulation.FindShip("player-frigate")!.IsManuallyControlled,
        "Disconnected helm returns to the deterministic pilot");
}

static void AuthoritativeSessionsRemainDeterministic()
{
    var left = new AuthoritativeFleetSession(MissionId.BrokenShield, 46);
    var right = new AuthoritativeFleetSession(MissionId.BrokenShield, 46);
    foreach (var session in new[] { left, right })
    {
        session.AssignPlayer("captain", "player-frigate");
        session.Submit(new(1, 0, "captain", "player-frigate", OrderType.Intercept, "nearest bomber"));
    }
    for (var tick = 0; tick < 600; tick++)
    {
        var a = left.Step();
        var b = right.Step();
        Equal(a.Checksum, b.Checksum, "Authoritative checksum remains deterministic");
    }
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
