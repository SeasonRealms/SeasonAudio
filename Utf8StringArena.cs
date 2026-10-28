// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// Owns the native UTF-8 strings a single native call needs.
/// </summary>
/// <remarks>
/// Every <c>const char *</c> the ABI takes is read during the call, so a caller
/// only has to keep the strings alive across that one call. The arena is used that
/// way - <c>using var strings = new Utf8StringArena()</c> around the P/Invoke - and
/// never stored on a long-lived object, so an <see cref="IntPtr"/> it handed out is
/// never dereferenced later.
/// </remarks>
internal sealed class Utf8StringArena : IDisposable
{
    private readonly List<IntPtr> _allocated = new(4);

    /// <summary>
    /// Copies <paramref name="value"/> to native memory and returns its pointer.
    /// <see langword="null"/> and empty both map to <see cref="IntPtr.Zero"/>, which
    /// the ABI documents as "not set" for every parameter that accepts it.
    /// </summary>
    public IntPtr Add(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return IntPtr.Zero;
        }

        var pointer = Marshal.StringToCoTaskMemUTF8(value);
        _allocated.Add(pointer);
        return pointer;
    }

    public void Dispose()
    {
        foreach (var pointer in _allocated)
        {
            Marshal.FreeCoTaskMem(pointer);
        }

        _allocated.Clear();
    }
}
