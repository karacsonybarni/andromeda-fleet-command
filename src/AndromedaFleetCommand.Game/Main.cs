using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Simulation;
using AndromedaFleetCommand.Game.Infrastructure;
using Godot;

namespace AndromedaFleetCommand.Game;

public sealed partial class Main : Node2D
{
    private static readonly Color Cyan = new("22d8ff");
    private static readonly Color Blue = new("2c8bff");
    private static readonly Color Red = new("ff4b46");
    private static readonly Color Orange = new("ff9b42");
    private static readonly Color Panel = new(0.015f, 0.06f, 0.11f, 0.9f);

    private readonly BattleSimulation _simulation = new();
    private readonly RuleBasedCommandInterpreter _rules = new();
    private readonly CommandDispatcher _dispatcher = new();
    private readonly Queue<string> _log = new();
    private readonly List<Star> _stars = [];
    private LocalCommandInterpreter? _interpreter;
    private WhisperVoiceInput? _voiceInput;
    private LineEdit? _commandLine;
    private double _accumulator;
    private bool _paused;
    private bool _showHelp = true;
    private bool _commandMode;
    private string _status = "Press H when ready";
    private double _statusTime = 5;
    private bool _smokeTest;

    public override void _Ready()
    {
        _interpreter = new(_rules);
        _voiceInput = new(this);
        CreateStars();
        CreateCommandLine();
        AddLog("Battle joined. Press H when ready.");
        AddLog("Enter opens the fleet command channel.");
        _smokeTest = OS.GetCmdlineUserArgs().Contains("--smoke-test", StringComparer.Ordinal);
        if (_smokeTest)
        {
            _showHelp = false;
            var command = _rules.Parse("All ships, attack the enemy flagship").Command!;
            _dispatcher.Dispatch(command, _simulation);
        }
        GetViewport().SizeChanged += QueueRedraw;
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        _voiceInput?.Dispose();
        _interpreter?.Dispose();
    }

