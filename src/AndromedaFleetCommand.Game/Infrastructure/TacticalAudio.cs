using Godot;

namespace AndromedaFleetCommand.Game.Infrastructure;

public enum TacticalCue
{
    Acknowledgement,
    Weapon,
    Impact,
    Destruction,
    Ability,
    Alert,
    Victory,
    Defeat
}

/// <summary>
/// Generates a compact tactical soundscape at runtime. Keeping the cues procedural makes the
/// game redistributable without licensed samples while still providing layered, varied feedback.
/// </summary>
public sealed class TacticalAudio(Node owner)
{
    private const int SampleRate = 44_100;
    private readonly Random _random = new(0xAFC2026);
    private readonly Dictionary<TacticalCue, ulong> _lastPlayedAt = [];
    private AudioStreamPlayer? _ambient;
    private AudioStreamWav? _ambientStream;

    public void StartAmbient()
    {
        if (_ambient is not null) return;
        const int seconds = 4;
        var frameCount = SampleRate * seconds;
        var data = new byte[frameCount * sizeof(short) * 2];
        var noiseState = 0d;

        for (var index = 0; index < frameCount; index++)
        {
            var time = (double)index / SampleRate;
            // Integer-cycle oscillators make the loop seamless. A filtered noise floor gives the
            // bridge some air without masking tactical cues.
            noiseState = noiseState * 0.985 + (_random.NextDouble() * 2 - 1) * 0.015;
            var pulse = 0.74 + Math.Sin(time * Math.Tau * 0.25) * 0.1;
            var left = (Math.Sin(time * Math.Tau * 43) * 0.48 +
                        Math.Sin(time * Math.Tau * 67) * 0.2 + noiseState * 0.22) * pulse;
            var right = (Math.Sin(time * Math.Tau * 47) * 0.46 +
                         Math.Sin(time * Math.Tau * 71) * 0.19 + noiseState * 0.2) * pulse;
            WriteStereo(data, index, left * 0.12, right * 0.12);
        }

        _ambientStream = CreateStream(data);
        _ambientStream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        _ambientStream.LoopBegin = 0;
        _ambientStream.LoopEnd = frameCount;
        _ambient = new() { Stream = _ambientStream, VolumeDb = -20 };
        owner.AddChild(_ambient);
        _ambient.Play();
    }

    public void StopAmbient()
    {
        if (_ambient is null || !GodotObject.IsInstanceValid(_ambient)) return;
        _ambient.Stop();
        _ambient.Stream = null;
        _ambient.Free();
        _ambient = null;
        _ambientStream?.Dispose();
        _ambientStream = null;
    }

