using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Simulation;

public sealed class BattleSimulation
{
    public const double WorldWidth = 1600;
    public const double WorldHeight = 900;
    public const double FixedStep = 1.0 / 60.0;

    private readonly List<Ship> _ships = [];
    private readonly List<Projectile> _projectiles = [];
    private readonly List<CombatEvent> _events = [];
    private string _selectedShipId = "player-flagship";
    private ManualInput _manualInput;

    public BattleSimulation(long seed = 0xAFC2026)
    {
        Seed = seed;
        Reset();
    }

    public long Seed { get; }
    public IReadOnlyList<Ship> Ships => _ships;
    public IReadOnlyList<Projectile> Projectiles => _projectiles;
    public IReadOnlyList<CombatEvent> Events => _events;
    public BattleStatus Status { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public Ship SelectedShip =>
        FindShip(_selectedShipId) is { IsAlive: true } selected
            ? selected
            : _ships.First(ship => ship.Team == Team.Player && ship.IsAlive);

    public void Reset()
    {
        _ships.Clear();
        _projectiles.Clear();
        _events.Clear();
        ElapsedSeconds = 0;
        Status = BattleStatus.Active;
        _manualInput = ManualInput.None;

        AddPlayer("player-flagship", "Flagship", ShipClass.Flagship, 260, 450, 0);
        AddPlayer("player-carrier", "Carrier One", ShipClass.Carrier, 205, 265, 0.08);
        AddPlayer("player-frigate", "Frigate Two", ShipClass.Frigate, 330, 335, -0.04);
        AddPlayer("player-destroyer", "Destroyer Three", ShipClass.Destroyer, 260, 620, 0.03);

        AddEnemy("enemy-flagship", "Enemy Flagship", ShipClass.Flagship, 1340, 450, Math.PI);
        AddEnemy("enemy-carrier", "Enemy Carrier", ShipClass.Carrier, 1390, 235, Math.PI);
        AddEnemy("enemy-destroyer-1", "Enemy Destroyer One", ShipClass.Destroyer, 1250, 300, Math.PI);
        AddEnemy("enemy-destroyer-2", "Enemy Destroyer Two", ShipClass.Destroyer, 1270, 650, Math.PI);
        AddEnemy("enemy-bomber-1", "Bomber One", ShipClass.Bomber, 1170, 220, Math.PI);
        AddEnemy("enemy-bomber-2", "Bomber Two", ShipClass.Bomber, 1210, 185, Math.PI);
        AddEnemy("enemy-bomber-3", "Bomber Three", ShipClass.Bomber, 1205, 730, Math.PI);
        AddEnemy("enemy-escort-1", "Enemy Escort One", ShipClass.Escort, 1300, 540, Math.PI);
        AddEnemy("enemy-escort-2", "Enemy Escort Two", ShipClass.Escort, 1320, 580, Math.PI);

        _selectedShipId = "player-flagship";
        SelectedShip.IsManuallyControlled = true;
        FindShip("player-carrier")!.Order = new(OrderType.Defend, "player-flagship");
        FindShip("player-frigate")!.Order = new(OrderType.Intercept, "enemy-bomber-1");
        FindShip("player-destroyer")!.Order = new(OrderType.Attack, "enemy-flagship");
        foreach (var enemy in _ships.Where(ship => ship.Team == Team.Enemy))
        {
            enemy.Order = new(OrderType.Attack,
                enemy.Class == ShipClass.Bomber ? "player-carrier" : "player-flagship");
        }
        AddOrderEvent("Battle joined. Command the fleet.");
    }

    public void Update(double delta)
    {
        if (Status != BattleStatus.Active || delta <= 0) return;
        var dt = Math.Min(delta, 0.05);
        ElapsedSeconds += dt;
        UpdateEvents(dt);

        foreach (var ship in _ships)
        {
            if (!ship.IsAlive) continue;
            ship.WeaponCooldown = Math.Max(0, ship.WeaponCooldown - dt);
            ship.AbilityCooldown = Math.Max(0, ship.AbilityCooldown - dt);
            ship.OverdriveRemaining = Math.Max(0, ship.OverdriveRemaining - dt);
            ship.RecoverEnergy(7 * dt);
            ship.RegenerateShield(1.8 * dt);
            if (ship.Id == _selectedShipId) ApplyManualControl(ship, dt);
            else UpdatePilot(ship, dt);
            SeparateShips(ship, dt);
            Integrate(ship, dt);
        }
        UpdateProjectiles(dt);
        UpdateBattleStatus();
    }

    public void SetManualInput(ManualInput input) => _manualInput = input;

    public string TryActivateSelectedAbility()
    {
        var ship = SelectedShip;
        if (ship.AbilityCooldown > 0) return $"{ship.Name} ability ready in {Math.Ceiling(ship.AbilityCooldown)}s";
        string message;
        switch (ship.Class)
        {
            case ShipClass.Flagship:
                foreach (var ally in _ships.Where(candidate =>
                             candidate.IsAlive && candidate.Team == Team.Player &&
                             candidate.Position.DistanceTo(ship.Position) < 310))
                {
                    ally.RegenerateShield(55);
                    ally.RecoverEnergy(24);
                }
                ship.AbilityCooldown = 16;
                message = "Flagship: command pulse restored the formation";
                break;
            case ShipClass.Carrier:
                LaunchDroneVolley(ship);
                ship.AbilityCooldown = 13;
                message = "Carrier One: drone strike launched";
                break;
            case ShipClass.Frigate:
                ship.ActivateOverdrive(4);
                ship.RecoverEnergy(35);
                ship.AbilityCooldown = 12;
                message = "Frigate Two: overdrive engaged";
                break;
            case ShipClass.Destroyer:
                var target = NearestEnemy(ship, 680);
                if (target is null) return "Destroyer Three: no railgun target in range";
                target.ApplyDamage(105);
                _events.Add(new(CombatEventType.Impact, target.Position, "Railgun impact", 0.5));
                ship.AbilityCooldown = 15;
                message = $"Destroyer Three: railgun hit {target.Name}";
                break;
            default:
                return $"{ship.Name}: no tactical ability";
        }
        AddOrderEvent(message);
        return message;
    }

    public Ship? FindShip(string id) =>
        _ships.FirstOrDefault(ship => ship.Id.Equals(id, StringComparison.Ordinal));

    public IReadOnlyList<Ship> PlayerFleet =>
        _ships.Where(ship => ship.IsAlive && ship.Team == Team.Player).ToList();

    public void SelectPlayerShip(int index)
    {
        var fleet = PlayerFleet;
        if (fleet.Count == 0) return;
        SelectedShip.IsManuallyControlled = false;
        _selectedShipId = fleet[PositiveModulo(index, fleet.Count)].Id;
        SelectedShip.IsManuallyControlled = true;
        AddOrderEvent($"Manual control: {SelectedShip.Name}");
    }

    public void CycleSelectedShip()
    {
        var fleet = PlayerFleet;
        var current = fleet.Select((ship, index) => (ship, index))
            .FirstOrDefault(entry => entry.ship.Id == _selectedShipId).index;
        SelectPlayerShip(current + 1);
    }

    public void AddOrderEvent(string message) =>
        _events.Add(new(CombatEventType.Order,
            _ships.Count == 0 ? Vector2D.Zero : SelectedShip.Position, message, 4.5));

    public double FleetStrength(Team team) =>
        _ships.Where(ship => ship.Team == team && ship.IsAlive)
            .Sum(ship => ship.HullRatio + ship.ShieldRatio * 0.55);

    private void ApplyManualControl(Ship ship, double dt)
    {
        var rotation = (_manualInput.TurnRight ? 1 : 0) - (_manualInput.TurnLeft ? 1 : 0);
        ship.Angle += rotation * ship.Stats.TurnRate * dt;
        ship.NormalizeAngle();

        var throttle = (_manualInput.Thrust ? 1.0 : 0) - (_manualInput.Reverse ? 0.55 : 0);
        ship.Velocity += Vector2D.FromAngle(ship.Angle) * (ship.Stats.Acceleration * throttle * dt);
        ship.Velocity = ship.Velocity.Limit(ship.EffectiveMaxSpeed);
        if (Math.Abs(throttle) < 1e-9) ship.Velocity *= Math.Pow(0.988, dt * 60);
        if (_manualInput.Fire && NearestEnemy(ship, ship.Stats.WeaponRange) is { } target) Fire(ship, target);
    }

    private void UpdatePilot(Ship ship, double dt)
    {
        var order = ship.Order;
        var target = order.TargetId is null ? null : FindShip(order.TargetId);
        if (target is { IsAlive: false }) target = null;

        switch (order.Type)
        {
            case OrderType.Hold:
                ship.Velocity *= Math.Pow(0.94, dt * 60);
                break;
            case OrderType.Move:
            case OrderType.Retreat:
            case OrderType.FormUp:
                if (order.Destination is { } destination)
                {
                    SteerTo(ship, destination, ship.Stats.MaxSpeed * 0.82, dt);
                    if (ship.Position.DistanceTo(destination) < 42) ship.Order = ShipOrder.Hold;
                }
                break;
            case OrderType.Defend:
                UpdateDefendOrder(ship, target, dt);
                break;
            case OrderType.Attack:
            case OrderType.Intercept:
                UpdateAttackOrder(ship, target, dt);
                break;
        }
    }

    private void UpdateDefendOrder(Ship ship, Ship? protectedShip, double dt)
    {
        if (protectedShip is null)
        {
            ship.Order = ShipOrder.Hold;
            return;
        }
        var threat = _ships.Where(candidate =>
                candidate.IsAlive && candidate.Team != ship.Team &&
                candidate.Position.DistanceTo(protectedShip.Position) < 390)
            .MinBy(candidate => candidate.Position.DistanceTo(protectedShip.Position));
        if (threat is not null)
        {
            UpdateAttackOrder(ship, threat, dt);
            return;
        }
        var phase = Math.Abs(ship.Id.GetHashCode(StringComparison.Ordinal) % 7) * 0.8 + ElapsedSeconds * 0.18;
        var guardPoint = protectedShip.Position + Vector2D.FromAngle(phase) * 105;
        SteerTo(ship, guardPoint, ship.Stats.MaxSpeed * 0.65, dt);
    }

    private void UpdateAttackOrder(Ship ship, Ship? target, double dt)
    {
        target ??= NearestEnemy(ship, double.PositiveInfinity);
        if (target is null)
        {
            ship.Order = ShipOrder.Hold;
            return;
        }
        if (ship.Order.TargetId != target.Id) ship.Order = ship.Order with { TargetId = target.Id };

        var distance = ship.Position.DistanceTo(target.Position);
        var desiredRange = Math.Min(ship.Stats.WeaponRange * 0.72, 330);
        if (distance > desiredRange)
        {
            var leadTime = Math.Min(1, distance / 520);
            SteerTo(ship, target.Position + target.Velocity * leadTime, ship.EffectiveMaxSpeed, dt);
        }
        else if (distance < desiredRange * 0.48)
        {
            var escape = ship.Position + (ship.Position - target.Position).Normalized * 150;
            SteerTo(ship, escape, ship.EffectiveMaxSpeed * 0.68, dt);
        }
        else
        {
            TurnToward(ship, target.Position, dt);
            ship.Velocity *= Math.Pow(0.985, dt * 60);
        }
        if (distance <= ship.Stats.WeaponRange && FacingError(ship, target.Position) < 0.48) Fire(ship, target);
    }

    private static void SteerTo(Ship ship, Vector2D destination, double desiredSpeed, double dt)
    {
        TurnToward(ship, destination, dt);
        var alignment = Math.Max(0.15, 1 - FacingError(ship, destination) / Math.PI);
        var desiredVelocity = Vector2D.FromAngle(ship.Angle) * (desiredSpeed * alignment);
        var delta = (desiredVelocity - ship.Velocity).Limit(ship.Stats.Acceleration * dt);
        ship.Velocity = (ship.Velocity + delta).Limit(ship.Stats.MaxSpeed);
    }

    private static void TurnToward(Ship ship, Vector2D destination, double dt)
    {
        var desired = (destination - ship.Position).Angle;
        var difference = AngleDifference(desired, ship.Angle);
        var maximum = ship.Stats.TurnRate * dt;
        ship.Angle += Math.Clamp(difference, -maximum, maximum);
        ship.NormalizeAngle();
    }

    private void SeparateShips(Ship ship, double dt)
    {
        var push = Vector2D.Zero;
        foreach (var other in _ships)
        {
            if (ReferenceEquals(other, ship) || !other.IsAlive) continue;
            var delta = ship.Position - other.Position;
            var minimum = ship.Stats.Radius + other.Stats.Radius + 12;
            var distance = delta.Length;
            if (distance > 0 && distance < minimum)
            {
                push += delta.Normalized * ((minimum - distance) * 2.2);
            }
        }
        ship.Velocity = (ship.Velocity + push * dt).Limit(ship.EffectiveMaxSpeed);
    }

    private static void Integrate(Ship ship, double dt)
    {
        var next = ship.Position + ship.Velocity * dt;
        var x = Math.Clamp(next.X, 35, WorldWidth - 35);
        var y = Math.Clamp(next.Y, 35, WorldHeight - 35);
        if (Math.Abs(x - next.X) > 1e-9) ship.Velocity = new(-ship.Velocity.X * 0.45, ship.Velocity.Y);
        if (Math.Abs(y - next.Y) > 1e-9) ship.Velocity = new(ship.Velocity.X, -ship.Velocity.Y * 0.45);
        ship.Position = new(x, y);
    }

    private void Fire(Ship source, Ship target)
    {
        if (source.WeaponCooldown > 0 || source.Energy < 4 || !target.IsAlive) return;
        var direction = (target.Position - source.Position).Normalized;
        var origin = source.Position + direction * (source.Stats.Radius + 8);
        var speed = source.Class == ShipClass.Destroyer ? 650 : 540;
        _projectiles.Add(new(source.Id, source.Team, source.Stats.WeaponDamage, origin, direction * speed, 1.35));
        source.WeaponCooldown = source.Stats.FireCooldown;
        source.DrainEnergy(4);
        _events.Add(new(CombatEventType.MuzzleFlash, origin, null, 0.12));
    }

    private void LaunchDroneVolley(Ship source)
    {
        var target = NearestEnemy(source, 720);
        if (target is null) return;
        var baseAngle = (target.Position - source.Position).Angle;
        for (var index = -2; index <= 2; index++)
        {
            var direction = Vector2D.FromAngle(baseAngle + index * 0.055);
            _projectiles.Add(new(source.Id, source.Team, 16,
                source.Position + direction * (source.Stats.Radius + 10), direction * 500, 1.55));
        }
        _events.Add(new(CombatEventType.MuzzleFlash, source.Position, "Drone volley", 0.32));
    }

    private void UpdateProjectiles(double dt)
    {
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var projectile = _projectiles[index];
            projectile.Update(dt);
            var hit = _ships.FirstOrDefault(ship =>
                ship.IsAlive && ship.Team != projectile.Team &&
                ship.Position.DistanceTo(projectile.Position) <= ship.Stats.Radius + 4);
            if (hit is not null)
            {
                var wasAlive = hit.IsAlive;
                hit.ApplyDamage(projectile.Damage);
                _events.Add(new(CombatEventType.Impact, projectile.Position, null, 0.24));
                if (wasAlive && !hit.IsAlive)
                {
                    _events.Add(new(CombatEventType.Destroyed, hit.Position, $"{hit.Name} destroyed", 1.1));
                }
                _projectiles.RemoveAt(index);
            }
            else if (!projectile.IsAlive)
            {
                _projectiles.RemoveAt(index);
            }
        }
    }

