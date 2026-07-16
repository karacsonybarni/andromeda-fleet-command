using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Configuration;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Multiplayer;
using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Replay;
using AndromedaFleetCommand.Core.Simulation;
using AndromedaFleetCommand.Game.Infrastructure;
using Godot;

namespace AndromedaFleetCommand.Game;

public sealed partial class Main : Node2D
{
    private Color Cyan => _settings.ColorMode switch
    {
        ColorVisionMode.Deuteranopia => new("3295ff"),
        ColorVisionMode.Tritanopia => new("46e39a"),
        ColorVisionMode.HighContrast => Colors.White,
        _ => new("22d8ff")
    };
    private Color Red => _settings.ColorMode switch
    {
        ColorVisionMode.Deuteranopia => new("ffd447"),
        ColorVisionMode.Tritanopia => new("ff62bc"),
        ColorVisionMode.HighContrast => new("ffb000"),
        _ => new("ff4b46")
    };
    private Color Orange => _settings.ColorMode == ColorVisionMode.Tritanopia
        ? new("ff8fd1")
        : new("ff9b42");
    private static Color Panel => new(0.015f, 0.06f, 0.11f, 0.92f);

    private BattleSimulation _simulation = new(MissionId.FirstCommand);
    private readonly RuleBasedCommandInterpreter _rules = new();
    private readonly CommandDispatcher _dispatcher = new();
    private readonly Queue<string> _log = new();
    private readonly List<Star> _stars = [];
    private readonly Dictionary<ShipClass, Texture2D> _shipTextures = [];
    private LocalCommandInterpreter? _interpreter;
    private WhisperVoiceInput? _voiceInput;
    private TacticalAudio? _audio;
    private LineEdit? _commandLine;
    private double _accumulator;
    private bool _paused;
    private bool _showHelp = true;
    private bool _commandMode;
    private string _status = "Press H when ready";
    private double _statusTime = 5;
    private bool _smokeTest;
    private CampaignProgressStore? _progressStore;
    private CampaignProgress _progress = CampaignProgress.New;
    private TutorialTracker _tutorial = new();
    private bool _showMissionSelect;
    private BattleStatus _observedBattleStatus = BattleStatus.Active;
    private int _previousProjectileCount;
    private double _weaponAudioCooldown;
    private readonly HashSet<CombatEvent> _heardCombatEvents = [];
    private LocalAiConfigurationStore? _localAiStore;
    private LocalAiConfiguration _localAiConfiguration = LocalAiConfiguration.Default;
    private LocalAiSetupService? _localAiSetup;
    private LocalAiReadiness _localAiReadiness = new(false, false, false, false,
        "Press R to scan local AI services");
    private bool _showLocalAiSetup;
    private bool _localAiBusy;
    private GameSettingsStore? _settingsStore;
    private GameSettings _settings = GameSettings.Default;
    private InputBindingsStore? _bindingsStore;
    private InputBindings _bindings = InputBindings.Default;
    private GamepadBindingsStore? _gamepadBindingsStore;
    private GamepadBindings _gamepadBindings = GamepadBindings.Default;
    private CrashReportService? _crashReports;
    private bool _showSettings;
    private bool _showBindings;
    private int _bindingSelection;
    private bool _captureBinding;
    private bool _bindingDeviceGamepad;
    private string _audioCaption = string.Empty;
    private double _audioCaptionTime;
    private BattleReplayStore? _replayStore;
    private ReplayRecorder? _replayRecorder;
    private int _simulationTick;
    private IPlatformServices? _platform;
    private bool _lastInputWasController;
    private double _tutorialStepFlash;
    private double _tutorialCelebrationTime;
    private double _visualTime;
    private double _combatKick;
    private Vector2 _worldDrawOffset;
    private double _commandPulseTime;
    private string _lastIssuedCommand = "Fleet standing by";
    private string _lastAcknowledgement = "Awaiting tactical order";
    private bool _visualQa;
    private bool _visualQaFreeze;
    private int _visualQaStage;
    private int _visualQaFrames;
    private string _visualQaDirectory = string.Empty;
    private readonly List<string> _visualQaCaptures = [];
    private MultiplayerManager? _multiplayer;
    private bool _showMultiplayer;
    private bool _wasMultiplayerMatch;
    private LineEdit? _multiplayerName;
    private LineEdit? _multiplayerAddress;
    private double _networkControlAccumulator;
    private bool _multiplayerSmokeHost;
    private bool _multiplayerSmokeClient;
    private bool _multiplayerSmokePassed;
    private bool _multiplayerSmokeSnapshotSeen;

    public override void _Ready()
    {
        var commandArguments = OS.GetCmdlineUserArgs();
        _smokeTest = commandArguments.Contains("--smoke-test", StringComparer.Ordinal);
        _multiplayerSmokeHost = commandArguments.Contains("--multiplayer-smoke-host", StringComparer.Ordinal);
        _multiplayerSmokeClient = commandArguments.Contains("--multiplayer-smoke-client", StringComparer.Ordinal);
        ReportMultiplayerSmokeBoot("arguments");
        _visualQa = commandArguments.Contains("--visual-qa", StringComparer.Ordinal);
        var benchmarkMode = commandArguments.Contains("--benchmark", StringComparer.Ordinal);
        _settingsStore = new(ProjectSettings.GlobalizePath("user://settings.json"));
        _settings = _settingsStore.Load();
        _bindingsStore = new(ProjectSettings.GlobalizePath("user://input-bindings.json"));
        _bindings = _bindingsStore.Load();
        _gamepadBindingsStore = new(ProjectSettings.GlobalizePath("user://gamepad-bindings.json"));
        _gamepadBindings = _gamepadBindingsStore.Load();
        ReportMultiplayerSmokeBoot("settings");
        _status = $"Press {BindingLabel(GameActionIds.Help)} when ready";
        _crashReports = new(ProjectSettings.GlobalizePath("user://crashes"));
        _replayStore = new(ProjectSettings.GlobalizePath("user://replays"));
        ApplySettings();
        _localAiStore = new(ProjectSettings.GlobalizePath("user://local-ai.json"));
        _localAiConfiguration = LocalAiConfiguration.ApplyEnvironment(_localAiStore.Load());
        _localAiSetup = new();
        RebuildLocalAiAdapters();
        ReportMultiplayerSmokeBoot("local-ai");
        _audio = new(this);
        if (!_smokeTest && !_visualQa && !benchmarkMode && !_multiplayerSmokeHost && !_multiplayerSmokeClient)
            _audio.StartAmbient();
        _platform = PlatformServicesFactory.Create();
        _progressStore = new(ProjectSettings.GlobalizePath("user://campaign-progress.json"));
        _progress = _progressStore.Load();
        CreateStars();
        LoadShipArt();
        CreateCommandLine();
        CreateMultiplayerControls();
        ReportMultiplayerSmokeBoot("scene");
        _multiplayer = new MultiplayerManager { Name = "MultiplayerManager" };
        _multiplayer.LobbyChanged += OnMultiplayerLobbyChanged;
        _multiplayer.MatchStarted += OnMultiplayerMatchStarted;
        _multiplayer.SnapshotReceived += OnMultiplayerSnapshot;
        _multiplayer.NoticeReceived += OnMultiplayerNotice;
        _multiplayer.StateChanged += OnMultiplayerStateChanged;
        AddChild(_multiplayer);
        ReportMultiplayerSmokeBoot("network-manager");
        if (_multiplayerSmokeHost)
        {
            _showHelp = false;
            ReportMultiplayerSmokeBoot("host-create");
            var result = _multiplayer.Host(MultiplayerMode.Cooperative, "Smoke Host");
            if (!result.Accepted) throw new InvalidOperationException(result.Message);
            GD.Print("AFC_MP_HOST_READY");
        }
        else if (_multiplayerSmokeClient)
        {
            _showHelp = false;
            var result = _multiplayer.Join("127.0.0.1", "Smoke Client");
            if (!result.Accepted) throw new InvalidOperationException(result.Message);
            GD.Print("AFC_MP_CLIENT_CONNECTING");
        }
        AddLog($"Mission 1: {_simulation.Mission.Title}. Press {BindingLabel(GameActionIds.Help)} when ready.");
        AddLog($"{BindingLabel(GameActionIds.Command)} opens the fleet command channel.");
        AddLog($"Platform: {_platform.Name}");
        StartReplayRecording();
        if (benchmarkMode)
        {
            RunBenchmark(true);
            return;
        }
        if (_smokeTest)
        {
            _showHelp = false;
            var command = _rules.Parse("All ships, attack the nearest enemy").Command!;
            _dispatcher.Dispatch(command, _simulation);
        }
        if (_visualQa)
        {
            _visualQaDirectory = System.Environment.GetEnvironmentVariable("AFC_VISUAL_QA_DIR") ??
                                 ProjectSettings.GlobalizePath("user://visual-qa");
            Directory.CreateDirectory(_visualQaDirectory);
            _showHelp = true;
            _status = string.Empty;
            _statusTime = 0;
        }
        GetViewport().SizeChanged += QueueRedraw;
        QueueRedraw();
    }

