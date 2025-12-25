using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using YamlDotNet.Serialization;

namespace SamplePlugin.Windows;

public sealed class ChangelogWindow : Window, IDisposable
{
    private const string EmbeddedChangelogResource = "SamplePlugin.Changelog.changelog.yaml";

    private readonly Plugin plugin;
    private ChangelogFile changelog = new();
    private string statusMessage = string.Empty;

    public ChangelogWindow(Plugin plugin)
        : base("Changelog##ChangelogWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 260),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        LoadChangeLog(openOnSuccess: false);
    }

    public void Dispose() { }

    public void CheckForUpdates()
    {
        if (!LoadChangeLog(openOnSuccess: false))
        {
            return;
        }

        var currentId = GetCurrentChangelogId();
        if (string.IsNullOrWhiteSpace(currentId))
        {
            return;
        }

        if (!string.Equals(plugin.Configuration.LastSeenChangelogId, currentId, StringComparison.Ordinal))
        {
            plugin.Configuration.LastSeenChangelogId = currentId;
            plugin.Configuration.Save();
            IsOpen = true;
        }
    }

    public override void Draw()
    {
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            ImGui.TextUnformatted(statusMessage);
        }

        if (!string.IsNullOrWhiteSpace(changelog.Tagline))
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f);
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), changelog.Tagline);
            if (!string.IsNullOrWhiteSpace(changelog.Subline))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.75f, 1.0f), $" - {changelog.Subline}");
            }
        }

        ImGui.Spacing();

        ImGui.BeginChild("ChangelogScroll", ImGui.GetContentRegionAvail(), false,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);

        if (changelog.Changelog.Count == 0)
        {
            ImGui.TextUnformatted("No changelog entries loaded.");
            ImGui.EndChild();
            return;
        }

        foreach (var entry in changelog.Changelog)
        {
            RenderEntry(entry);
            ImGui.Spacing();
        }

        ImGui.EndChild();
    }

    private void RenderEntry(ChangelogEntry entry)
    {
        var header = entry.Name ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(entry.Tagline))
        {
            header = $"{header} - {entry.Tagline}";
        }

        if (!string.IsNullOrWhiteSpace(entry.Date))
        {
            header = $"{header} ({entry.Date})";
        }

        ImGui.TextUnformatted(header);
        if (entry.IsCurrent)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.45f, 0.85f, 0.55f, 1.0f), "Current");
        }

        ImGui.Indent();
        foreach (var version in entry.Versions)
        {
            var label = version.Number ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(version.Icon))
            {
                label = $"{version.Icon} {label}";
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                ImGui.TextUnformatted(label);
            }

            ImGui.Indent();
            foreach (var item in version.Items)
            {
                ImGui.BulletText(item);
            }
            ImGui.Unindent();
        }
        ImGui.Unindent();
    }

    private bool LoadChangeLog(bool openOnSuccess)
    {
        statusMessage = string.Empty;

        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(EmbeddedChangelogResource);
            if (stream == null)
            {
                changelog = new ChangelogFile();
                statusMessage = "Embedded changelog not found.";
                return false;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 128);
            var yaml = reader.ReadToEnd();

            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            changelog = deserializer.Deserialize<ChangelogFile>(yaml) ?? new ChangelogFile();
            if (openOnSuccess)
            {
                IsOpen = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to parse changelog.yaml");
            statusMessage = "Failed to parse changelog.yaml.";
            changelog = new ChangelogFile();
            return false;
        }
    }

    private string GetCurrentChangelogId()
    {
        foreach (var entry in changelog.Changelog)
        {
            if (entry.IsCurrent)
            {
                return entry.Name;
            }
        }

        if (changelog.Changelog.Count > 0)
        {
            return changelog.Changelog[0].Name;
        }

        return string.Empty;
    }

    private sealed class ChangelogFile
    {
        [YamlMember(Alias = "tagline")]
        public string Tagline { get; set; } = string.Empty;

        [YamlMember(Alias = "subline")]
        public string Subline { get; set; } = string.Empty;

        [YamlMember(Alias = "changelog")]
        public List<ChangelogEntry> Changelog { get; set; } = new();
    }

    private sealed class ChangelogEntry
    {
        [YamlMember(Alias = "name")]
        public string Name { get; set; } = string.Empty;

        [YamlMember(Alias = "tagline")]
        public string Tagline { get; set; } = string.Empty;

        [YamlMember(Alias = "date")]
        public string Date { get; set; } = string.Empty;

        [YamlMember(Alias = "isCurrent")]
        public bool IsCurrent { get; set; }

        [YamlMember(Alias = "versions")]
        public List<ChangelogVersion> Versions { get; set; } = new();
    }

    private sealed class ChangelogVersion
    {
        [YamlMember(Alias = "number")]
        public string Number { get; set; } = string.Empty;

        [YamlMember(Alias = "icon")]
        public string Icon { get; set; } = string.Empty;

        [YamlMember(Alias = "items")]
        public List<string> Items { get; set; } = new();
    }
}