    private void UpdateEvents(double dt)
    {
        foreach (var combatEvent in _events) combatEvent.Update(dt);
        _events.RemoveAll(combatEvent => !combatEvent.IsAlive);
    }

    private void UpdateBattleStatus()
    {
        var playerFlagshipAlive = FindShip("player-flagship")?.IsAlive == true;
        var enemyFlagshipAlive = FindShip("enemy-flagship")?.IsAlive == true;
        var anyPlayerAlive = _ships.Any(ship => ship.IsAlive && ship.Team == Team.Player);
        var anyEnemyAlive = _ships.Any(ship => ship.IsAlive && ship.Team == Team.Enemy);
        if (!enemyFlagshipAlive || !anyEnemyAlive)
        {
            Status = BattleStatus.PlayerVictory;
            AddOrderEvent("Enemy flagship destroyed. Victory.");
        }
        else if (!playerFlagshipAlive || !anyPlayerAlive)
        {
            Status = BattleStatus.EnemyVictory;
            AddOrderEvent("Flagship lost. The fleet is in retreat.");
        }
    }

    private Ship? NearestEnemy(Ship source, double maximumDistance) =>
        _ships.Where(ship => ship.IsAlive && ship.Team != source.Team &&
                            ship.Position.DistanceTo(source.Position) <= maximumDistance)
            .MinBy(ship => ship.Position.DistanceTo(source.Position));

    private static double FacingError(Ship ship, Vector2D point) =>
        Math.Abs(AngleDifference((point - ship.Position).Angle, ship.Angle));

    private static double AngleDifference(double target, double current)
    {
        var difference = target - current;
        while (difference > Math.PI) difference -= Math.PI * 2;
        while (difference < -Math.PI) difference += Math.PI * 2;
        return difference;
    }

    private void AddPlayer(string id, string name, ShipClass shipClass, double x, double y, double angle) =>
        _ships.Add(new(id, name, shipClass, Team.Player, new(x, y), angle));

    private void AddEnemy(string id, string name, ShipClass shipClass, double x, double y, double angle) =>
        _ships.Add(new(id, name, shipClass, Team.Enemy, new(x, y), angle));

    private static int PositiveModulo(int value, int modulo) => (value % modulo + modulo) % modulo;
}
