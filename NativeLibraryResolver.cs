// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio

#pragma warning disable CA2255

using System.Reflection;
using System.Runtime.CompilerServices;

namespace Season.Audio;

/// <summary>
/// Resolves libaudiocpp.so on Linux.
/// </summary>
/// <remarks>
/// Nothing has to be preloaded first: this build links its own ggml statically and
/// the export list hides it, so <c>ldd libaudiocpp.so</c> shows no ggml and there is
/// no DT_NEEDED chain to satisfy. Only the library itself is openable by name in
/// two places - the flat output root and the runtimes/&lt;rid&gt;/native layout a
/// NuGet consumer sees - so both are probed by absolute path.
///
/// Windows resolves through the DLL search path (the MSBuild Content items copy
/// audiocpp.dll to the output root) and Mac Catalyst resolves the dylib the Apple
/// SDK placed under Contents/MonoBundle, so neither needs this.
/// </remarks>
internal static class NativeLibraryResolver
{
    // Mirrors NativeMethods.LibraryName.
    private const string LibraryName = "audiocpp";

    [ModuleInitializer]
    public static void Init()
    {
        if (OperatingSystem.IsLinux())
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
        }
    }

    private static readonly object s_lock = new();
    private static IntPtr s_handle;

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        lock (s_lock)
        {
            if (s_handle != IntPtr.Zero)
            {
                return s_handle;
            }

            foreach (var directory in EnumerateNativeSearchDirectories())
            {
                var candidate = Path.Combine(directory, "libaudiocpp.so");
                if (File.Exists(candidate) &&
                    NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                {
                    s_handle = handle;
                    return handle;
                }
            }
        }

        // Fall through to the default probing, which covers a host that put the
        // library on the loader search path itself.
        return IntPtr.Zero;
    }

    /// <summary>
    /// The two layouts a consumer sees: the flat output root (<c>Content</c> items
    /// with <c>Link</c> flatten the RID folders) and the RID layout preserved inside
    /// the NuGet package.
    /// </summary>
    private static string[] EnumerateNativeSearchDirectories()
    {
        var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var directories = new List<string>(2) { baseDirectory };

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

        if (arch is not null)
        {
            directories.Add(Path.Combine(baseDirectory, "runtimes", $"linux-{arch}", "native"));
        }

        return directories.ToArray();
    }
}

#pragma warning restore CA2255
