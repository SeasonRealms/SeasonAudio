// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// One task bound to one loaded model, one mode and one backend.
/// </summary>
/// <remarks>
/// <para><b>Why a session at all?</b> Because the two halves of a call cost differently.
/// Loading is memory-mapping and identifying weights; a session is where the backend,
/// its buffers and the family's own runtime state live. Creating one per call throws
/// away the second half every time.</para>
///
/// <para><b>But it is not a cache.</b> <c>audiocpp_session_run</c> always prepares with
/// the request it is about to run, and preparation carries the input length - a
/// session prepared for one request must not run another without being prepared
/// again (the ABI does that for you, which is why <see cref="Prepare"/> is only an
/// eager-allocation hint). Run requests of similar size back to back and the buffers
/// are reused; alternate between a 3-second and a 3-minute input and you pay the
/// allocation each time regardless.</para>
///
/// <para><b>Compared to SeasonThreeD:</b> that package drives an out-of-process
/// worker that streams weights in as the pipeline advances, because a TRELLIS run is
/// minutes long and the model set is huge. audio.cpp has no such pipeline - the whole
/// model is mapped up front and generation is the inner loop - so the load/inference
/// split here buys weight reuse, not weight paging. Streaming
/// (<see cref="StartStream"/>) is the analogue of "inference as you go": it feeds PCM
/// chunks and pulls events, it does not fetch weights lazily.</para>
///
/// <para>Sessions are not thread-safe. One session per thread, or serialize; separate
/// handles may run concurrently.</para>
/// </remarks>
public sealed class AudioSession : IDisposable
{
    private readonly AudioModel _model;
    private IntPtr _handle;
    private bool _disposed;

    internal AudioSession(AudioModel model, IntPtr handle)
    {
        _model = model;
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

    /// <summary>The family this session was created from.</summary>
    public string Family => NativeMethods.PtrToStringOrEmpty(NativeMethods.audiocpp_session_family(Handle));

    /// <summary>
    /// Allocates early with a representative request. Optional: <see cref="Run"/> and
    /// <see cref="StartStream"/> always prepare with the request they are about to run.
    /// </summary>
    public void Prepare(AudioRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_session_prepare(Handle, request.Handle));
    }

    /// <summary>Runs one request to completion and returns its result.</summary>
    public AudioResult Run(AudioRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_session_run(Handle, request.Handle, out var result));
        if (result == IntPtr.Zero)
        {
            throw new AudioCppException(AudioStatus.Runtime, "audiocpp_session_run produced no result");
        }

        return new AudioResult(result, ownsHandle: true);
    }

    /// <summary>
    /// What a streaming session expects and emits. Requires a session created with
    /// <see cref="AudioMode.Streaming"/>.
    /// </summary>
    public AudioStreamPolicy GetStreamPolicy()
    {
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_stream_policy(
            Handle,
            out var input,
            out var output,
            out var preferredChunkSamples,
            out var preferredChunkSeconds));

        return new AudioStreamPolicy(input, output, preferredChunkSamples, preferredChunkSeconds);
    }

    /// <summary>
    /// Begins a stream, resetting any stream already in flight.
    /// <paramref name="request"/> may be <see langword="null"/>.
    /// </summary>
    public void StartStream(AudioRequest? request = null)
    {
        NativeMethods.ThrowIfFailed(
            NativeMethods.audiocpp_stream_start(Handle, request?.Handle ?? IntPtr.Zero));
    }

    /// <summary>
    /// Feeds one chunk and returns the event it produced, or <see langword="null"/>
    /// when the caller only wants the final result.
    /// </summary>
    public unsafe AudioStreamEvent? Push(
        float[] samples,
        int sampleRate,
        int channels = 1,
        long startSample = 0)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        var handle = Handle;
        var frames = (nuint)(samples.Length / channels);
        IntPtr streamEvent;
        fixed (float* pointer = samples)
        {
            NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_stream_push(
                handle, pointer, frames, sampleRate, channels, startSample, out streamEvent));
        }

        return streamEvent == IntPtr.Zero ? null : new AudioStreamEvent(streamEvent);
    }

    /// <summary>
    /// Drains an event the family queued itself. Returns <see langword="null"/> when
    /// the queue is empty, which is not an error.
    /// </summary>
    public AudioStreamEvent? NextEvent()
    {
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_stream_next_event(Handle, out var streamEvent));
        return streamEvent == IntPtr.Zero ? null : new AudioStreamEvent(streamEvent);
    }

    /// <summary>Ends the stream and returns its result.</summary>
    public AudioResult FinishStream()
    {
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_stream_finish(Handle, out var result));
        if (result == IntPtr.Zero)
        {
            throw new AudioCppException(AudioStatus.Runtime, "audiocpp_stream_finish produced no result");
        }

        return new AudioResult(result, ownsHandle: true);
    }

    /// <summary>Drops stream state so the session can start another stream.</summary>
    public void ResetStream()
    {
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_stream_reset(Handle));
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
            NativeMethods.audiocpp_session_free(_handle);
            _handle = IntPtr.Zero;
        }

        GC.KeepAlive(_model);
    }
}
