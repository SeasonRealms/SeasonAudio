// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// How a model directory is identified. Every field may be <see langword="null"/>.
/// </summary>
/// <param name="FamilyHint">The family, or <see langword="null"/> to identify the
/// model from its path.</param>
/// <param name="ConfigId">Which config, for packages that ship several.</param>
/// <param name="WeightId">Which weight set, for packages that ship several.</param>
/// <param name="ModelSpecOverride">A spec that overrides the built-in one.</param>
public sealed record AudioModelConfig(
    string? FamilyHint = null,
    string? ConfigId = null,
    string? WeightId = null,
    string? ModelSpecOverride = null);

/// <summary>
/// A loaded model: the weights, mapped and resident, plus everything the ABI can
/// answer about them without running anything.
/// </summary>
/// <remarks>
/// <para>
/// This is the layer worth keeping alive. Loading is the expensive part of a cold
/// call (weights are memory-mapped and identified), while every run also has to
/// prepare its own buffers; holding an <see cref="AudioModel"/> across calls to the
/// same model directory is what makes the second call cheap.
/// </para>
/// <para>
/// Sessions do not have to be one-shot, but they are not free-form either: a session
/// is prepared for the request it is about to run, because preparation carries the
/// input length. Reusing one session for a long series of similar-length requests
/// avoids re-allocating its buffers; switching between very different lengths
/// re-prepares anyway. See <see cref="AudioSession"/>.
/// </para>
/// </remarks>
public sealed class AudioModel : IDisposable
{
    private readonly AudioRegistry _registry;
    private IntPtr _handle;
    private bool _disposed;

    internal AudioModel(AudioRegistry registry, IntPtr handle)
    {
        _registry = registry;
        _handle = handle;
    }

    internal IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>The family that claimed these weights.</summary>
    public string Family => NativeMethods.PtrToStringOrEmpty(NativeMethods.audiocpp_model_family(Handle));

    /// <summary>A human-readable description, straight from the family.</summary>
    public string Description => NativeMethods.PtrToStringOrEmpty(NativeMethods.audiocpp_model_description(Handle));

    /// <summary>True when the model does not need a separate backend session.</summary>
    public bool Supports(string task, string mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(task);
        ArgumentException.ThrowIfNullOrEmpty(mode);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var taskPointer = strings.Add(task);
        var modePointer = strings.Add(mode);
        return NativeMethods.audiocpp_model_supports(handle, taskPointer, modePointer) != 0;
    }

    public bool SupportsTimestamps => NativeMethods.audiocpp_model_supports_timestamps(Handle) != 0;

    public bool SupportsSpeakerReference => NativeMethods.audiocpp_model_supports_speaker_reference(Handle) != 0;

    public bool SupportsStyleCondition => NativeMethods.audiocpp_model_supports_style_condition(Handle) != 0;

    /// <summary>The languages this model declares, empty when it declares none.</summary>
    public IReadOnlyList<string> GetLanguages()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_model_language_count(handle);
        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var languages = new List<string>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_model_language(handle, i, out var language);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            if (status != AudioStatus.Ok)
            {
                continue;
            }

            languages.Add(NativeMethods.PtrToStringOrEmpty(language));
        }

        return languages;
    }

    /// <summary>
    /// The options this model declares for one scope, read off its spec. This is the
    /// replacement for hardcoding per-family knowledge in a picker: enumerate, then
    /// show the ones that apply.
    /// </summary>
    public IReadOnlyList<AudioModelOption> GetOptions(AudioOptionScope scope)
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_model_option_count(handle, scope);
        if (count == 0)
        {
            return Array.Empty<AudioModelOption>();
        }

        var options = new List<AudioModelOption>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_model_option(
                handle, scope, i,
                out var name, out var valueName, out var description,
                out var defaultValue, out var minValue, out var maxValue, out var required);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            if (status != AudioStatus.Ok)
            {
                continue;
            }

            options.Add(new AudioModelOption(
                NativeMethods.PtrToStringOrEmpty(name),
                NativeMethods.PtrToStringOrEmpty(valueName),
                NativeMethods.PtrToStringOrEmpty(description),
                NativeMethods.PtrToStringOrEmpty(defaultValue),
                NativeMethods.PtrToStringOrEmpty(minValue),
                NativeMethods.PtrToStringOrEmpty(maxValue),
                required != 0));
        }

        return options;
    }

    /// <summary>
    /// Creates a session for one task and mode. <paramref name="backend"/> defaults to
    /// CPU with one thread, which is the right choice when stable diffusion or an LLM
    /// is resident - see the README on sharing a process with SeasonGGML.
    /// </summary>
    public AudioSession CreateSession(
        string task,
        string mode = AudioMode.Offline,
        AudioBackendConfig? backend = null,
        AudioOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(task);
        ArgumentException.ThrowIfNullOrEmpty(mode);

        var effective = backend ?? AudioBackendConfig.Default;
        var handle = Handle;

        using var strings = new Utf8StringArena();
        var taskPointer = strings.Add(task);
        var modePointer = strings.Add(mode);
        var backendName = strings.Add(effective.ToNativeName());

        var native = new NativeMethods.NativeBackendConfig
        {
            backend = backendName,
            device = effective.Device,
            threads = effective.Threads,
        };

        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_session_create(
            handle, taskPointer, modePointer, in native, options?.Handle ?? IntPtr.Zero, out var session));

        if (session == IntPtr.Zero)
        {
            throw new AudioCppException(AudioStatus.Runtime, "audiocpp_session_create returned NULL");
        }

        return new AudioSession(this, session);
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
            NativeMethods.audiocpp_model_free(_handle);
            _handle = IntPtr.Zero;
        }

        GC.KeepAlive(_registry);
    }
}
