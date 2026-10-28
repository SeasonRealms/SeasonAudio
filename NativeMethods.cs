// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// P/Invoke surface for include/audiocpp.h. One declaration per exported entry
/// point; nothing here adds behaviour.
/// </summary>
/// <remarks>
/// The ABI is opaque-handle only - no C++ type crosses it - and every handle keeps
/// its parents alive internally, so the managed wrappers may be disposed in any
/// order. Handles are not thread-safe individually; separate handles may be used
/// from separate threads.
/// </remarks>
internal static unsafe class NativeMethods
{
#if IOS
    // Unreachable today: this package deliberately has no iOS target (libaudiocpp.a
    // would duplicate ggml inside the app). Kept so the constant has one home if a
    // uniquely-owned iOS archive ever exists.
    private const string LibraryName = "__Internal";
#else
    private const string LibraryName = "audiocpp";
#endif

    // ---- Versioning -------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern uint audiocpp_abi_version();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_build_version();

    // ---- Status and errors ------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_last_error();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_status_string(AudioStatus status);

    // ---- Options ----------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_options_create();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_options_set(IntPtr options, IntPtr key, IntPtr value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void audiocpp_options_free(IntPtr options);

    // ---- Registry ---------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_registry_create(IntPtr configPath, out IntPtr registry);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void audiocpp_registry_free(IntPtr registry);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_registry_family_count(IntPtr registry);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_registry_family(IntPtr registry, nuint index, out IntPtr family);

    // ---- Task vocabulary --------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_task_count();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_task_name(nuint index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_task_from_spec_name(IntPtr specTask);

