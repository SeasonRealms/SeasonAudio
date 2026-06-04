// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio
// SeasonAudio for Stable Audio Models

namespace Season.AI;

public partial class StableAudio
{
    static InferenceSession CreateSession(string modelPath, string? provider, bool allowCpuFallback = false)
    {
        try
        {
            return CreateSessionCore(modelPath, provider);
        }
        catch (OnnxRuntimeException ex) when (allowCpuFallback && !string.Equals(provider, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            Trace.WriteLine(
                $"[StableAudio] provider session creation failed for `{modelPath}` with provider `{provider ?? "auto"}`. " +
                $"Fallback to CPUExecutionProvider. Reason: {ex.Message}");
            return CreateSessionCore(modelPath, "cpu");
        }
    }

    static InferenceSession CreateSessionCore(string modelPath, string? provider)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        foreach (var methodName in GetProviderMethodNames(provider))
        {
            var method = typeof(SessionOptions).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
                continue;

            try
            {
                var args = method.GetParameters()
                    .Select(CreateDefaultValue)
                    .ToArray();
                method.Invoke(options, args);
                break;
            }
            catch
            {
                // Provider 不可用时继续回退。
            }
        }

        return new InferenceSession(modelPath, options);
    }

    static void ValidateDiTSessionContract(InferenceSession session, string modelPath)
    {
        ValidateMetadata(session.InputMetadata, "x", typeof(float), new int?[] { 1, LatentChannels, null }, modelPath);
        ValidateMetadata(session.InputMetadata, "t", typeof(float), new int?[] { 1 }, modelPath);
        ValidateMetadata(session.InputMetadata, "t5_hidden", typeof(float), new int?[] { 1, MaxTextTokens, 768 }, modelPath);
        ValidateMetadata(session.InputMetadata, "t5_mask", typeof(float), new int?[] { 1, MaxTextTokens }, modelPath);
        ValidateMetadata(session.InputMetadata, "seconds_total", typeof(float), new int?[] { 1 }, modelPath);
        ValidateMetadata(session.InputMetadata, "local_add_cond", typeof(float), new int?[] { 1, InpaintConditionChannels, null }, modelPath);
        ValidateMetadata(session.OutputMetadata, "velocity", typeof(float), new int?[] { null, LatentChannels, null }, modelPath);
    }

    static void ValidateMetadata(
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        string name,
        Type expectedElementType,
        params int?[] expectedDimensions) =>
        ValidateMetadata(metadataMap, name, expectedElementType, expectedDimensions, string.Empty);

    static void ValidateMetadata(
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        string name,
        Type expectedElementType,
        int?[] expectedDimensions,
        string modelPath)
    {
        if (!metadataMap.TryGetValue(name, out var metadata))
            throw new InvalidDataException($"ONNX 输入/输出契约缺少 `{name}`: {modelPath}");

        if (metadata.ElementType != expectedElementType)
        {
            throw new InvalidDataException(
                $"ONNX 张量 `{name}` 的元素类型不符，期望 `{expectedElementType.Name}`，实际 `{metadata.ElementType?.Name ?? "unknown"}`: {modelPath}");
        }

        int[] actualDimensions = metadata.Dimensions;
        if (actualDimensions.Length != expectedDimensions.Length)
        {
            throw new InvalidDataException(
                $"ONNX 张量 `{name}` 的维度数不符，期望 {expectedDimensions.Length}，实际 {actualDimensions.Length}: {modelPath}");
        }

        for (int i = 0; i < expectedDimensions.Length; i++)
        {
            int? expected = expectedDimensions[i];
            if (expected.HasValue && actualDimensions[i] != expected.Value)
            {
                throw new InvalidDataException(
                    $"ONNX 张量 `{name}` 的第 {i} 维不符，期望 {expected.Value}，实际 {actualDimensions[i]}: {modelPath}");
            }
        }
    }

    static void ValidateRunDiTInputs(
        float[] latent,
        int latentLength,
        DenseTensor<float> hiddenStates,
        DenseTensor<float> t5Mask,
        DenseTensor<float> secondsTotal,
        DenseTensor<float> localAddCond)
    {
        if (latent.Length != LatentChannels * latentLength)
            throw new InvalidDataException($"DiT 输入 `x` 的展平长度不匹配，期望 {LatentChannels * latentLength}，实际 {latent.Length}。");

        ValidateTensorShape(hiddenStates, "t5_hidden", 1, MaxTextTokens, 768);
        ValidateTensorShape(t5Mask, "t5_mask", 1, MaxTextTokens);
        ValidateTensorShape(secondsTotal, "seconds_total", 1);
        ValidateTensorShape(localAddCond, "local_add_cond", 1, InpaintConditionChannels, latentLength);
    }

    static void ValidateTensorShape<T>(DenseTensor<T> tensor, string name, params int[] expectedDimensions)
        where T : struct
    {
        var actualDimensions = tensor.Dimensions.ToArray();
        if (actualDimensions.Length != expectedDimensions.Length)
        {
            throw new InvalidDataException(
                $"DiT 输入 `{name}` 的维度数不匹配，期望 {expectedDimensions.Length}，实际 {actualDimensions.Length}。");
        }

        for (int i = 0; i < expectedDimensions.Length; i++)
        {
            if (actualDimensions[i] != expectedDimensions[i])
            {
                throw new InvalidDataException(
                    $"DiT 输入 `{name}` 的第 {i} 维不匹配，期望 {expectedDimensions[i]}，实际 {actualDimensions[i]}。");
            }
        }
    }

    static object? CreateDefaultValue(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue)
            return parameter.DefaultValue;

        return parameter.ParameterType.IsValueType
            ? Activator.CreateInstance(parameter.ParameterType)
            : null;
    }

    static string[] GetProviderMethodNames(string? provider)
    {
        return (provider ?? "auto").ToLowerInvariant() switch
        {
            "cpu" => Array.Empty<string>(),
            "dml" => new[] { "AppendExecutionProvider_DML" },
            "cuda" => new[] { "AppendExecutionProvider_CUDA" },
            "coreml" => new[] { "AppendExecutionProvider_CoreML" },
            "nnapi" => new[] { "AppendExecutionProvider_Nnapi", "AppendExecutionProvider_NNAPI" },
            _ when OperatingSystem.IsWindows() => new[] { "AppendExecutionProvider_DML", "AppendExecutionProvider_CUDA" },
            _ when OperatingSystem.IsAndroid() => new[] { "AppendExecutionProvider_Nnapi", "AppendExecutionProvider_NNAPI" },
            _ when OperatingSystem.IsIOS() || OperatingSystem.IsMacOS() => new[] { "AppendExecutionProvider_CoreML" },
            _ => new[] { "AppendExecutionProvider_CUDA" }
        };
    }

    static string DescribeProviderPlan(string? provider)
    {
        string requested = provider ?? "auto";
        string[] methods = GetProviderMethodNames(provider);

        if (methods.Length == 0)
            return $"requested={requested}, session=CPUExecutionProvider";

        return $"requested={requested}, session=ORT default + [{string.Join(" -> ", methods)}] fallback chain";
    }
}