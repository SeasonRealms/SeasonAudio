// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// Which option map a <see cref="AudioModelOption"/> belongs to. Mirrors
/// <c>audiocpp_option_scope</c>.
/// </summary>
public enum AudioOptionScope
{
    Request = 0,
    Session = 1,
    Load = 2,
}

/// <summary>
/// Opaque payloads a family produces or consumes - speaker embeddings, acoustic
/// tokens, MIDI, diarization state. Persisting one from a result and feeding it back
/// into a later request is how voice state is reused without re-deriving it.
/// </summary>
public enum AudioArtifactKind
{
    SpeakerEmbedding = 0,
    StyleEmbedding = 1,
    PromptEmbedding = 2,
    AcousticTokens = 3,
    Midi = 4,
    TranscriptAlignment = 5,
    DiarizationState = 6,
    VadState = 7,
    Custom = 8,
}

public enum AudioStreamInputKind
{
    None = 0,
    AudioChunks = 1,
}

public enum AudioStreamOutputKind
{
    FinalResult = 0,
    PullEvents = 1,
}

public enum AudioVoiceActivityKind
{
    SpeechStart = 0,
    SpeechEnd = 1,
    SpeechSegment = 2,
}

/// <summary>
/// The ABI's task vocabulary. These are the tokens <c>audiocpp_task_name()</c>
/// enumerates - note they are not the same spellings the model specs use
/// (<c>music</c> becomes <c>gen</c>, <c>clon</c> keeps its abbreviation, and so on);
/// <see cref="SeasonAudio.TaskFromSpecName"/> translates.
/// </summary>
/// <remarks>
/// Every task-aware entry point takes a string rather than an enum so a build that
/// adds a token does not need a new wrapper: <see cref="SeasonAudio.Tasks"/> reports
/// what this particular build knows.
/// </remarks>
public static class AudioTask
{
    public const string Vad = "vad";
    public const string Asr = "asr";
    public const string Diar = "diar";
    public const string Sep = "sep";
    public const string Gen = "gen";
    public const string Tts = "tts";
    public const string Clone = "clon";
    public const string VoiceConversion = "vc";
    public const string SpeechToSpeech = "s2s";
    public const string Align = "align";
    public const string VoiceDesign = "vdes";
    public const string Speaker = "spk";
    public const string SingingConversion = "svc";
    public const string Midi = "midi";
    public const string Wake = "wake";
    public const string Classify = "cls";
}

/// <summary>Session modes, as <c>audiocpp_session_create</c> takes them.</summary>
public static class AudioMode
{
    public const string Offline = "offline";
    public const string Streaming = "streaming";
}

/// <summary>A ggml backend, as <c>audiocpp_backend_config.backend</c> spells it.</summary>
public enum AudioBackend
{
    Cpu = 0,
    Cuda = 1,
    Hip = 2,
    Vulkan = 3,
    Metal = 4,
    Best = 5,
}

/// <summary>
/// Device and thread choice for one session. This is the only place the wrapper
/// selects hardware - the rest of the process keeps whatever SeasonGGML chose.
/// </summary>
/// <param name="Backend">The backend to run on.</param>
/// <param name="Device">Device index within that backend.</param>
/// <param name="Threads">CPU threads; 1 is a reasonable default when another
/// runtime (stable diffusion, an LLM) is also resident.</param>
public sealed record AudioBackendConfig(
    AudioBackend Backend = AudioBackend.Cpu,
    int Device = 0,
    int Threads = 1)
{
    /// <summary>CPU, device 0, one thread - the ABI's defaults.</summary>
    public static AudioBackendConfig Default { get; } = new();

    internal string ToNativeName() => Backend switch
    {
        AudioBackend.Cuda => "cuda",
        AudioBackend.Hip => "hip",
        AudioBackend.Vulkan => "vulkan",
        AudioBackend.Metal => "metal",
        AudioBackend.Best => "best",
        _ => "cpu",
    };
}
