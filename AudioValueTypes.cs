// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// Interleaved 32-bit float PCM, the only audio format the ABI produces.
/// </summary>
/// <param name="Samples">Interleaved samples; length is frames * channels.</param>
/// <param name="SampleRate">Samples per second.</param>
/// <param name="Channels">Channel count.</param>
public sealed record AudioData(float[] Samples, int SampleRate, int Channels)
{
    private static readonly AudioData s_empty = new(Array.Empty<float>(), 0, 0);

    public static AudioData Empty => s_empty;

    /// <summary>Frames per channel.</summary>
    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;

    public TimeSpan Duration => SampleRate > 0
        ? TimeSpan.FromSeconds((double)FrameCount / SampleRate)
        : TimeSpan.Zero;

    public bool IsEmpty => Samples.Length == 0;
}

/// <summary>One transcript segment. Sample indices are relative to the request audio.</summary>
public sealed record AudioSegment(long StartSample, long EndSample, float Confidence, string Text);

/// <summary>One word with its own timing, for the families that align.</summary>
public sealed record AudioWord(string Word, long StartSample, long EndSample, float Confidence);

/// <summary>One speaker turn, from a diarization family.</summary>
public sealed record AudioSpeakerTurn(
    long StartSample,
    long EndSample,
    string SpeakerId,
    float Confidence,
    string Text);

/// <summary>
/// A named output stream - source separation stems, per-speaker TTS, and anything
/// else that emits more than one signal per run.
/// </summary>
public sealed record AudioNamedAudio(string Id, AudioData Audio);

/// <summary>
/// An opaque payload attached to a result. Feed the bytes back through
/// <see cref="AudioRequest.AddArtifact"/> to reuse derived state - a speaker
/// embedding, acoustic tokens, diarization state.
/// </summary>
public sealed record AudioArtifact(
    AudioArtifactKind Kind,
    string Id,
    byte[] Payload,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// One declared option, read straight off the model's spec. This is what lets a
/// picker enumerate what a family accepts instead of hardcoding per-family
/// knowledge; values the model did not declare come back as empty strings.
/// </summary>
public sealed record AudioModelOption(
    string Name,
    string ValueName,
    string Description,
    string DefaultValue,
    string MinValue,
    string MaxValue,
    bool Required);

/// <summary>
/// What a streaming session expects and emits. Push
/// <see cref="PreferredChunkSamples"/> frames per call where you can.
/// </summary>
public sealed record AudioStreamPolicy(
    AudioStreamInputKind Input,
    AudioStreamOutputKind Output,
    long PreferredChunkSamples,
    double PreferredChunkSeconds);

/// <summary>A voice-activity marker inside a streaming event.</summary>
public sealed record AudioVoiceActivity(AudioVoiceActivityKind Kind, long Sample, float Probability);
