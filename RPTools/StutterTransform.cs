using System;
using System.Text;

namespace RPTools;

public static class StutterTransform
{
    /*
    Example:
    /em she walks over and asks the bartender "Can I have a drink?"
    -> /em she walks over and asks the bartender "C-can I have a d-drink?"

    /say "I-I already stutter" and "Another line"
    -> /say "I-I already stutter" and "A-another line"
    */

    public static string Transform(string input, StutterSettings settings)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        if (settings == null)
        {
            return input;
        }

        var effective = ResolveEffectiveSettings(settings);
        if ((effective.WordStutterChance <= 0f && !effective.AlwaysStutterFirstWord) ||
            effective.MaxRepeatsPerWord <= 0)
        {
            return input;
        }

        return TransformByLines(input, effective, settings);
    }

    private static string TransformByLines(string input, EffectiveSettings effective, StutterSettings settings)
    {
        var rng = CreateRng(input, settings);
        var sb = new StringBuilder(input.Length + 16);
        var lineStart = 0;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '\r')
            {
                var line = input.Substring(lineStart, i - lineStart);
                sb.Append(TransformLine(line, effective, settings, rng));
                if (i + 1 < input.Length && input[i + 1] == '\n')
                {
                    sb.Append("\r\n");
                    i++;
                }
                else
                {
                    sb.Append('\r');
                }

                lineStart = i + 1;
                continue;
            }

            if (c == '\n')
            {
                var line = input.Substring(lineStart, i - lineStart);
                sb.Append(TransformLine(line, effective, settings, rng));
                sb.Append('\n');
                lineStart = i + 1;
            }
        }

        if (lineStart <= input.Length)
        {
            var line = input.Substring(lineStart);
            sb.Append(TransformLine(line, effective, settings, rng));
        }

        return sb.ToString();
    }

    private static string TransformLine(string line, EffectiveSettings effective, StutterSettings settings, Random rng)
    {
        if (!HasQuotes(line, settings.StutterSingleQuotes, out var balanced))
        {
            return line;
        }

        if (!balanced)
        {
            return line;
        }

        return TransformQuotedText(line, effective, settings, rng);
    }

    private static string TransformQuotedText(string input, EffectiveSettings effective, StutterSettings settings, Random rng)
    {
        // Quote parsing is a single pass: toggle on " or optional ' while ignoring apostrophes inside words.
        var sb = new StringBuilder(input.Length + 16);
        var word = new StringBuilder();
        char? activeQuote = null;
        var stuttersInQuote = 0;
        var firstEligibleInQuote = true;
        string? lastWord = null;
        var lastSeparator = '\0';
        var suppressNextWord = false;
        var lastWordStuttered = false;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (activeQuote == null)
            {
                if (IsQuoteChar(input, i, settings.StutterSingleQuotes, out var quoteChar))
                {
                    activeQuote = quoteChar;
                    stuttersInQuote = 0;
                    firstEligibleInQuote = true;
                    lastWord = null;
                    lastSeparator = '\0';
                    suppressNextWord = false;
                    lastWordStuttered = false;
                    sb.Append(c);
                    continue;
                }

                sb.Append(c);
                continue;
            }

            if (IsClosingQuote(input, i, activeQuote.Value))
            {
                FlushWord();
                activeQuote = null;
                sb.Append(c);
                continue;
            }

            if (c == '/' && word.Length == 0)
            {
                suppressNextWord = true;
                sb.Append(c);
                continue;
            }

            if (IsWordChar(input, i))
            {
                word.Append(c);
                continue;
            }

            FlushWord();
            sb.Append(c);
            lastSeparator = c;
        }

        if (activeQuote != null)
        {
            FlushWord();
        }

        return sb.ToString();

        void FlushWord()
        {
            if (word.Length == 0)
            {
                return;
            }

            var token = word.ToString();
            var alreadyStuttered = lastSeparator == '-' && IsStutterPrefix(lastWord, token);
            var isRepeated = lastWord != null && token.Equals(lastWord, StringComparison.OrdinalIgnoreCase);
            var canRepeat = effective.Mode == StutterMode.Hard || !lastWordStuttered || !isRepeated;
            var transformed = ApplyStutter(token, effective, rng, ref stuttersInQuote, ref firstEligibleInQuote, alreadyStuttered, suppressNextWord, canRepeat);
            sb.Append(transformed.Text);
            lastWord = token;
            lastWordStuttered = transformed.Stuttered;
            word.Clear();
            suppressNextWord = false;
        }
    }

    private static StutterResult ApplyStutter(
        string word,
        EffectiveSettings effective,
        Random rng,
        ref int stuttersInSegment,
        ref bool firstEligibleInSegment,
        bool alreadyStuttered,
        bool suppressStutter,
        bool canRepeat)
    {
        if (!IsEligibleWord(word, effective))
        {
            return new StutterResult(word, false);
        }

        if (suppressStutter)
        {
            return new StutterResult(word, false);
        }

        var wasFirstEligible = firstEligibleInSegment;
        firstEligibleInSegment = false;

        if (alreadyStuttered && effective.RespectExistingStutters)
        {
            return new StutterResult(word, false);
        }

        if (!canRepeat)
        {
            return new StutterResult(word, false);
        }

        if (effective.MaxRepeatsPerWord <= 0)
        {
            return new StutterResult(word, false);
        }

        if (effective.MaxStuttersPerQuote > 0 && stuttersInSegment >= effective.MaxStuttersPerQuote)
        {
            return new StutterResult(word, false);
        }

        var startsWithVowel = IsVowel(word[0]);
        var baseChance = GetWordChance(effective, startsWithVowel);
        if (effective.Mode == StutterMode.Hard)
        {
            baseChance = Math.Clamp(baseChance * 1.1f, 0f, 1f);
        }

        var shouldStutter = (effective.AlwaysStutterFirstWord && wasFirstEligible) ||
                            (baseChance > 0f && rng.NextDouble() < baseChance);

        if (!shouldStutter)
        {
            return new StutterResult(word, false);
        }

        var repeatCount = GetRepeatCount(effective, startsWithVowel, rng);
        if (repeatCount <= 0)
        {
            return new StutterResult(word, false);
        }

        stuttersInSegment++;
        return new StutterResult(BuildStutteredWord(word, repeatCount), true);
    }

    private static string BuildStutteredWord(string word, int repeatCount)
    {
        if (repeatCount <= 0)
        {
            return word;
        }

        var prefix = word.Substring(0, 1);
        var sb = new StringBuilder(word.Length + repeatCount * 2);
        for (var i = 0; i < repeatCount; i++)
        {
            sb.Append(prefix);
            sb.Append('-');
        }

        sb.Append(word);
        return sb.ToString();
    }

    private static int GetRepeatCount(EffectiveSettings effective, bool startsWithVowel, Random rng)
    {
        var baseChance = startsWithVowel ? effective.VowelRepeatChance : effective.ConsonantRepeatChance;
        var biasFactor = startsWithVowel
            ? 0.5f + 0.5f * (1f - effective.ConsonantBias)
            : 0.5f + 0.5f * effective.ConsonantBias;
        var chance = Math.Clamp(baseChance * biasFactor, 0f, 1f);
        chance *= effective.Mode == StutterMode.Hard ? 1.1f : 0.7f;
        chance = Math.Clamp(chance, 0f, 1f);

        var repeats = 1;
        for (var i = 2; i <= effective.MaxRepeatsPerWord; i++)
        {
            if (rng.NextDouble() < chance)
            {
                repeats++;
                chance *= effective.Mode == StutterMode.Hard ? 0.75f : 0.5f;
                continue;
            }

            break;
        }

        return repeats;
    }

    private static bool IsEligibleWord(string word, EffectiveSettings effective)
    {
        if (word.Length < Math.Max(effective.MinWordLength, 1))
        {
            return false;
        }

        if (!char.IsLetter(word[0]))
        {
            return false;
        }

        if (word.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            word.StartsWith("www", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool HasQuotes(string input, bool allowSingleQuotes, out bool balanced)
    {
        balanced = true;
        var hasQuotes = false;
        char? activeQuote = null;
        for (var i = 0; i < input.Length; i++)
        {
            if (!IsQuoteChar(input, i, allowSingleQuotes, out var quoteChar))
            {
                continue;
            }

            hasQuotes = true;
            if (activeQuote == null)
            {
                activeQuote = quoteChar;
            }
            else if (activeQuote == quoteChar)
            {
                activeQuote = null;
            }
        }

        if (activeQuote != null)
        {
            balanced = false;
        }

        return hasQuotes;
    }

    private static bool IsQuoteChar(string input, int index, bool allowSingleQuotes, out char quoteChar)
    {
        var c = input[index];
        if (c == '"')
        {
            quoteChar = c;
            return true;
        }

        if (allowSingleQuotes && c == '\'' && !IsApostropheInWord(input, index))
        {
            quoteChar = c;
            return true;
        }

        quoteChar = '\0';
        return false;
    }

    private static bool IsClosingQuote(string input, int index, char activeQuote)
    {
        var c = input[index];
        if (c != activeQuote)
        {
            return false;
        }

        if (activeQuote == '\'' && IsApostropheInWord(input, index))
        {
            return false;
        }

        return true;
    }

    private static bool IsApostropheInWord(string input, int index)
    {
        if (index <= 0 || index >= input.Length - 1)
        {
            return false;
        }

        return char.IsLetter(input[index - 1]) && char.IsLetter(input[index + 1]);
    }

    private static bool IsWordChar(string input, int index)
    {
        var c = input[index];
        if (char.IsLetter(c))
        {
            return true;
        }

        return c == '\'' && IsApostropheInWord(input, index);
    }

    private static bool IsStutterPrefix(string? prefix, string word)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        if (prefix.Length > 2 || word.Length < prefix.Length)
        {
            return false;
        }

        return word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static float GetWordChance(EffectiveSettings effective, bool startsWithVowel)
    {
        var biasFactor = startsWithVowel
            ? 0.5f + 0.5f * (1f - effective.ConsonantBias)
            : 0.5f + 0.5f * effective.ConsonantBias;
        var chance = effective.WordStutterChance * biasFactor;
        if (effective.Mode == StutterMode.Soft)
        {
            chance *= 0.9f;
        }

        return Math.Clamp(chance, 0f, 1f);
    }

    private static bool IsVowel(char c)
    {
        return c is 'A' or 'E' or 'I' or 'O' or 'U' or 'a' or 'e' or 'i' or 'o' or 'u';
    }

    private static EffectiveSettings ResolveEffectiveSettings(StutterSettings settings)
    {
        if (settings.StrengthPreset == StutterStrengthPreset.Custom)
        {
            return new EffectiveSettings
            {
                StrengthPreset = settings.StrengthPreset,
                Mode = settings.Mode,
                WordStutterChance = Clamp01(settings.WordStutterChance),
                ConsonantBias = Clamp01(settings.ConsonantBias),
                VowelRepeatChance = Clamp01(settings.VowelRepeatChance),
                ConsonantRepeatChance = Clamp01(settings.ConsonantRepeatChance),
                MaxRepeatsPerWord = Math.Max(settings.MaxRepeatsPerWord, 1),
                MinWordLength = Math.Max(settings.MinWordLength, 1),
                RespectExistingStutters = settings.RespectExistingStutters,
                AlwaysStutterFirstWord = settings.AlwaysStutterFirstWord,
                MaxStuttersPerQuote = settings.MaxStuttersPerQuote,
            };
        }

        return settings.StrengthPreset switch
        {
            StutterStrengthPreset.Off => new EffectiveSettings
            {
                StrengthPreset = settings.StrengthPreset,
                Mode = StutterMode.Soft,
                WordStutterChance = 0f,
                ConsonantBias = 0.6f,
                VowelRepeatChance = 0.2f,
                ConsonantRepeatChance = 0.25f,
                MaxRepeatsPerWord = 1,
                MinWordLength = Math.Max(settings.MinWordLength, 1),
                RespectExistingStutters = settings.RespectExistingStutters,
                AlwaysStutterFirstWord = settings.AlwaysStutterFirstWord,
                MaxStuttersPerQuote = settings.MaxStuttersPerQuote,
            },
            StutterStrengthPreset.Light => new EffectiveSettings
            {
                StrengthPreset = settings.StrengthPreset,
                Mode = StutterMode.Soft,
                WordStutterChance = 0.15f,
                ConsonantBias = 0.65f,
                VowelRepeatChance = 0.25f,
                ConsonantRepeatChance = 0.3f,
                MaxRepeatsPerWord = 1,
                MinWordLength = Math.Max(settings.MinWordLength, 1),
                RespectExistingStutters = settings.RespectExistingStutters,
                AlwaysStutterFirstWord = settings.AlwaysStutterFirstWord,
                MaxStuttersPerQuote = settings.MaxStuttersPerQuote,
            },
            StutterStrengthPreset.Medium => new EffectiveSettings
            {
                StrengthPreset = settings.StrengthPreset,
                Mode = StutterMode.Soft,
                WordStutterChance = 0.3f,
                ConsonantBias = 0.7f,
                VowelRepeatChance = 0.35f,
                ConsonantRepeatChance = 0.45f,
                MaxRepeatsPerWord = 2,
                MinWordLength = Math.Max(settings.MinWordLength, 1),
                RespectExistingStutters = settings.RespectExistingStutters,
                AlwaysStutterFirstWord = settings.AlwaysStutterFirstWord,
                MaxStuttersPerQuote = settings.MaxStuttersPerQuote,
            },
            StutterStrengthPreset.Heavy => new EffectiveSettings
            {
                StrengthPreset = settings.StrengthPreset,
                Mode = StutterMode.Hard,
                WordStutterChance = 0.5f,
                ConsonantBias = 0.8f,
                VowelRepeatChance = 0.5f,
                ConsonantRepeatChance = 0.65f,
                MaxRepeatsPerWord = 3,
                MinWordLength = Math.Max(settings.MinWordLength, 1),
                RespectExistingStutters = settings.RespectExistingStutters,
                AlwaysStutterFirstWord = settings.AlwaysStutterFirstWord,
                MaxStuttersPerQuote = settings.MaxStuttersPerQuote,
            },
            _ => new EffectiveSettings
            {
                StrengthPreset = StutterStrengthPreset.Medium,
                Mode = StutterMode.Soft,
                WordStutterChance = 0.3f,
                ConsonantBias = 0.7f,
                VowelRepeatChance = 0.35f,
                ConsonantRepeatChance = 0.45f,
                MaxRepeatsPerWord = 2,
                MinWordLength = Math.Max(settings.MinWordLength, 1),
                RespectExistingStutters = settings.RespectExistingStutters,
                AlwaysStutterFirstWord = settings.AlwaysStutterFirstWord,
                MaxStuttersPerQuote = settings.MaxStuttersPerQuote,
            }
        };
    }

    private static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        if (value > 1f)
        {
            return 1f;
        }

        return value;
    }

    private static Random CreateRng(string input, StutterSettings settings)
    {
        if (!settings.StableSeed)
        {
            return new Random();
        }

        // Stable mode hashes input + settings to keep identical text deterministic between runs.
        var seed = ComputeSeed(input, settings);
        return new Random(seed);
    }

    private static int ComputeSeed(string input, StutterSettings settings)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (int)settings.StrengthPreset;
            hash = hash * 31 + (int)settings.Mode;
            hash = hash * 31 + (int)(Clamp01(settings.WordStutterChance) * 1000);
            hash = hash * 31 + (int)(Clamp01(settings.ConsonantBias) * 1000);
            hash = hash * 31 + (int)(Clamp01(settings.VowelRepeatChance) * 1000);
            hash = hash * 31 + (int)(Clamp01(settings.ConsonantRepeatChance) * 1000);
            hash = hash * 31 + settings.MaxRepeatsPerWord;
            hash = hash * 31 + settings.MinWordLength;
            hash = hash * 31 + (settings.RespectExistingStutters ? 1 : 0);
            hash = hash * 31 + (settings.StutterSingleQuotes ? 1 : 0);
            hash = hash * 31 + (settings.AlwaysStutterFirstWord ? 1 : 0);
            hash = hash * 31 + settings.MaxStuttersPerQuote;
            for (var i = 0; i < input.Length; i++)
            {
                hash = (hash * 31) + input[i];
            }

            return hash;
        }
    }

    private readonly struct EffectiveSettings
    {
        public StutterStrengthPreset StrengthPreset { get; init; }
        public StutterMode Mode { get; init; }
        public float WordStutterChance { get; init; }
        public float ConsonantBias { get; init; }
        public float VowelRepeatChance { get; init; }
        public float ConsonantRepeatChance { get; init; }
        public int MaxRepeatsPerWord { get; init; }
        public int MinWordLength { get; init; }
        public bool RespectExistingStutters { get; init; }
        public bool AlwaysStutterFirstWord { get; init; }
        public int MaxStuttersPerQuote { get; init; }
    }

    private readonly struct StutterResult
    {
        public StutterResult(string text, bool stuttered)
        {
            Text = text;
            Stuttered = stuttered;
        }

        public string Text { get; }
        public bool Stuttered { get; }
    }
}

