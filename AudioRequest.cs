// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// One unit of work handed to <see cref="AudioSession.Run"/>: what to say or what to
/// listen to, plus whatever conditioning the family declares.
/// </summary>
/// <remarks>
/// A request is not reusable across runs: it is native state that a session reads
/// (and, for streaming, consumes). Build a fresh one per call - it is cheap, and
/// every setter copies its payload, so no managed buffer has to outlive the call.
/// </remarks>
public sealed class AudioRequest : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    public AudioRequest()
    {
        _handle = NativeMethods.audiocpp_request_create();
        if (_handle == IntPtr.Zero)
        {
            throw new AudioCppException(AudioStatus.OutOfMemory, "audiocpp_request_create returned NULL");
        }
    }

    internal IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>
    /// Sets the text, and - when <paramref name="language"/> is given - both the
    /// transcript language and <c>options["language"]</c>, which is what the CLI's
    /// <c>--language</c> does. Some families read only the option, so the two travel
    /// together. Use <see cref="SetTextLanguage"/> when they must not.
    /// </summary>
    public AudioRequest SetText(string text, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var textPointer = strings.Add(text);
        var languagePointer = strings.Add(language);
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_text(handle, textPointer, languagePointer));
        return this;
    }

    /// <summary>
    /// Sets the transcript language without touching <c>options["language"]</c>.
    /// Needed because "does this model declare a language option" and "does this
    /// model need a transcript language" are different questions: parakeet_tdt
    /// refuses a language it does not declare, while qwen3_forced_aligner declares
    /// none and requires one anyway.
    /// </summary>
    public AudioRequest SetTextLanguage(string? language)
    {
        var handle = Handle;
        using var strings = new Utf8StringArena();
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_text_language(handle, strings.Add(language)));
        return this;
    }

    /// <summary>
    /// Sets interleaved float PCM to analyse. Copied into the request.
    /// </summary>
    public unsafe AudioRequest SetAudio(float[] samples, int sampleRate, int channels = 1)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        var handle = Handle;
        var frames = (nuint)(samples.Length / channels);
        fixed (float* pointer = samples)
        {
            NativeMethods.ThrowIfFailed(
                NativeMethods.audiocpp_request_set_audio(handle, pointer, frames, sampleRate, channels));
        }

        return this;
    }

    /// <summary>Sets the speaker reference audio for cloning / conversion families.</summary>
    public unsafe AudioRequest SetVoiceAudio(float[] samples, int sampleRate, int channels = 1)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        var handle = Handle;
        var frames = (nuint)(samples.Length / channels);
        fixed (float* pointer = samples)
        {
            NativeMethods.ThrowIfFailed(
                NativeMethods.audiocpp_request_set_voice_audio(handle, pointer, frames, sampleRate, channels));
        }

        return this;
    }

    /// <summary>
    /// Reuses a speaker identity a previous run produced, instead of re-deriving it
    /// from audio.
    /// </summary>
    public AudioRequest SetVoiceId(string cachedVoiceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(cachedVoiceId);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_voice_id(handle, strings.Add(cachedVoiceId)));
        return this;
    }

    public AudioRequest SetStyleLanguage(string? language)
    {
        var handle = Handle;
        using var strings = new Utf8StringArena();
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_style_language(handle, strings.Add(language)));
        return this;
    }

    public AudioRequest SetEmotion(string? emotion)
    {
        var handle = Handle;
        using var strings = new Utf8StringArena();
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_emotion(handle, strings.Add(emotion)));
        return this;
    }

    public AudioRequest SetSpeakingRate(float speakingRate)
    {
        var handle = Handle;
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_speaking_rate(handle, speakingRate));
        return this;
    }

    public AudioRequest SetPitchShift(float pitchShift)
    {
        var handle = Handle;
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_pitch_shift(handle, pitchShift));
        return this;
    }

    public AudioRequest SetEnergyScale(float energyScale)
    {
        var handle = Handle;
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_energy_scale(handle, energyScale));
        return this;
    }

    public AudioRequest SetStyleTag(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var keyPointer = strings.Add(key);
        var valuePointer = strings.Add(value);
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_style_tag(handle, keyPointer, valuePointer));
        return this;
    }

    /// <summary>
    /// Attaches an opaque payload - typically one a previous result produced. The
    /// bytes are copied.
    /// </summary>
    /// <returns>The artifact's index, for <see cref="SetArtifactMeta"/>.</returns>
    public unsafe nuint AddArtifact(AudioArtifactKind kind, string id, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var idPointer = strings.Add(id);
        fixed (byte* payloadPointer = payload)
        {
            NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_add_artifact(
                handle, kind, idPointer, payloadPointer, (nuint)payload.Length, out var index));
            return index;
        }
    }

    public AudioRequest SetArtifactMeta(nuint index, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var keyPointer = strings.Add(key);
        var valuePointer = strings.Add(value);
        NativeMethods.ThrowIfFailed(
            NativeMethods.audiocpp_request_set_artifact_meta(handle, index, keyPointer, valuePointer));
        return this;
    }

    /// <summary>Corresponds to the CLI's <c>--request-option</c>.</summary>
    public AudioRequest SetOption(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var keyPointer = strings.Add(key);
        var valuePointer = strings.Add(value);
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_request_set_option(handle, keyPointer, valuePointer));
        return this;
    }

    /// <summary>
    /// Sets a list-valued request option - the transport for the <c>*_list</c> option
    /// types a model spec declares. A second call with the same key replaces the
    /// list; an empty sequence is distinct from never setting the key.
    /// </summary>
    public unsafe AudioRequest SetOptionArray(string key, IReadOnlyList<string> values)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(values);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var keyPointer = strings.Add(key);

        var pointers = new IntPtr[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            pointers[i] = strings.Add(values[i]);
        }

        fixed (IntPtr* pointer = pointers)
        {
            NativeMethods.ThrowIfFailed(
                NativeMethods.audiocpp_request_set_option_array(handle, keyPointer, pointer, (nuint)pointers.Length));
        }

        return this;
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
            NativeMethods.audiocpp_request_free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
