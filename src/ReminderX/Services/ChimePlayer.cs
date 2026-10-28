using System.IO;
using System.Media;

namespace ReminderX.Services;

/// <summary>
/// The alert chime: two soft bell-like notes synthesized in memory. Built in rather than using a
/// Windows system sound, because many sound schemes leave those silent.
/// </summary>
public static class ChimePlayer
{
    private const int SampleRate = 44100;
    private static readonly Lazy<byte[]> Wav = new(BuildWav);
    private static SoundPlayer? _player;

    public static void Play()
    {
        try
        {
            _player ??= new SoundPlayer(new MemoryStream(Wav.Value));
            _player.Stream!.Position = 0;
            _player.Play(); // asynchronous
        }
        catch (Exception ex)
        {
            Logger.Log($"Chime failed to play: {ex.Message}");
        }
    }

    public static byte[] BuildWav()
    {
        const double durationSeconds = 1.3;
        var sampleCount = (int)(SampleRate * durationSeconds);
        var samples = new double[sampleCount];

        AddNote(samples, frequency: 880.00, startSeconds: 0.00);   // A5
        AddNote(samples, frequency: 1318.51, startSeconds: 0.16);  // E6

        var peak = samples.Max(Math.Abs);
        var gain = peak > 0 ? 0.55 / peak : 0;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataSize = sampleCount * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                 // fmt chunk size
        writer.Write((short)1);           // PCM
        writer.Write((short)1);           // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);     // byte rate
        writer.Write((short)2);           // block align
        writer.Write((short)16);          // bits per sample
        writer.Write("data"u8);
        writer.Write(dataSize);

        foreach (var s in samples)
            writer.Write((short)Math.Clamp(s * gain * short.MaxValue, short.MinValue, short.MaxValue));

        writer.Flush();
        return stream.ToArray();
    }

    private static void AddNote(double[] samples, double frequency, double startSeconds)
    {
        var start = (int)(startSeconds * SampleRate);
        for (var i = start; i < samples.Length; i++)
        {
            var t = (i - start) / (double)SampleRate;
            var attack = Math.Min(1.0, t / 0.006);   // 6 ms fade-in avoids a click
            var decay = Math.Exp(-t / 0.32);         // bell-like ring-out
            var tone = Math.Sin(2 * Math.PI * frequency * t)
                       + 0.25 * Math.Sin(2 * Math.PI * frequency * 2 * t) * Math.Exp(-t / 0.12)
                       + 0.08 * Math.Sin(2 * Math.PI * frequency * 3 * t) * Math.Exp(-t / 0.06);
            samples[i] += tone * attack * decay;
        }
    }
}
