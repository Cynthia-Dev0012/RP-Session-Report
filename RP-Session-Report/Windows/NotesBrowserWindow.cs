using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class NotesBrowserWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly MainWindow mainWindow;
    private readonly List<string> noteFiles = new();
    private string statusMessage = string.Empty;
    private bool hasRefreshed;

    public NotesBrowserWindow(Plugin plugin, MainWindow mainWindow)
        : base("Notes Browser##NotesBrowser")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        this.mainWindow = mainWindow;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (!hasRefreshed)
        {
            RefreshList();
        }

        if (ImGui.Button("Refresh"))
        {
            RefreshList();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"Notes: {noteFiles.Count}");

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            ImGui.TextUnformatted(statusMessage);
        }

        var childSize = ImGui.GetContentRegionAvail();
        if (childSize.Y < ImGui.GetTextLineHeight() * 4f)
        {
            childSize.Y = ImGui.GetTextLineHeight() * 4f;
        }

        ImGui.BeginChild("NotesList", childSize, true);
        foreach (var fileName in noteFiles)
        {
            var isSelected = string.Equals(fileName, plugin.Configuration.CurrentNoteFileName, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(fileName, isSelected))
            {
                mainWindow.LoadNoteFromFile(fileName);
                statusMessage = $"Loaded {fileName}.";
            }
        }
        ImGui.EndChild();
    }

    private void RefreshList()
    {
        noteFiles.Clear();

        var notesDir = GetNotesDirectory();
        Directory.CreateDirectory(notesDir);

        foreach (var file in Directory.GetFiles(notesDir))
        {
            var name = Path.GetFileName(file);
            if (!string.IsNullOrWhiteSpace(name))
            {
                noteFiles.Add(name);
            }
        }

        noteFiles.Sort(StringComparer.OrdinalIgnoreCase);
        statusMessage = string.Empty;
        hasRefreshed = true;
    }

    private string GetNotesDirectory()
    {
        var baseDir = Plugin.PluginInterface.ConfigDirectory.FullName;
        return Path.Combine(baseDir, "notes");
    }
}