    public override void _Process(double delta)
    {
        if (_statusTime > 0)
        {
            _statusTime -= delta;
            if (_statusTime <= 0) _status = string.Empty;
        }

        if (!_paused && !_showHelp && !_commandMode && _simulation.Status == BattleStatus.Active)
        {
            _accumulator += Math.Min(delta, 0.2);
            _simulation.SetManualInput(new(
                Input.IsKeyPressed(Key.W),
                Input.IsKeyPressed(Key.S),
                Input.IsKeyPressed(Key.A),
                Input.IsKeyPressed(Key.D),
                Input.IsKeyPressed(Key.Space)));
            var safety = 0;
            while (_accumulator >= BattleSimulation.FixedStep && safety++ < 12)
            {
                _simulation.Update(BattleSimulation.FixedStep);
                _accumulator -= BattleSimulation.FixedStep;
            }
        }
        else
        {
            _simulation.SetManualInput(ManualInput.None);
            _accumulator = 0;
        }
        QueueRedraw();
        if (_smokeTest && _simulation.ElapsedSeconds >= 2)
        {
            var finite = _simulation.Ships.All(ship => ship.Position.IsFinite && ship.Velocity.IsFinite);
            if (!finite) throw new InvalidOperationException("Smoke test detected invalid simulation state");
            GD.Print($"AFC_SMOKE_PASS ships={_simulation.Ships.Count} projectiles={_simulation.Projectiles.Count}");
            _smokeTest = false;
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_commandMode)
        {
            if (key.Keycode == Key.Escape) CloseCommandLine("Command cancelled");
            return;
        }

        switch (key.Keycode)
        {
            case Key.H:
                _showHelp = !_showHelp;
                break;
            case Key.Enter when !_showHelp:
                OpenCommandLine();
                break;
            case Key.V when !_showHelp:
                CaptureVoiceCommand();
                break;
            case Key.Q when !_showHelp:
                var abilityMessage = _simulation.TryActivateSelectedAbility();
                SetStatus(abilityMessage);
                AddLog(abilityMessage);
                break;
            case Key.P when !_showHelp:
                _paused = !_paused;
                break;
            case Key.R when !_showHelp:
                Restart();
                break;
            case Key.Tab when !_showHelp:
                _simulation.CycleSelectedShip();
                break;
            case Key.Key1 when !_showHelp:
                _simulation.SelectPlayerShip(0);
                break;
            case Key.Key2 when !_showHelp:
                _simulation.SelectPlayerShip(1);
                break;
            case Key.Key3 when !_showHelp:
                _simulation.SelectPlayerShip(2);
                break;
            case Key.Key4 when !_showHelp:
                _simulation.SelectPlayerShip(3);
                break;
            case Key.Escape:
                GetTree().Quit();
                break;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        DrawSpace();
        DrawGrid();
        foreach (var projectile in _simulation.Projectiles) DrawProjectile(projectile);
        foreach (var ship in _simulation.Ships.Where(ship => ship.IsAlive)) DrawShip(ship);
        foreach (var combatEvent in _simulation.Events.Where(item => item.Type != CombatEventType.Order))
            DrawCombatEvent(combatEvent);
        DrawHud();
        DrawOverlay();
    }

    private void CreateCommandLine()
    {
        var canvas = new CanvasLayer { Layer = 20 };
        AddChild(canvas);
        _commandLine = new()
        {
            Visible = false,
            PlaceholderText = "Frigate Two, intercept the bomber wing…",
            Position = new(260, 770),
            Size = new(1080, 58),
            MaxLength = 140,
            CaretBlink = true,
            KeepEditingOnTextSubmit = false
        };
        _commandLine.AddThemeFontSizeOverride("font_size", 21);
        var style = new StyleBoxFlat
        {
            BgColor = new(0.01f, 0.05f, 0.09f, 0.98f),
            BorderColor = Cyan,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 20
        };
        _commandLine.AddThemeStyleboxOverride("normal", style);
        _commandLine.AddThemeStyleboxOverride("focus", style);
        _commandLine.TextSubmitted += SubmitCommand;
        canvas.AddChild(_commandLine);
    }

    private void OpenCommandLine()
    {
        if (_commandLine is null) return;
        _commandMode = true;
        _commandLine.Text = string.Empty;
        _commandLine.Visible = true;
        _commandLine.GrabFocus();
        SetStatus("Type an order and press Enter");
    }

    private void CloseCommandLine(string status)
    {
        if (_commandLine is null) return;
        _commandMode = false;
        _commandLine.Visible = false;
        _commandLine.ReleaseFocus();
        SetStatus(status);
    }

    private async void SubmitCommand(string text)
    {
        if (_interpreter is null || string.IsNullOrWhiteSpace(text))
        {
            CloseCommandLine("Command cancelled");
            return;
        }
        CloseCommandLine("Interpreting command…");
        AddLog($"YOU  {text.Trim()}");
        var result = await _interpreter.InterpretAsync(text);
        if (!result.Success || result.Command is null)
        {
            SetStatus(result.Message);
            AddLog(result.Message);
            return;
        }
        var acknowledgement = _dispatcher.Dispatch(result.Command, _simulation);
        SetStatus(acknowledgement);
        AddLog($"FLEET  {acknowledgement}");
    }

    private async void CaptureVoiceCommand()
    {
        if (_voiceInput is null || !_voiceInput.IsAvailable)
        {
            var reason = _voiceInput?.UnavailableReason ?? "Voice input is unavailable";
            SetStatus(reason);
            AddLog(reason);
            return;
        }
        try
        {
            SetStatus("Listening for four seconds…");
            AddLog("VOICE  Listening…");
            var transcript = await _voiceInput.CaptureAsync();
            AddLog($"VOICE  {transcript}");
            SubmitCommand(transcript);
        }
        catch (Exception error)
        {
            SetStatus($"Voice failed: {error.Message}");
            AddLog($"VOICE  {error.Message}");
        }
    }

    private void Restart()
    {
        _simulation.Reset();
        _log.Clear();
        AddLog("New battle initialized.");
        AddLog("Destroy the enemy flagship.");
        _paused = false;
        SetStatus("Fleet ready");
    }

    private void DrawSpace()
    {
        DrawRect(new(0, 0, 1600, 900), new Color("020915"));
        DrawCircle(new(1230, 175), 520, new Color(0.06f, 0.18f, 0.32f, 0.23f));
        foreach (var star in _stars)
        {
            DrawCircle(star.Position, star.Size, new Color(star.Blue ? 0.58f : 1f, star.Blue ? 0.82f : 0.9f, 1f, star.Alpha));
        }
        DrawCircle(new(210, 1030), 700, new Color("0b2f56"));
        DrawArc(new(210, 1030), 700, 3.55f, 5.85f, 96, new Color(0.2f, 0.68f, 1f, 0.62f), 6);
    }

    private void DrawGrid()
    {
        var color = new Color(0.16f, 0.58f, 0.78f, 0.08f);
        for (var x = 100; x < 1600; x += 100) DrawLine(new(x, 74), new(x, 838), color);
        for (var y = 100; y < 850; y += 100) DrawLine(new(0, y), new(1600, y), color);
        DrawArc(new(800, 450), 210, 0, Mathf.Tau, 96, new(0.2f, 0.75f, 1f, 0.12f));
        DrawArc(new(800, 450), 335, 0, Mathf.Tau, 96, new(0.2f, 0.75f, 1f, 0.1f));
    }

    private void DrawProjectile(Projectile projectile)
    {
        var color = projectile.Team == Team.Player ? Cyan : Orange;
        var head = ToVector(projectile.Position);
        var tail = ToVector(projectile.Position - projectile.Velocity.Normalized * 22);
        DrawLine(tail, head, new(color, 0.28f), 8, true);
        DrawLine(tail, head, color, 2.5f, true);
    }

    private void DrawShip(Ship ship)
    {
        var position = ToVector(ship.Position);
        var teamColor = ship.Team == Team.Player ? Cyan : Red;
        var selected = ship.Id == _simulation.SelectedShip.Id;
        DrawSetTransform(position, (float)ship.Angle);
        if (ship.Velocity.Length > 8)
        {
            DrawLine(new(-(float)ship.Stats.Radius, 0), new(-(float)ship.Stats.Radius - 28, 0),
                new(teamColor, 0.78f), 8, true);
        }
        var hull = CreateHull(ship);
        DrawColoredPolygon(hull, new Color("17273b"));
        DrawPolyline(hull.Append(hull[0]).ToArray(), selected ? Colors.White : teamColor, selected ? 3 : 1.7f, true);
        DrawLine(new(-(float)ship.Stats.Radius * 0.35f, 0), new((float)ship.Stats.Radius * 0.72f, 0),
            new(teamColor, 0.75f), 2);
        DrawSetTransform(Vector2.Zero, 0);

        if (selected)
        {
            DrawArc(position, (float)ship.Stats.Radius + 14, 0, Mathf.Tau, 64, Colors.White, 2);
        }
        DrawLabel(ship.Name.ToUpperInvariant(), position + new Vector2(0, -(float)ship.Stats.Radius - 18),
            12, teamColor, HorizontalAlignment.Center, 130);
        DrawBar(new(position.X - 50, position.Y - (float)ship.Stats.Radius - 8), 100, 4,
            (float)ship.HullRatio, teamColor);
    }

    private static Vector2[] CreateHull(Ship ship)
    {
        var radius = (float)ship.Stats.Radius;
        var length = ship.Class switch
        {
            ShipClass.Flagship or ShipClass.Carrier => radius * 1.55f,
            ShipClass.Destroyer => radius * 1.45f,
            _ => radius * 1.3f
        };
        var width = ship.Class switch
        {
            ShipClass.Carrier => radius * 0.75f,
            ShipClass.Bomber or ShipClass.Escort => radius * 0.55f,
            _ => radius * 0.68f
        };
        return
        [
            new(length, 0), new(radius * 0.35f, -width), new(-length * 0.72f, -width * 0.55f),
            new(-length, -width * 0.22f), new(-length, width * 0.22f),
            new(-length * 0.72f, width * 0.55f), new(radius * 0.35f, width)
        ];
    }

    private void DrawCombatEvent(CombatEvent combatEvent)
    {
        var life = (float)Math.Clamp(combatEvent.RemainingLife / combatEvent.InitialLife, 0, 1);
        var radius = combatEvent.Type switch
        {
            CombatEventType.MuzzleFlash => 8,
            CombatEventType.Impact => 18,
            CombatEventType.Destroyed => (int)(64 * (1.2f - life * 0.3f)),
            _ => 12
        };
        var color = combatEvent.Type == CombatEventType.Destroyed ? Orange : Cyan;
        DrawArc(ToVector(combatEvent.Position), radius, 0, Mathf.Tau, 48, new(color, life), 4);
        if (combatEvent.Type == CombatEventType.Destroyed)
            DrawCircle(ToVector(combatEvent.Position), radius * 0.45f, new Color(1, 0.72f, 0.28f, life * 0.55f));
    }

    private void DrawHud()
    {
        DrawRect(new(0, 0, 1600, 74), new Color(0.004f, 0.03f, 0.07f, 0.95f));
        DrawLine(new(0, 73), new(1600, 73), new(Cyan, 0.32f));
        DrawLabel("ANDROMEDA", new(26, 31), 24, Colors.White);
        DrawLabel("FLEET COMMAND", new(28, 54), 15, Cyan);

        DrawFactionBar(new(520, 18), 245, (float)(_simulation.FleetStrength(Team.Player) / 6.2),
            "ANDROMEDA FLEET", Cyan);
        DrawFactionBar(new(835, 18), 245, (float)(_simulation.FleetStrength(Team.Enemy) / 13.5),
            "KETZAL EMPIRE", Red);
        var totalSeconds = (int)_simulation.ElapsedSeconds;
        DrawLabel($"{totalSeconds / 60:00}:{totalSeconds % 60:00}", new(800, 40), 18,
            new Color("e5edf5"), HorizontalAlignment.Center, 120);
        DrawFleetPanel();
        DrawSelectedPanel();
        DrawCommandLog();
        DrawObjective();

        if (!string.IsNullOrWhiteSpace(_status) && !_commandMode)
        {
            DrawPanel(new(510, 87, 580, 38));
            DrawLabel(_status, new(800, 112), 14, new Color("a0e1f5"), HorizontalAlignment.Center, 560);
        }
    }

    private void DrawFactionBar(Vector2 position, float width, float ratio, string text, Color color)
    {
        DrawLabel(text, position + new Vector2(0, 11), 12, color);
        DrawBar(position + new Vector2(0, 20), width, 8, Mathf.Clamp(ratio, 0, 1), color);
    }

    private void DrawFleetPanel()
    {
        var area = new Rect2(20, 104, 230, 192);
        DrawPanel(area);
        DrawLabel("FRIENDLY FLEET", new(34, 126), 13, new Color("a0d2eb"));
        var fleet = _simulation.Ships.Where(ship => ship.Team == Team.Player).ToList();
        for (var index = 0; index < fleet.Count; index++)
        {
            var ship = fleet[index];
            var y = 140 + index * 37;
            if (ship.IsAlive && ship.Id == _simulation.SelectedShip.Id)
                DrawRect(new(28, y - 11, 214, 32), new Color(0.1f, 0.66f, 0.86f, 0.2f));
            DrawLabel($"{index + 1}  {ship.Name.ToUpperInvariant()}", new(34, y + 4), 12,
                ship.IsAlive ? Colors.White : new Color("666a72"));
            DrawBar(new(122, y + 10), 105, 4, (float)ship.HullRatio, ship.IsAlive ? Cyan : Red);
        }
    }

    private void DrawSelectedPanel()
    {
        var ship = _simulation.SelectedShip;
        DrawPanel(new(20, 710, 300, 166));
        DrawLabel(ship.Name.ToUpperInvariant(), new(36, 737), 18, Colors.White);
        DrawLabel($"{ship.Class.ToString().ToUpperInvariant()}  •  MANUAL CONTROL", new(36, 756), 12,
            new Color("82beda"));
        DrawMeter(new(36, 778), "HULL", (float)ship.HullRatio, new Color("4be6a3"));
        DrawMeter(new(36, 804), "SHIELD", (float)ship.ShieldRatio, Cyan);
        DrawMeter(new(36, 830), "ENERGY", (float)ship.EnergyRatio, new Color("ffc74d"));
        DrawLabel($"{ship.Order.Type.ToString().ToUpperInvariant()}  •  {(int)ship.Velocity.Length} m/s",
            new(36, 860), 11, new Color("7dacC6"));
        var ability = ship.AbilityCooldown <= 0 ? "Q  ABILITY READY" : $"Q  {Math.Ceiling(ship.AbilityCooldown)}s";
        DrawLabel(ability, new(205, 860), 11, ship.AbilityCooldown <= 0 ? new Color("ffd065") : new Color("6f8794"));
    }

    private void DrawMeter(Vector2 position, string label, float ratio, Color color)
    {
        DrawLabel(label, position, 11, new Color("90bacf"));
        DrawBar(position + new Vector2(70, -8), 135, 7, ratio, color);
    }

    private void DrawCommandLog()
    {
        DrawPanel(new(345, 745, 740, 131));
        DrawLabel("COMMAND CHANNEL  •  ENTER TO ISSUE ORDER", new(361, 769), 13, Cyan);
        var y = 793;
        foreach (var line in _log.Take(4))
        {
            DrawLabel($"› {line}", new(363, y), 12, new Color("aed1e1"));
            y += 19;
        }
    }

    private void DrawObjective()
    {
        DrawPanel(new(1300, 104, 278, 105));
        DrawLabel("PRIMARY OBJECTIVE", new(1316, 127), 13, Cyan);
        DrawLabel("DESTROY ENEMY FLAGSHIP", new(1316, 154), 15, Colors.White);
        var flagship = _simulation.FindShip("enemy-flagship")!;
        DrawBar(new(1316, 172), 246, 8, (float)flagship.HullRatio, Red);
        DrawLabel($"{(int)(flagship.HullRatio * 100)}% HULL", new(1316, 198), 11, new Color("91b9ce"));
    }

    private void DrawOverlay()
    {
        if (_showHelp)
        {
            DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.75f));
            DrawPanel(new(420, 175, 760, 480));
            DrawLabel("YOU ARE THE COMMANDER", new(800, 238), 31, Colors.White,
                HorizontalAlignment.Center, 700);
            DrawLabel("Fly one ship. Command the whole fleet.", new(800, 270), 16,
                new Color("99d3e9"), HorizontalAlignment.Center, 700);
            var controls = new[]
            {
                ("1–4 / TAB", "Switch controlled ship"),
                ("W S / A D", "Thrust and rotate"),
                ("SPACE", "Fire at nearest target"),
                ("ENTER", "Type a natural-language fleet order"),
                ("Q", "Use the selected ship’s tactical ability"),
                ("V", "Local voice command adapter"),
                ("P / H / R", "Pause, help, restart")
            };
            var y = 322;
            foreach (var (key, description) in controls)
            {
                DrawLabel(key, new(510, y), 15, Cyan);
                DrawLabel(description, new(705, y), 16, new Color("dce7ee"));
                y += 43;
            }
            DrawLabel("Try: “Frigate Two, intercept the bombers.”", new(800, 618), 14,
                new Color("ffd065"), HorizontalAlignment.Center, 700);
            DrawLabel("Press H to enter the battle", new(800, 644), 13,
                new Color("87b5ca"), HorizontalAlignment.Center, 700);
        }
        else if (_paused && _simulation.Status == BattleStatus.Active)
        {
            DrawBanner("PAUSED", "Press P to return to the battle", Cyan);
        }
        else if (_simulation.Status == BattleStatus.PlayerVictory)
        {
            DrawBanner("VICTORY", "Enemy flagship destroyed • Press R to replay", new Color("48eba9"));
        }
        else if (_simulation.Status == BattleStatus.EnemyVictory)
        {
            DrawBanner("FLEET LOST", "The flagship was destroyed • Press R to try again", Red);
        }
    }

    private void DrawBanner(string title, string subtitle, Color color)
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.78f));
        DrawLabel(title, new(800, 410), 58, color, HorizontalAlignment.Center, 900);
        DrawLabel(subtitle, new(800, 452), 19, Colors.White, HorizontalAlignment.Center, 900);
    }

    private void DrawPanel(Rect2 rect)
    {
        DrawStyleBox(new StyleBoxFlat
        {
            BgColor = Panel,
            BorderColor = new(0.2f, 0.72f, 0.92f, 0.35f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12
        }, rect);
    }

    private void DrawLabel(string text, Vector2 position, int size, Color color,
        HorizontalAlignment alignment = HorizontalAlignment.Left, float width = -1)
    {
        DrawString(ThemeDB.FallbackFont, position, text, alignment, width, size, color);
    }

    private void DrawBar(Vector2 position, float width, float height, float ratio, Color color)
    {
        DrawRect(new(position, new(width, height)), new Color(1, 1, 1, 0.1f));
        DrawRect(new(position, new(width * Mathf.Clamp(ratio, 0, 1), height)), color);
    }

    private void SetStatus(string text)
    {
        _status = text;
        _statusTime = 4.5;
    }

    private void AddLog(string line)
    {
        _log.Enqueue(line);
        while (_log.Count > 4) _log.Dequeue();
    }

    private void CreateStars()
    {
        var random = new Random(0xAFC2026);
        for (var index = 0; index < 330; index++)
        {
            _stars.Add(new(new(random.Next(1600), random.Next(76, 880)),
                0.7f + (float)random.NextDouble() * 2.3f,
                0.25f + (float)random.NextDouble() * 0.7f,
                random.NextDouble() < 0.42));
        }
    }

    private static Vector2 ToVector(Vector2D value) => new((float)value.X, (float)value.Y);
    private readonly record struct Star(Vector2 Position, float Size, float Alpha, bool Blue);
}
