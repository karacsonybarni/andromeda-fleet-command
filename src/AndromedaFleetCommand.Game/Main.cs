using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Configuration;
using AndromedaFleetCommand.Core.Model;
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

    private readonly BattleSimulation _simulation = new(MissionId.FirstCommand);
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
    private int _visualQaStage;
    private int _visualQaFrames;
    private string _visualQaDirectory = string.Empty;
    private readonly List<string> _visualQaCaptures = [];

    public override void _Ready()
    {
        var commandArguments = OS.GetCmdlineUserArgs();
        _smokeTest = commandArguments.Contains("--smoke-test", StringComparer.Ordinal);
        _visualQa = commandArguments.Contains("--visual-qa", StringComparer.Ordinal);
        var benchmarkMode = commandArguments.Contains("--benchmark", StringComparer.Ordinal);
        _settingsStore = new(ProjectSettings.GlobalizePath("user://settings.json"));
        _settings = _settingsStore.Load();
        _bindingsStore = new(ProjectSettings.GlobalizePath("user://input-bindings.json"));
        _bindings = _bindingsStore.Load();
        _gamepadBindingsStore = new(ProjectSettings.GlobalizePath("user://gamepad-bindings.json"));
        _gamepadBindings = _gamepadBindingsStore.Load();
        _status = $"Press {BindingLabel(GameActionIds.Help)} when ready";
        _crashReports = new(ProjectSettings.GlobalizePath("user://crashes"));
        _replayStore = new(ProjectSettings.GlobalizePath("user://replays"));
        ApplySettings();
        _localAiStore = new(ProjectSettings.GlobalizePath("user://local-ai.json"));
        _localAiConfiguration = LocalAiConfiguration.ApplyEnvironment(_localAiStore.Load());
        _localAiSetup = new();
        RebuildLocalAiAdapters();
        _audio = new(this);
        if (!_smokeTest && !_visualQa && !benchmarkMode) _audio.StartAmbient();
        _platform = PlatformServicesFactory.Create();
        _progressStore = new(ProjectSettings.GlobalizePath("user://campaign-progress.json"));
        _progress = _progressStore.Load();
        CreateStars();
        LoadShipArt();
        CreateCommandLine();
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

    public override void _ExitTree()
    {
        _voiceInput?.Dispose();
        _interpreter?.Dispose();
        _localAiSetup?.Dispose();
        _crashReports?.Dispose();
        _audio?.StopAmbient();
        _platform?.Dispose();
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

        if (!_paused && !_showHelp && !_commandMode && !_showSettings && !_showBindings &&
            !_showMissionSelect && !_showLocalAiSetup && _simulation.Status == BattleStatus.Active)
        {
            _accumulator += Math.Min(delta, 0.2);
            var joyX = Input.GetJoyAxis(0, JoyAxis.LeftX);
            var joyY = Input.GetJoyAxis(0, JoyAxis.LeftY);
            var deadzone = (float)_settings.GamepadDeadzone;
            var manualInput = new ManualInput(
                IsActionPressed(GameActionIds.Thrust) || joyY < -deadzone,
                IsActionPressed(GameActionIds.Reverse) || joyY > deadzone,
                IsActionPressed(GameActionIds.TurnLeft) || joyX < -deadzone,
                IsActionPressed(GameActionIds.TurnRight) || joyX > deadzone,
                IsActionPressed(GameActionIds.Fire) ||
                Input.IsJoyButtonPressed(0, BoundButton(GamepadActionIds.Fire)));
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
            _simulation.SetManualInput(ManualInput.None);
            _accumulator = 0;
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
            _paused = !_paused;
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Restart))
        {
            Restart();
        }
        else if (!_showHelp && IsActionKey(key.Keycode, GameActionIds.Missions))
        {
            _showMissionSelect = true;
        }
        else if (!_showHelp && _simulation.Status == BattleStatus.PlayerVictory &&
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
        foreach (var projectile in _simulation.Projectiles) DrawProjectile(projectile);
        foreach (var ship in _simulation.Ships.Where(ship => ship.IsAlive)) DrawShip(ship);
        DrawTargetingOverlay();
        foreach (var combatEvent in _simulation.Events.Where(item => item.Type != CombatEventType.Order &&
                     (!_settings.ReduceFlashes || item.Type != CombatEventType.MuzzleFlash)))
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
        var acknowledgement = _dispatcher.Dispatch(result.Command, _simulation);
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
        _replayRecorder?.RecordShipSelection(_simulationTick, index);
        _simulation.SelectPlayerShip(index);
        AdvanceTutorial(TutorialAction.SwitchShip);
    }

    private void CyclePlayerShip()
    {
        _replayRecorder?.RecordShipCycle(_simulationTick);
        _simulation.CycleSelectedShip();
        AdvanceTutorial(TutorialAction.SwitchShip);
    }

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
            _showMissionSelect = true;
        }
        else if (IsGamepadButton(button, GamepadActionIds.Pause))
        {
            _paused = !_paused;
        }
        else if (IsGamepadButton(button, GamepadActionIds.Restart))
        {
            Restart();
        }
        else if (_simulation.Status == BattleStatus.PlayerVictory &&
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
        for (var layer = 6; layer >= 1; layer--)
        {
            var phase = (float)_visualTime * 0.012f + layer * 1.73f;
            var offset = new Vector2(Mathf.Cos(phase) * radius * 0.09f,
                Mathf.Sin(phase * 1.31f) * radius * 0.07f);
            var layerColor = new Color(color, color.A * (0.25f + layer * 0.11f));
            DrawCircle(center + offset, radius * (0.35f + layer * 0.105f), layerColor);
        }
    }

    private void DrawDistantFleet()
    {
        for (var index = 0; index < 18; index++)
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
        DrawCircle(center, radius, new Color("092846"));
        DrawCircle(center + new Vector2(-105, 65), radius * 0.82f, new Color("0c3c63"));
        DrawCircle(center + new Vector2(-260, 40), radius * 0.58f, new Color("105078"));
        for (var band = 0; band < 6; band++)
        {
            var angle = 3.61f + band * 0.31f;
            DrawArc(center + new Vector2(-30 + band * 9, 12 - band * 7), radius - 44 - band * 18,
                angle, angle + 0.72f, 42, new Color(0.23f, 0.68f, 0.82f, 0.08f + band * 0.012f),
                9 - band * 0.8f, true);
        }
        DrawCircle(center + new Vector2(410, -245), radius * 0.92f, new Color(0.005f, 0.012f, 0.028f, 0.57f));
        DrawArc(center, radius - 15, 3.54f, 5.88f, 120, new Color(0.08f, 0.42f, 0.72f, 0.3f), 20);
        DrawArc(center, radius, 3.54f, 5.88f, 120, new Color(0.27f, 0.8f, 1f, 0.78f), 5);
    }

    private void DrawGrid()
    {
        var color = new Color(0.16f, 0.58f, 0.78f, 0.055f);
        for (var x = 100; x < 1600; x += 100) DrawLine(new(x, 74), new(x, 838), color);
        for (var y = 100; y < 850; y += 100) DrawLine(new(0, y), new(1600, y), color);
        DrawArc(new(800, 450), 210, 0, Mathf.Tau, 96, new(0.2f, 0.75f, 1f, 0.12f));
        DrawArc(new(800, 450), 335, 0, Mathf.Tau, 96, new(0.2f, 0.75f, 1f, 0.1f));
        var sweepAngle = (float)_visualTime * 0.22f;
        var sweep = new Vector2(Mathf.Cos(sweepAngle), Mathf.Sin(sweepAngle));
        DrawLine(new(800, 450), new Vector2(800, 450) + sweep * 335,
            new Color(Cyan, 0.08f), 2);
        DrawArc(new(800, 450), 335, sweepAngle - 0.12f, sweepAngle, 10,
            new Color(Cyan, 0.16f), 4);
    }

    private void DrawOrderVisualizations()
    {
        foreach (var ship in _simulation.Ships.Where(candidate =>
                     candidate.IsAlive && candidate.Team == Team.Player))
        {
            var start = ToVector(ship.Position);
            Vector2? destination = null;
            if (ship.Order.TargetId is { } targetId && _simulation.FindShip(targetId) is { IsAlive: true } target)
                destination = ToVector(target.Position);
            else if (ship.Order.Destination is { } orderedPosition)
                destination = ToVector(orderedPosition);
            if (destination is not { } end || start.DistanceTo(end) < 45) continue;

            var selected = ship.Id == _simulation.SelectedShip.Id;
            var orderColor = ship.Order.Type switch
            {
                OrderType.Defend => new Color("4be6a3"),
                OrderType.Intercept => new Color("ffd065"),
                OrderType.Retreat => Orange,
                _ => Cyan
            };
            DrawDashedLine(start, end, new Color(orderColor, selected ? 0.42f : 0.17f),
                selected ? 18 : 14, selected ? 2.2f : 1.2f);
            var direction = (end - start).Normalized();
            var tangent = new Vector2(-direction.Y, direction.X);
            for (var marker = 1; marker <= 3; marker++)
            {
                var center = start.Lerp(end, marker / 4f);
                DrawLine(center - direction * 7 + tangent * 5, center, new Color(orderColor, 0.54f), 1.6f);
                DrawLine(center - direction * 7 - tangent * 5, center, new Color(orderColor, 0.54f), 1.6f);
            }
            if (!selected) continue;
            var labelPosition = start.Lerp(end, 0.52f);
            DrawCenteredLabel(ship.Order.Type.ToString().ToUpperInvariant(), labelPosition.X,
                labelPosition.Y - 11, 9, new Color(orderColor, 0.78f), 100);
        }
    }

    private void DrawProjectile(Projectile projectile)
    {
        var color = projectile.Team == Team.Player ? Cyan : Orange;
        var head = ToVector(projectile.Position);
        var tail = ToVector(projectile.Position - projectile.Velocity.Normalized * 22);
        var pulse = 0.72f + Mathf.Sin((float)_visualTime * 26f + head.X * 0.03f) * 0.18f;
        DrawLine(tail, head, new(color, 0.18f), 12, true);
        DrawLine(tail, head, new(color, pulse), 3.2f, true);
        DrawCircle(head, 7, new Color(color, 0.13f));
        DrawCircle(head, 2.4f, Colors.White);
    }

    private void DrawShip(Ship ship)
    {
        var position = ToVector(ship.Position);
        var teamColor = ship.Team == Team.Player ? Cyan : Red;
        var selected = ship.Id == _simulation.SelectedShip.Id;
        var visualScale = ship.Class switch
        {
            ShipClass.Flagship => 1.32f,
            ShipClass.Carrier => 1.24f,
            ShipClass.Destroyer => 1.14f,
            _ => 1f
        };
        var visualRadius = (float)ship.Stats.Radius * visualScale;
        DrawCircle(position, visualRadius * 2.15f, new Color(teamColor, selected ? 0.075f : 0.035f));
        DrawCircle(position + new Vector2(7, 10), visualRadius * 1.24f,
            new Color(0, 0, 0, 0.32f));
        DrawSetTransform(position, (float)ship.Angle);
        if (ship.Velocity.Length > 8)
        {
            var thrust = Mathf.Clamp((float)(ship.Velocity.Length / ship.EffectiveMaxSpeed), 0.2f, 1);
            var flicker = 0.82f + Mathf.Sin((float)_visualTime * 21f + position.X * 0.04f) * 0.16f;
            var engineSpacing = visualRadius * (ship.Class is ShipClass.Flagship or ShipClass.Carrier ? 0.34f : 0.18f);
            DrawColoredPolygon(new Vector2[]
            {
                new(-visualRadius * 1.16f, -10),
                new(-visualRadius * 1.38f - 48 * thrust * flicker, 0),
                new(-visualRadius * 1.16f, 10)
            }, new Color(teamColor, 0.12f));
            foreach (var engineY in new[] { -engineSpacing, engineSpacing })
            {
                DrawLine(new(-visualRadius * 1.35f, engineY),
                    new(-visualRadius * 1.35f - 38 * thrust, engineY),
                    new(teamColor, 0.28f), 13, true);
                DrawLine(new(-visualRadius * 1.35f, engineY),
                    new(-visualRadius * 1.35f - 32 * thrust * flicker, engineY),
                    new(teamColor, 0.94f), 3.4f, true);
                DrawCircle(new(-visualRadius * 1.34f, engineY), 3.5f, Colors.White);
            }
        }
        if (_shipTextures.TryGetValue(ship.Class, out var texture))
        {
            var rect = new Rect2(-visualRadius * 1.82f, -visualRadius * 0.94f,
                visualRadius * 3.64f, visualRadius * 1.88f);
            DrawTextureRect(texture, rect.Grow(4), false, new Color(teamColor, 0.24f));
            DrawTextureRect(texture, rect, false, new Color(0.82f, 0.9f, 0.96f, 0.98f));
            DrawLine(new(-visualRadius * 0.72f, 0), new(visualRadius * 0.82f, 0),
                new Color(teamColor, 0.5f), 1.4f, true);
            for (var light = -1; light <= 1; light++)
                DrawCircle(new(visualRadius * (0.25f + light * 0.32f), -visualRadius * 0.28f),
                    light == 0 ? 2.2f : 1.5f, new Color(teamColor, 0.92f));
        }
        else
        {
            var hull = CreateHull(ship);
            DrawColoredPolygon(hull, new Color("17273b"));
            DrawPolyline(hull.Append(hull[0]).ToArray(), selected ? Colors.White : teamColor,
                selected ? 3 : 1.7f, true);
        }
        DrawSetTransform(_worldDrawOffset, 0);

        if (selected)
        {
            var radius = visualRadius + 19;
            var rotation = (float)_visualTime * 0.72f;
            for (var segment = 0; segment < 4; segment++)
            {
                var start = rotation + segment * Mathf.Pi / 2f;
                DrawArc(position, radius, start, start + 0.52f, 12, new Color(Colors.White, 0.88f), 2.4f);
            }
            var facing = new Vector2(Mathf.Cos((float)ship.Angle), Mathf.Sin((float)ship.Angle));
            DrawLine(position + facing * radius, position + facing * (radius + 12), Colors.White, 2);
            DrawCircle(position, radius + 5 + Mathf.Sin((float)_visualTime * 3f) * 2,
                new Color(Cyan, 0.045f));
        }
        if (ship.ShieldRatio > 0.05)
        {
            var shieldRadius = visualRadius + 10;
            var shieldAlpha = (float)(0.08 + ship.ShieldRatio * 0.24);
            for (var segment = 0; segment < 3; segment++)
            {
                var start = (float)_visualTime * 0.18f + segment * Mathf.Tau / 3f;
                DrawArc(position, shieldRadius, start, start + 0.86f, 20,
                    new(teamColor, shieldAlpha), 3);
            }
        }
        DrawCenteredLabel(ship.Name.ToUpperInvariant(), position.X,
            position.Y - visualRadius - 20, 12, teamColor, 150);
        DrawBar(new(position.X - 55, position.Y - visualRadius - 9), 110, 4,
            (float)ship.HullRatio, teamColor);
    }

    private void DrawTargetingOverlay()
    {
        var selected = _simulation.SelectedShip;
        if (!selected.IsAlive) return;
        var target = _simulation.Ships
            .Where(ship => ship.IsAlive && ship.Team != selected.Team)
            .OrderBy(ship => (ship.Position - selected.Position).Length)
            .FirstOrDefault();
        if (target is null) return;

        var start = ToVector(selected.Position);
        var end = ToVector(target.Position);
        var distance = (float)(target.Position - selected.Position).Length;
        DrawDashedLine(start, end, new Color(Red, 0.19f), 12, 1.4f);
        var radius = (float)target.Stats.Radius + 14 + Mathf.Sin((float)_visualTime * 4f) * 2;
        for (var corner = 0; corner < 4; corner++)
        {
            var angle = corner * Mathf.Pi / 2f + Mathf.Pi / 4f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var tangent = new Vector2(-direction.Y, direction.X);
            var anchor = end + direction * radius;
            DrawLine(anchor, anchor - direction * 9 + tangent * 6, new Color(Red, 0.82f), 2);
            DrawLine(anchor, anchor - direction * 9 - tangent * 6, new Color(Red, 0.82f), 2);
        }
        DrawCenteredLabel($"TARGET  {distance:0} m", end.X, end.Y + radius + 18, 10,
            new Color(Red, 0.82f), 150);
    }

    private void DrawDashedLine(Vector2 from, Vector2 to, Color color, int segments, float width)
    {
        for (var index = 0; index < segments; index += 2)
        {
            var a = from.Lerp(to, index / (float)segments);
            var b = from.Lerp(to, Math.Min(1, (index + 1) / (float)segments));
            DrawLine(a, b, color, width, true);
        }
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
        var rawLife = (float)Math.Clamp(combatEvent.RemainingLife / combatEvent.InitialLife, 0, 1);
        var progress = 1f - rawLife;
        var life = rawLife;
        if (_settings.ReduceFlashes) life *= 0.42f;
        var radius = combatEvent.Type switch
        {
            CombatEventType.MuzzleFlash => 7 + (int)(progress * 9),
            CombatEventType.Impact => 12 + (int)(progress * 24),
            CombatEventType.Destroyed => 28 + (int)(progress * 72),
            _ => 12
        };
        var color = combatEvent.Type == CombatEventType.Destroyed ? Orange : Cyan;
        var position = ToVector(combatEvent.Position);
        DrawArc(position, radius, 0, Mathf.Tau, 48, new(color, life * 0.9f), 4);
        DrawArc(position, radius * 0.62f, 0, Mathf.Tau, 36, new(Colors.White, life * 0.34f), 2);
        if (combatEvent.Type == CombatEventType.Destroyed)
        {
            DrawCircle(position, radius * 0.45f, new Color(1, 0.45f, 0.12f, life * 0.28f));
            var seed = position.X * 0.017f + position.Y * 0.031f;
            for (var particle = 0; particle < 12; particle++)
            {
                var angle = seed + particle * Mathf.Tau / 12f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var inner = position + direction * radius * 0.22f;
                var outer = position + direction * radius * (0.72f + (particle % 3) * 0.13f);
                DrawLine(inner, outer, new Color(particle % 2 == 0 ? Colors.White : Orange,
                    life * 0.72f), particle % 3 == 0 ? 3 : 1.5f, true);
            }
        }
        else if (combatEvent.Type == CombatEventType.Impact)
        {
            var rotation = position.X * 0.04f + position.Y * 0.02f;
            for (var spark = 0; spark < 6; spark++)
            {
                var direction = new Vector2(Mathf.Cos(rotation + spark * Mathf.Pi / 3f),
                    Mathf.Sin(rotation + spark * Mathf.Pi / 3f));
                DrawLine(position + direction * 4, position + direction * radius * 1.25f,
                    new Color(Colors.White, life * 0.75f), 1.5f, true);
            }
        }
    }

    private void DrawHud()
    {
        DrawBattlefieldFrame();
        DrawRect(new(0, 0, 1600, 74), new Color(0.004f, 0.03f, 0.07f, 0.95f));
        DrawLine(new(0, 73), new(1600, 73), new(Cyan, 0.32f));
        DrawPolyline(new Vector2[] { new(22, 52), new(38, 16), new(54, 52), new(44, 38),
            new(32, 38), new(22, 52) }, new Color(Cyan, 0.86f), 2.2f, true);
        DrawLabel("ANDROMEDA", new(68, 31), 24, Colors.White);
        DrawLabel("FLEET COMMAND", new(70, 54), 15, Cyan);
        DrawLabel($"MISSION {MissionCatalog.IndexOf(_simulation.Mission.Id) + 1}  •  {_simulation.Mission.Title.ToUpperInvariant()}",
            new(280, 44), 12, new Color("8bbdd2"), HorizontalAlignment.Center, 210);

        DrawFactionBar(new(520, 18), 245, (float)(_simulation.FleetStrength(Team.Player) /
            Math.Max(0.01, _simulation.InitialPlayerStrength)),
            "ANDROMEDA FLEET", Cyan);
        DrawFactionBar(new(835, 18), 245, (float)(_simulation.FleetStrength(Team.Enemy) /
            Math.Max(0.01, _simulation.InitialEnemyStrength)),
            "KETZAL EMPIRE", Red);
        var totalSeconds = (int)_simulation.ElapsedSeconds;
        DrawCenteredLabel($"{totalSeconds / 60:00}:{totalSeconds % 60:00}", 800, 40, 18,
            new Color("e5edf5"), 120);
        DrawFleetPanel();
        DrawSelectedPanel();
        DrawCommandLog();
        DrawObjective();
        DrawRadar();

        if (!string.IsNullOrWhiteSpace(_status) && !_commandMode)
        {
            DrawPanel(new(510, 87, 580, 38));
            DrawCenteredLabel(_status, 800, 112, 14, new Color("a0e1f5"), 560);
        }
        if (_simulation.Mission.Id == MissionId.FirstCommand && !_showHelp &&
            (!_tutorial.IsComplete || _tutorialCelebrationTime > 0))
            DrawTutorialCoach();
        if (_settings.Subtitles && _audioCaptionTime > 0)
        {
            DrawPanel(new(530, 681, 540, 36));
            DrawCenteredLabel($"♪  {_audioCaption}", 800, 705, 13, Colors.White, 510);
        }
        DrawDamageAlert();
    }

    private void DrawBattlefieldFrame()
    {
        var line = new Color(Cyan, 0.19f);
        DrawLine(new(8, 84), new(8, 248), line, 2);
        DrawLine(new(8, 84), new(172, 84), line, 2);
        DrawLine(new(1592, 84), new(1428, 84), line, 2);
        DrawLine(new(1592, 84), new(1592, 248), line, 2);
        DrawLine(new(8, 892), new(8, 728), line, 2);
        DrawLine(new(8, 892), new(172, 892), line, 2);
        DrawLine(new(1592, 892), new(1428, 892), line, 2);
        DrawLine(new(1592, 892), new(1592, 728), line, 2);
        DrawRect(new(0, 74, 10, 826), new Color(0, 0, 0, 0.21f));
        DrawRect(new(1590, 74, 10, 826), new Color(0, 0, 0, 0.21f));
    }

    private void DrawDamageAlert()
    {
        var ship = _simulation.SelectedShip;
        if (!ship.IsAlive || ship.HullRatio >= 0.35) return;
        var pulse = _settings.ReduceFlashes
            ? 0.09f
            : 0.08f + (0.07f * (0.5f + 0.5f * Mathf.Sin((float)_visualTime * 4.2f)));
        var color = new Color(Red, pulse);
        DrawRect(new(0, 74, 18, 826), color);
        DrawRect(new(1582, 74, 18, 826), color);
        DrawRect(new(0, 74, 1600, 12), color);
        DrawRect(new(0, 888, 1600, 12), color);
        DrawCenteredLabel("HULL CRITICAL", 800, 100, 12, new Color(Red, 0.82f), 200);
    }

    private void DrawFactionBar(Vector2 position, float width, float ratio, string text, Color color)
    {
        DrawLabel(text, position + new Vector2(0, 11), 12, color);
        DrawBar(position + new Vector2(0, 20), width, 8, Mathf.Clamp(ratio, 0, 1), color);
    }

    private void DrawFleetPanel()
    {
        var area = new Rect2(20, 104, 250, 192);
        DrawPanel(area);
        DrawLabel("FRIENDLY FLEET", new(34, 126), 13, new Color("a0d2eb"));
        var fleet = _simulation.Ships.Where(ship => ship.Team == Team.Player).ToList();
        for (var index = 0; index < fleet.Count; index++)
        {
            var ship = fleet[index];
            var y = 140 + index * 37;
            if (ship.IsAlive && ship.Id == _simulation.SelectedShip.Id)
                DrawRect(new(28, y - 11, 234, 32), new Color(0.1f, 0.66f, 0.86f, 0.2f));
            DrawLabel($"{index + 1}", new(34, y + 4), 11, ship.IsAlive ? Cyan : new Color("666a72"));
            if (_shipTextures.TryGetValue(ship.Class, out var texture))
                DrawTextureRect(texture, new Rect2(50, y - 9, 39, 20), false,
                    ship.IsAlive ? new Color(0.76f, 0.88f, 0.95f, 0.92f) : new Color("555b62"));
            DrawLabel(ship.Name.ToUpperInvariant(), new(94, y + 4), 11,
                ship.IsAlive ? Colors.White : new Color("666a72"));
            DrawBar(new(142, y + 10), 105, 4, (float)ship.HullRatio, ship.IsAlive ? Cyan : Red);
        }
    }

    private void DrawSelectedPanel()
    {
        var ship = _simulation.SelectedShip;
        DrawPanel(new(20, 710, 300, 166));
        DrawLabel(ship.Name.ToUpperInvariant(), new(36, 737), 18, Colors.White);
        DrawLabel($"{ship.Class.ToString().ToUpperInvariant()}  •  MANUAL CONTROL", new(36, 756), 12,
            new Color("82beda"));
        if (_shipTextures.TryGetValue(ship.Class, out var texture))
        {
            DrawCircle(new(261, 744), 36, new Color(Cyan, 0.05f));
            DrawTextureRect(texture, new Rect2(218, 724, 82, 41), false,
                new Color(0.8f, 0.9f, 0.96f, 0.96f));
        }
        DrawMeter(new(36, 778), "HULL", (float)ship.HullRatio, new Color("4be6a3"));
        DrawMeter(new(36, 804), "SHIELD", (float)ship.ShieldRatio, Cyan);
        DrawMeter(new(36, 830), "ENERGY", (float)ship.EnergyRatio, new Color("ffc74d"));
        DrawLabel($"{ship.Order.Type.ToString().ToUpperInvariant()}  •  {(int)ship.Velocity.Length} m/s",
            new(36, 860), 11, new Color("7dacC6"));
        var abilityKey = _lastInputWasController
            ? GamepadButtonLabel(GamepadActionIds.Ability)
            : BindingLabel(GameActionIds.Ability);
        var ability = ship.AbilityCooldown <= 0
            ? $"{abilityKey}  ABILITY READY"
            : $"{abilityKey}  {Math.Ceiling(ship.AbilityCooldown)}s";
        DrawLabel(ability, new(205, 860), 11, ship.AbilityCooldown <= 0 ? new Color("ffd065") : new Color("6f8794"));
    }

    private void DrawMeter(Vector2 position, string label, float ratio, Color color)
    {
        DrawLabel(label, position, 11, new Color("90bacf"));
        DrawBar(position + new Vector2(70, -8), 135, 7, ratio, color);
    }

    private void DrawCommandLog()
    {
        var glow = (float)Math.Clamp(_commandPulseTime / 4.5, 0, 1);
        DrawPanel(new(345, 720, 740, 156));
        if (glow > 0)
        {
            DrawRect(new(351, 726, 728, 144), new Color(Cyan, 0.025f + glow * 0.045f));
            DrawLine(new(355, 727), new(1075, 727), new Color(Cyan, 0.42f + glow * 0.32f), 2.5f);
        }
        DrawLabel($"TACTICAL COMMAND  •  {BindingLabel(GameActionIds.Command)} TYPE  •  " +
                  $"{BindingLabel(GameActionIds.Voice)} VOICE",
            new(361, 747), 12, Cyan);
        DrawVoiceWaveform(new(374, 796), glow);
        DrawLabel("ORDER", new(476, 780), 10, new Color("6f9fb5"));
        DrawLabel(ClipText(_lastIssuedCommand, 73), new(476, 802), 14, Colors.White);
        DrawLabel("ACKNOWLEDGED", new(476, 825), 10, new Color("4be6a3"));
        DrawLabel(ClipText(_lastAcknowledgement, 76), new(476, 848), 12, new Color("a9d9e8"));
    }

    private void DrawVoiceWaveform(Vector2 center, float glow)
    {
        DrawCircle(center, 36, new Color(Cyan, 0.045f + glow * 0.04f));
        DrawArc(center, 36, 0, Mathf.Tau, 40, new Color(Cyan, 0.32f + glow * 0.35f), 1.5f);
        for (var bar = -5; bar <= 5; bar++)
        {
            var pulse = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin((float)_visualTime * (2.8f + glow * 4) + bar * 0.76f));
            var height = (6 + (5 - Math.Abs(bar)) * 2.4f) * pulse;
            DrawLine(center + new Vector2(bar * 5, -height), center + new Vector2(bar * 5, height),
                new Color(Cyan, 0.58f + glow * 0.32f), 2, true);
        }
    }

    private void DrawRadar()
    {
        var center = new Vector2(1430, 750);
        const float radius = 92;
        DrawPanel(new(1302, 624, 276, 252));
        DrawLabel("TACTICAL RADAR", new(1320, 650), 12, Cyan);
        DrawLabel("LIVE FLEETSPACE", new(1450, 650), 10, new Color("7299aa"));
        DrawCircle(center, radius + 8, new Color(0, 0.02f, 0.05f, 0.86f));
        DrawArc(center, radius, 0, Mathf.Tau, 72, new Color(Cyan, 0.42f), 1.5f);
        DrawArc(center, radius * 0.66f, 0, Mathf.Tau, 60, new Color(Cyan, 0.15f), 1);
        DrawArc(center, radius * 0.33f, 0, Mathf.Tau, 48, new Color(Cyan, 0.12f), 1);
        DrawLine(center - new Vector2(radius, 0), center + new Vector2(radius, 0), new Color(Cyan, 0.1f));
        DrawLine(center - new Vector2(0, radius), center + new Vector2(0, radius), new Color(Cyan, 0.1f));
        var sweepAngle = (float)_visualTime * 0.72f;
        var sweep = new Vector2(Mathf.Cos(sweepAngle), Mathf.Sin(sweepAngle));
        DrawLine(center, center + sweep * radius, new Color(Cyan, 0.28f), 2);
        foreach (var ship in _simulation.Ships.Where(candidate => candidate.IsAlive))
        {
            var relative = new Vector2(
                (float)(ship.Position.X / BattleSimulation.WorldWidth - 0.5),
                (float)(ship.Position.Y / BattleSimulation.WorldHeight - 0.5)) * radius * 1.76f;
            relative = relative.LimitLength(radius - 6);
            var color = ship.Team == Team.Player ? Cyan : Red;
            var size = ship.Class is ShipClass.Flagship or ShipClass.Carrier ? 4.5f : 3f;
            DrawCircle(center + relative, size + 3, new Color(color, 0.1f));
            DrawCircle(center + relative, size, new Color(color, 0.92f));
            if (ship.Id == _simulation.SelectedShip.Id)
                DrawArc(center + relative, size + 7, 0, Mathf.Tau, 24, Colors.White, 1.5f);
        }
        DrawCenteredLabel("N", center.X, center.Y - radius - 8, 9, new Color("8eb7c8"), 18);
    }

    private void DrawObjective()
    {
        DrawPanel(new(1270, 104, 308, 122));
        DrawPolyline(new Vector2[] { new(1293, 115), new(1302, 119), new(1302, 132),
            new(1293, 140), new(1284, 132), new(1284, 119), new(1293, 115) }, Cyan, 1.6f, true);
        DrawLabel("PRIMARY OBJECTIVE", new(1316, 127), 13, Cyan);
        DrawLabel(_simulation.Mission.Objective.Title.ToUpperInvariant(), new(1286, 154), 14, Colors.White,
            HorizontalAlignment.Center, 276);
        var progress = _simulation.ObjectiveProgress;
        DrawBar(new(1286, 174), 276, 8, (float)progress.ClampedRatio, Red);
        DrawLabel(progress.Label.ToUpperInvariant(), new(1286, 201), 11, new Color("91b9ce"),
            HorizontalAlignment.Center, 276);
    }

    private void DrawOverlay()
    {
        if (_showBindings)
        {
            DrawBindings();
        }
        else if (_showSettings)
        {
            DrawSettings();
        }
        else if (_showLocalAiSetup)
        {
            DrawLocalAiSetup();
        }
        else if (_showMissionSelect)
        {
            DrawMissionSelect();
        }
        else if (_showHelp)
        {
            if (_simulation.Mission.Id == MissionId.FirstCommand && !_tutorial.IsComplete)
            {
                DrawTutorialBriefing();
                return;
            }
            var mission = _simulation.Mission;
            DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.78f));
            DrawPanel(new(320, 90, 960, 720));
            DrawCenteredLabel(mission.Narrative.Chapter, 800, 145, 14, Cyan, 820);
            DrawCenteredLabel(
                $"MISSION {MissionCatalog.IndexOf(mission.Id) + 1}  •  {mission.Title.ToUpperInvariant()}",
                800, 185, 30, Colors.White, 820);
            DrawCenteredLabel(mission.Subtitle, 800, 216, 15, new Color("99d3e9"), 820);
            DrawCenteredLabel(mission.Narrative.Speaker, 800, 258, 12, new Color("ffd065"), 820);
            DrawCenteredLabel(mission.Narrative.BriefingLines[0], 800, 286, 14,
                new Color("dce7ee"), 860);
            DrawCenteredLabel(mission.Narrative.BriefingLines[1], 800, 310, 14,
                new Color("dce7ee"), 860);
            DrawCenteredLabel($"OBJECTIVE  •  {mission.Objective.Title.ToUpperInvariant()}",
                800, 350, 15, new Color("ffd065"), 820);
            DrawCenteredLabel(
                $"COMMAND {mission.Complexity.Rating}/3  •  {mission.Complexity.Tier}  •  " +
                $"{mission.Complexity.SimultaneousThreatGroups} THREAT GROUPS",
                800, 378, 13, Cyan, 820);
            DrawCenteredLabel(mission.Complexity.TacticalFocus, 800, 402, 12,
                new Color("9fc5d6"), 860);
            var controls = new[]
            {
                ($"1–4 / {BindingLabel(GameActionIds.SwitchShip)}", "Switch controlled ship"),
                ($"{BindingLabel(GameActionIds.Thrust)} {BindingLabel(GameActionIds.Reverse)} / " +
                 $"{BindingLabel(GameActionIds.TurnLeft)} {BindingLabel(GameActionIds.TurnRight)}",
                    "Thrust and rotate"),
                (BindingLabel(GameActionIds.Fire), "Fire at nearest target"),
                (BindingLabel(GameActionIds.Command), "Type a natural-language fleet order"),
                (BindingLabel(GameActionIds.Ability), "Use the selected ship’s tactical ability"),
                (BindingLabel(GameActionIds.Voice), "Local voice command adapter")
            };
            var y = 455;
            foreach (var (key, description) in controls)
            {
                DrawLabel(key, new(490, y), 14, Cyan);
                DrawLabel(description, new(700, y), 15, new Color("dce7ee"));
                y += 34;
            }
            DrawCenteredLabel($"Suggested opening: “{mission.RecommendedOrder}”", 800, 690, 14,
                new Color("ffd065"), 700);
            DrawCenteredLabel($"Press {BindingLabel(GameActionIds.Help)} to enter the battle", 800, 756, 13,
                new Color("87b5ca"), 700);
        }
        else if (_paused && _simulation.Status == BattleStatus.Active)
        {
            var pause = _lastInputWasController
                ? GamepadButtonLabel(GamepadActionIds.Pause)
                : BindingLabel(GameActionIds.Pause);
            DrawBanner("PAUSED", $"Press {pause} to return to the battle", Cyan);
        }
        else if (_simulation.Status == BattleStatus.PlayerVictory)
        {
            var index = MissionCatalog.IndexOf(_simulation.Mission.Id);
            var nextKey = _lastInputWasController
                ? GamepadButtonLabel(GamepadActionIds.NextMission)
                : BindingLabel(GameActionIds.NextMission);
            var restart = _lastInputWasController
                ? GamepadButtonLabel(GamepadActionIds.Restart)
                : BindingLabel(GameActionIds.Restart);
            var missions = _lastInputWasController
                ? GamepadButtonLabel(GamepadActionIds.Missions)
                : BindingLabel(GameActionIds.Missions);
            var next = index + 1 < MissionCatalog.All.Count
                ? $"Press {nextKey} for the next mission • {restart} to replay • {missions} for mission select"
                : $"Campaign demo complete • {restart} to replay • {missions} for mission select";
            DrawStoryOutcome("VICTORY", _simulation.Mission.Narrative.VictoryLines,
                next, new Color("48eba9"));
        }
        else if (_simulation.Status == BattleStatus.EnemyVictory)
        {
            var restart = _lastInputWasController
                ? GamepadButtonLabel(GamepadActionIds.Restart)
                : BindingLabel(GameActionIds.Restart);
            DrawStoryOutcome("MISSION FAILED", _simulation.Mission.Narrative.FailureLines,
                $"Press {restart} to regroup and try again", Red);
        }
    }

    private void DrawTutorialBriefing()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.84f));
        DrawPanel(new(270, 135, 1060, 630));
        DrawCenteredLabel(_simulation.Mission.Narrative.Chapter, 800, 174, 13, Cyan, 900);
        DrawCenteredLabel("CAPTAIN'S DRILL", 800, 205, 38, Colors.White, 900);
        DrawCenteredLabel("Four actions. About sixty seconds. Learn by commanding.", 800, 242, 16,
            Cyan, 900);
        DrawCenteredLabel(_simulation.Mission.Narrative.BriefingLines[0],
            800, 278, 13, new Color("c9dce6"), 920);
        DrawCenteredLabel(_simulation.Mission.Narrative.BriefingLines[1],
            800, 300, 13, new Color("c9dce6"), 920);

        for (var index = 0; index < TutorialTracker.Steps.Count; index++)
        {
            var step = TutorialTracker.Steps[index];
            var completed = index < _tutorial.CompletedSteps;
            var active = index == _tutorial.CompletedSteps;
            var x = 332 + index * 238;
            var color = completed ? new Color("48eba9") : active ? new Color("ffd065") : Cyan;
            DrawPanel(new(x, 350, 214, 230));
            DrawCircle(new(x + 107, 397), 23, new(color, 0.2f));
            DrawArc(new(x + 107, 397), 23, 0, Mathf.Tau, 36, color, 2);
            DrawCenteredLabel(completed ? "✓" : $"{index + 1}", x + 107, 405, 18, color, 30);
            DrawCenteredLabel(step.Title.ToUpperInvariant(), x + 107, 450, 15, Colors.White, 190);
            DrawCenteredLabel(step.Action switch
            {
                TutorialAction.SwitchShip =>
                    $"{BindingLabel(GameActionIds.SwitchShip)}  /  {GamepadButtonLabel(GamepadActionIds.SwitchShip)}",
                TutorialAction.ManualControl =>
                    $"{BindingLabel(GameActionIds.Thrust)}{BindingLabel(GameActionIds.TurnLeft)}" +
                    $"{BindingLabel(GameActionIds.Reverse)}{BindingLabel(GameActionIds.TurnRight)}  /  STICK",
                TutorialAction.IssueOrder =>
                    $"{BindingLabel(GameActionIds.Command)}  /  {GamepadButtonLabel(GamepadActionIds.Voice)}",
                _ =>
                    $"{BindingLabel(GameActionIds.Ability)}  /  {GamepadButtonLabel(GamepadActionIds.Ability)}"
            }, x + 107, 490, 14, color, 190);
            DrawCenteredLabel(step.Purpose, x + 107, 538, 12, new Color("9fc5d6"), 190);
        }

        DrawCenteredLabel($"Objective after training: {_simulation.Mission.Objective.Title}", 800, 638, 16,
            new Color("ffd065"), 900);
        DrawCenteredLabel(_lastInputWasController ? "Press A or START to deploy" :
                $"Press {BindingLabel(GameActionIds.Help)} to deploy",
            800, 700, 18, Colors.White, 900);
        DrawCenteredLabel("The battle is paused while this briefing is open", 800, 730, 12,
            new Color("789bac"), 900);
    }

    private void DrawTutorialCoach()
    {
        var flash = (float)Math.Clamp(_tutorialStepFlash, 0, 1);
        var border = _tutorial.IsComplete ? new Color("48eba9") : new Color("ffd065");
        DrawStyleBox(new StyleBoxFlat
        {
            BgColor = new Color(0.01f, 0.045f, 0.08f, 0.96f),
            BorderColor = new(border, 0.65f + flash * 0.35f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14
        }, new Rect2(430, 132, 740, 104));

        if (_tutorial.IsComplete)
        {
            DrawCenteredLabel("CAPTAIN CERTIFIED", 800, 174, 19, border, 690);
            DrawCenteredLabel("Training complete • Destroy the raider leader", 800, 207, 14,
                Colors.White, 690);
            return;
        }

        var step = _tutorial.CurrentStep!;
        for (var index = 0; index < TutorialTracker.Steps.Count; index++)
        {
            var completed = index < _tutorial.CompletedSteps;
            var active = index == _tutorial.CompletedSteps;
            var color = completed ? new Color("48eba9") : active ? border : new Color("405a68");
            DrawCircle(new(665 + index * 90, 153), active ? 7 : 5, color);
            if (index + 1 < TutorialTracker.Steps.Count)
                DrawLine(new(673 + index * 90, 153), new(747 + index * 90, 153),
                    new(color, 0.45f), 2);
        }
        DrawCenteredLabel(step.Title.ToUpperInvariant(), 800, 183, 16, border, 690);
        DrawCenteredLabel(TutorialPrompt(), 800, 207, 14, Colors.White, 690);
        DrawCenteredLabel(step.Purpose, 800, 226, 11, new Color("8db6c9"), 690);
    }

    private void DrawMissionSelect()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.82f));
        DrawPanel(new(390, 130, 820, 620));
        DrawCenteredLabel("THE BLACK SUN INCIDENT", 800, 198, 34, Colors.White, 700);
        DrawCenteredLabel("A three-chapter Andromeda Fleet Command story", 800, 230, 15,
            new Color("99d3e9"), 700);

        for (var index = 0; index < MissionCatalog.All.Count; index++)
        {
            var mission = MissionCatalog.All[index];
            var unlocked = _progress.IsUnlocked(index);
            var completed = _progress.IsCompleted(mission.Id);
            var y = 290 + index * 120;
            DrawPanel(new(455, y - 35, 690, 92));
            DrawLabel($"{index + 1}", new(485, y + 10), 28, unlocked ? Cyan : new Color("566a73"));
            DrawLabel(mission.Title.ToUpperInvariant(), new(545, y - 1), 19,
                unlocked ? Colors.White : new Color("687983"));
            DrawLabel($"COMMAND {mission.Complexity.Rating}/3  •  {mission.Complexity.Tier}",
                new(875, y - 1), 12, unlocked ? new Color("ffd065") : new Color("66727a"),
                HorizontalAlignment.Right, 235);
            DrawLabel(unlocked ? mission.Subtitle : "LOCKED — complete the previous mission",
                new(545, y + 25), 13, unlocked ? new Color("9bc9dc") : new Color("66727a"));
            if (completed) DrawLabel("COMPLETE", new(1025, y + 25), 12, new Color("48eba9"));
        }

        DrawCenteredLabel($"Press 1–3 to deploy • {BindingLabel(GameActionIds.Missions)} or Esc to close",
            800, 690, 14, new Color("ffd065"), 700);
    }

    private void DrawLocalAiSetup()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.86f));
        DrawPanel(new(350, 105, 900, 690));
        DrawCenteredLabel("LOCAL AI CONTROL CENTER", 800, 175, 32, Colors.White, 780);
        DrawCenteredLabel("Runtime commands stay on this computer", 800, 208, 15, Cyan, 780);

        DrawSetupRow(280, "OFFLINE COMMAND PARSER", true,
            "Always available — no model, account, or internet required");
        DrawSetupRow(390, $"OLLAMA  •  {_localAiConfiguration.OllamaModel}",
            _localAiReadiness.OllamaReachable && _localAiReadiness.OllamaModelInstalled,
            !_localAiReadiness.OllamaReachable
                ? "Ollama is not running on 127.0.0.1"
                : _localAiReadiness.OllamaModelInstalled
                    ? _localAiConfiguration.PreferGpu == false
                        ? "Ready • CPU-only mode"
                        : "Ready • maximum GPU offload with automatic CPU fallback"
                    : "Service detected; press O to pull the recommended model");
        DrawSetupRow(500, "WHISPER.CPP VOICE", _localAiReadiness.VoiceReady,
            _localAiReadiness.VoiceReady
                ? "whisper-cli and the base English speech model are ready"
                : _localAiReadiness.WhisperCliFound
                    ? "whisper-cli found; press W to download the speech model"
                    : "Bundled runtime not found; source builds can provide whisper-cli on PATH");

        DrawCenteredLabel(_localAiBusy ? "WORKING — this may take several minutes" : _localAiReadiness.Detail,
            800, 625, 14, _localAiBusy ? new Color("ffd065") : new Color("b9d9e7"), 780);
        DrawCenteredLabel("O  Install model     G  GPU/CPU mode     W  Speech model     R  Rescan",
            800, 690, 15, new Color("ffd065"), 780);
        DrawCenteredLabel("L or Esc to close", 800, 735, 13, new Color("87b5ca"), 780);
    }

    private void DrawSettings()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.87f));
        DrawPanel(new(390, 115, 820, 670));
        DrawCenteredLabel("SETTINGS & ACCESSIBILITY", 800, 185, 31, Colors.White, 700);
        DrawCenteredLabel("Changes save immediately", 800, 216, 14, Cyan, 700);

        var rows = new (string Key, string Name, string Value)[]
        {
            ("A", "Master volume", $"{(int)Math.Round(_settings.MasterVolume * 100)}%"),
            ("C", "Color-vision palette", SplitPascalCase(_settings.ColorMode.ToString())),
            ("F", "Reduced flashes", _settings.ReduceFlashes ? "ON" : "OFF"),
            ("U", "Tactical cue captions", _settings.Subtitles ? "ON" : "OFF"),
            ("D", "Controller deadzone", $"{_settings.GamepadDeadzone:0.00}")
        };
        var y = 290;
        foreach (var (key, name, value) in rows)
        {
            DrawPanel(new(470, y - 33, 660, 72));
            DrawLabel(key, new(505, y + 8), 17, Cyan);
            DrawLabel(name, new(555, y + 7), 16, Colors.White);
            DrawLabel(value.ToUpperInvariant(), new(1000, y + 7), 14, new Color("ffd065"),
                HorizontalAlignment.Right, 100);
            y += 86;
        }
        DrawCenteredLabel("K  Keyboard controls     •     PAD Y  Controller buttons",
            800, 684, 14, new Color("ffd065"), 700);
        DrawCenteredLabel($"Controller: left stick fly • {GamepadButtonLabel(GamepadActionIds.Fire)} fire • " +
                  $"{GamepadButtonLabel(GamepadActionIds.Ability)} ability • " +
                  $"{GamepadButtonLabel(GamepadActionIds.SwitchShip)} switch ship",
            800, 716, 13, new Color("9bc9dc"), 700);
        DrawCenteredLabel("F10 or Esc to close", 800, 750, 13, new Color("87b5ca"), 700);
    }

    private void DrawBindings()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.9f));
        DrawPanel(new(255, 75, 1090, 750));
        DrawCenteredLabel(_bindingDeviceGamepad ? "CONTROLLER BUTTONS" : "KEYBOARD CONTROLS",
            800, 140, 31, Colors.White, 900);
        DrawCenteredLabel("Assignments save immediately • conflicts swap automatically • G / LB switches device",
            800, 173, 14, Cyan, 900);

        var actions = _bindingDeviceGamepad
            ? GamepadActions.All.Select(action => (action.Id, action.Label)).ToArray()
            : GameActions.All.Select(action => (action.Id, action.Label)).ToArray();
        var rowsPerColumn = _bindingDeviceGamepad ? 4 : 7;
        var rowSpacing = _bindingDeviceGamepad ? 86 : 64;
        var firstRowY = _bindingDeviceGamepad ? 250 : 235;
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            var column = index / rowsPerColumn;
            var row = index % rowsPerColumn;
            var x = 315 + column * 500;
            var y = firstRowY + row * rowSpacing;
            var selected = index == _bindingSelection;
            if (selected)
                DrawRect(new(x - 16, y - (_bindingDeviceGamepad ? 36 : 29), 455,
                    _bindingDeviceGamepad ? 62 : 48), new Color(0.12f, 0.58f, 0.72f, 0.2f));
            DrawLabel(action.Label.ToUpperInvariant(), new(x, y), 14,
                selected ? Colors.White : new Color("b8d5e2"));
            var binding = _bindingDeviceGamepad
                ? GamepadButtonLabel(action.Id)
                : BindingLabel(action.Id);
            DrawLabel(binding, new(x + 315, y), 15,
                selected ? new Color("ffd065") : Cyan, HorizontalAlignment.Right, 105);
        }

        if (_captureBinding)
        {
            DrawPanel(new(420, 645, 760, 82));
            DrawCenteredLabel($"PRESS A {(_bindingDeviceGamepad ? "BUTTON" : "KEY")} FOR " +
                      actions[_bindingSelection].Label.ToUpperInvariant(),
                800, 681, 18, new Color("ffd065"), 700);
            DrawCenteredLabel(_bindingDeviceGamepad
                    ? "PAD BACK cancels • the Settings button stays reserved"
                    : "Esc cancels • system and diagnostic keys are reserved",
                800, 708, 12, new Color("a9c7d5"), 700);
        }
        else
        {
            DrawCenteredLabel(_bindingDeviceGamepad
                    ? "D-PAD choose  •  A rebind  •  X default  •  Y reset all"
                    : "↑/↓ choose  •  Enter rebind  •  Backspace default  •  R reset all",
                800, 705, 14, new Color("ffd065"), 920);
        }
        DrawCenteredLabel(_bindingDeviceGamepad ? "B / BACK returns to settings" : "K or Esc returns to settings",
            800, 772, 13, new Color("87b5ca"), 900);
    }

    private void DrawSetupRow(float y, string title, bool ready, string detail)
    {
        DrawPanel(new(430, y - 42, 740, 88));
        DrawCircle(new(474, y), 10, ready ? new Color("48eba9") : new Color("ffb047"));
        DrawLabel(title, new(505, y - 7), 17, Colors.White);
        DrawLabel(detail, new(505, y + 22), 13, new Color("9bc9dc"));
        DrawLabel(ready ? "READY" : "SETUP", new(1070, y + 8), 12,
            ready ? new Color("48eba9") : new Color("ffb047"), HorizontalAlignment.Center, 70);
    }

    private void DrawStoryOutcome(string title, IReadOnlyList<string> storyLines, string instruction, Color color)
    {
        var mission = _simulation.Mission;
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.82f));
        DrawPanel(new(280, 155, 1040, 590));
        DrawCenteredLabel(title, 800, 250, 52, color, 900);
        DrawCenteredLabel(mission.Narrative.Chapter, 800, 292, 14, Cyan, 900);
        DrawCenteredLabel(mission.Narrative.Speaker, 800, 334, 12, new Color("ffd065"), 900);
        DrawCenteredLabel(storyLines[0], 800, 378, 16, Colors.White, 940);
        DrawCenteredLabel(storyLines[1], 800, 410, 16, Colors.White, 940);
        DrawCenteredLabel($"OBJECTIVE  •  {mission.Objective.Title.ToUpperInvariant()}",
            800, 475, 14, color, 900);
        DrawCenteredLabel(mission.Complexity.TacticalFocus, 800, 508, 13,
            new Color("9fc5d6"), 900);
        DrawCenteredLabel(instruction, 800, 675, 13, new Color("87b5ca"), 1000);
    }

    private void DrawBanner(string title, string subtitle, Color color)
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.78f));
        DrawCenteredLabel(title, 800, 410, 58, color, 900);
        DrawCenteredLabel(subtitle, 800, 452, 19, Colors.White, 900);
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
        DrawLine(rect.Position + new Vector2(12, 1),
            rect.Position + new Vector2(rect.Size.X - 12, 1), new Color(Cyan, 0.22f), 1.2f);
        DrawLine(rect.Position + new Vector2(12, rect.Size.Y - 1),
            rect.Position + new Vector2(rect.Size.X - 12, rect.Size.Y - 1),
            new Color(0.25f, 0.45f, 0.58f, 0.08f), 1);
    }

    private void DrawLabel(string text, Vector2 position, int size, Color color,
        HorizontalAlignment alignment = HorizontalAlignment.Left, float width = -1)
    {
        DrawString(ThemeDB.FallbackFont, position, text, alignment, width, size, color);
    }

    private void DrawCenteredLabel(string text, float centerX, float y, int size, Color color, float width)
    {
        DrawLabel(text, new(centerX - width / 2, y), size, color, HorizontalAlignment.Center, width);
    }

    private static string SplitPascalCase(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

    private static string ClipText(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";

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
                random.NextDouble() < 0.42,
                0.35f + (float)random.NextDouble() * 1.65f,
                (float)random.NextDouble() * Mathf.Tau));
        }
    }

    private void LoadShipArt()
    {
        foreach (var shipClass in Enum.GetValues<ShipClass>())
        {
            var name = shipClass.ToString().ToLowerInvariant();
            if (GD.Load<Texture2D>($"res://art/ships/{name}.svg") is { } texture)
                _shipTextures[shipClass] = texture;
        }
    }

    private static Vector2 ToVector(Vector2D value) => new((float)value.X, (float)value.Y);
    private readonly record struct Star(
        Vector2 Position,
        float Size,
        float Alpha,
        bool Blue,
        float Depth,
        float Phase);
}
