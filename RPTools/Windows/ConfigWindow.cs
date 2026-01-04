using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace RPTools.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("A Wonderful Configuration Window###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(640, 480);
        SizeCondition = ImGuiCond.Always;

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("ConfigTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("General"))
        {
            DrawGeneralSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Stutter"))
        {
            var avail = ImGui.GetContentRegionAvail();
            if (ImGui.BeginChild("StutterSettingsScroll", avail, false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                DrawStutterSettings();
                ImGui.EndChild();
            }
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawGeneralSettings()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Autosave");

        var autosaveEnabled = configuration.AutoSaveEnabled;
        if (ImGui.Checkbox("Enable autosave", ref autosaveEnabled))
        {
            configuration.AutoSaveEnabled = autosaveEnabled;
            configuration.Save();
        }

        ImGui.SameLine();
        var autosaveInterval = configuration.AutoSaveIntervalSeconds;
        ImGui.SetNextItemWidth(90f);
        if (ImGui.InputInt("Every (sec)", ref autosaveInterval))
        {
            autosaveInterval = Math.Max(autosaveInterval, MainWindow.MinAutoSaveSeconds);
            configuration.AutoSaveIntervalSeconds = autosaveInterval;
            configuration.Save();
        }

        ImGui.Separator();
        if (ImGui.Button("Open Notes Folder"))
        {
            OpenNotesFolder();
        }

        ImGui.Separator();
        if (ImGui.Button("Open Changelog"))
        {
            plugin.ToggleChangelogUi();
        }
    }

    private void DrawStutterSettings()
    {
        var settings = configuration.StutterWriterSettings ??= new StutterSettings();
        var changed = false;

        var presetIndex = (int)settings.StrengthPreset;
        if (ImGui.Combo("Preset", ref presetIndex, "Off\0Light\0Medium\0Heavy\0Custom\0"))
        {
            settings.StrengthPreset = (StutterStrengthPreset)presetIndex;
            changed = true;
        }

        var isCustom = settings.StrengthPreset == StutterStrengthPreset.Custom;
        var effective = GetPresetDefaults(settings);

        if (!isCustom)
        {
            ImGui.BeginDisabled();
        }

        var mode = isCustom ? settings.Mode : effective.Mode;
        var modeIndex = mode == StutterMode.Soft ? 0 : 1;
        if (ImGui.Combo("Stutter mode", ref modeIndex, "Soft\0Hard\0"))
        {
            settings.Mode = modeIndex == 0 ? StutterMode.Soft : StutterMode.Hard;
            changed = true;
        }

        var wordChance = isCustom ? settings.WordStutterChance : effective.WordStutterChance;
        if (ImGui.SliderFloat("Word stutter chance", ref wordChance, 0f, 1f))
        {
            settings.WordStutterChance = wordChance;
            changed = true;
        }

        var consonantBias = isCustom ? settings.ConsonantBias : effective.ConsonantBias;
        if (ImGui.SliderFloat("Consonant bias", ref consonantBias, 0f, 1f))
        {
            settings.ConsonantBias = consonantBias;
            changed = true;
        }

        var vowelRepeatChance = isCustom ? settings.VowelRepeatChance : effective.VowelRepeatChance;
        if (ImGui.SliderFloat("Vowel repeat chance", ref vowelRepeatChance, 0f, 1f))
        {
            settings.VowelRepeatChance = vowelRepeatChance;
            changed = true;
        }

        var consonantRepeatChance = isCustom ? settings.ConsonantRepeatChance : effective.ConsonantRepeatChance;
        if (ImGui.SliderFloat("Consonant repeat chance", ref consonantRepeatChance, 0f, 1f))
        {
            settings.ConsonantRepeatChance = consonantRepeatChance;
            changed = true;
        }

        var maxRepeats = isCustom ? settings.MaxRepeatsPerWord : effective.MaxRepeatsPerWord;
        if (ImGui.SliderInt("Max repeats per word", ref maxRepeats, 1, 4))
        {
            settings.MaxRepeatsPerWord = Math.Max(1, maxRepeats);
            changed = true;
        }

        if (!isCustom)
        {
            ImGui.EndDisabled();
        }

        var minWordLength = settings.MinWordLength;
        if (ImGui.SliderInt("Min word length", ref minWordLength, 1, 6))
        {
            settings.MinWordLength = Math.Max(1, minWordLength);
            changed = true;
        }

        var respectExisting = settings.RespectExistingStutters;
        if (ImGui.Checkbox("Respect existing stutters", ref respectExisting))
        {
            settings.RespectExistingStutters = respectExisting;
            changed = true;
        }

        var alwaysFirst = settings.AlwaysStutterFirstWord;
        if (ImGui.Checkbox("Always stutter first word in quote", ref alwaysFirst))
        {
            settings.AlwaysStutterFirstWord = alwaysFirst;
            changed = true;
        }

        var maxStutters = settings.MaxStuttersPerQuote;
        if (ImGui.InputInt("Max stutters per quote (0 = unlimited)", ref maxStutters))
        {
            settings.MaxStuttersPerQuote = Math.Max(0, maxStutters);
            changed = true;
        }

        var livePreview = settings.LivePreview;
        if (ImGui.Checkbox("Live preview updates", ref livePreview))
        {
            settings.LivePreview = livePreview;
            changed = true;
        }

        var stableSeed = settings.StableSeed;
        if (ImGui.Checkbox("Use deterministic seed", ref stableSeed))
        {
            settings.StableSeed = stableSeed;
            changed = true;
        }

        if (changed)
        {
            configuration.StutterWriterSettings = settings;
            configuration.Save();
        }
    }

    private static EffectiveStutterSettings GetPresetDefaults(StutterSettings settings)
    {
        return settings.StrengthPreset switch
        {
            StutterStrengthPreset.Off => new EffectiveStutterSettings(StutterMode.Soft, 0f, 0.6f, 0.2f, 0.25f, 1),
            StutterStrengthPreset.Light => new EffectiveStutterSettings(StutterMode.Soft, 0.15f, 0.65f, 0.25f, 0.3f, 1),
            StutterStrengthPreset.Medium => new EffectiveStutterSettings(StutterMode.Soft, 0.3f, 0.7f, 0.35f, 0.45f, 2),
            StutterStrengthPreset.Heavy => new EffectiveStutterSettings(StutterMode.Hard, 0.5f, 0.8f, 0.5f, 0.65f, 3),
            _ => new EffectiveStutterSettings(settings.Mode, settings.WordStutterChance, settings.ConsonantBias, settings.VowelRepeatChance, settings.ConsonantRepeatChance, settings.MaxRepeatsPerWord),
        };
    }

    private readonly struct EffectiveStutterSettings
    {
        public EffectiveStutterSettings(StutterMode mode, float wordChance, float consonantBias, float vowelChance, float consonantChance, int maxRepeats)
        {
            Mode = mode;
            WordStutterChance = wordChance;
            ConsonantBias = consonantBias;
            VowelRepeatChance = vowelChance;
            ConsonantRepeatChance = consonantChance;
            MaxRepeatsPerWord = maxRepeats;
        }

        public StutterMode Mode { get; }
        public float WordStutterChance { get; }
        public float ConsonantBias { get; }
        public float VowelRepeatChance { get; }
        public float ConsonantRepeatChance { get; }
        public int MaxRepeatsPerWord { get; }
    }

    private void OpenNotesFolder()
    {
        var baseDir = Plugin.PluginInterface.ConfigDirectory.FullName;
        var notesDir = System.IO.Path.Combine(baseDir, "notes");
        System.IO.Directory.CreateDirectory(notesDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = notesDir,
            UseShellExecute = true,
        });
    }
}