    // ---- Model ------------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_model_load(
        IntPtr registry,
        IntPtr modelPath,
        in NativeModelConfig config,
        IntPtr options,
        out IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void audiocpp_model_free(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_model_family(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_model_description(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int audiocpp_model_supports(IntPtr model, IntPtr task, IntPtr mode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int audiocpp_model_supports_timestamps(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int audiocpp_model_supports_speaker_reference(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int audiocpp_model_supports_style_condition(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_model_language_count(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_model_language(IntPtr model, nuint index, out IntPtr language);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_model_option_count(IntPtr model, AudioOptionScope scope);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_model_option(
        IntPtr model,
        AudioOptionScope scope,
        nuint index,
        out IntPtr name,
        out IntPtr valueName,
        out IntPtr description,
        out IntPtr defaultValue,
        out IntPtr minValue,
        out IntPtr maxValue,
        out int required);

    // ---- Session ----------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_session_create(
        IntPtr model,
        IntPtr task,
        IntPtr mode,
        in NativeBackendConfig backendConfig,
        IntPtr options,
        out IntPtr session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void audiocpp_session_free(IntPtr session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_session_family(IntPtr session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_session_prepare(IntPtr session, IntPtr request);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_session_run(IntPtr session, IntPtr request, out IntPtr result);

    // ---- Request ----------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_request_create();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void audiocpp_request_free(IntPtr request);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_text(IntPtr request, IntPtr text, IntPtr language);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_text_language(IntPtr request, IntPtr language);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_audio(
        IntPtr request, float* samples, nuint frames, int sampleRate, int channels);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_voice_audio(
        IntPtr request, float* samples, nuint frames, int sampleRate, int channels);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_voice_id(IntPtr request, IntPtr cachedVoiceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_style_language(IntPtr request, IntPtr language);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_emotion(IntPtr request, IntPtr emotion);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_speaking_rate(IntPtr request, float speakingRate);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_pitch_shift(IntPtr request, float pitchShift);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_energy_scale(IntPtr request, float energyScale);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_style_tag(IntPtr request, IntPtr key, IntPtr value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_add_artifact(
        IntPtr request, AudioArtifactKind kind, IntPtr id, void* payload, nuint payloadBytes, out nuint index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_artifact_meta(
        IntPtr request, nuint index, IntPtr key, IntPtr value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_option(IntPtr request, IntPtr key, IntPtr value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_request_set_option_array(
        IntPtr request, IntPtr key, IntPtr* values, nuint count);

    // ---- Result -----------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void audiocpp_result_free(IntPtr result);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_audio(
        IntPtr result, out IntPtr samples, out nuint frames, out int sampleRate, out int channels);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_text(IntPtr result, out IntPtr text, out IntPtr language);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_result_segment_count(IntPtr result);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_segment(
        IntPtr result, nuint index, out long startSample, out long endSample, out float confidence, out IntPtr text);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_result_speaker_turn_count(IntPtr result);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_speaker_turn(
        IntPtr result, nuint index, out long startSample, out long endSample, out IntPtr speakerId,
        out float confidence, out IntPtr text);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_result_word_count(IntPtr result);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_word(
        IntPtr result, nuint index, out IntPtr word, out long startSample, out long endSample, out float confidence);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_result_named_audio_count(IntPtr result);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_named_audio(
        IntPtr result, nuint index, out IntPtr id, out IntPtr samples, out nuint frames,
        out int sampleRate, out int channels);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_result_artifact_count(IntPtr result);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_artifact(
        IntPtr result, nuint index, out AudioArtifactKind kind, out IntPtr id, out IntPtr payload, out nuint payloadBytes);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_result_artifact_meta_count(IntPtr result, nuint index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_result_artifact_meta(
        IntPtr result, nuint index, nuint metaIndex, out IntPtr key, out IntPtr value);

    // ---- Streaming --------------------------------------------------------

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_stream_policy(
        IntPtr session, out AudioStreamInputKind input, out AudioStreamOutputKind output,
        out long preferredChunkSamples, out double preferredChunkSeconds);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_stream_start(IntPtr session, IntPtr request);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_stream_push(
        IntPtr session, float* samples, nuint frames, int sampleRate, int channels,
        long startSample, out IntPtr streamEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_stream_next_event(IntPtr session, out IntPtr streamEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_stream_finish(IntPtr session, out IntPtr result);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_stream_reset(IntPtr session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void audiocpp_event_free(IntPtr streamEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int audiocpp_event_is_final(IntPtr streamEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr audiocpp_event_as_result(IntPtr streamEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint audiocpp_event_voice_activity_count(IntPtr streamEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern AudioStatus audiocpp_event_voice_activity(
        IntPtr streamEvent, nuint index, out AudioVoiceActivityKind kind, out long sample, out float probability);

    // ---- Helpers ----------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeModelConfig
    {
        public IntPtr family_hint;
        public IntPtr config_id;
        public IntPtr weight_id;
        public IntPtr model_spec_override;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeBackendConfig
    {
        public IntPtr backend;
        public int device;
        public int threads;
    }

    /// <summary>
    /// Throws when a call failed, reading <c>audiocpp_last_error()</c> immediately -
    /// the detail is per-thread and a later call on this thread may clear it.
    /// </summary>
    internal static void ThrowIfFailed(AudioStatus status)
    {
        if (status == AudioStatus.Ok)
        {
            return;
        }

        throw new AudioCppException(status, PtrToString(NativeMethods.audiocpp_last_error()));
    }

    internal static string? PtrToString(IntPtr pointer) =>
        pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);

    internal static string PtrToStringOrEmpty(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;

    /// <summary>
    /// Copies a borrowed <c>const float *</c> block. The ABI only guarantees the
    /// pointer until the producing handle is freed or mutated, so nothing is kept.
    /// </summary>
    internal static float[] CopySamples(IntPtr samples, nuint frames, int channels)
    {
        if (samples == IntPtr.Zero || frames == 0 || channels <= 0)
        {
            return Array.Empty<float>();
        }

        var count = checked((int)(frames * (nuint)channels));
        var result = new float[count];
        fixed (float* destination = result)
        {
            Buffer.MemoryCopy((void*)samples, destination, count * sizeof(float), count * sizeof(float));
        }

        return result;
    }
}
