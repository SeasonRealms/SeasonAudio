// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonAudio
// SeasonAudio for Stable Audio Models

namespace Season.Audio;

public partial class StableAudio
{
    static PromptTokenizer CreateTokenizer(string tokenizerModelPath, string tokenizerConfigPath, string specialTokensMapPath, string? tokenizerJsonPath)
    {
        var metadata = LoadTokenizerMetadata(tokenizerConfigPath, specialTokensMapPath);
        if (File.Exists(tokenizerModelPath))
        {
            using var stream = File.OpenRead(tokenizerModelPath);
            SentencePieceTokenizer tokenizer = LlamaTokenizer.Create(
                stream,
                addBeginOfSentence: false,
                addEndOfSentence: false,
                specialTokens: metadata.SpecialTokens);

            Trace.WriteLine("[StableAudio] tokenizer.model preferred over tokenizer.json for HF-compatible SentencePiece tokenization.");
            return new PromptTokenizer(
                metadata.PadTokenId,
                NormalizeSentencePiecePrompt,
                text => tokenizer.EncodeToIds(text));
        }

        if (!string.IsNullOrWhiteSpace(tokenizerJsonPath) && File.Exists(tokenizerJsonPath))
        {
            try
            {
                var fastTokenizer = CreateFastTokenizer(tokenizerJsonPath, metadata);
                Trace.WriteLine("[StableAudio] tokenizer.json fast-path enabled.");
                return fastTokenizer;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[StableAudio] tokenizer.json fast-path unavailable, fallback to tokenizer.model: {ex.Message}");
            }
        }
        throw new FileNotFoundException("No usable tokenizer file was found (requires `tokenizer.model` or `tokenizer.json`).", tokenizerModelPath);
    }

