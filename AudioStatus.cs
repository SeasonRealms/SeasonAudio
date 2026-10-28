// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// <c>audiocpp_status</c>. Mirrors include/audiocpp.h; the numeric values are part
/// of the ABI.
/// </summary>
public enum AudioStatus
{
    Ok = 0,
    InvalidArgument = 1,
    UnsupportedFamily = 2,
    LoadFailed = 3,
    Runtime = 4,
    OutOfMemory = 5,
    OutOfRange = 6,
    NotAvailable = 7,
}

/// <summary>
/// A failed <c>audiocpp_*</c> call. The message is <c>audiocpp_last_error()</c>,
/// which is per-thread and only valid immediately after the failing call, so it is
/// captured the moment the status is seen.
/// </summary>
public sealed class AudioCppException : Exception
{
    public AudioCppException(AudioStatus status, string? detail)
        : base(BuildMessage(status, detail))
    {
        Status = status;
        Detail = detail;
    }

    /// <summary>The native status code.</summary>
    public AudioStatus Status { get; }

    /// <summary>The raw detail string, or <see langword="null"/> when the native side had none.</summary>
    public string? Detail { get; }

    private static string BuildMessage(AudioStatus status, string? detail)
    {
        var name = status switch
        {
            AudioStatus.InvalidArgument => "invalid argument",
            AudioStatus.UnsupportedFamily => "unsupported family",
            AudioStatus.LoadFailed => "load failed",
            AudioStatus.Runtime => "runtime error",
            AudioStatus.OutOfMemory => "out of memory",
            AudioStatus.OutOfRange => "out of range",
            AudioStatus.NotAvailable => "not available",
            _ => status.ToString(),
        };

        return string.IsNullOrWhiteSpace(detail)
            ? $"audio.cpp call failed: {name} ({status})."
            : $"audio.cpp call failed: {name} ({status}): {detail}";
    }
}
