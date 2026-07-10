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
    private CrashReportService? _crashReports;
    private bool _showSettings;
    private string _audioCaption = string.Empty;
    private double _audioCaptionTime;
    private BattleReplayStore? _replayStore;
    private ReplayRecorder? _replayRecorder;
    private int _simulationTick;
    private IPlatformServices? _platform;
    private bool _lastInputWasController;
    private double _tutorialStepFlash;
    private double _tutorialCelebrationTime;

    public override void _Ready()
    {
        var commandArguments = OS.GetCmdlineUserArgs();
        _smokeTest = commandArguments.Contains("--smoke-test", StringComparer.Ordinal);
        var benchmarkMode = commandArguments.Contains("--benchmark", StringComparer.Ordinal);
        _settingsStore = new(ProjectSettings.GlobalizePath("user://settings.json"));
        _settings = _settingsStore.Load();
        _crashReports = new(ProjectSettings.GlobalizePath("user://crashes"));
        _replayStore = new(ProjectSettings.GlobalizePath("user://replays"));
        ApplySettings();
        _localAiStore = new(ProjectSettings.GlobalizePath("user://local-ai.json"));
        _localAiConfiguration = LocalAiConfiguration.ApplyEnvironment(_localAiStore.Load());
        _localAiSetup = new();
        RebuildLocalAiAdapters();
        _audio = new(this);
        if (!_smokeTest && !benchmarkMode) _audio.StartAmbient();
        _platform = PlatformServicesFactory.Create();
        _progressStore = new(ProjectSettings.GlobalizePath("user://campaign-progress.json"));
        _progress = _progressStore.Load();
        CreateStars();
        LoadShipArt();
        CreateCommandLine();
        AddLog($"Mission 1: {_simulation.Mission.Title}. Press H when ready.");
        AddLog("Enter opens the fleet command channel.");
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
        _weaponAudioCooldown = Math.Max(0, _weaponAudioCooldown - delta);
        _tutorialStepFlash = Math.Max(0, _tutorialStepFlash - delta);
        _tutorialCelebrationTime = Math.Max(0, _tutorialCelebrationTime - delta);
        _platform?.RunCallbacks();
        _audioCaptionTime = Math.Max(0, _audioCaptionTime - delta);
        if (_statusTime > 0)
        {
            _statusTime -= delta;
            if (_statusTime <= 0) _status = string.Empty;
        }

        if (!_paused && !_showHelp && !_commandMode && _simulation.Status == BattleStatus.Active)
        {
            _accumulator += Math.Min(delta, 0.2);
            var joyX = Input.GetJoyAxis(0, JoyAxis.LeftX);
            var joyY = Input.GetJoyAxis(0, JoyAxis.LeftY);
            var deadzone = (float)_settings.GamepadDeadzone;
            var manualInput = new ManualInput(
                Input.IsKeyPressed(Key.W) || joyY < -deadzone,
                Input.IsKeyPressed(Key.S) || joyY > deadzone,
                Input.IsKeyPressed(Key.A) || joyX < -deadzone,
                Input.IsKeyPressed(Key.D) || joyX > deadzone,
                Input.IsKeyPressed(Key.Space) || Input.IsJoyButtonPressed(0, JoyButton.A));
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

        if (_showSettings)
        {
            HandleSettingsInput(key.Keycode);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_showMissionSelect)
        {
            if (key.Keycode == Key.M || key.Keycode == Key.Escape)
                _showMissionSelect = false;
            else if (key.Keycode is Key.Key1 or Key.Key2 or Key.Key3)
                LoadMission((int)key.Keycode - (int)Key.Key1);
            GetViewport().SetInputAsHandled();
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
                ActivateSelectedAbility();
                break;
            case Key.P when !_showHelp:
                _paused = !_paused;
                break;
            case Key.R when !_showHelp:
                Restart();
                break;
            case Key.M when !_showHelp:
                _showMissionSelect = true;
                break;
            case Key.L:
                _showLocalAiSetup = true;
                RefreshLocalAiStatus();
                break;
            case Key.F10:
                _showSettings = true;
                break;
            case Key.N when !_showHelp && _simulation.Status == BattleStatus.PlayerVictory:
                LoadMission(MissionCatalog.IndexOf(_simulation.Mission.Id) + 1);
                break;
            case Key.Tab when !_showHelp:
                CyclePlayerShip();
                break;
            case Key.Key1 when !_showHelp:
                SelectPlayerShip(0);
                break;
            case Key.Key2 when !_showHelp:
                SelectPlayerShip(1);
                break;
            case Key.Key3 when !_showHelp:
                SelectPlayerShip(2);
                break;
            case Key.Key4 when !_showHelp:
                SelectPlayerShip(3);
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
        GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        DrawSpace();
        DrawGrid();
        foreach (var projectile in _simulation.Projectiles) DrawProjectile(projectile);
        foreach (var ship in _simulation.Ships.Where(ship => ship.IsAlive)) DrawShip(ship);
        foreach (var combatEvent in _simulation.Events.Where(item => item.Type != CombatEventType.Order &&
                     (!_settings.ReduceFlashes || item.Type != CombatEventType.MuzzleFlash)))
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
        _replayRecorder?.RecordCommand(_simulationTick, result.Command);
        var acknowledgement = _dispatcher.Dispatch(result.Command, _simulation);
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
        if (_showSettings)
        {
            if (button is JoyButton.Back or JoyButton.B) _showSettings = false;
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

        switch (button)
        {
            case JoyButton.LeftShoulder:
            case JoyButton.RightShoulder:
                CyclePlayerShip();
                break;
            case JoyButton.B:
                ActivateSelectedAbility();
                break;
            case JoyButton.Y:
                CaptureVoiceCommand();
                break;
            case JoyButton.X:
                _showMissionSelect = true;
                break;
            case JoyButton.Start:
                _paused = !_paused;
                break;
            case JoyButton.Back:
                _showSettings = true;
                break;
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
            default:
                return;
        }
        _settings = _settings.Normalize();
        _settingsStore?.Save(_settings);
        ApplySettings();
        SetStatus("Settings saved");
    }

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
                    break;
                case CombatEventType.Destroyed:
                    PlayCue(TacticalCue.Destruction, combatEvent.Message ?? "Ship destroyed");
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
            if (_localAiConfiguration.WhisperCli is null && discoveredCli is not null)
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
                WhisperCli = _localAiConfiguration.WhisperCli ?? LocalAiSetupService.FindWhisperCli(),
                WhisperModel = destination
            };
            _localAiStore?.Save(_localAiConfiguration);
            RebuildLocalAiAdapters();
            _localAiReadiness = await _localAiSetup.CheckAsync(_localAiConfiguration);
            SetStatus(_localAiReadiness.VoiceReady
                ? "Local voice control ready"
                : "Speech model installed; whisper-cli still needs to be installed");
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
        AddLog(_tutorial.GetPrompt(_lastInputWasController));
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
        DrawCircle(position + new Vector2(5, 7), (float)ship.Stats.Radius * 1.1f,
            new Color(0, 0, 0, 0.32f));
        DrawSetTransform(position, (float)ship.Angle);
        if (ship.Velocity.Length > 8)
        {
            var thrust = Mathf.Clamp((float)(ship.Velocity.Length / ship.EffectiveMaxSpeed), 0.2f, 1);
            DrawLine(new(-(float)ship.Stats.Radius * 1.35f, 0),
                new(-(float)ship.Stats.Radius * 1.35f - 34 * thrust, 0),
                new(teamColor, 0.34f), 14, true);
            DrawLine(new(-(float)ship.Stats.Radius * 1.35f, 0),
                new(-(float)ship.Stats.Radius * 1.35f - 29 * thrust, 0),
                new(teamColor, 0.9f), 4, true);
        }
        if (_shipTextures.TryGetValue(ship.Class, out var texture))
        {
            var radius = (float)ship.Stats.Radius;
            DrawTextureRect(texture, new Rect2(-radius * 1.65f, -radius * 0.85f,
                radius * 3.3f, radius * 1.7f), false, new Color(teamColor, 0.96f));
        }
        else
        {
            var hull = CreateHull(ship);
            DrawColoredPolygon(hull, new Color("17273b"));
            DrawPolyline(hull.Append(hull[0]).ToArray(), selected ? Colors.White : teamColor,
                selected ? 3 : 1.7f, true);
        }
        DrawSetTransform(Vector2.Zero, 0);

        if (selected)
        {
            DrawArc(position, (float)ship.Stats.Radius + 14, 0, Mathf.Tau, 64, Colors.White, 2);
        }
        if (ship.ShieldRatio > 0.05)
            DrawArc(position, (float)ship.Stats.Radius + 8, -0.8f, 0.8f, 28,
                new(teamColor, (float)(0.12 + ship.ShieldRatio * 0.28)), 3);
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
        if (_settings.ReduceFlashes) life *= 0.42f;
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
        DrawLabel($"MISSION {MissionCatalog.IndexOf(_simulation.Mission.Id) + 1}  •  {_simulation.Mission.Title.ToUpperInvariant()}",
            new(280, 44), 12, new Color("8bbdd2"), HorizontalAlignment.Center, 210);

        DrawFactionBar(new(520, 18), 245, (float)(_simulation.FleetStrength(Team.Player) /
            Math.Max(0.01, _simulation.InitialPlayerStrength)),
            "ANDROMEDA FLEET", Cyan);
        DrawFactionBar(new(835, 18), 245, (float)(_simulation.FleetStrength(Team.Enemy) /
            Math.Max(0.01, _simulation.InitialEnemyStrength)),
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
        if (_simulation.Mission.Id == MissionId.FirstCommand && !_showHelp &&
            (!_tutorial.IsComplete || _tutorialCelebrationTime > 0))
            DrawTutorialCoach();
        if (_settings.Subtitles && _audioCaptionTime > 0)
        {
            DrawPanel(new(530, 681, 540, 36));
            DrawLabel($"♪  {_audioCaption}", new(800, 705), 13, Colors.White,
                HorizontalAlignment.Center, 510);
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
        DrawPanel(new(1270, 104, 308, 122));
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
        if (_showSettings)
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
            DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.75f));
            DrawPanel(new(370, 120, 860, 650));
            DrawLabel($"MISSION {MissionCatalog.IndexOf(_simulation.Mission.Id) + 1}  •  {_simulation.Mission.Title.ToUpperInvariant()}",
                new(800, 178), 29, Colors.White,
                HorizontalAlignment.Center, 700);
            DrawLabel(_simulation.Mission.Subtitle, new(800, 210), 16,
                new Color("99d3e9"), HorizontalAlignment.Center, 700);
            DrawLabel(_simulation.Mission.Briefing, new(800, 253), 14, new Color("dce7ee"),
                HorizontalAlignment.Center, 750);
            var controls = new[]
            {
                ("1–4 / TAB", "Switch controlled ship"),
                ("W S / A D", "Thrust and rotate"),
                ("SPACE", "Fire at nearest target"),
                ("ENTER", "Type a natural-language fleet order"),
                ("Q", "Use the selected ship’s tactical ability"),
                ("V", "Local voice command adapter"),
                ("P / H / R", "Pause, help, restart"),
                ("M", "Open mission selection"),
                ("L", "Open local AI setup"),
                ("F10 / PAD BACK", "Settings and accessibility")
            };
            var y = 315;
            foreach (var (key, description) in controls)
            {
                DrawLabel(key, new(490, y), 14, Cyan);
                DrawLabel(description, new(700, y), 15, new Color("dce7ee"));
                y += 34;
            }
            DrawLabel($"Try: “{_simulation.Mission.RecommendedOrder}”", new(800, 675), 14,
                new Color("ffd065"), HorizontalAlignment.Center, 700);
            DrawLabel("Press H to enter the battle", new(800, 724), 13,
                new Color("87b5ca"), HorizontalAlignment.Center, 700);
        }
        else if (_paused && _simulation.Status == BattleStatus.Active)
        {
            DrawBanner("PAUSED", "Press P to return to the battle", Cyan);
        }
        else if (_simulation.Status == BattleStatus.PlayerVictory)
        {
            var index = MissionCatalog.IndexOf(_simulation.Mission.Id);
            var next = index + 1 < MissionCatalog.All.Count
                ? "Press N for the next mission • R to replay • M for mission select"
                : "Campaign demo complete • R to replay • M for mission select";
            DrawBanner("VICTORY", next, new Color("48eba9"));
        }
        else if (_simulation.Status == BattleStatus.EnemyVictory)
        {
            DrawBanner("MISSION FAILED", "A protected ship was lost • Press R to try again", Red);
        }
    }

    private void DrawTutorialBriefing()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.84f));
        DrawPanel(new(270, 135, 1060, 630));
        DrawLabel("CAPTAIN'S DRILL", new(800, 205), 38, Colors.White,
            HorizontalAlignment.Center, 900);
        DrawLabel("Four actions. About sixty seconds. Learn by commanding.", new(800, 242), 16,
            Cyan, HorizontalAlignment.Center, 900);
        DrawLabel("Raiders have trapped a civilian convoy. Master the fleet, then disable their leader.",
            new(800, 282), 14, new Color("c9dce6"), HorizontalAlignment.Center, 920);

        for (var index = 0; index < TutorialTracker.Steps.Count; index++)
        {
            var step = TutorialTracker.Steps[index];
            var completed = index < _tutorial.CompletedSteps;
            var active = index == _tutorial.CompletedSteps;
            var x = 332 + index * 238;
            var color = completed ? new Color("48eba9") : active ? new Color("ffd065") : Cyan;
            DrawPanel(new(x, 335, 214, 230));
            DrawCircle(new(x + 107, 382), 23, new(color, 0.2f));
            DrawArc(new(x + 107, 382), 23, 0, Mathf.Tau, 36, color, 2);
            DrawLabel(completed ? "✓" : $"{index + 1}", new(x + 107, 390), 18, color,
                HorizontalAlignment.Center, 30);
            DrawLabel(step.Title.ToUpperInvariant(), new(x + 107, 435), 15, Colors.White,
                HorizontalAlignment.Center, 190);
            DrawLabel(step.Action switch
            {
                TutorialAction.SwitchShip => "TAB  /  LB–RB",
                TutorialAction.ManualControl => "WASD  /  STICK",
                TutorialAction.IssueOrder => "ENTER  /  VOICE",
                _ => "Q  /  B"
            }, new(x + 107, 475), 14, color, HorizontalAlignment.Center, 190);
            DrawLabel(step.Purpose, new(x + 107, 523), 12, new Color("9fc5d6"),
                HorizontalAlignment.Center, 190);
        }

        DrawLabel($"Objective after training: {_simulation.Mission.Objective.Title}", new(800, 628), 16,
            new Color("ffd065"), HorizontalAlignment.Center, 900);
        DrawLabel(_lastInputWasController ? "Press A or START to deploy" : "Press H to deploy",
            new(800, 700), 18, Colors.White, HorizontalAlignment.Center, 900);
        DrawLabel("The battle is paused while this briefing is open", new(800, 730), 12,
            new Color("789bac"), HorizontalAlignment.Center, 900);
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
            DrawLabel("CAPTAIN CERTIFIED", new(800, 174), 19, border,
                HorizontalAlignment.Center, 690);
            DrawLabel("Training complete • Destroy the raider leader", new(800, 207), 14,
                Colors.White, HorizontalAlignment.Center, 690);
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
        DrawLabel(step.Title.ToUpperInvariant(), new(800, 183), 16, border,
            HorizontalAlignment.Center, 690);
        DrawLabel(_tutorial.GetPrompt(_lastInputWasController), new(800, 207), 14, Colors.White,
            HorizontalAlignment.Center, 690);
        DrawLabel(step.Purpose, new(800, 226), 11, new Color("8db6c9"),
            HorizontalAlignment.Center, 690);
    }

    private void DrawMissionSelect()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.82f));
        DrawPanel(new(390, 130, 820, 620));
        DrawLabel("CAMPAIGN MISSIONS", new(800, 198), 34, Colors.White,
            HorizontalAlignment.Center, 700);
        DrawLabel("Three escalating fleet-command engagements", new(800, 230), 15,
            new Color("99d3e9"), HorizontalAlignment.Center, 700);

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
            DrawLabel(unlocked ? mission.Subtitle : "LOCKED — complete the previous mission",
                new(545, y + 25), 13, unlocked ? new Color("9bc9dc") : new Color("66727a"));
            if (completed) DrawLabel("COMPLETE", new(1010, y + 12), 13, new Color("48eba9"));
        }

        DrawLabel("Press 1–3 to deploy • M or Esc to close", new(800, 690), 14,
            new Color("ffd065"), HorizontalAlignment.Center, 700);
    }

    private void DrawLocalAiSetup()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.86f));
        DrawPanel(new(350, 105, 900, 690));
        DrawLabel("LOCAL AI CONTROL CENTER", new(800, 175), 32, Colors.White,
            HorizontalAlignment.Center, 780);
        DrawLabel("Runtime commands stay on this computer", new(800, 208), 15, Cyan,
            HorizontalAlignment.Center, 780);

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
                    : "Press W for the model; install whisper-cli to enable recording");

        DrawLabel(_localAiBusy ? "WORKING — this may take several minutes" : _localAiReadiness.Detail,
            new(800, 625), 14, _localAiBusy ? new Color("ffd065") : new Color("b9d9e7"),
            HorizontalAlignment.Center, 780);
        DrawLabel("O  Install model     G  GPU/CPU mode     W  Speech model     R  Rescan",
            new(800, 690), 15, new Color("ffd065"), HorizontalAlignment.Center, 780);
        DrawLabel("L or Esc to close", new(800, 735), 13, new Color("87b5ca"),
            HorizontalAlignment.Center, 780);
    }

    private void DrawSettings()
    {
        DrawRect(new(0, 0, 1600, 900), new Color(0, 0.015f, 0.04f, 0.87f));
        DrawPanel(new(390, 115, 820, 670));
        DrawLabel("SETTINGS & ACCESSIBILITY", new(800, 185), 31, Colors.White,
            HorizontalAlignment.Center, 700);
        DrawLabel("Changes save immediately", new(800, 216), 14, Cyan,
            HorizontalAlignment.Center, 700);

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
        DrawLabel("Controller: left stick fly • A fire • B ability • shoulders switch ship",
            new(800, 707), 13, new Color("9bc9dc"), HorizontalAlignment.Center, 700);
        DrawLabel("F10 or Esc to close", new(800, 750), 13, new Color("87b5ca"),
            HorizontalAlignment.Center, 700);
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

    private static string SplitPascalCase(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

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
    private readonly record struct Star(Vector2 Position, float Size, float Alpha, bool Blue);
}
