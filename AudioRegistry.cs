// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

namespace Season.Audio;

/// <summary>
/// The set of model families this build knows. Cheap to create, safe to share, and
/// the parent of every model.
/// </summary>
/// <remarks>
/// The registry only carries loaders - no weights, no device state - so one instance
/// per process is enough, which is what <see cref="Default"/> provides. Do not
/// dispose <see cref="Default"/>; models and sessions stay valid when you do (the
/// ABI keeps parents alive internally), but the next caller would have to build a
/// new one.
/// </remarks>
public sealed class AudioRegistry : IDisposable
{
    private static readonly Lazy<AudioRegistry> s_default =
        new(() => Create(), LazyThreadSafetyMode.ExecutionAndPublication);

    private IntPtr _handle;
    private bool _disposed;

    private AudioRegistry(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>The process-wide registry.</summary>
    public static AudioRegistry Default => s_default.Value;

    /// <param name="configPath">
    /// A loader config, or <see langword="null"/> for the default set. The default set
    /// is compiled in by this build (AUDIOCPP_DEPLOYMENT_BUILD=ON), so a packaged app
    /// does not have to ship model_specs/*.json.
    /// </param>
    public static AudioRegistry Create(string? configPath = null)
    {
        using var strings = new Utf8StringArena();
        NativeMethods.ThrowIfFailed(
            NativeMethods.audiocpp_registry_create(strings.Add(configPath), out var handle));

        if (handle == IntPtr.Zero)
        {
            throw new AudioCppException(AudioStatus.Runtime, "audiocpp_registry_create returned NULL");
        }

        return new AudioRegistry(handle);
    }

    internal IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>The families this build can load. A family missing here returns
    /// <see cref="AudioStatus.UnsupportedFamily"/> from <see cref="LoadModel"/>.</summary>
    public IReadOnlyList<string> GetFamilies()
    {
        var handle = Handle;
        var count = NativeMethods.audiocpp_registry_family_count(handle);
        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var families = new List<string>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var status = NativeMethods.audiocpp_registry_family(handle, i, out var family);
            if (status != AudioStatus.Ok)
            {
                continue;
            }

            families.Add(NativeMethods.PtrToStringOrEmpty(family));
        }

        return families;
    }

    /// <summary>
    /// Maps and identifies the weights at <paramref name="modelPath"/>. This is the
    /// expensive half of a cold call - keep the returned model alive and reuse it.
    /// </summary>
    public AudioModel LoadModel(
        string modelPath,
        AudioModelConfig? config = null,
        AudioOptions? loadOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);

        var handle = Handle;
        using var strings = new Utf8StringArena();

        var native = new NativeMethods.NativeModelConfig
        {
            family_hint = strings.Add(config?.FamilyHint),
            config_id = strings.Add(config?.ConfigId),
            weight_id = strings.Add(config?.WeightId),
            model_spec_override = strings.Add(config?.ModelSpecOverride),
        };

        NativeMethods.ThrowIfFailed(NativeMethods.audiocpp_model_load(
            handle,
            strings.Add(modelPath),
            in native,
            loadOptions?.Handle ?? IntPtr.Zero,
            out var model));

        if (model == IntPtr.Zero)
        {
            throw new AudioCppException(AudioStatus.LoadFailed, "audiocpp_model_load returned NULL");
        }

        return new AudioModel(this, model);
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
            NativeMethods.audiocpp_registry_free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
