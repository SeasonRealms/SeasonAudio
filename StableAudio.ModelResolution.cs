// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio
// SeasonAudio for Stable Audio Models

namespace Season.AI;

public partial class StableAudio
{
    static ModelBundle LoadBundle((ModelSpec spec, string? provider) state)
    {
        var spec = state.spec;
        var textEncoderSession = CreateSession(spec.TextEncoderPath, state.provider, allowCpuFallback: true);
        var ditSession = CreateSession(spec.DitPath, state.provider);
        var decoderSession = CreateSession(spec.DecoderPath, state.provider);
        ValidateDiTSessionContract(ditSession, spec.DitPath);

        return new ModelBundle(
            CreateTokenizer(spec.TokenizerDirectory),
            textEncoderSession,
            ditSession,
            decoderSession);
    }

    static ModelSpec ResolveModelSpec(string model, string? provider)
    {
        string normalized = model.Replace('\\', '/').Trim().Trim('/');

        if (File.Exists(model) && Path.GetFileName(model).Equals("dit.onnx", StringComparison.OrdinalIgnoreCase))
            normalized = Path.GetDirectoryName(Path.GetFullPath(model)) ?? model;

        if (Directory.Exists(model))
        {
            string fullDirectory = Path.GetFullPath(model);
            if (File.Exists(Path.Combine(fullDirectory, "dit.onnx")))
            {
                string rootDirectory = Directory.GetParent(fullDirectory)?.FullName ?? fullDirectory;
                string decoderPath = ResolveSiblingDecoder(rootDirectory, Path.GetFileName(fullDirectory));
                string tokenizerDirectory = ResolveTokenizerDirectory(rootDirectory);
                return CreateSpec(
                    fullDirectory,
                    decoderPath,
                    Path.Combine(rootDirectory, "t5gemma", "encoder.onnx"),
                    tokenizerDirectory,
                    provider);
            }
        }

        string key = normalized.ToLowerInvariant();
        return key switch
        {
            "small-sfx" or "small_sfx" or "stable-audio-3-small-sfx" or "sa3-sm-sfx" =>
                CreateSmallSfxSpec(provider),
            "stable-audio-3-small-sfx-onnx" or "stable-audio-3-small-sfx-onnx/sa3-sm-sfx" =>
                CreateSmallSfxSpec(provider),
            "small-music" or "small_music" or "stable-audio-3-small-music" or "sa3-sm-music" =>
                CreateSpec(
                    Path.Combine(ResolveStableAudioModelRoot("stable-audio-3-optimized"), "sa3-sm-music"),
                    Path.Combine(ResolveStableAudioModelRoot("stable-audio-3-optimized"), "same-s", "dec_dynamic_bf16.onnx"),
                    Path.Combine(ResolveStableAudioModelRoot("stable-audio-3-optimized"), "t5gemma", "encoder.onnx"),
                    ResolveTokenizerDirectory(ResolveStableAudioModelRoot("stable-audio-3-optimized")),
                    provider),
            "medium" or "sa3-m" or "stable-audio-3-medium" =>
                CreateSpec(
                    Path.Combine(ResolveStableAudioModelRoot("stable-audio-3-optimized"), "sa3-m"),
                    Path.Combine(ResolveStableAudioModelRoot("stable-audio-3-optimized"), "same-l", "dec_dynamic_triton_swa.onnx"),
                    Path.Combine(ResolveStableAudioModelRoot("stable-audio-3-optimized"), "t5gemma", "encoder.onnx"),
                    ResolveTokenizerDirectory(ResolveStableAudioModelRoot("stable-audio-3-optimized")),
                    provider),
            _ => throw new FileNotFoundException($"未识别的 Stable Audio 模型: {model}")
        };
    }

    static ModelSpec CreateSmallSfxSpec(string? provider)
    {
        string modelRoot = ResolveStableAudioModelRoot("stable-audio-3-small-sfx-onnx", "stable-audio-3-optimized");
        return CreateSpec(
            Path.Combine(modelRoot, "sa3-sm-sfx"),
            Path.Combine(modelRoot, "same-s", "dec_dynamic_bf16.onnx"),
            Path.Combine(modelRoot, "t5gemma", "encoder.onnx"),
            ResolveTokenizerDirectory(modelRoot),
            provider);
    }

