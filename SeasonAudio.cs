// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// Process-wide entry points: version introspection, the task vocabulary, and the
/// one-shot helpers that hide the handle graph.
/// </summary>
/// <remarks>
/// <para>The one-shot helpers exist so a caller can try a model in three lines. They
/// create a registry, a model and a session per call, then tear all three down - so
/// they pay the load cost every time. Anything called in a loop should hold an
/// <see cref="AudioModel"/> (and usually an <see cref="AudioSession"/>) instead; that
/// is the whole reason the layers are exposed.</para>
///
/// <para>This class forces the native library to be loaded the first time anything
/// on it is touched, which is what surfaces a missing <c>audiocpp</c> binary as a
/// <see cref="DllNotFoundException"/> at a predictable point rather than inside a
/// later call.</para>
/// </remarks>
public static class SeasonAudio
{
    private static readonly Lazy<IReadOnlyList<string>> s_tasks =
        new(EnumerateTasks, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The build string the native library was compiled with.</summary>
    public static string Version =>
        NativeMethods.PtrToStringOrEmpty(NativeMethods.audiocpp_build_version());

    /// <summary>
    /// The ABI the native library implements, packed as
    /// <c>(major &lt;&lt; 16) | (minor &lt;&lt; 8) | patch</c>.
    /// </summary>
    public static uint AbiVersion => NativeMethods.audiocpp_abi_version();

    public static int AbiVersionMajor => (int)(AbiVersion >> 16);

    public static int AbiVersionMinor => (int)((AbiVersion >> 8) & 0xFF);

    public static int AbiVersionPatch => (int)(AbiVersion & 0xFF);

    /// <summary>
    /// The task tokens this particular build understands - not every family is
    /// compiled into every build (<c>AUDIOCPP_MODELS</c> at configure time).
    /// </summary>
    public static IReadOnlyList<string> Tasks => s_tasks.Value;

    /// <summary>The process-wide registry. Disposing it is never necessary.</summary>
    public static AudioRegistry DefaultRegistry => AudioRegistry.Default;

    /// <summary>
    /// Translates a model-spec task spelling to the ABI token. Specs say
    /// <c>music</c>, <c>voice_design</c>, <c>speaker</c>; the ABI says <c>gen</c>,
    /// <c>vdes</c>, <c>spk</c>. Unknown names are returned unchanged, so a caller may
    /// pass either spelling.
    /// </summary>
    public static string TaskFromSpecName(string specName)
    {
        ArgumentException.ThrowIfNullOrEmpty(specName);

        using var strings = new Utf8StringArena();
        var resolved = NativeMethods.audiocpp_task_from_spec_name(strings.Add(specName));
        return resolved == IntPtr.Zero ? specName : NativeMethods.PtrToStringOrEmpty(resolved);
    }

    /// <summary>The ABI's own spelling of a status code.</summary>
    public static string StatusString(AudioStatus status) =>
        NativeMethods.PtrToStringOrEmpty(NativeMethods.audiocpp_status_string(status));

    /// <summary>
    /// Loads a model directory on the default registry. Equivalent to
    /// <c>AudioRegistry.Default.LoadModel</c>; kept here so a first call needs only
    /// one type name.
    /// </summary>
    public static AudioModel LoadModel(
        string modelPath,
        AudioModelConfig? config = null,
        AudioOptions? loadOptions = null) =>
        AudioRegistry.Default.LoadModel(modelPath, config, loadOptions);

    /// <summary>
    /// Text to speech (or whatever the family's <c>tts</c> task means) in one call.
    /// The model is loaded and dropped around the run - hold an
    /// <see cref="AudioModel"/> instead if this repeats.
    /// </summary>
    public static AudioData Synthesize(
        string modelPath,
        string text,
        string? language = null,
        AudioBackendConfig? backend = null,
        AudioOptions? loadOptions = null,
        Action<AudioRequest>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);
        ArgumentException.ThrowIfNullOrEmpty(text);

        using var model = LoadModel(modelPath, loadOptions: loadOptions);
        using var session = model.CreateSession(AudioTask.Tts, AudioMode.Offline, backend);
        using var request = new AudioRequest();
        request.SetText(text, language);
        configure?.Invoke(request);

        using var result = session.Run(request);
        return result.GetAudio() ?? AudioData.Empty;
    }

    /// <summary>
    /// Speech to text in one call. Returns the transcript and the language the
    /// family reported, or an empty pair when the model produced no text.
    /// </summary>
    public static (string Text, string? Language) Transcribe(
        string modelPath,
        float[] samples,
        int sampleRate = 16000,
        string? language = null,
        AudioBackendConfig? backend = null,
        AudioOptions? loadOptions = null,
        Action<AudioRequest>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);
        ArgumentNullException.ThrowIfNull(samples);

        using var model = LoadModel(modelPath, loadOptions: loadOptions);
        using var session = model.CreateSession(AudioTask.Asr, AudioMode.Offline, backend);
        using var request = new AudioRequest();
        request.SetAudio(samples, sampleRate);
        if (language is not null)
        {
            request.SetTextLanguage(language);
        }

        configure?.Invoke(request);

        using var result = session.Run(request);
        return result.GetText() ?? (string.Empty, null);
    }

    private static IReadOnlyList<string> EnumerateTasks()
    {
        var count = NativeMethods.audiocpp_task_count();
        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var tasks = new List<string>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var name = NativeMethods.audiocpp_task_name(i);
            if (name != IntPtr.Zero)
            {
                tasks.Add(NativeMethods.PtrToStringOrEmpty(name));
            }
        }

        return tasks;
    }
}