    static PromptTokenizer CreateFastTokenizer(string tokenizerJsonPath, TokenizerMetadata metadata)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(tokenizerJsonPath));
        var root = document.RootElement;
        var model = root.GetProperty("model");

        string modelType = model.GetProperty("type").GetString() ?? string.Empty;
        if (!modelType.Equals("BPE", StringComparison.Ordinal))
            throw new InvalidDataException($"Only BPE models in tokenizer.json are currently supported; got {modelType}.");

        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in model.GetProperty("vocab").EnumerateObject())
            vocabulary[property.Name] = property.Value.GetInt32();

        bool fuseUnknownTokens =
            model.TryGetProperty("fuse_unk", out var fuseUnknownElement) &&
            fuseUnknownElement.ValueKind == JsonValueKind.True;

        bool byteFallback =
            model.TryGetProperty("byte_fallback", out var byteFallbackElement) &&
            byteFallbackElement.ValueKind == JsonValueKind.True;

        int unknownTokenId = vocabulary.TryGetValue(metadata.UnknownToken, out int resolvedUnknownTokenId)
            ? resolvedUnknownTokenId
            : throw new InvalidDataException($"The vocab is missing the unk token: {metadata.UnknownToken}");

        Func<string, string> modelNormalizer = BuildFastTokenizerNormalizer(root);
        ValidateFastTokenizerPreTokenizer(root);

        var tokenizer = new FastBpeTokenizer(
            vocabulary,
            BuildMergeRanks(model.GetProperty("merges")),
            metadata.SpecialTokens,
            metadata.UnknownToken,
            unknownTokenId,
            fuseUnknownTokens,
            byteFallback,
            modelNormalizer);

        return new PromptTokenizer(
            metadata.PadTokenId,
            NormalizeFastPathPrompt,
            tokenizer.EncodeToIds);
    }

    static Dictionary<BpeMergePair, int> BuildMergeRanks(JsonElement mergesElement)
    {
        if (mergesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The merges field in tokenizer.json has an invalid format.");

        var ranks = new Dictionary<BpeMergePair, int>();
        int rank = 0;

        foreach (var merge in mergesElement.EnumerateArray())
        {
            if (merge.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("A merge entry in tokenizer.json is not an array.");

            using var pair = merge.EnumerateArray();
            if (!pair.MoveNext())
                continue;
            string left = pair.Current.GetString() ?? string.Empty;

            if (!pair.MoveNext())
                continue;
            string right = pair.Current.GetString() ?? string.Empty;

            ranks[new BpeMergePair(left, right)] = rank++;
        }

        return ranks;
    }

    static Func<string, string> BuildFastTokenizerNormalizer(JsonElement root)
    {
        if (!root.TryGetProperty("normalizer", out var normalizerElement) ||
            normalizerElement.ValueKind == JsonValueKind.Null ||
            normalizerElement.ValueKind == JsonValueKind.Undefined)
        {
            return static text => text;
        }

        string normalizerType = normalizerElement.GetProperty("type").GetString() ?? string.Empty;
        if (!normalizerType.Equals("Replace", StringComparison.Ordinal))
            throw new InvalidDataException($"Only the Replace normalizer in tokenizer.json is currently supported; got {normalizerType}.");

        string pattern = normalizerElement
            .GetProperty("pattern")
            .GetProperty("String")
            .GetString() ?? string.Empty;
        string replacement = normalizerElement.GetProperty("content").GetString() ?? string.Empty;

        return text => text.Replace(pattern, replacement, StringComparison.Ordinal);
    }

    static void ValidateFastTokenizerPreTokenizer(JsonElement root)
    {
        if (!root.TryGetProperty("pre_tokenizer", out var preTokenizerElement) ||
            preTokenizerElement.ValueKind == JsonValueKind.Null ||
            preTokenizerElement.ValueKind == JsonValueKind.Undefined)
        {
            return;
        }

        string type = preTokenizerElement.GetProperty("type").GetString() ?? string.Empty;
        string behavior = preTokenizerElement.TryGetProperty("behavior", out var behaviorElement)
            ? behaviorElement.GetString() ?? string.Empty
            : string.Empty;
        bool invert = preTokenizerElement.TryGetProperty("invert", out var invertElement) &&
                      invertElement.ValueKind == JsonValueKind.True;
        string pattern = preTokenizerElement
            .GetProperty("pattern")
            .GetProperty("String")
            .GetString() ?? string.Empty;

        bool isSupported =
            type.Equals("Split", StringComparison.Ordinal) &&
            behavior.Equals("MergedWithPrevious", StringComparison.Ordinal) &&
            !invert &&
            pattern.Equals(" ", StringComparison.Ordinal);

        if (!isSupported)
        {
            throw new InvalidDataException(
                $"Only the Split/MergedWithPrevious/space pre_tokenizer in tokenizer.json is currently supported; got type={type}, behavior={behavior}, invert={invert}, pattern={pattern}.");
        }
    }


    static TokenizerMetadata LoadTokenizerMetadata(string tokenizerConfigPath, string specialTokensMapPath)
    {
        var knownTokenIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var specialTokens = new Dictionary<string, int>(StringComparer.Ordinal);
        string? padToken = null;
        string? unknownToken = null;

        if (File.Exists(tokenizerConfigPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(tokenizerConfigPath));
            var root = document.RootElement;

            if (root.TryGetProperty("added_tokens_decoder", out var addedTokens) &&
                addedTokens.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in addedTokens.EnumerateObject())
                {
                    if (!int.TryParse(property.Name, out int id))
                        continue;
                    if (!property.Value.TryGetProperty("content", out var contentElement))
                        continue;

                    string? token = contentElement.GetString();
                    if (string.IsNullOrEmpty(token))
                        continue;

                    knownTokenIds[token] = id;

                    if (property.Value.TryGetProperty("special", out var specialElement) &&
                        specialElement.ValueKind == JsonValueKind.True)
                    {
                        specialTokens[token] = id;
                    }
                }
            }

            if (root.TryGetProperty("pad_token", out var padTokenElement) &&
                padTokenElement.ValueKind == JsonValueKind.String)
            {
                padToken = padTokenElement.GetString();
            }

            if (root.TryGetProperty("unk_token", out var unknownTokenElement) &&
                unknownTokenElement.ValueKind == JsonValueKind.String)
            {
                unknownToken = unknownTokenElement.GetString();
            }
        }

        if (File.Exists(specialTokensMapPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(specialTokensMapPath));
            var root = document.RootElement;

            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("additional_special_tokens") &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tokenElement in property.Value.EnumerateArray())
                    {
                        string? token = tokenElement.GetString();
                        if (!string.IsNullOrEmpty(token) && knownTokenIds.TryGetValue(token, out int id0))
                            specialTokens[token] = id0;
                    }

                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object ||
                    !property.Value.TryGetProperty("content", out var contentElement))
                {
                    continue;
                }

                string? tokenValue = contentElement.GetString();
                if (string.IsNullOrEmpty(tokenValue))
                    continue;

                if (knownTokenIds.TryGetValue(tokenValue, out int id))
                    specialTokens[tokenValue] = id;

                if (property.NameEquals("pad_token") && padToken is null)
                    padToken = tokenValue;
            }
        }

        int padTokenId = 0;
        if (!string.IsNullOrEmpty(padToken) && knownTokenIds.TryGetValue(padToken, out int resolvedPadTokenId))
            padTokenId = resolvedPadTokenId;

        return new TokenizerMetadata(specialTokens, padTokenId, unknownToken ?? "<unk>");
    }
}
