// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio
// SeasonAudio for Stable Audio Models

namespace Season.AI;

public partial class StableAudio
{
    static string NormalizeSentencePiecePrompt(string prompt) => NormalizeLineEndings(prompt);

    static string NormalizeFastPathPrompt(string prompt) => NormalizeLineEndings(prompt);

    static string NormalizeLineEndings(string prompt) =>
        string.IsNullOrEmpty(prompt)
            ? string.Empty
            : prompt.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

    static string EscapeForLog(string text) =>
        text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    static string FormatSlice<T>(IReadOnlyList<T> values, int maxCount)
    {
        int count = Math.Min(values.Count, maxCount);
        var builder = new StringBuilder();
        builder.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(values[i]);
        }

        if (count < values.Count)
            builder.Append(", ...");

        builder.Append(']');
        return builder.ToString();
    }

    static string FormatFloatSlice(IReadOnlyList<float> values, int maxCount)
    {
        int count = Math.Min(values.Count, maxCount);
        var builder = new StringBuilder();
        builder.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(values[i].ToString("F6"));
        }

        if (count < values.Count)
            builder.Append(", ...");

        builder.Append(']');
        return builder.ToString();
    }

    static string FormatHiddenPreview(float[] hiddenStates, int validTokenCount, int previewHiddenTokenCount, int previewHiddenDimensionCount)
    {
        int tokenCount = Math.Min(Math.Max(validTokenCount, 1), Math.Min(MaxTextTokens, previewHiddenTokenCount));
        int dimCount = Math.Min(768, previewHiddenDimensionCount);
        var builder = new StringBuilder();

        for (int tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append($"hidden[{tokenIndex}][0:{dimCount}]=[");
            int baseIndex = tokenIndex * 768;
            for (int dimIndex = 0; dimIndex < dimCount; dimIndex++)
            {
                if (dimIndex > 0)
                    builder.Append(", ");
                builder.Append(hiddenStates[baseIndex + dimIndex].ToString("F6"));
            }

            if (dimCount < 768)
                builder.Append(", ...");

            builder.Append(']');
        }

        return builder.ToString();
    }

    static string DescribeStats(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
            return "count=0";

        double sum = 0d;
        double sumSquares = 0d;
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        for (int i = 0; i < values.Length; i++)
        {
            float value = values[i];
            if (float.IsNaN(value) || float.IsInfinity(value))
                continue;

            if (value < min)
                min = value;
            if (value > max)
                max = value;

            sum += value;
            sumSquares += value * value;
        }

        double mean = sum / values.Length;
        double variance = Math.Max(0d, (sumSquares / values.Length) - (mean * mean));
        double std = Math.Sqrt(variance);
        return $"count={values.Length}, min={min:F5}, max={max:F5}, mean={mean:F5}, std={std:F5}";
    }

    static string DescribeArrayDifference(string name, float[] left, float[] right)
    {
        if (left.Length != right.Length)
            throw new InvalidDataException($"{name} 的数组长度不一致，left={left.Length}, right={right.Length}");

        var diff = new float[left.Length];
        double dot = 0d;
        double leftNormSquared = 0d;
        double rightNormSquared = 0d;
        double maxAbsDiff = 0d;

        for (int i = 0; i < left.Length; i++)
        {
            float leftValue = left[i];
            float rightValue = right[i];
            float delta = leftValue - rightValue;
            diff[i] = delta;
            dot += leftValue * rightValue;
            leftNormSquared += leftValue * leftValue;
            rightNormSquared += rightValue * rightValue;
            maxAbsDiff = Math.Max(maxAbsDiff, Math.Abs(delta));
        }

        double cosine = leftNormSquared <= 1e-12d || rightNormSquared <= 1e-12d
            ? 0d
            : dot / Math.Sqrt(leftNormSquared * rightNormSquared);

        return $"{name}: cosine={cosine:F6}, maxAbsDiff={maxAbsDiff:F6}, diff={DescribeStats(diff)}";
    }

    static void AppendInputVariantDebug(
        StringBuilder builder,
        string name,
        float[] latent,
        float[] velocity,
        float[] denoised,
        float[] baselineVelocity,
        float[] baselineDenoised)
    {
        builder.AppendLine(name);
        builder.AppendLine($"  latent={DescribeStats(latent)}");
        builder.AppendLine($"  velocity={DescribeStats(velocity)}");
        builder.AppendLine($"  velocity vs baseline={DescribeArrayDifference("diff", baselineVelocity, velocity)}");
        builder.AppendLine($"  denoised={DescribeStats(denoised)}");
        builder.AppendLine($"  denoised vs baseline={DescribeArrayDifference("diff", baselineDenoised, denoised)}");
    }

    static float[] ScaleArray(float[] source, float scale)
    {
        var scaled = new float[source.Length];
        for (int i = 0; i < source.Length; i++)
            scaled[i] = source[i] * scale;

        return scaled;
    }

    static float[] ComputeDenoisedWithDelta(float[] latent, float[] velocity, float delta)
    {
        var candidate = new float[latent.Length];
        for (int i = 0; i < latent.Length; i++)
            candidate[i] = latent[i] - (delta * velocity[i]);

        return candidate;
    }

    static float[] ComputeDenoisedWithPositiveDelta(float[] latent, float[] velocity, float delta)
    {
        var candidate = new float[latent.Length];
        for (int i = 0; i < latent.Length; i++)
            candidate[i] = latent[i] + (delta * velocity[i]);

        return candidate;
    }

    static string DescribeDecodedAudio(DecodedAudio audio)
    {
        float[] samples = audio.Samples;
        float peak = 0f;
        int normalizedOverflowCount = 0;
        int pcmNearSaturationCount = 0;
        int pcmRangeOverflowCount = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i];
            if (float.IsNaN(value) || float.IsInfinity(value))
                continue;

            float abs = MathF.Abs(value);
            if (abs > peak)
                peak = abs;
            if (abs > 1f)
                normalizedOverflowCount++;
            if (abs >= 32000f)
                pcmNearSaturationCount++;
            if (abs > short.MaxValue)
                pcmRangeOverflowCount++;
        }

        bool looksLikePcm = peak > 2f;
        return looksLikePcm
            ? $"channels={audio.ChannelCount}, looksLikePcm={looksLikePcm}, peak={peak:F5}, pcmNearSaturationCount={pcmNearSaturationCount}, pcmRangeOverflowCount={pcmRangeOverflowCount}, stats={DescribeStats(samples)}"
            : $"channels={audio.ChannelCount}, looksLikePcm={looksLikePcm}, peak={peak:F5}, normalizedOverflowCount={normalizedOverflowCount}, pcmRangeOverflowCount={pcmRangeOverflowCount}, stats={DescribeStats(samples)}";
    }
}