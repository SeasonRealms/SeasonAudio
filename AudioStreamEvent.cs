// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// One event produced while streaming. A pull-based design: no callback crosses the
/// ABI, so the caller asks for the next event instead of being called back.
/// </summary>
public sealed class AudioStreamEvent : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    internal AudioStreamEvent(IntPtr handle)
    {
        _handle = handle;
    }

    private IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>True when this event is the last one for the stream.</summary>
    public bool IsFinal => NativeMethods.audiocpp_event_is_final(Handle) != 0;

    /// <summary>
    /// The event's payload, read through the result accessors. Borrowed: the returned
    /// result is only valid while this event is alive, so copy out what you keep.
    /// </summary>
    public AudioResult? GetResult()
    {
        var pointer = NativeMethods.audiocpp_event_as_result(Handle);
        return pointer == IntPtr.Zero ? null : new AudioResult(pointer, ownsHandle: false);
    }

    /// <summary>Voice-activity markers carried by this event.</summary>
    public IReadOnlyList<AudioVoiceActivity> GetVoiceActivity()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_event_voice_activity_count(handle);
        if (count == 0)
        {
            return Array.Empty<AudioVoiceActivity>();
        }

        var activities = new List<AudioVoiceActivity>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_event_voice_activity(
                handle, i, out var kind, out var sample, out var probability);
            if (status == AudioStatus.OutOfRange)
            {
                break;
            }

            NativeMethods.ThrowIfFailed(status);
            activities.Add(new AudioVoiceActivity(kind, sample, probability));
        }

        return activities;
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
            NativeMethods.audiocpp_event_free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
