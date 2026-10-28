// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// What one run produced. Which of the accessors have anything to say depends on
/// the task: generation and TTS fill <see cref="GetAudio"/>, ASR fills
/// <see cref="GetText"/> and <see cref="GetSegments"/>, separation fills
/// <see cref="GetNamedAudio"/>.
/// </summary>
/// <remarks>
/// Every accessor copies what it reads, so nothing here has to be called before
/// <see cref="Dispose"/> - but the copies are made on demand, not eagerly, so
/// reading the same collection twice reads native memory twice.
/// </remarks>
public sealed class AudioResult : IDisposable
{
    private readonly bool _ownsHandle;
    private IntPtr _handle;
    private bool _disposed;

    internal AudioResult(IntPtr handle, bool ownsHandle)
    {
        _handle = handle;
        _ownsHandle = ownsHandle;
    }

    internal IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>The generated or converted audio, or <see langword="null"/> when the task produced none.</summary>
    public AudioData? GetAudio()
    {
        var handle = Handle;
        var status = NativeMethods.audiocpp_result_audio(handle, out var samples, out var frames, out var sampleRate, out var channels);
        if (status == AudioStatus.NotAvailable)
        {
            return null;
        }

        NativeMethods.ThrowIfFailed(status);
        return new AudioData(NativeMethods.CopySamples(samples, frames, channels), sampleRate, channels);
    }

    /// <summary>The transcript, or <see langword="null"/> when the task produced none.</summary>
    public (string Text, string? Language)? GetText()
    {
        var handle = Handle;
        var status = NativeMethods.audiocpp_result_text(handle, out var text, out var language);
        if (status == AudioStatus.NotAvailable)
        {
            return null;
        }

        NativeMethods.ThrowIfFailed(status);
        return (NativeMethods.PtrToStringOrEmpty(text), NativeMethods.PtrToString(language));
    }

    /// <summary>Transcript segments with sample-accurate bounds.</summary>
    public IReadOnlyList<AudioSegment> GetSegments()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_result_segment_count(handle);
        if (count == 0)
        {
            return Array.Empty<AudioSegment>();
        }

        var segments = new List<AudioSegment>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_result_segment(
                handle, i, out var start, out var end, out var confidence, out var text);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            NativeMethods.ThrowIfFailed(status);
            segments.Add(new AudioSegment(start, end, confidence, NativeMethods.PtrToStringOrEmpty(text)));
        }

        return segments;
    }

    /// <summary>Word-level timings, for the families that align.</summary>
    public IReadOnlyList<AudioWord> GetWords()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_result_word_count(handle);
        if (count == 0)
        {
            return Array.Empty<AudioWord>();
        }

        var words = new List<AudioWord>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_result_word(
                handle, i, out var word, out var start, out var end, out var confidence);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            NativeMethods.ThrowIfFailed(status);
            words.Add(new AudioWord(NativeMethods.PtrToStringOrEmpty(word), start, end, confidence));
        }

        return words;
    }

    /// <summary>Speaker turns, from a diarization family.</summary>
    public IReadOnlyList<AudioSpeakerTurn> GetSpeakerTurns()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_result_speaker_turn_count(handle);
        if (count == 0)
        {
            return Array.Empty<AudioSpeakerTurn>();
        }

        var turns = new List<AudioSpeakerTurn>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_result_speaker_turn(
                handle, i, out var start, out var end, out var speakerId, out var confidence, out var text);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            NativeMethods.ThrowIfFailed(status);
            turns.Add(new AudioSpeakerTurn(
                start,
                end,
                NativeMethods.PtrToStringOrEmpty(speakerId),
                confidence,
                NativeMethods.PtrToStringOrEmpty(text)));
        }

        return turns;
    }

    /// <summary>
    /// Named streams: separation stems, per-speaker TTS, anything that emits more
    /// than one signal per run.
    /// </summary>
    public IReadOnlyList<AudioNamedAudio> GetNamedAudio()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_result_named_audio_count(handle);
        if (count == 0)
        {
            return Array.Empty<AudioNamedAudio>();
        }

        var streams = new List<AudioNamedAudio>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_result_named_audio(
                handle, i, out var id, out var samples, out var frames, out var sampleRate, out var channels);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            NativeMethods.ThrowIfFailed(status);
            streams.Add(new AudioNamedAudio(
                NativeMethods.PtrToStringOrEmpty(id),
                new AudioData(NativeMethods.CopySamples(samples, frames, channels), sampleRate, channels)));
        }

        return streams;
    }

    /// <summary>
    /// Opaque outputs to persist and feed back into a later request. This is how
    /// voice state is reused without re-deriving it.
    /// </summary>
    public IReadOnlyList<AudioArtifact> GetArtifacts()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_result_artifact_count(handle);
        if (count == 0)
        {
            return Array.Empty<AudioArtifact>();
        }

        var artifacts = new List<AudioArtifact>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_result_artifact(
                handle, i, out var kind, out var id, out var payload, out var payloadBytes);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            NativeMethods.ThrowIfFailed(status);

            var bytes = new byte[(int)payloadBytes];
            if (payloadBytes > 0 && payload != IntPtr.Zero)
            {
                Marshal.Copy(payload, bytes, 0, bytes.Length);
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            var metaCount = NativeMethods.audiocpp_result_artifact_meta_count(handle, i);
            for (nuint m = 0; m < metaCount; m++)
            {
                var metaStatus = NativeMethods.audiocpp_result_artifact_meta(handle, i, m, out var key, out var value);
                if (metaStatus != AudioStatus.Ok)
                {
                    continue;
                }

                metadata[NativeMethods.PtrToStringOrEmpty(key)] = NativeMethods.PtrToStringOrEmpty(value);
            }

            artifacts.Add(new AudioArtifact(kind, NativeMethods.PtrToStringOrEmpty(id), bytes, metadata));
        }

        return artifacts;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            // A result obtained from audiocpp_event_as_result is owned by the event
            // and must not be freed here.
            if (_ownsHandle)
            {
                NativeMethods.audiocpp_result_free(_handle);
            }

            _handle = IntPtr.Zero;
        }
    }
}