    private void ReportMultiplayerSmokeBoot(string stage)
    {
        if (_multiplayerSmokeHost || _multiplayerSmokeClient)
            GD.Print($"AFC_MP_BOOT role={(_multiplayerSmokeHost ? "host" : "client")} stage={stage}");
    }

    public override void _ExitTree()
    {
        _voiceInput?.Dispose();
        _interpreter?.Dispose();
        _localAiSetup?.Dispose();
        _crashReports?.Dispose();
        _audio?.StopAmbient();
        _platform?.Dispose();
        if (_multiplayer is not null)
        {
            _multiplayer.LobbyChanged -= OnMultiplayerLobbyChanged;
            _multiplayer.MatchStarted -= OnMultiplayerMatchStarted;
            _multiplayer.SnapshotReceived -= OnMultiplayerSnapshot;
            _multiplayer.NoticeReceived -= OnMultiplayerNotice;
            _multiplayer.StateChanged -= OnMultiplayerStateChanged;
        }
    }

    public override void _Process(double delta)
    {
        _visualTime += delta;
        _combatKick = Math.Max(0, _combatKick - delta * 3.8);
        _weaponAudioCooldown = Math.Max(0, _weaponAudioCooldown - delta);
        _tutorialStepFlash = Math.Max(0, _tutorialStepFlash - delta);
        _tutorialCelebrationTime = Math.Max(0, _tutorialCelebrationTime - delta);
        _commandPulseTime = Math.Max(0, _commandPulseTime - delta);
        _platform?.RunCallbacks();
        _audioCaptionTime = Math.Max(0, _audioCaptionTime - delta);
        if (_statusTime > 0)
        {
            _statusTime -= delta;
            if (_statusTime <= 0) _status = string.Empty;
        }

        var gameplayInputAvailable = !_paused && !_visualQaFreeze && !_showHelp && !_commandMode &&
            !_showSettings && !_showBindings && !_showMissionSelect && !_showLocalAiSetup && !_showMultiplayer &&
            _simulation.Status == BattleStatus.Active;
        if (gameplayInputAvailable && _multiplayer?.IsInMatch == true)
        {
            var manualInput = ReadManualInput();
            _networkControlAccumulator += delta;
            if (_networkControlAccumulator >= 1.0 / 30.0)
            {
                _networkControlAccumulator %= 1.0 / 30.0;
                _multiplayer.SendControl(_simulation.SelectedShip.Id, manualInput);
            }
            ObserveCombatAudio();
            ObserveBattleStatus();
            if (_simulation.Projectiles.Count > _previousProjectileCount && _weaponAudioCooldown <= 0)
            {
                PlayCue(TacticalCue.Weapon, "Weapons fire");
                _weaponAudioCooldown = 0.09;
            }
            _previousProjectileCount = _simulation.Projectiles.Count;
            _accumulator = 0;
        }
        else if (gameplayInputAvailable)
        {
            _accumulator += Math.Min(delta, 0.2);
            var manualInput = ReadManualInput();
            if (manualInput != ManualInput.None) AdvanceTutorial(TutorialAction.ManualControl);
            _replayRecorder?.RecordInput(_simulationTick, manualInput);
            _simulation.SetManualInput(manualInput);
            var safety = 0;
            while (_accumulator >= BattleSimulation.FixedStep && safety++ < 12)
            {
                _simulation.Update(BattleSimulation.FixedStep);
                _simulationTick++;
                _accumulator -= BattleSimulation.FixedStep;
            }
            ObserveCombatAudio();
            ObserveBattleStatus();
            if (_simulation.Projectiles.Count > _previousProjectileCount && _weaponAudioCooldown <= 0)
            {
                PlayCue(TacticalCue.Weapon, "Weapons fire");
                _weaponAudioCooldown = 0.09;
            }
            _previousProjectileCount = _simulation.Projectiles.Count;
        }
        else
        {
            if (_multiplayer?.IsInMatch != true) _simulation.SetManualInput(ManualInput.None);
            _accumulator = 0;
            _networkControlAccumulator = 0;
        }
        QueueRedraw();
        if (_visualQa) AdvanceVisualQa();
        if (_smokeTest && _simulation.ElapsedSeconds >= 2)
        {
            var finite = _simulation.Ships.All(ship => ship.Position.IsFinite && ship.Velocity.IsFinite);
            if (!finite) throw new InvalidOperationException("Smoke test detected invalid simulation state");
            GD.Print($"AFC_SMOKE_PASS ships={_simulation.Ships.Count} projectiles={_simulation.Projectiles.Count}");
            _smokeTest = false;
            GetTree().Quit();
        }
    }

    private ManualInput ReadManualInput()
    {
        var joyX = Input.GetJoyAxis(0, JoyAxis.LeftX);
        var joyY = Input.GetJoyAxis(0, JoyAxis.LeftY);
        var deadzone = (float)_settings.GamepadDeadzone;
        return new(
            IsActionPressed(GameActionIds.Thrust) || joyY < -deadzone,
            IsActionPressed(GameActionIds.Reverse) || joyY > deadzone,
            IsActionPressed(GameActionIds.TurnLeft) || joyX < -deadzone,
            IsActionPressed(GameActionIds.TurnRight) || joyX > deadzone,
            IsActionPressed(GameActionIds.Fire) || Input.IsJoyButtonPressed(0, BoundButton(GamepadActionIds.Fire)));
    }

    private void AdvanceVisualQa()
    {
        _visualQaFrames++;
        if (_visualQaFrames < 10) return;

        switch (_visualQaStage)
        {
            case 0:
                CaptureVisualQaFrame("01-tutorial-briefing");
                _showHelp = false;
                _dispatcher.Dispatch(_rules.Parse("All ships, attack the nearest enemy").Command!, _simulation);
                NextVisualQaStage();
                break;
            case 1 when _simulation.ElapsedSeconds >= 2:
                CaptureVisualQaFrame("02-live-combat");
                _paused = true;
                NextVisualQaStage();
                break;
            case 2:
                CaptureVisualQaFrame("03-pause");
                _paused = false;
                _showSettings = true;
                NextVisualQaStage();
                break;
            case 3:
                CaptureVisualQaFrame("04-settings");
                _showSettings = false;
                _showBindings = true;
                _bindingDeviceGamepad = false;
                NextVisualQaStage();
                break;
            case 4:
                CaptureVisualQaFrame("05-keyboard-bindings");
                _bindingDeviceGamepad = true;
                NextVisualQaStage();
                break;
            case 5:
                CaptureVisualQaFrame("06-controller-bindings");
                _showBindings = false;
                _showMissionSelect = true;
                NextVisualQaStage();
                break;
            case 6:
                CaptureVisualQaFrame("07-mission-select");
                _showMissionSelect = false;
                _simulation.LoadMission(MissionId.BrokenShield);
                _showHelp = true;
                NextVisualQaStage();
                break;
            case 7:
                CaptureVisualQaFrame("08-story-briefing");
                _showHelp = false;
                _simulation.LoadMission(MissionId.FirstCommand);
                _simulation.FindShip("enemy-raider-leader")!.ApplyDamage(10_000);
                _simulation.Update(BattleSimulation.FixedStep);
                NextVisualQaStage();
                break;
            case 8:
                CaptureVisualQaFrame("09-victory-debrief");
                _simulation.LoadMission(MissionId.BlackSun);
                _showHelp = false;
                _lastIssuedCommand = "All ships, attack the enemy flagship";
                _lastAcknowledgement = _dispatcher.Dispatch(
                    _rules.Parse(_lastIssuedCommand).Command!, _simulation);
                _commandPulseTime = 12;
                NextVisualQaStage();
                break;
            case 9 when _simulation.ElapsedSeconds >= 8:
                _visualQaFreeze = true;
                NextVisualQaStage();
                break;
            case 10:
                CaptureVisualQaFrame("10-fleet-battle");
                GD.Print($"AFC_VISUAL_QA_PASS captures={_visualQaCaptures.Count} directory={_visualQaDirectory}");
                _visualQa = false;
                GetTree().Quit();
                break;
        }
    }

