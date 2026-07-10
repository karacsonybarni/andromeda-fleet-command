using Godot;

namespace AndromedaFleetCommand.Game.Infrastructure;

public enum TacticalCue
{
    Acknowledgement,
    Weapon,
    Ability,
    Alert,
    Victory
}

public sealed class TacticalAudio(Node owner)
{
    private const int SampleRate = 22_050;

    public void Play(TacticalCue cue)
    {
        var (frequency, endFrequency, duration, volume) = cue switch
        {
            TacticalCue.Acknowledgement => (520f, 760f, 0.16f, 0.20f),
            TacticalCue.Weapon => (170f, 82f, 0.10f, 0.24f),
            TacticalCue.Ability => (310f, 980f, 0.34f, 0.24f),
            TacticalCue.Alert => (250f, 250f, 0.42f, 0.18f),
            TacticalCue.Victory => (440f, 880f, 0.62f, 0.20f),
            _ => (440f, 440f, 0.15f, 0.18f)
        };
        var sampleCount = Math.Max(1, (int)(SampleRate * duration));
        var data = new byte[sampleCount * sizeof(short)];
        var phase = 0d;
        for (var index = 0; index < sampleCount; index++)
        {
            var progress = (float)index / sampleCount;
            var envelope = MathF.Sin(MathF.PI * MathF.Min(1, progress * 5)) *
                           MathF.Pow(1 - progress, 1.4f);
            var sweep = Mathf.Lerp(frequency, endFrequency, progress);
            if (cue == TacticalCue.Alert) sweep *= index / (SampleRate / 12) % 2 == 0 ? 1f : 1.3f;
            phase += Math.Tau * sweep / SampleRate;
            var harmonic = Math.Sin(phase) + Math.Sin(phase * 2.01) * 0.22;
            var sample = (short)Math.Clamp(harmonic * envelope * volume * short.MaxValue,
                short.MinValue, short.MaxValue);
            data[index * 2] = (byte)(sample & 0xff);
            data[index * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        var player = new AudioStreamPlayer
        {
            Stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = SampleRate,
                Stereo = false,
                Data = data
            },
            VolumeDb = -4
        };
        owner.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }
}
