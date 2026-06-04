// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio
// SeasonAudio for Stable Audio Models

namespace Season.AI;

public partial class StableAudio
{
    TextCondition EncodePrompt(ModelBundle bundle, string prompt)
    {
        return EncodePromptDebug(bundle, prompt).Condition;
    }

    PromptEncodingDebug EncodePromptDebug(ModelBundle bundle, string prompt)
    {
        var ids = new long[MaxTextTokens];
        var mask = new long[MaxTextTokens];
        Array.Fill(ids, bundle.Tokenizer.PadTokenId);

        string normalizedPrompt = bundle.Tokenizer.Normalize(prompt);
        IReadOnlyList<int> encoded = string.IsNullOrWhiteSpace(normalizedPrompt)
            ? Array.Empty<int>()
            : bundle.Tokenizer.EncodeToIds(normalizedPrompt);
        int length = Math.Min(encoded.Count, MaxTextTokens);

        for (int i = 0; i < length; i++)
        {
            ids[i] = encoded[i];
            mask[i] = 1;
        }

        var inputIds = new DenseTensor<long>(ids, TokenMaskDimensions);
        var attentionMask = new DenseTensor<long>(mask, TokenMaskDimensions);

        using var results = bundle.TextEncoderSession.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
        });

        var hidden = CopyRequiredFloatTensor(results, "hidden_states");
        Sanitize(hidden);

        var ditMaskData = new float[mask.Length];
        for (int i = 0; i < mask.Length; i++)
            ditMaskData[i] = mask[i] == 0 ? 0f : 1f;

        return new PromptEncodingDebug(
            normalizedPrompt,
            ids,
            mask,
            hidden,
            length,
            new TextCondition(
                new DenseTensor<float>(hidden, HiddenStateDimensions),
                new DenseTensor<float>(ditMaskData, TokenMaskDimensions)));
    }

    static TextCondition CreateZeroCondition() => ZeroCondition;

    static float[] RunDiT(
        InferenceSession session,
        float[] latent,
        int latentLength,
        float timestep,
        DenseTensor<float> hiddenStates,
        DenseTensor<float> t5Mask,
        DenseTensor<float> secondsTotal,
        DenseTensor<float> localAddCond)
    {
        ValidateRunDiTInputs(latent, latentLength, hiddenStates, t5Mask, secondsTotal, localAddCond);
        var latentTensor = new DenseTensor<float>(latent, new[] { 1, LatentChannels, latentLength });
        float[] tValue = [timestep];
        var tTensor = new DenseTensor<float>(tValue, SingleValueDimensions);

        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("x", latentTensor),
            NamedOnnxValue.CreateFromTensor("t", tTensor),
            NamedOnnxValue.CreateFromTensor("t5_hidden", hiddenStates),
            NamedOnnxValue.CreateFromTensor("t5_mask", t5Mask),
            NamedOnnxValue.CreateFromTensor("seconds_total", secondsTotal),
            NamedOnnxValue.CreateFromTensor("local_add_cond", localAddCond)
        });

        return CopyRequiredFloatTensor(results, "velocity");
    }

    static DecodedAudio DecodeAudio(InferenceSession session, float[] latent, int latentLength)
    {
        var latentTensor = new DenseTensor<float>(latent, new[] { 1, LatentChannels, latentLength });

        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("latent", latentTensor)
        });

        if (TryReadTensor(results, "pcm", out Tensor<float>? floatTensor))
            return ToAudio(floatTensor!);
        if (TryReadTensor(results, "pcm", out Tensor<Half>? halfTensor))
            return ToAudio(halfTensor!);
        if (TryReadTensor(results, "pcm", out Tensor<int>? intTensor))
            return ToAudio(intTensor!);
        if (TryReadTensor(results, "pcm", out Tensor<short>? shortTensor))
            return ToAudio(shortTensor!);

        throw new InvalidDataException("解码器输出 `pcm` 的张量类型不受支持。");
    }

    static DecodedAudio ToAudio<T>(Tensor<T> tensor)
        where T : struct
    {
        int[] dims = tensor.Dimensions.ToArray();
        T[] raw = tensor.ToArray();
        float[] values = new float[raw.Length];

        for (int i = 0; i < raw.Length; i++)
            values[i] = Convert.ToSingle(raw[i]);

        if (dims.Length == 3)
        {
            int batch = dims[0];
            if (batch != 1)
                throw new InvalidDataException($"当前仅支持 batch=1，实际为 {batch}。");

            if (dims[1] is 1 or 2)
                return ExtractChannelsFirst(values, dims[1], dims[2]);

            if (dims[2] is 1 or 2)
                return ExtractChannelsLast(values, dims[1], dims[2]);
        }
        else if (dims.Length == 2)
        {
            if (dims[0] is 1 or 2)
                return ExtractChannelsFirst(values, dims[0], dims[1]);

            if (dims[1] is 1 or 2)
                return ExtractChannelsLast(values, dims[0], dims[1]);
        }

        throw new InvalidDataException($"无法识别解码器输出形状: [{string.Join(", ", dims)}]");
    }

    static DecodedAudio ExtractChannelsFirst(float[] values, int channelCount, int frameCount)
    {
        var interleaved = new float[channelCount * frameCount];
        for (int channel = 0; channel < channelCount; channel++)
        {
            for (int frame = 0; frame < frameCount; frame++)
                interleaved[(frame * channelCount) + channel] = values[(channel * frameCount) + frame];
        }

        return new DecodedAudio(interleaved, channelCount);
    }

    static DecodedAudio ExtractChannelsLast(float[] values, int frameCount, int channelCount)
    {
        var interleaved = new float[channelCount * frameCount];
        Array.Copy(values, interleaved, interleaved.Length);
        return new DecodedAudio(interleaved, channelCount);
    }

    static float[] CopyRequiredFloatTensor(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        string outputName)
    {
        if (TryReadTensor(results, outputName, out Tensor<float>? floatTensor))
            return floatTensor!.ToArray();
        if (TryReadTensor(results, outputName, out Tensor<Half>? halfTensor))
            return Array.ConvertAll(halfTensor!.ToArray(), static value => (float)value);

        throw new InvalidDataException($"输出 `{outputName}` 不是受支持的浮点张量。");
    }

    static bool TryReadTensor<T>(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        string outputName,
        out Tensor<T>? tensor)
        where T : struct
    {
        foreach (var result in results)
        {
            if (!result.Name.Equals(outputName, StringComparison.Ordinal))
                continue;

            try
            {
                tensor = result.AsTensor<T>();
                if (tensor != null)
                    return true;
            }
            catch
            {
                // 输出名称匹配但张量元素类型不匹配时，继续尝试其它 T。
            }
        }

        tensor = null;
        return false;
    }
}