    public void Play(TacticalCue cue)
    {
        var now = Time.GetTicksMsec();
        var minimumGap = cue switch
        {
            TacticalCue.Weapon => 65UL,
            TacticalCue.Impact => 55UL,
            TacticalCue.Destruction => 120UL,
            _ => 35UL
        };
        if (_lastPlayedAt.TryGetValue(cue, out var previous) && now - previous < minimumGap) return;
        _lastPlayedAt[cue] = now;

        var duration = cue switch
        {
            TacticalCue.Acknowledgement => 0.24,
            TacticalCue.Weapon => 0.16,
            TacticalCue.Impact => 0.2,
            TacticalCue.Destruction => 0.72,
            TacticalCue.Ability => 0.48,
            TacticalCue.Alert => 0.62,
            TacticalCue.Victory => 0.9,
            TacticalCue.Defeat => 0.95,
            _ => 0.2
        };
        var stream = CreateStream(RenderCue(cue, duration));
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = -3 };
        owner.AddChild(player);
        player.Finished += () =>
        {
            player.Stream = null;
            stream.Dispose();
            player.QueueFree();
        };
        player.Play();
    }

    private byte[] RenderCue(TacticalCue cue, double duration)
    {
        var frameCount = Math.Max(1, (int)(SampleRate * duration));
        var data = new byte[frameCount * sizeof(short) * 2];
        var pitchVariation = 0.96 + _random.NextDouble() * 0.08;
        var fixedPan = _random.NextDouble() * 0.34 - 0.17;
        var phase = 0d;
        var phaseTwo = 0d;
        var filteredNoise = 0d;

        for (var index = 0; index < frameCount; index++)
        {
            var progress = (double)index / frameCount;
            var time = (double)index / SampleRate;
            filteredNoise = filteredNoise * 0.72 + (_random.NextDouble() * 2 - 1) * 0.28;
            var frequency = 440d;
            var envelope = 1d;
            var signal = 0d;

            switch (cue)
            {
                case TacticalCue.Acknowledgement:
                {
                    var secondTone = progress >= 0.48;
                    var local = secondTone ? (progress - 0.48) / 0.52 : progress / 0.48;
                    frequency = (secondTone ? 720 : 510) * pitchVariation + local * 95;
                    phase += Math.Tau * frequency / SampleRate;
                    envelope = Pulse(local) * (secondTone ? 0.8 : 1);
                    signal = Math.Sin(phase) + Math.Sin(phase * 2) * 0.13;
                    break;
                }
                case TacticalCue.Weapon:
                    frequency = (210 * Math.Pow(0.29, progress) + 48) * pitchVariation;
                    phase += Math.Tau * frequency / SampleRate;
                    envelope = FastAttackDecay(progress, 3.5);
                    signal = Math.Sin(phase) + Math.Sin(phase * 2.03) * 0.36 +
                             filteredNoise * Math.Pow(1 - progress, 7) * 0.75;
                    break;
                case TacticalCue.Impact:
                    frequency = (118 - progress * 58) * pitchVariation;
                    phase += Math.Tau * frequency / SampleRate;
                    envelope = FastAttackDecay(progress, 5);
                    signal = Math.Sin(phase) * 0.85 + filteredNoise * Math.Pow(1 - progress, 3) * 1.15;
                    break;
                case TacticalCue.Destruction:
                    frequency = (92 - progress * 54) * pitchVariation;
                    phase += Math.Tau * frequency / SampleRate;
                    phaseTwo += Math.Tau * (41 - progress * 16) / SampleRate;
                    envelope = FastAttackDecay(progress, 2.1);
                    var secondaryBlast = Math.Exp(-Math.Pow((progress - 0.29) / 0.1, 2));
                    signal = Math.Sin(phase) * 0.72 + Math.Sin(phaseTwo) * 0.6 +
                             filteredNoise * (Math.Pow(1 - progress, 2) + secondaryBlast * 0.75);
                    break;
                case TacticalCue.Ability:
                    frequency = (240 + progress * progress * 1_080) * pitchVariation;
                    phase += Math.Tau * frequency / SampleRate;
                    phaseTwo += Math.Tau * (frequency * 1.505) / SampleRate;
                    envelope = Math.Sin(Math.PI * Math.Min(1, progress * 5)) * Math.Pow(1 - progress, 0.58);
                    signal = Math.Sin(phase) * 0.68 + Math.Sin(phaseTwo) * 0.34 +
                             Math.Sin(time * Math.Tau * 38) * 0.12;
                    break;
                case TacticalCue.Alert:
                {
                    var pulseIndex = Math.Min(2, (int)(progress * 3));
                    var local = progress * 3 - pulseIndex;
                    frequency = (pulseIndex % 2 == 0 ? 238 : 318) * pitchVariation;
                    phase += Math.Tau * frequency / SampleRate;
                    envelope = Pulse(local) * Math.Pow(0.88, pulseIndex);
                    signal = Math.Sin(phase) + Math.Sin(phase * 2) * 0.22;
                    break;
                }
                case TacticalCue.Victory:
                    signal = RenderArpeggio(progress, pitchVariation, [440, 554.37, 659.25, 880], ref phase,
                        out envelope);
                    break;
                case TacticalCue.Defeat:
                    signal = RenderArpeggio(progress, pitchVariation, [440, 349.23, 261.63, 196], ref phase,
                        out envelope);
                    envelope *= 0.88;
                    break;
            }

            var gain = cue switch
            {
                TacticalCue.Weapon => 0.3,
                TacticalCue.Impact => 0.32,
                TacticalCue.Destruction => 0.36,
                TacticalCue.Alert => 0.25,
                _ => 0.27
            };
            var animatedPan = cue is TacticalCue.Ability or TacticalCue.Victory
                ? Math.Sin(progress * Math.Tau) * 0.35
                : fixedPan;
            var value = Math.Tanh(signal * envelope * gain);
            var left = value * Math.Sqrt(1 - animatedPan);
            var right = value * Math.Sqrt(1 + animatedPan);
            WriteStereo(data, index, left, right);
        }
        return data;
    }

    private static double RenderArpeggio(double progress, double pitchVariation, double[] notes,
        ref double phase, out double envelope)
    {
        var noteIndex = Math.Min(notes.Length - 1, (int)(progress * notes.Length));
        var local = progress * notes.Length - noteIndex;
        phase += Math.Tau * notes[noteIndex] * pitchVariation / SampleRate;
        envelope = Pulse(local) * Math.Pow(0.91, noteIndex);
        return Math.Sin(phase) + Math.Sin(phase * 2.002) * 0.18;
    }

    private static double Pulse(double progress) =>
        Math.Sin(Math.PI * Math.Min(1, progress * 4)) * Math.Pow(Math.Max(0, 1 - progress), 0.75);

    private static double FastAttackDecay(double progress, double decay) =>
        Math.Sin(Math.PI * Math.Min(1, progress * 10)) * Math.Pow(Math.Max(0, 1 - progress), decay);

    private static AudioStreamWav CreateStream(byte[] data) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = SampleRate,
        Stereo = true,
        Data = data
    };

    private static void WriteStereo(byte[] data, int frame, double left, double right)
    {
        WriteSample(data, frame * 4, left);
        WriteSample(data, frame * 4 + 2, right);
    }

    private static void WriteSample(byte[] data, int offset, double value)
    {
        var sample = (short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue);
        data[offset] = (byte)(sample & 0xff);
        data[offset + 1] = (byte)((sample >> 8) & 0xff);
    }
}
