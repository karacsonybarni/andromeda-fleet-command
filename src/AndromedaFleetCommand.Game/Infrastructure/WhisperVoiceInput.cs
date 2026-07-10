using System.Diagnostics;
using Godot;

namespace AndromedaFleetCommand.Game.Infrastructure;

public sealed class WhisperVoiceInput : IDisposable
{
    private const string BusName = "AfcMicrophone";
    private readonly Node _owner;
    private readonly string? _executable;
    private readonly string? _model;
    private readonly int _busIndex = -1;
    private readonly AudioEffectRecord? _recordEffect;
    private readonly AudioStreamPlayer? _microphonePlayer;

    public WhisperVoiceInput(Node owner)
    {
        _owner = owner;
        _executable = System.Environment.GetEnvironmentVariable("AFC_WHISPER_CLI");
        _model = System.Environment.GetEnvironmentVariable("AFC_WHISPER_MODEL");
        if (string.IsNullOrWhiteSpace(_executable) || string.IsNullOrWhiteSpace(_model) ||
            !File.Exists(_executable) || !File.Exists(_model))
        {
            return;
        }
        _busIndex = AudioServer.BusCount;
        AudioServer.AddBus(_busIndex);
        AudioServer.SetBusName(_busIndex, BusName);
        AudioServer.SetBusMute(_busIndex, true);
        _recordEffect = new() { Format = AudioStreamWav.FormatEnum.Format16Bits };
        AudioServer.AddBusEffect(_busIndex, _recordEffect);
        _microphonePlayer = new()
        {
            Stream = new AudioStreamMicrophone(),
            Bus = BusName,
            Autoplay = true
        };
        owner.AddChild(_microphonePlayer);
    }

    public bool IsAvailable =>
        _recordEffect is not null && _microphonePlayer is not null;

    public string UnavailableReason =>
        "Voice needs local whisper.cpp: set AFC_WHISPER_CLI and AFC_WHISPER_MODEL";

    public async Task<string> CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        _recordEffect!.SetRecordingActive(true);
        try
        {
            await _owner.ToSignal(_owner.GetTree().CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        }
        finally
        {
            _recordEffect.SetRecordingActive(false);
        }

        var recording = _recordEffect!.GetRecording();
        var directory = ProjectSettings.GlobalizePath("user://voice");
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, $"command-{Guid.NewGuid():N}");
        var saveError = recording.SaveToWav(basePath);
        if (saveError != Error.Ok) throw new IOException($"Could not save microphone recording: {saveError}");
        var wavePath = basePath + ".wav";
        try
        {
            using var process = new Process
            {
                StartInfo = new()
                {
                    FileName = _executable!,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-m");
            process.StartInfo.ArgumentList.Add(_model!);
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add(wavePath);
            process.StartInfo.ArgumentList.Add("-nt");
            process.StartInfo.ArgumentList.Add("-np");
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode != 0) throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "whisper.cpp failed" : error);
            var transcript = System.Text.RegularExpressions.Regex
                .Replace(output, "\\[[^]]*]", " ")
                .ReplaceLineEndings(" ")
                .Trim();
            if (string.IsNullOrWhiteSpace(transcript)) throw new InvalidOperationException("No speech detected");
            return transcript;
        }
        finally
        {
            if (File.Exists(wavePath)) File.Delete(wavePath);
        }
    }

    public void Dispose()
    {
        if (_microphonePlayer is not null && GodotObject.IsInstanceValid(_microphonePlayer))
        {
            _microphonePlayer.Stop();
            _microphonePlayer.Stream = null;
            _microphonePlayer.Free();
        }
        if (_busIndex >= 0 && _busIndex < AudioServer.BusCount) AudioServer.RemoveBus(_busIndex);
    }
}
