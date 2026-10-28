// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// A string-to-string option map, mirroring the framework's own maps 1:1. The same
/// type is used for load options, session options and request options - the ABI
/// distinguishes them only by which call it is passed to.
/// </summary>
/// <remarks>
/// Passing <see langword="null"/> wherever an <see cref="AudioOptions"/> is expected
/// means "no options", which is what every entry point documents as its default.
/// </remarks>
public sealed class AudioOptions : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    public AudioOptions()
    {
        _handle = NativeMethods.audiocpp_options_create();
        if (_handle == IntPtr.Zero)
        {
            throw new AudioCppException(AudioStatus.OutOfMemory, "audiocpp_options_create returned NULL");
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
    /// Sets <paramref name="key"/>. Assignment semantics: a second call with the same
    /// key replaces the first.
    /// </summary>
    public AudioOptions Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var handle = Handle;
        using var strings = new Utf8StringArena();
        var keyPointer = strings.Add(key);
        var valuePointer = strings.Add(value);
        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_options_set(handle, keyPointer, valuePointer));
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
            NativeMethods.audiocpp_options_free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
