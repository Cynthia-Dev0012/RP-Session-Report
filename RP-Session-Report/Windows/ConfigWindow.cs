using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

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

        Size = new Vector2(320, 180);
        SizeCondition = ImGuiCond.Always;

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Keep settings window fixed.
        Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
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
