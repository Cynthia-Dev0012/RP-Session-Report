using System;

namespace RPTools;

[Serializable]
public class StutterSettings
{
    public StutterStrengthPreset StrengthPreset { get; set; } = StutterStrengthPreset.Medium;
    public StutterMode Mode { get; set; } = StutterMode.Soft;
    public float WordStutterChance { get; set; } = 0.3f;
    public float ConsonantBias { get; set; } = 0.7f;
    public float VowelRepeatChance { get; set; } = 0.35f;
    public float ConsonantRepeatChance { get; set; } = 0.45f;
    public int MaxRepeatsPerWord { get; set; } = 2;
    public int MinWordLength { get; set; } = 2;
    public bool RespectExistingStutters { get; set; } = true;
    public bool AlwaysStutterFirstWord { get; set; } = true;
    public int MaxStuttersPerQuote { get; set; } = 0;
    public bool StableSeed { get; set; } = true;
    public bool LivePreview { get; set; } = true;
    public bool StutterSingleQuotes { get; set; } = false;
    public bool StutterEverythingWhenNoQuotes { get; set; } = false;
}

public enum StutterStrengthPreset
{
    Off,
    Light,
    Medium,
    Heavy,
    Custom,
}

public enum StutterMode
{
    Soft,
    Hard,
}

