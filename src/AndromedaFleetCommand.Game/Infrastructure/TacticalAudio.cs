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
        var data = RenderSoundtrack();
        var frameCount = data.Length / (sizeof(short) * 2);
        _ambientStream = CreateStream(data);
        _ambientStream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        _ambientStream.LoopBegin = 0;
        _ambientStream.LoopEnd = frameCount;
        // Tactical cues sit well in front of this bus, so the hook remains energizing without
        // obscuring command acknowledgements or combat information.
        _ambient = new() { Stream = _ambientStream, VolumeDb = -16 };
        owner.AddChild(_ambient);
        _ambient.Play();
    }

    private static byte[] RenderSoundtrack()
    {
        // "Signal Across Andromeda" — an original 16-bar synth theme at 120 BPM. The arrangement
        // deliberately leaves alternate phrases open so dense battles still have breathing room.
        const int seconds = 32;
        int[] roots = [45, 48, 43, 50]; // A minor, C major, G major, D suspended
        int[][] chordIntervals = [[0, 3, 7, 10], [0, 4, 7, 11], [0, 4, 7, 9], [0, 5, 7, 10]];
        int[] hook = [76, -1, 72, 74, 76, 79, 76, -1, 74, 72, 69, 72, 74, 76, 72, -1];
        var frameCount = SampleRate * seconds;
        var data = new byte[frameCount * sizeof(short) * 2];
        var percussionRandom = new Random(0x51A1AFC);
        var smoothedNoise = 0d;

        for (var index = 0; index < frameCount; index++)
        {
            var time = (double)index / SampleRate;
            var beat = time * 2; // 120 BPM
            var bar = (int)(beat / 4) % 16;
            var beatInBar = beat % 4;
            var chordIndex = bar % roots.Length;
            var root = roots[chordIndex];
            var intervals = chordIntervals[chordIndex];
            var barEdge = Math.Min(1, Math.Min(beatInBar * 7, (4 - beatInBar) * 7));

            // Wide, slow chord bed. Each chord fades at its boundary, including the loop seam.
            var padLeft = 0d;
            var padRight = 0d;
            for (var voice = 0; voice < intervals.Length; voice++)
            {
                var frequency = MidiFrequency(root + intervals[voice] + 12);
                var movement = Math.Sin(time * Math.Tau * (0.07 + voice * 0.011)) * 0.008;
                var tone = Math.Sin(time * Math.Tau * frequency * (1 + movement)) +
                           Math.Sin(time * Math.Tau * frequency * 2.001) * 0.12;
                var pan = (voice - 1.5) * 0.18;
                padLeft += tone * (1 - pan);
                padRight += tone * (1 + pan);
            }
            padLeft *= 0.027 * barEdge;
            padRight *= 0.027 * barEdge;

            // Eighth-note arpeggio is the constant navigational pulse.
            var eighth = beat * 2;
            var eighthIndex = (int)eighth;
            var eighthLocal = eighth - eighthIndex;
            var arpDegree = (eighthIndex + bar / 4) % intervals.Length;
            var arpFrequency = MidiFrequency(root + intervals[arpDegree] + 24);
            var arpEnvelope = NoteEnvelope(eighthLocal, 0.7);
            var arp = (Math.Sin(time * Math.Tau * arpFrequency) +
                       Math.Sin(time * Math.Tau * arpFrequency * 2) * 0.16) * arpEnvelope * 0.1;
            var arpPan = Math.Sin(eighthIndex * 1.7) * 0.48;

            // A warm quarter-note bass gives thrust without competing with weapon transients.
            var quarterIndex = (int)beat;
            var quarterLocal = beat - quarterIndex;
            var bassFrequency = MidiFrequency(root - 12 + (quarterIndex % 4 == 3 ? 7 : 0));
            var bassEnvelope = NoteEnvelope(quarterLocal, 1.45);
            var bass = (Math.Sin(time * Math.Tau * bassFrequency) +
                        Math.Sin(time * Math.Tau * bassFrequency * 2) * 0.23) * bassEnvelope * 0.14;

            // The two-bar melodic call sign enters every other phrase and gains an octave answer
            // near the end of the loop, making the motif recognizable without becoming relentless.
            var phraseBar = bar % 4;
            var hookActive = phraseBar >= 2;
            var hookStep = ((bar % 2) * 8 + (int)(beatInBar * 2)) % hook.Length;
            var hookNote = hook[hookStep];
            var hookSignal = 0d;
            if (hookActive && hookNote >= 0)
            {
                if (bar >= 14 && hookStep is 4 or 5) hookNote += 12;
                var hookFrequency = MidiFrequency(hookNote);
                hookSignal = (Math.Sin(time * Math.Tau * hookFrequency) * 0.78 +
                              Math.Sin(time * Math.Tau * hookFrequency * 2.003) * 0.2) *
                             NoteEnvelope(eighthLocal, 0.58) * 0.13;
            }
            var hookPan = Math.Sin(beat * Math.PI * 0.25) * 0.24;

            // Restrained electronic percussion: heartbeat kick, backbeat, and quiet hats.
            smoothedNoise = smoothedNoise * 0.35 + (percussionRandom.NextDouble() * 2 - 1) * 0.65;
            var kickPhase = quarterLocal;
            var kick = Math.Sin(Math.Tau * (46 * kickPhase + 34 * Math.Sqrt(kickPhase))) *
                       Math.Exp(-quarterLocal * 10) * 0.16;
            var beatNumber = quarterIndex % 4;
            var snare = beatNumber is 1 or 3
                ? smoothedNoise * Math.Exp(-quarterLocal * 14) * 0.065
                : 0;
            var hat = smoothedNoise * Math.Exp(-eighthLocal * 28) * 0.018;

            var left = padLeft + bass + arp * (1 - arpPan) + hookSignal * (1 - hookPan) + kick + snare + hat;
            var right = padRight + bass + arp * (1 + arpPan) + hookSignal * (1 + hookPan) + kick + snare - hat * 0.5;
            WriteStereo(data, index, Math.Tanh(left * 0.82), Math.Tanh(right * 0.82));
        }
        return data;
    }

    private static double MidiFrequency(int note) => 440 * Math.Pow(2, (note - 69) / 12d);

    private static double NoteEnvelope(double progress, double decay) =>
        Math.Sin(Math.PI * Math.Min(1, progress * 12)) * Math.Pow(Math.Max(0, 1 - progress), decay);

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
