// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

using System.Text;

namespace Season.Audio;

/// <summary>
/// Encodes what the ABI produces - interleaved float32 PCM - into the one container
/// every host on the fleet can already play: 16-bit PCM WAV.
/// </summary>
/// <remarks>
/// <para>Lives in this package rather than in a panel because it is a property of the
/// data, not of a UI: <see cref="AudioData"/> is the only thing the C ABI emits, and a
/// host that wants a file has to make the same conversion. <c>SeasonStudio</c> is the
/// first consumer, not the only one.</para>
///
/// <para><b>The trailing-silence rule is measured, not assumed.</b> ACE-Step stops
/// producing content well before the requested duration and then leaves a noise floor
/// around 0.0009 RMS - not digital zero. A "sample == 0" trim therefore removes
/// nothing; the threshold below is three times that floor, and the kept tail is a
/// quarter second so the release is not cut mid-decay.</para>
///
/// <para>Samples are clamped before narrowing: the model may return values slightly
/// above 1.0, and an unclamped cast wraps to the opposite polarity - an audible click
/// instead of a rounded peak.</para>
/// </remarks>
public static class AudioWav
{
    /// <summary>Three times the measured ACE-Step noise floor (see remarks).</summary>
    private const float SilenceFloor = 3e-3f;

    /// <summary>Audio kept after the last audible frame, so a decay is not clipped.</summary>
    private const double KeepSeconds = 0.25;

    /// <param name="audio">Interleaved float32 PCM, as <see cref="AudioResult.GetAudio"/> returns.</param>
    /// <param name="trimTrailingSilence">
    /// Drops the noise floor after the last audible frame. Turn it off to keep the
    /// exact length the model reported (useful when comparing runs).
    /// </param>
    public static byte[] Encode(AudioData audio, bool trimTrailingSilence = true)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var channels = Math.Max(1, audio.Channels);
        var frames = audio.FrameCount;

        if (trimTrailingSilence)
        {
            frames = AudibleFrames(audio.Samples, frames, channels, audio.SampleRate);
        }

        var dataBytes = frames * channels * sizeof(short);

        using var stream = new MemoryStream(44 + dataBytes);

        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            var byteRate = audio.SampleRate * channels * sizeof(short);
            var blockAlign = (short)(channels * sizeof(short));

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                   // PCM fmt chunk size
            writer.Write((short)1);             // PCM
            writer.Write((short)channels);
            writer.Write(audio.SampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)16);            // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            var total = frames * channels;

            for (var i = 0; i < total; i++)
            {
                writer.Write((short)(Math.Clamp(audio.Samples[i], -1f, 1f) * short.MaxValue));
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Frames up to and including the last one whose per-frame RMS clears the floor,
    /// plus <see cref="KeepSeconds"/> of tail. An entirely silent buffer is returned
    /// untouched: shortening it to nothing would turn "the model produced silence"
    /// into "the model produced no audio".
    /// </summary>
    private static int AudibleFrames(float[] samples, int frames, int channels, int sampleRate)
    {
        var lastAudible = -1;

        for (var frame = 0; frame < frames; frame++)
        {
            var energy = 0f;

            for (var c = 0; c < channels; c++)
            {
                var value = samples[(frame * channels) + c];
                energy += value * value;
            }

            if (MathF.Sqrt(energy / channels) > SilenceFloor)
            {
                lastAudible = frame;
            }
        }

        if (lastAudible < 0 || sampleRate <= 0)
        {
            return frames;
        }

        var keep = lastAudible + 1 + (int)(KeepSeconds * sampleRate);

        return Math.Min(frames, keep);
    }
}