    static string ResolveStableAudioModelRoot(params string[] directoryNames)
    {
        foreach (string directoryName in directoryNames)
        {
            string candidate = TryResolvePath(Path.Combine("Models", directoryName));
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new DirectoryNotFoundException(
            $"未找到 Stable Audio 模型目录。已尝试: {string.Join(", ", Array.ConvertAll(directoryNames, static name => $"Models/{name}"))}");
    }

    static ModelSpec CreateSpec(string ditDirectory, string decoderPath, string textEncoderPath, string tokenizerDirectory, string? provider)
    {
        string resolvedDitDirectory = ResolveExistingDirectory(ditDirectory);
        string resolvedDecoderPath = ResolveExistingFile(decoderPath);
        string resolvedTextEncoderPath = ResolveExistingFile(textEncoderPath);
        string resolvedTokenizerDirectory = ResolveExistingDirectory(tokenizerDirectory);

        string ditPath = ResolveExistingFile(Path.Combine(resolvedDitDirectory, "dit.onnx"));
        return new ModelSpec(
            ditPath,
            resolvedDecoderPath,
            resolvedTextEncoderPath,
            resolvedTokenizerDirectory,
            $"{ditPath}|{resolvedDecoderPath}|{resolvedTextEncoderPath}|{resolvedTokenizerDirectory}|{provider ?? "auto"}");
    }

    static string ResolveSiblingDecoder(string rootDirectory, string modelDirectoryName)
    {
        string lower = modelDirectoryName.ToLowerInvariant();
        if (lower.Contains("sa3-sm"))
            return Path.Combine(rootDirectory, "same-s", "dec_dynamic_bf16.onnx");
        if (lower.Contains("sa3-m"))
            return Path.Combine(rootDirectory, "same-l", "dec_dynamic_triton_swa.onnx");

        throw new FileNotFoundException($"无法根据模型目录推断解码器: {modelDirectoryName}");
    }

    static string ResolveTokenizerDirectory(string modelRoot)
    {
        var candidates = new[]
        {
            Path.Combine(modelRoot, "t5gemma"),
            Path.Combine(modelRoot, "..", "t5gemma"),
            Path.Combine("Models", "t5gemma"),
            "t5gemma"
        };

        foreach (var candidate in candidates)
        {
            string resolved = TryResolvePath(candidate);
            if (Directory.Exists(resolved))
                return Path.GetFullPath(resolved);
        }

        throw new DirectoryNotFoundException("未找到 Stable Audio tokenizer 目录（已尝试 `modelRoot/t5gemma`、`Models/t5gemma` 等位置）。");
    }

    static string ResolveExistingDirectory(string path)
    {
        string resolved = TryResolvePath(path);
        if (Directory.Exists(resolved))
            return Path.GetFullPath(resolved);

        throw new DirectoryNotFoundException($"目录不存在: {path}");
    }

    static string ResolveExistingFile(string path)
    {
        string resolved = TryResolvePath(path);
        if (File.Exists(resolved))
            return Path.GetFullPath(resolved);

        throw new FileNotFoundException($"文件不存在: {path}");
    }

    static string TryResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (Path.IsPathRooted(path))
            return path;

        string normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string? deviceServicesPath = TryResolveWithSeasonDeviceServices(normalized);
        if (!string.IsNullOrWhiteSpace(deviceServicesPath) &&
            (File.Exists(deviceServicesPath) || Directory.Exists(deviceServicesPath)))
        {
            return deviceServicesPath;
        }

        foreach (string baseDirectory in EnumerateSearchRoots())
        {
            string candidate = Path.Combine(baseDirectory, normalized);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }

        return normalized;
    }

    static string? TryResolveWithSeasonDeviceServices(string normalized)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? deviceServicesType = assembly.GetType("Season.Basic.DeviceServices", throwOnError: false, ignoreCase: false);
                if (deviceServicesType is null)
                    continue;

                PropertyInfo? coreProperty = deviceServicesType.GetProperty("Core", BindingFlags.Public | BindingFlags.Static);
                object? core = coreProperty?.GetValue(null);
                if (core is null)
                    return null;

                MethodInfo? loadFilePathMethod = core.GetType().GetMethod("LoadFilePath", new[] { typeof(string) });
                if (loadFilePathMethod is null)
                    return null;

                object? result = loadFilePathMethod.Invoke(
                    core,
                    new object[] { normalized.Replace(Path.DirectorySeparatorChar, '/') });
                if (result is string resolved && !string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }
            catch
            {
                // SeasonEngine 未加载或运行环境未提供 DeviceServices 时，继续使用本地路径探测。
            }
        }

        return null;
    }

    static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? start in new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                if (seen.Add(directory.FullName))
                    yield return directory.FullName;
            }
        }
    }
}