    private void NextVisualQaStage()
    {
        _visualQaStage++;
        _visualQaFrames = 0;
    }

    private void CaptureVisualQaFrame(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image is null)
        {
            _visualQa = false;
            GD.PushError($"Visual QA capture {name} has no active renderer");
            GetTree().Quit(1);
            return;
        }
        if (image.IsEmpty() || image.GetWidth() < 1280 || image.GetHeight() < 720)
            throw new InvalidOperationException($"Visual QA capture {name} has no full-size rendered frame");

        var sampledColors = new HashSet<uint>();
        for (var y = 0; y < image.GetHeight(); y += Math.Max(1, image.GetHeight() / 18))
        for (var x = 0; x < image.GetWidth(); x += Math.Max(1, image.GetWidth() / 24))
            sampledColors.Add(image.GetPixel(x, y).ToRgba32());
        if (sampledColors.Count < 8)
            throw new InvalidOperationException($"Visual QA capture {name} appears blank");

        var path = Path.Combine(_visualQaDirectory, name + ".png");
        var result = image.SavePng(path);
        if (result != Error.Ok)
            throw new IOException($"Could not save visual QA capture {path}: {result}");
        _visualQaCaptures.Add(path);
        GD.Print($"AFC_VISUAL_QA_CAPTURE name={name} colors={sampledColors.Count}");
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventJoypadButton { Pressed: true } joypad)
        {
            _lastInputWasController = true;
            HandleGamepadButton(joypad.ButtonIndex);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key) return;
        _lastInputWasController = false;
        if (_commandMode)
        {
            if (key.Keycode == Key.Escape) CloseCommandLine("Command cancelled");
            return;
        }

        if (_showMultiplayer)
        {
            HandleMultiplayerInput(key.Keycode);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_showLocalAiSetup)
        {
            switch (key.Keycode)
            {
                case Key.L:
                case Key.Escape:
                    _showLocalAiSetup = false;
                    break;
                case Key.R:
                    RefreshLocalAiStatus();
                    break;
                case Key.O:
                    InstallOllamaModel();
                    break;
                case Key.W:
                    InstallWhisperModel();
                    break;
                case Key.G:
                    ToggleGpuPreference();
                    break;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_showBindings)
        {
            HandleBindingsInput(key.Keycode);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_showSettings)
        {
            HandleSettingsInput(key.Keycode);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_showMissionSelect)
        {
            if (IsActionKey(key.Keycode, GameActionIds.Missions) || key.Keycode == Key.Escape)
                _showMissionSelect = false;
            else if (key.Keycode is Key.Key1 or Key.Key2 or Key.Key3)
                LoadMission((int)key.Keycode - (int)Key.Key1);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (IsActionKey(key.Keycode, GameActionIds.Help))
        {
            _showHelp = !_showHelp;
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Command))
        {
            OpenCommandLine();
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Voice))
        {
            CaptureVoiceCommand();
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Ability))
        {
            ActivateSelectedAbility();
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Pause))
        {
            if (_multiplayer?.IsInMatch == true) SetStatus("Online battles cannot be paused");
            else _paused = !_paused;
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Restart))
        {
            Restart();
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Missions))
        {
            if (_multiplayer?.IsInMatch == true) SetStatus("Mission selection is controlled by the host lobby");
            else _showMissionSelect = true;
        }
        else if (!_showHelp && _multiplayer?.IsInMatch != true &&
                 _simulation.Status == BattleStatus.PlayerVictory &&
                 IsActionKey(key.Keycode, GameActionIds.NextMission))
        {
            LoadMission(MissionCatalog.IndexOf(_simulation.Mission.Id) + 1);
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.SwitchShip))
        {
            CyclePlayerShip();
        }
        else if (!_showHelp && key.Keycode is >= Key.Key1 and <= Key.Key4)
        {
            SelectPlayerShip((int)key.Keycode - (int)Key.Key1);
        }
        else
        {
            switch (key.Keycode)
            {
                case Key.L:
                    _showLocalAiSetup = true;
                    RefreshLocalAiStatus();
                    break;
                case Key.F10:
                    _showSettings = true;
                    break;
                case Key.F6:
                    ToggleMultiplayerOverlay();
                    break;
                case Key.F7:
                    RunBenchmark(false);
                    break;
                case Key.F8:
                    OS.ShellOpen("https://github.com/karacsonybarni/andromeda-fleet-command/issues/new/choose");
                    SetStatus("Opened the feedback form");
                    break;
                case Key.F9:
                    ValidateLatestReplay();
                    break;
                case Key.Escape:
                    GetTree().Quit();
                    break;
            }
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        DrawSpace();
        var kick = (float)_combatKick;
        _worldDrawOffset = _settings.ReduceFlashes
            ? Vector2.Zero
            : new Vector2(Mathf.Sin((float)_visualTime * 71f), Mathf.Cos((float)_visualTime * 57f)) * kick;
        DrawSetTransform(_worldDrawOffset, 0);
        DrawGrid();
        DrawOrderVisualizations();
        DrawEnemyGroupLabels();
        foreach (var projectile in _simulation.Projectiles) DrawProjectile(projectile);
        foreach (var ship in _simulation.Ships.Where(ship => ship.IsAlive)) DrawShip(ship);
        DrawTargetingOverlay();
        foreach (var combatEvent in _simulation.Events.Where(item => item.Type != CombatEventType.Order &&
                     (!_settings.ReduceFlashes || item.Type != CombatEventType.MuzzleFlash)).TakeLast(14))
            DrawCombatEvent(combatEvent);
        DrawSetTransform(Vector2.Zero, 0);
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

    private void CreateMultiplayerControls()
    {
        var canvas = new CanvasLayer { Layer = 21 };
        AddChild(canvas);
        _multiplayerName = CreateMultiplayerLineEdit("Captain name", new(515, 315), 570);
        _multiplayerName.Text = string.IsNullOrWhiteSpace(System.Environment.UserName)
            ? "Captain"
            : System.Environment.UserName;
        _multiplayerAddress = CreateMultiplayerLineEdit("Host address (example: 192.168.1.20:7777)",
            new(515, 405), 570);
        _multiplayerAddress.Text = $"127.0.0.1:{MultiplayerManager.DefaultPort}";
        canvas.AddChild(_multiplayerName);
        canvas.AddChild(_multiplayerAddress);
    }

    private LineEdit CreateMultiplayerLineEdit(string placeholder, Vector2 position, float width)
    {
        var line = new LineEdit
        {
            Visible = false,
            PlaceholderText = placeholder,
            Position = position,
            Size = new(width, 52),
            MaxLength = 80,
            CaretBlink = true
        };
        line.AddThemeFontSizeOverride("font_size", 18);
        var style = new StyleBoxFlat
        {
            BgColor = new(0.01f, 0.05f, 0.09f, 0.98f),
            BorderColor = Cyan,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 16
        };
        line.AddThemeStyleboxOverride("normal", style);
        line.AddThemeStyleboxOverride("focus", style);
        return line;
    }

    private void ToggleMultiplayerOverlay()
    {
        _showMultiplayer = !_showMultiplayer;
        RefreshMultiplayerControlVisibility();
        if (_showMultiplayer)
        {
            _showHelp = false;
            _showMissionSelect = false;
            _showSettings = false;
            _showBindings = false;
            _showLocalAiSetup = false;
            _multiplayerName?.GrabFocus();
        }
        else
        {
            _multiplayerName?.ReleaseFocus();
            _multiplayerAddress?.ReleaseFocus();
        }
    }

    private void RefreshMultiplayerControlVisibility()
    {
        var showFields = _showMultiplayer && _multiplayer?.State is null or MultiplayerSessionState.Offline;
        if (_multiplayerName is not null) _multiplayerName.Visible = showFields;
        if (_multiplayerAddress is not null) _multiplayerAddress.Visible = showFields;
    }

    private void HandleMultiplayerInput(Key key)
    {
        if (key is Key.F6 or Key.Escape)
        {
            ToggleMultiplayerOverlay();
            return;
        }
        if (_multiplayer is null) return;

        if (_multiplayer.State == MultiplayerSessionState.Offline)
        {
            LobbyActionResult? result = key switch
            {
                Key.H => _multiplayer.Host(MultiplayerMode.Cooperative, MultiplayerDisplayName()),
                Key.V => _multiplayer.Host(MultiplayerMode.Versus, MultiplayerDisplayName()),
                Key.J => JoinMultiplayer(),
                _ => null
            };
            if (result is not null) SetStatus(result.Message);
            RefreshMultiplayerControlVisibility();
            return;
        }

        if (key == Key.D)
        {
            _multiplayer.Close();
            RefreshMultiplayerControlVisibility();
            return;
        }
        if (_multiplayer.IsInMatch)
        {
            if (key == Key.R && _multiplayer.IsHost) SetStatus(_multiplayer.Rematch().Message);
            return;
        }
        if (!_multiplayer.IsHost) return;

        switch (key)
        {
            case Key.Enter:
            case Key.KpEnter:
                SetStatus(_multiplayer.StartMatch().Message);
                break;
            case Key.M:
                var next = _multiplayer.Lobby?.Mode == MultiplayerMode.Cooperative
                    ? MultiplayerMode.Versus
                    : MultiplayerMode.Cooperative;
                SetStatus(_multiplayer.SetMode(next).Message);
                break;
            case >= Key.Key1 and <= Key.Key3:
                SetStatus(_multiplayer.SetCooperativeMission(
                    MissionCatalog.All[(int)key - (int)Key.Key1].Id).Message);
                break;
        }
    }

    private LobbyActionResult JoinMultiplayer()
    {
        if (_multiplayer is null) return new(false, "Multiplayer is unavailable");
        var endpoint = (_multiplayerAddress?.Text ?? string.Empty).Trim();
        var host = endpoint;
        var port = MultiplayerManager.DefaultPort;
        var separator = endpoint.LastIndexOf(':');
        if (separator > 0 && int.TryParse(endpoint[(separator + 1)..], out var parsedPort))
        {
            host = endpoint[..separator];
            port = parsedPort;
        }
        return _multiplayer.Join(host, MultiplayerDisplayName(), port);
    }

    private string MultiplayerDisplayName()
    {
        var name = _multiplayerName?.Text;
        return string.IsNullOrWhiteSpace(name) ? "Captain" : name.Trim();
    }

    private void OnMultiplayerLobbyChanged(FleetLobbySnapshot lobby)
    {
        SetStatus($"{lobby.Mode} lobby • {lobby.Players.Count}/{lobby.MaximumPlayers} captains");
        RefreshMultiplayerControlVisibility();
        if (_multiplayerSmokeHost && !lobby.MatchStarted && lobby.Players.Count >= 2)
        {
            GD.Print($"AFC_MP_HOST_JOINED players={lobby.Players.Count}");
            var result = _multiplayer!.StartMatch();
            if (!result.Accepted) throw new InvalidOperationException(result.Message);
        }
    }

    private void OnMultiplayerMatchStarted(MatchStartMessage start)
    {
        _wasMultiplayerMatch = true;
        _simulation = new(start.Snapshot.Frame.MissionId, start.Snapshot.Frame.Seed);
        _simulation.ApplyFrame(start.Snapshot.Frame);
        _simulationTick = start.Snapshot.ServerTick;
        _observedBattleStatus = BattleStatus.Active;
        _previousProjectileCount = 0;
        _heardCombatEvents.Clear();
        _paused = false;
        _showHelp = false;
        _showMultiplayer = false;
        RefreshMultiplayerControlVisibility();
        EnsureLocalShipSelected();
        _log.Clear();
        AddLog($"MULTIPLAYER  {start.Lobby.Mode} match started.");
        AddLog("The host is authoritative; disconnected ships return to bot control.");
        SetStatus("Fleet link synchronized");
        if (_multiplayerSmokeHost || _multiplayerSmokeClient)
        {
            GD.Print($"AFC_MP_MATCH_STARTED role={(_multiplayerSmokeHost ? "host" : "client")}");
            var command = _rules.Parse("All ships, attack the enemy flagship").Command!;
            var admission = _multiplayer!.SendCommand(command, _simulation.SelectedShip.Id, _simulation);
            if (!admission.Accepted) throw new InvalidOperationException(admission.Message);
        }
    }

    private void OnMultiplayerSnapshot(AuthoritativeSnapshot snapshot)
    {
        if (_multiplayer?.IsInMatch != true) return;
        _simulation.ApplyFrame(snapshot.Frame);
        _simulationTick = snapshot.ServerTick;
        EnsureLocalShipSelected();
        if ((_multiplayerSmokeHost || _multiplayerSmokeClient) && !_multiplayerSmokePassed)
        {
            if (!_multiplayerSmokeSnapshotSeen)
            {
                _multiplayerSmokeSnapshotSeen = true;
                GD.Print($"AFC_MP_SNAPSHOT role={(_multiplayerSmokeHost ? "host" : "client")}" +
                         $" tick={snapshot.ServerTick}");
            }
            var checksum = SimulationChecksum.Compute(_simulation);
            if (!checksum.Equals(snapshot.Checksum, StringComparison.Ordinal))
                throw new InvalidOperationException("Multiplayer snapshot checksum did not recover the client state");
            var requiredTick = _multiplayerSmokeHost ? 240 : 180;
            if (snapshot.ServerTick >= requiredTick)
            {
                _multiplayerSmokePassed = true;
                var role = _multiplayerSmokeHost ? "HOST" : "CLIENT";
                GD.Print($"AFC_MP_{role}_PASS tick={snapshot.ServerTick} ships={_multiplayer!.LocalShipIds.Count}");
                GetTree().Quit();
            }
        }
    }

    private void OnMultiplayerNotice(string notice)
    {
        SetStatus(notice);
        AddLog($"NETWORK  {notice}");
    }

    private void OnMultiplayerStateChanged(MultiplayerSessionState state)
    {
        RefreshMultiplayerControlVisibility();
        if (state != MultiplayerSessionState.Offline || !_wasMultiplayerMatch) return;
        _wasMultiplayerMatch = false;
        _simulation = new(MissionId.FirstCommand);
        _simulationTick = 0;
        _observedBattleStatus = BattleStatus.Active;
        _showHelp = true;
        _paused = false;
        _log.Clear();
        AddLog("Returned to the single-player campaign.");
        StartReplayRecording();
    }

    private void EnsureLocalShipSelected()
    {
        if (_multiplayer?.IsInMatch != true) return;
        var assigned = _multiplayer.LocalShipIds;
        if (assigned.Contains(_simulation.SelectedShip.Id, StringComparer.Ordinal) &&
            _simulation.SelectedShip.IsAlive) return;
        var first = assigned.Select(_simulation.FindShip).FirstOrDefault(ship => ship is { IsAlive: true });
        if (first is not null) _simulation.SelectShip(first.Id);
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
        _replayRecorder?.RecordCommand(_simulationTick, result.Command);
        var acknowledgement = _multiplayer?.IsInMatch == true
            ? _multiplayer.SendCommand(result.Command, _simulation.SelectedShip.Id, _simulation).Message
            : _dispatcher.Dispatch(result.Command, _simulation);
        _lastIssuedCommand = text.Trim();
        _lastAcknowledgement = acknowledgement;
        _commandPulseTime = 4.5;
        PlayCue(TacticalCue.Acknowledgement, "Fleet acknowledges order");
        AdvanceTutorial(TutorialAction.IssueOrder);
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

    private void ActivateSelectedAbility()
    {
        if (_multiplayer?.IsInMatch == true)
        {
            var admission = _multiplayer.SendControl(_simulation.SelectedShip.Id, ReadManualInput(), true);
            SetStatus(admission.Accepted ? $"{_simulation.SelectedShip.Name} ability requested" : admission.Message);
            if (admission.Accepted) PlayCue(TacticalCue.Ability, "Ability request relayed");
            return;
        }
        _replayRecorder?.RecordAbility(_simulationTick);
        var cooldownBefore = _simulation.SelectedShip.AbilityCooldown;
        var abilityMessage = _simulation.TryActivateSelectedAbility();
        if (cooldownBefore <= 0 && _simulation.SelectedShip.AbilityCooldown > 0)
        {
            AdvanceTutorial(TutorialAction.ActivateAbility);
            PlayCue(TacticalCue.Ability, $"{_simulation.SelectedShip.Name} ability activated");
        }
        SetStatus(abilityMessage);
        AddLog(abilityMessage);
    }

    private void SelectPlayerShip(int index)
    {
        if (_multiplayer?.IsInMatch == true)
        {
            var ships = LocalSelectableShips();
            if (ships.Count > 0) _simulation.SelectShip(ships[Math.Clamp(index, 0, ships.Count - 1)].Id);
            return;
        }
        _replayRecorder?.RecordShipSelection(_simulationTick, index);
        _simulation.SelectPlayerShip(index);
        AdvanceTutorial(TutorialAction.SwitchShip);
    }

    private void CyclePlayerShip()
    {
        if (_multiplayer?.IsInMatch == true)
        {
            var ships = LocalSelectableShips();
            if (ships.Count == 0) return;
            var current = ships.FindIndex(ship => ship.Id == _simulation.SelectedShip.Id);
            _simulation.SelectShip(ships[(current + 1 + ships.Count) % ships.Count].Id);
            return;
        }
        _replayRecorder?.RecordShipCycle(_simulationTick);
        _simulation.CycleSelectedShip();
        AdvanceTutorial(TutorialAction.SwitchShip);
    }

    private List<Ship> LocalSelectableShips() => _multiplayer?.LocalShipIds
        .Select(_simulation.FindShip).Where(ship => ship is { IsAlive: true }).Cast<Ship>().ToList() ?? [];

    private void HandleGamepadButton(JoyButton button)
    {
        if (_commandMode)
        {
            if (button == JoyButton.B) CloseCommandLine("Command cancelled");
            return;
        }
        if (_showLocalAiSetup)
        {
            if (button is JoyButton.Back or JoyButton.B) _showLocalAiSetup = false;
            return;
        }
        if (_showBindings)
        {
            HandleGamepadBindingsInput(button);
            return;
        }
        if (_showSettings)
        {
            if (button == JoyButton.Y)
            {
                _showSettings = false;
                _showBindings = true;
                _bindingDeviceGamepad = true;
                _bindingSelection = 0;
                _captureBinding = false;
            }
            else if (button is JoyButton.Back or JoyButton.B) _showSettings = false;
            return;
        }
        if (_showMissionSelect)
        {
            if (button is JoyButton.Back or JoyButton.B or JoyButton.X) _showMissionSelect = false;
            return;
        }
        if (_showHelp)
        {
            if (button is JoyButton.A or JoyButton.Start) _showHelp = false;
            return;
        }

        if (IsGamepadButton(button, GamepadActionIds.SwitchShip))
        {
            CyclePlayerShip();
        }
        else if (IsGamepadButton(button, GamepadActionIds.Ability))
        {
            ActivateSelectedAbility();
        }
        else if (IsGamepadButton(button, GamepadActionIds.Voice))
        {
            CaptureVoiceCommand();
        }
        else if (IsGamepadButton(button, GamepadActionIds.Missions))
        {
            if (_multiplayer?.IsInMatch == true) SetStatus("Mission selection is controlled by the host lobby");
            else _showMissionSelect = true;
        }
        else if (IsGamepadButton(button, GamepadActionIds.Pause))
        {
            if (_multiplayer?.IsInMatch == true) SetStatus("Online battles cannot be paused");
            else _paused = !_paused;
        }
        else if (IsGamepadButton(button, GamepadActionIds.Restart))
        {
            Restart();
        }
        else if (_multiplayer?.IsInMatch != true && _simulation.Status == BattleStatus.PlayerVictory &&
                 IsGamepadButton(button, GamepadActionIds.NextMission))
        {
            LoadMission(MissionCatalog.IndexOf(_simulation.Mission.Id) + 1);
        }
        else if (button == JoyButton.Back)
        {
            _showSettings = true;
        }
    }

    private void HandleSettingsInput(Key key)
    {
        switch (key)
        {
            case Key.F10:
            case Key.Escape:
                _showSettings = false;
                return;
            case Key.A:
                var nextVolume = _settings.MasterVolume <= 0.01 ? 1 :
                    Math.Round(Math.Max(0, _settings.MasterVolume - 0.25), 2);
                _settings = _settings with { MasterVolume = nextVolume };
                break;
            case Key.C:
                var modes = Enum.GetValues<ColorVisionMode>();
                _settings = _settings with
                {
                    ColorMode = modes[((int)_settings.ColorMode + 1) % modes.Length]
                };
                break;
            case Key.F:
                _settings = _settings with { ReduceFlashes = !_settings.ReduceFlashes };
                break;
            case Key.U:
                _settings = _settings with { Subtitles = !_settings.Subtitles };
                break;
            case Key.D:
                var deadzone = _settings.GamepadDeadzone >= 0.4
                    ? 0.12
                    : Math.Round(_settings.GamepadDeadzone + 0.1, 2);
                _settings = _settings with { GamepadDeadzone = deadzone };
                break;
            case Key.K:
                _showSettings = false;
                _showBindings = true;
                _bindingDeviceGamepad = false;
                _bindingSelection = 0;
                _captureBinding = false;
                return;
            default:
                return;
        }
        _settings = _settings.Normalize();
        _settingsStore?.Save(_settings);
        ApplySettings();
        SetStatus("Settings saved");
    }

    private void HandleBindingsInput(Key key)
    {
        if (_bindingDeviceGamepad)
        {
            if (_captureBinding)
            {
                if (key == Key.Escape)
                {
                    _captureBinding = false;
                    SetStatus("Controller binding cancelled");
                }
                return;
            }
            switch (key)
            {
                case Key.Up:
                    MoveBindingSelection(-1, GamepadActions.All.Count);
                    break;
                case Key.Down:
                    MoveBindingSelection(1, GamepadActions.All.Count);
                    break;
                case Key.Enter:
                    _captureBinding = true;
                    break;
                case Key.Backspace:
                    var selected = GamepadActions.All[_bindingSelection];
                    _gamepadBindings = _gamepadBindings.Reset(selected.Id);
                    _gamepadBindingsStore?.Save(_gamepadBindings);
                    SetStatus($"{selected.Label} restored to {GamepadButtonLabel(selected.Id)}");
                    break;
                case Key.R:
                    _gamepadBindings = GamepadBindings.Default;
                    _gamepadBindingsStore?.Save(_gamepadBindings);
                    SetStatus("Controller bindings restored to defaults");
                    break;
                case Key.G:
                    _bindingDeviceGamepad = false;
                    _bindingSelection = 0;
                    break;
                case Key.K:
                case Key.Escape:
                    _showBindings = false;
                    _showSettings = true;
                    break;
            }
            return;
        }

        if (_captureBinding)
        {
            if (key == Key.Escape)
            {
                _captureBinding = false;
                SetStatus("Binding cancelled");
                return;
            }
            if (IsReservedBindingKey(key))
            {
                SetStatus("That key is reserved for menus, ship selection, or diagnostics");
                return;
            }

            var action = GameActions.All[_bindingSelection];
            _bindings = _bindings.Rebind(action.Id, key.ToString());
            _bindingsStore?.Save(_bindings);
            _captureBinding = false;
            SetStatus($"{action.Label} bound to {BindingLabel(action.Id)}");
            return;
        }

        switch (key)
        {
            case Key.Up:
                MoveBindingSelection(-1, GameActions.All.Count);
                break;
            case Key.Down:
                MoveBindingSelection(1, GameActions.All.Count);
                break;
            case Key.Enter:
                _captureBinding = true;
                break;
            case Key.Backspace:
                var selected = GameActions.All[_bindingSelection];
                _bindings = _bindings.Reset(selected.Id);
                _bindingsStore?.Save(_bindings);
                SetStatus($"{selected.Label} restored to {BindingLabel(selected.Id)}");
                break;
            case Key.R:
                _bindings = InputBindings.Default;
                _bindingsStore?.Save(_bindings);
                SetStatus("Keyboard bindings restored to defaults");
                break;
            case Key.G:
                _bindingDeviceGamepad = true;
                _bindingSelection = 0;
                break;
            case Key.K:
            case Key.Escape:
                _showBindings = false;
                _showSettings = true;
                break;
        }
    }

    private void HandleGamepadBindingsInput(JoyButton button)
    {
        if (!_bindingDeviceGamepad)
        {
            if (button == JoyButton.LeftShoulder)
            {
                _bindingDeviceGamepad = true;
                _bindingSelection = 0;
            }
            else if (button is JoyButton.Back or JoyButton.B)
            {
                _captureBinding = false;
                _showBindings = false;
                _showSettings = true;
            }
            return;
        }

        if (_captureBinding)
        {
            if (button == JoyButton.Back)
            {
                _captureBinding = false;
                SetStatus("Controller binding cancelled");
                return;
            }
            var action = GamepadActions.All[_bindingSelection];
            _gamepadBindings = _gamepadBindings.Rebind(action.Id, button.ToString());
            _gamepadBindingsStore?.Save(_gamepadBindings);
            _captureBinding = false;
            SetStatus($"{action.Label} bound to {GamepadButtonLabel(action.Id)}");
            return;
        }

        switch (button)
        {
            case JoyButton.DpadUp:
                MoveBindingSelection(-1, GamepadActions.All.Count);
                break;
            case JoyButton.DpadDown:
                MoveBindingSelection(1, GamepadActions.All.Count);
                break;
            case JoyButton.A:
                _captureBinding = true;
                break;
            case JoyButton.X:
                var selected = GamepadActions.All[_bindingSelection];
                _gamepadBindings = _gamepadBindings.Reset(selected.Id);
                _gamepadBindingsStore?.Save(_gamepadBindings);
                SetStatus($"{selected.Label} restored to {GamepadButtonLabel(selected.Id)}");
                break;
            case JoyButton.Y:
                _gamepadBindings = GamepadBindings.Default;
                _gamepadBindingsStore?.Save(_gamepadBindings);
                SetStatus("Controller bindings restored to defaults");
                break;
            case JoyButton.LeftShoulder:
                _bindingDeviceGamepad = false;
                _bindingSelection = 0;
                break;
            case JoyButton.Back:
            case JoyButton.B:
                _showBindings = false;
                _showSettings = true;
                break;
        }
    }

    private void MoveBindingSelection(int delta, int count) =>
        _bindingSelection = (_bindingSelection + delta + count) % count;

    private Key BoundKey(string actionId)
    {
        var configured = _bindings.Get(actionId);
        if (Enum.TryParse<Key>(configured, true, out var key) && key != Key.None) return key;
        var fallback = GameActions.Find(actionId)?.DefaultKey ?? nameof(Key.None);
        return Enum.TryParse<Key>(fallback, true, out key) ? key : Key.None;
    }

    private bool IsActionPressed(string actionId) => Input.IsKeyPressed(BoundKey(actionId));

    private bool IsActionKey(Key key, string actionId) => key == BoundKey(actionId);

    private JoyButton BoundButton(string actionId)
    {
        var configured = _gamepadBindings.Get(actionId);
        if (Enum.TryParse<JoyButton>(configured, true, out var button)) return button;
        var fallback = GamepadActions.Find(actionId)?.DefaultButton ?? nameof(JoyButton.A);
        return Enum.TryParse<JoyButton>(fallback, true, out button) ? button : JoyButton.A;
    }

    private bool IsGamepadButton(JoyButton button, string actionId) => button == BoundButton(actionId);

    private string GamepadButtonLabel(string actionId) => ButtonLabel(BoundButton(actionId));

    private string BindingLabel(string actionId) => KeyLabel(BoundKey(actionId));

    private static string KeyLabel(Key key) => key switch
    {
        Key.Space => "SPACE",
        Key.Enter => "ENTER",
        Key.Tab => "TAB",
        Key.Backspace => "BACKSPACE",
        _ => key.ToString().ToUpperInvariant()
    };

    private static string ButtonLabel(JoyButton button) => button switch
    {
        JoyButton.LeftShoulder => "LB",
        JoyButton.RightShoulder => "RB",
        JoyButton.LeftStick => "L3",
        JoyButton.RightStick => "R3",
        JoyButton.DpadUp => "D-PAD ↑",
        JoyButton.DpadDown => "D-PAD ↓",
        JoyButton.DpadLeft => "D-PAD ←",
        JoyButton.DpadRight => "D-PAD →",
        _ => button.ToString().ToUpperInvariant()
    };

    private static bool IsReservedBindingKey(Key key) => key is Key.None or Key.Escape or Key.L or
        Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4;

    private void ApplySettings()
    {
        var master = AudioServer.GetBusIndex("Master");
        if (master < 0) return;
        AudioServer.SetBusMute(master, _settings.MasterVolume <= 0.001);
        if (_settings.MasterVolume > 0.001)
            AudioServer.SetBusVolumeDb(master, Mathf.LinearToDb((float)_settings.MasterVolume));
    }

    private void PlayCue(TacticalCue cue, string caption)
    {
        _audio?.Play(cue);
        if (!_settings.Subtitles) return;
        _audioCaption = caption;
        _audioCaptionTime = 2.2;
    }

    private void ObserveCombatAudio()
    {
        _heardCombatEvents.RemoveWhere(item => !_simulation.Events.Contains(item));
        foreach (var combatEvent in _simulation.Events)
        {
            if (!_heardCombatEvents.Add(combatEvent)) continue;
            switch (combatEvent.Type)
            {
                case CombatEventType.Impact:
                    _audio?.Play(TacticalCue.Impact);
                    _combatKick = Math.Max(_combatKick, 2.4);
                    break;
                case CombatEventType.Destroyed:
                    PlayCue(TacticalCue.Destruction, combatEvent.Message ?? "Ship destroyed");
                    _combatKick = Math.Max(_combatKick, 6.5);
                    break;
            }
        }
    }

    private void StartReplayRecording()
    {
        _simulationTick = 0;
        _replayRecorder = new(_simulation.Mission.Id, _simulation.Seed);
    }

    private void SaveCompletedReplay()
    {
        if (_replayRecorder is null || _replayStore is null) return;
        var replay = _replayRecorder.Complete(_simulationTick, _simulation);
        var path = _replayStore.Save(replay);
        var valid = ReplayRunner.Validate(replay);
        AddLog(valid ? "Replay verified and saved." : "Replay desynchronization detected.");
        if (!valid) _crashReports?.WriteReport(new InvalidOperationException(
            "Recorded replay did not reproduce its final checksum"), $"Replay validation: {path}");
    }

    private void ValidateLatestReplay()
    {
        var replay = _replayStore?.LoadLatest();
        if (replay is null)
        {
            SetStatus("No saved replay is available yet");
            return;
        }
        var valid = ReplayRunner.Validate(replay);
        SetStatus(valid ? "Latest replay verified exactly" : "Replay desynchronization detected");
        AddLog(valid ? "REPLAY  deterministic checksum passed" : "REPLAY  checksum failed");
    }

    private void RunBenchmark(bool quitWhenComplete)
    {
        const int ticksPerMission = 60 * 180;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var ticks = 0;
        foreach (var mission in MissionCatalog.All)
        {
            var simulation = new BattleSimulation(mission.Id);
            var command = _rules.Parse("All ships, attack the nearest enemy").Command!;
            _dispatcher.Dispatch(command, simulation);
            for (var index = 0; index < ticksPerMission; index++)
            {
                if (simulation.Status != BattleStatus.Active)
                {
                    simulation.Reset();
                    _dispatcher.Dispatch(command, simulation);
                }
                simulation.Update(BattleSimulation.FixedStep);
                ticks++;
            }
        }
        stopwatch.Stop();
        var ticksPerSecond = ticks / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        var report = $"UTC={DateTime.UtcNow:O}\nTicks={ticks}\nSeconds={stopwatch.Elapsed.TotalSeconds:F4}\nTicksPerSecond={ticksPerSecond:F0}\n";
        var directory = ProjectSettings.GlobalizePath("user://benchmarks");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"benchmark-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt"), report);
        GD.Print($"AFC_BENCHMARK_PASS ticks={ticks} ticks_per_second={ticksPerSecond:F0}");
        SetStatus($"Benchmark: {ticksPerSecond:F0} simulation ticks/s");
        if (quitWhenComplete) GetTree().Quit();
    }

    private void RebuildLocalAiAdapters()
    {
        _voiceInput?.Dispose();
        _interpreter?.Dispose();
        _interpreter = new(_rules, _localAiConfiguration);
        _voiceInput = new(this, _localAiConfiguration.WhisperCli, _localAiConfiguration.WhisperModel);
    }

    private async void RefreshLocalAiStatus()
    {
        if (_localAiSetup is null || _localAiBusy) return;
        _localAiBusy = true;
        _localAiReadiness = _localAiReadiness with { Detail = "Scanning local services…" };
        QueueRedraw();
        try
        {
            var discoveredCli = LocalAiSetupService.FindWhisperCli();
            if ((_localAiConfiguration.WhisperCli is null ||
                 !File.Exists(_localAiConfiguration.WhisperCli)) && discoveredCli is not null)
            {
                _localAiConfiguration = _localAiConfiguration with { WhisperCli = discoveredCli };
                _localAiStore?.Save(_localAiConfiguration);
                RebuildLocalAiAdapters();
            }
            _localAiReadiness = await _localAiSetup.CheckAsync(_localAiConfiguration);
        }
        catch (Exception error)
        {
            _localAiReadiness = _localAiReadiness with { Detail = $"Readiness scan failed: {error.Message}" };
        }
        finally
        {
            _localAiBusy = false;
            QueueRedraw();
        }
    }

    private async void InstallOllamaModel()
    {
        if (_localAiSetup is null || _localAiBusy) return;
        _localAiBusy = true;
        _localAiReadiness = _localAiReadiness with
        {
            Detail = $"Pulling {_localAiConfiguration.OllamaModel} from local Ollama…"
        };
        QueueRedraw();
        try
        {
            await _localAiSetup.PullOllamaModelAsync(_localAiConfiguration);
            _localAiConfiguration = _localAiConfiguration with { OllamaEnabled = true };
            _localAiStore?.Save(_localAiConfiguration);
            RebuildLocalAiAdapters();
            _localAiReadiness = await _localAiSetup.CheckAsync(_localAiConfiguration);
            SetStatus("Local command model ready");
        }
        catch (Exception error)
        {
            _localAiReadiness = _localAiReadiness with
            {
                Detail = $"Ollama setup failed: {error.Message}. Start Ollama and retry."
            };
        }
        finally
        {
            _localAiBusy = false;
            QueueRedraw();
        }
    }

    private async void InstallWhisperModel()
    {
        if (_localAiSetup is null || _localAiBusy) return;
        _localAiBusy = true;
        _localAiReadiness = _localAiReadiness with { Detail = "Downloading the local speech model…" };
        QueueRedraw();
        try
        {
            var destination = ProjectSettings.GlobalizePath(
                $"user://models/{LocalAiSetupService.WhisperModelFileName}");
            await _localAiSetup.DownloadWhisperModelAsync(destination);
            _localAiConfiguration = _localAiConfiguration with
            {
                WhisperCli = _localAiConfiguration.WhisperCli is { } cli && File.Exists(cli)
                    ? cli
                    : LocalAiSetupService.FindWhisperCli(),
                WhisperModel = destination
            };
            _localAiStore?.Save(_localAiConfiguration);
            RebuildLocalAiAdapters();
            _localAiReadiness = await _localAiSetup.CheckAsync(_localAiConfiguration);
            SetStatus(_localAiReadiness.VoiceReady
                ? "Local voice control ready"
                : "Speech model installed; whisper-cli was not found");
        }
        catch (Exception error)
        {
            _localAiReadiness = _localAiReadiness with { Detail = $"Speech setup failed: {error.Message}" };
        }
        finally
        {
            _localAiBusy = false;
            QueueRedraw();
        }
    }

    private void ToggleGpuPreference()
    {
        _localAiConfiguration = _localAiConfiguration with
        {
            PreferGpu = _localAiConfiguration.PreferGpu == false
        };
        _localAiStore?.Save(_localAiConfiguration);
        RebuildLocalAiAdapters();
        var mode = _localAiConfiguration.PreferGpu == false
            ? "CPU-only inference selected"
            : "GPU acceleration preferred with CPU fallback";
        _localAiReadiness = _localAiReadiness with { Detail = mode };
        SetStatus(mode);
    }

    private void Restart()
    {
        if (_multiplayer?.IsInMatch == true)
        {
            SetStatus(_multiplayer.IsHost
                ? _multiplayer.Rematch().Message
                : "Only the host can start a rematch");
            return;
        }
        _simulation.Reset();
        _log.Clear();
        AddLog($"Mission restarted: {_simulation.Mission.Title}.");
        AddLog(_simulation.Mission.Objective.Title + ".");
        _paused = false;
        _observedBattleStatus = BattleStatus.Active;
        _previousProjectileCount = 0;
        _heardCombatEvents.Clear();
        StartReplayRecording();
        if (_simulation.Mission.Id == MissionId.FirstCommand) _tutorial = new();
        _tutorialCelebrationTime = 0;
        _tutorialStepFlash = 0;
        SetStatus("Fleet ready");
    }

    private void LoadMission(int missionIndex)
    {
        if (_multiplayer?.IsInMatch == true)
        {
            SetStatus("Return to the lobby before selecting a campaign mission");
            return;
        }
        if (missionIndex < 0 || missionIndex >= MissionCatalog.All.Count)
        {
            SetStatus("Campaign complete");
            return;
        }
        if (!_progress.IsUnlocked(missionIndex))
        {
            SetStatus("That mission is still locked");
            return;
        }

        var mission = MissionCatalog.All[missionIndex];
        _simulation.LoadMission(mission.Id);
        _tutorial = new();
        _tutorialCelebrationTime = 0;
        _tutorialStepFlash = 0;
        _observedBattleStatus = BattleStatus.Active;
        _previousProjectileCount = 0;
        _heardCombatEvents.Clear();
        StartReplayRecording();
        _log.Clear();
        AddLog($"Mission {missionIndex + 1}: {mission.Title}");
        AddLog(mission.Objective.Title + ".");
        _showMissionSelect = false;
        _showHelp = true;
        _paused = false;
        SetStatus(mission.Subtitle);
    }

    private void ObserveBattleStatus()
    {
        if (_simulation.Status == _observedBattleStatus) return;
        _observedBattleStatus = _simulation.Status;
        if (_multiplayer?.IsInMatch == true)
        {
            var won = LocalPlayerWon();
            PlayCue(won ? TacticalCue.Victory : TacticalCue.Defeat,
                won ? "Your fleet won the battle" : "Your fleet was defeated");
            AddLog(won ? "MULTIPLAYER  Victory secured." : "MULTIPLAYER  Your fleet was defeated.");
            return;
        }
        SaveCompletedReplay();
        if (_simulation.Status == BattleStatus.EnemyVictory)
        {
            PlayCue(TacticalCue.Defeat, "Mission failed: protected ship lost");
            return;
        }
        if (_simulation.Status != BattleStatus.PlayerVictory) return;

        _progress = _progress.Complete(_simulation.Mission.Id);
        _progressStore?.Save(_progress);
        _platform?.UnlockAchievement(_simulation.Mission.Id switch
        {
            MissionId.FirstCommand => "ACH_FIRST_COMMAND",
            MissionId.BrokenShield => "ACH_BROKEN_SHIELD",
            MissionId.BlackSun => "ACH_BLACK_SUN",
            _ => "ACH_MISSION_COMPLETE"
        });
        if (_progress.CompletedMissions.Count == MissionCatalog.All.Count)
            _platform?.UnlockAchievement("ACH_CAMPAIGN_COMPLETE");
        PlayCue(TacticalCue.Victory, "Mission accomplished");
        var current = MissionCatalog.IndexOf(_simulation.Mission.Id);
        AddLog(current + 1 < MissionCatalog.All.Count
            ? $"Mission complete. Mission {current + 2} unlocked."
            : "Campaign demo complete.");
    }

    private bool LocalPlayerWon()
    {
        var team = _multiplayer?.LocalTeam ?? Team.Player;
        return team == Team.Player
            ? _simulation.Status == BattleStatus.PlayerVictory
            : _simulation.Status == BattleStatus.EnemyVictory;
    }

    private void AdvanceTutorial(TutorialAction action)
    {
        if (_simulation.Mission.Id != MissionId.FirstCommand || _tutorial.IsComplete) return;
        var completed = _tutorial.CurrentStep!;
        if (!_tutorial.Notify(action)) return;

        _tutorialStepFlash = 1.1;
        PlayCue(TacticalCue.Acknowledgement, $"Training step complete: {completed.Title}");
        if (_tutorial.IsComplete)
        {
            _tutorialCelebrationTime = 4.5;
            SetStatus("CAPTAIN CERTIFIED • Fleet command is yours");
            AddLog("CAPTAIN'S DRILL COMPLETE • Destroy the raider leader.");
            return;
        }

        SetStatus($"{completed.Title.ToUpperInvariant()} COMPLETE • {_tutorial.CurrentStep!.Title}");
        AddLog(TutorialPrompt());
    }

    private string TutorialPrompt()
    {
        if (_tutorial.IsComplete) return _tutorial.GetPrompt(_lastInputWasController);
        if (_lastInputWasController)
        {
            return _tutorial.CurrentStep!.Action switch
            {
                TutorialAction.SwitchShip =>
                    $"Press {GamepadButtonLabel(GamepadActionIds.SwitchShip)} to take another helm",
                TutorialAction.ManualControl =>
                    $"Fly with the left stick; fire with {GamepadButtonLabel(GamepadActionIds.Fire)}",
                TutorialAction.IssueOrder =>
                    $"Press {GamepadButtonLabel(GamepadActionIds.Voice)} and speak a fleet order",
                TutorialAction.ActivateAbility =>
                    $"Press {GamepadButtonLabel(GamepadActionIds.Ability)} for this ship’s tactical ability",
                _ => _tutorial.GetPrompt(true)
            };
        }
        return _tutorial.CurrentStep!.Action switch
        {
            TutorialAction.SwitchShip =>
                $"Press {BindingLabel(GameActionIds.SwitchShip)} or 1–4 to take another helm",
            TutorialAction.ManualControl =>
                $"Fly with {BindingLabel(GameActionIds.Thrust)}/{BindingLabel(GameActionIds.Reverse)} and " +
                $"{BindingLabel(GameActionIds.TurnLeft)}/{BindingLabel(GameActionIds.TurnRight)}; " +
                $"fire with {BindingLabel(GameActionIds.Fire)}",
            TutorialAction.IssueOrder =>
                $"Press {BindingLabel(GameActionIds.Command)}, type an order, then confirm it",
            TutorialAction.ActivateAbility =>
                $"Press {BindingLabel(GameActionIds.Ability)} to activate this ship’s tactical ability",
            _ => _tutorial.CurrentPrompt
        };
    }

    private void DrawSpace()
    {
        DrawRect(new(0, 0, 1600, 900), new Color("020915"));
        var breath = 0.5f + 0.5f * Mathf.Sin((float)_visualTime * 0.18f);
        DrawNebulaCloud(new(1220, 175), 540, new Color(0.06f, 0.3f, 0.52f, 0.065f + breath * 0.018f));
        DrawNebulaCloud(new(1390, 390), 380, new Color(0.38f, 0.08f, 0.36f, 0.045f));
        DrawNebulaCloud(new(790, 285), 260, new Color(0.06f, 0.35f, 0.4f, 0.032f));
        foreach (var star in _stars)
        {
            var x = (star.Position.X - (float)_visualTime * star.Depth * 2.4f) % 1600f;
            if (x < 0) x += 1600f;
            var y = star.Position.Y + Mathf.Sin((float)_visualTime * (0.35f + star.Depth * 0.08f) +
                star.Phase) * star.Depth;
            var twinkle = 0.78f + 0.22f * Mathf.Sin((float)_visualTime * (0.8f + star.Depth * 0.14f) +
                star.Phase);
            var color = new Color(star.Blue ? 0.58f : 1f, star.Blue ? 0.82f : 0.9f, 1f,
                star.Alpha * twinkle);
            DrawCircle(new(x, y), star.Size, color);
            if (star.Size > 2.35f)
                DrawLine(new(x - star.Size * 2.2f, y), new(x + star.Size * 2.2f, y),
                    new(color, color.A * 0.34f), 1);
        }
        DrawDistantFleet();
        DrawPlanet();
        DrawRect(new(0, 74, 1600, 28), new Color(0, 0, 0, 0.18f));
        DrawRect(new(0, 850, 1600, 50), new Color(0, 0, 0, 0.32f));
    }

    private void DrawNebulaCloud(Vector2 center, float radius, Color color)
    {
        for (var layer = 4; layer >= 1; layer--)
        {
            var phase = (float)_visualTime * 0.012f + layer * 1.73f;
            var offset = new Vector2(Mathf.Cos(phase) * radius * 0.09f,
                Mathf.Sin(phase * 1.31f) * radius * 0.07f);
            var layerColor = new Color(color, color.A * (0.25f + layer * 0.11f));
            DrawCircle(center + offset, radius * (0.46f + layer * 0.135f), layerColor);
        }
    }

    private void DrawDistantFleet()
    {
        for (var index = 0; index < 12; index++)
        {
            var x = 390 + ((index * 137) % 1070);
            var y = 105 + ((index * 83) % 515);
            var drift = Mathf.Sin((float)_visualTime * 0.21f + index * 0.77f) * 5;
            var position = new Vector2(x + drift, y);
            var friendly = index % 3 != 0;
            var color = friendly ? new Color(Cyan, 0.2f) : new Color(Red, 0.16f);
            var size = 3.5f + index % 4;
            DrawLine(position - new Vector2(size * 2.4f, 0), position + new Vector2(size * 1.5f, 0),
                color, 1.2f, true);
            DrawLine(position - new Vector2(size * 1.2f, size * 0.55f),
                position + new Vector2(size * 1.5f, 0), color, 1, true);
            DrawCircle(position - new Vector2(size * 2.5f, 0), 1.5f,
                new Color(friendly ? Cyan : Orange, 0.32f));
        }
    }

    private void DrawPlanet()
    {
        var center = new Vector2(205, 1035);
        const float radius = 708;
        DrawCircle(center, radius + 24, new Color(0.04f, 0.38f, 0.72f, 0.055f));
        DrawCircle(center, radius + 12, new Color(0.05f, 0.5f, 0.88f, 0.09f));
