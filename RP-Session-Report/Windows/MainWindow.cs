using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private const int MaxNoteChars = 200_000;
    public const int MinAutoSaveSeconds = 5;
    private const string HeaderStart = "---RP-Session-Report---";
    private const string HeaderEnd = "---";
    private string noteText = string.Empty;
    private string noteFileName = string.Empty;
    private string statusMessage = string.Empty;
    private string sessionName = string.Empty;
    private string sessionGroup = string.Empty;
    private string sessionMeetingPlace = string.Empty;
    private string sessionMeetingDay = string.Empty;
    private string sessionRelationship = string.Empty;
    private bool noteDirty;
    private double lastAutoSaveTime;
    // We give this window a hidden ID using ## to keep ImGui IDs stable.
    public MainWindow(Plugin plugin)
        : base("Main Notes##MainNotesWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        noteText = plugin.Configuration.LastNoteText ?? string.Empty;
        noteFileName = plugin.Configuration.CurrentNoteFileName ?? string.Empty;
        sessionName = plugin.Configuration.SessionName ?? string.Empty;
        sessionGroup = plugin.Configuration.SessionGroup ?? string.Empty;
        sessionMeetingPlace = plugin.Configuration.SessionMeetingPlace ?? string.Empty;
        sessionMeetingDay = plugin.Configuration.SessionMeetingDay ?? string.Empty;
        sessionRelationship = plugin.Configuration.SessionRelationship ?? string.Empty;
        var existingPath = GetNotePath(noteFileName);
        if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
        {
            var contents = File.ReadAllText(existingPath);
            if (!TryParseNoteFileContents(contents, out var bodyText))
            {
                sessionName = string.Empty;
                sessionGroup = string.Empty;
                sessionMeetingPlace = string.Empty;
                sessionMeetingDay = string.Empty;
                plugin.Configuration.SessionName = sessionName;
                plugin.Configuration.SessionGroup = sessionGroup;
                plugin.Configuration.SessionMeetingPlace = sessionMeetingPlace;
                plugin.Configuration.SessionMeetingDay = sessionMeetingDay;
                plugin.Configuration.SessionRelationship = sessionRelationship;
                plugin.Configuration.Save();
                bodyText = contents;
            }

            noteText = bodyText;
        }
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (lastAutoSaveTime <= 0)
        {
            lastAutoSaveTime = ImGui.GetTime();
        }

        if (ImGui.Button("New note"))
        {
            noteText = string.Empty;
            noteFileName = string.Empty;
            noteDirty = true;
            statusMessage = "New note created.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Save"))
        {
            if (TrySaveCurrent(false))
            {
                statusMessage = $"Saved {noteFileName}.";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Save As"))
        {
            if (TrySaveCurrent(true))
            {
                statusMessage = $"Saved as {noteFileName}.";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            if (DeleteCurrentNote())
            {
                statusMessage = "Note deleted.";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Browse"))
        {
            plugin.ToggleNotesBrowserUi();
        }

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }

        if (ImGui.BeginTable("SessionFields", 2))
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("File");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##NoteFileName", ref noteFileName, 128);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Name");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##SessionName", ref sessionName, 128))
            {
                plugin.Configuration.SessionName = sessionName;
                plugin.Configuration.Save();
                noteDirty = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Group");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##SessionGroup", ref sessionGroup, 128))
            {
                plugin.Configuration.SessionGroup = sessionGroup;
                plugin.Configuration.Save();
                noteDirty = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Where They Meet");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##SessionMeetingPlace", ref sessionMeetingPlace, 128))
            {
                plugin.Configuration.SessionMeetingPlace = sessionMeetingPlace;
                plugin.Configuration.Save();
                noteDirty = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Day They Met");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##SessionMeetingDay", ref sessionMeetingDay, 128))
            {
                plugin.Configuration.SessionMeetingDay = sessionMeetingDay;
                plugin.Configuration.Save();
                noteDirty = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Relationship");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##SessionRelationship", ref sessionRelationship, 128))
            {
                plugin.Configuration.SessionRelationship = sessionRelationship;
                plugin.Configuration.Save();
                noteDirty = true;
            }

            ImGui.TableNextColumn();
            ImGui.Dummy(new Vector2(1f, 1f));
            ImGui.EndTable();
        }

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            ImGui.TextUnformatted(statusMessage);
        }
        var textBoxSize = ImGui.GetContentRegionAvail();
        if (textBoxSize.Y < ImGui.GetTextLineHeight() * 4f)
        {
            textBoxSize.Y = ImGui.GetTextLineHeight() * 4f;
        }

        if (ImGui.InputTextMultiline("##MainNotes", ref noteText, MaxNoteChars, textBoxSize))
        {
            noteDirty = true;
        }

        if (plugin.Configuration.AutoSaveEnabled && noteDirty)
        {
            var intervalSeconds = Math.Max(plugin.Configuration.AutoSaveIntervalSeconds, MinAutoSaveSeconds);
            var now = ImGui.GetTime();
            if (now - lastAutoSaveTime >= intervalSeconds)
            {
                if (TrySaveCurrent(false))
                {
                    statusMessage = $"Autosaved {noteFileName}.";
                }

                lastAutoSaveTime = now;
            }
        }
    }

    private string? GetNotePath(string fileName)
    {
        var normalized = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return Path.Combine(GetNotesDirectory(), normalized);
    }

    private string GetNotesDirectory()
    {
        var baseDir = Plugin.PluginInterface.ConfigDirectory.FullName;
        return Path.Combine(baseDir, "notes");
    }

    public void LoadNoteFromFile(string fileName)
    {
        var sanitizedName = SanitizeFileName(fileName);
        var path = GetNotePath(sanitizedName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            statusMessage = "Note file not found.";
            return;
        }

        var contents = File.ReadAllText(path);
        if (!TryParseNoteFileContents(contents, out var bodyText))
        {
            sessionName = string.Empty;
            sessionGroup = string.Empty;
            sessionMeetingPlace = string.Empty;
            sessionMeetingDay = string.Empty;
            plugin.Configuration.SessionName = sessionName;
            plugin.Configuration.SessionGroup = sessionGroup;
            plugin.Configuration.SessionMeetingPlace = sessionMeetingPlace;
            plugin.Configuration.SessionMeetingDay = sessionMeetingDay;
            plugin.Configuration.SessionRelationship = sessionRelationship;
            plugin.Configuration.Save();
            bodyText = contents;
        }

        noteText = bodyText;
        noteFileName = sanitizedName;
        noteDirty = false;
        statusMessage = $"Loaded {sanitizedName}.";

        plugin.Configuration.LastNoteText = noteText;
        plugin.Configuration.CurrentNoteFileName = sanitizedName;
        plugin.Configuration.Save();
    }

    private bool TrySaveCurrent(bool forceSaveAs)
    {
        var targetName = noteFileName;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            if (forceSaveAs)
            {
                statusMessage = "Enter a file name before Save As.";
                return false;
            }

            targetName = "Untitled.txt";
            noteFileName = targetName;
        }

        var sanitizedName = SanitizeFileName(targetName);
        var path = GetNotePath(sanitizedName);
        if (string.IsNullOrWhiteSpace(path))
        {
            statusMessage = "Invalid file name.";
            return false;
        }

        Directory.CreateDirectory(GetNotesDirectory());
        File.WriteAllText(path, ComposeNoteFileContents());

        noteDirty = false;
        noteFileName = sanitizedName;
        plugin.Configuration.LastNoteText = noteText;
        plugin.Configuration.CurrentNoteFileName = sanitizedName;
        plugin.Configuration.Save();
        return true;
    }

    private bool DeleteCurrentNote()
    {
        var path = GetNotePath(noteFileName);
        if (string.IsNullOrWhiteSpace(path))
        {
            statusMessage = "No file selected to delete.";
            return false;
        }

        if (!File.Exists(path))
        {
            statusMessage = "File not found.";
            return false;
        }

        File.Delete(path);
        noteFileName = string.Empty;
        noteText = string.Empty;
        noteDirty = false;
        plugin.Configuration.CurrentNoteFileName = string.Empty;
        plugin.Configuration.LastNoteText = string.Empty;
        plugin.Configuration.Save();
        return true;
    }

    private string ComposeNoteFileContents()
    {
        return $"{HeaderStart}\n" +
               $"Name={sessionName}\n" +
               $"Group={sessionGroup}\n" +
               $"WhereTheyMeet={sessionMeetingPlace}\n" +
               $"DayTheyMet={sessionMeetingDay}\n" +
               $"Relationship={sessionRelationship}\n" +
               $"{HeaderEnd}\n" +
               noteText;
    }

    private bool TryParseNoteFileContents(string contents, out string bodyText)
    {
        bodyText = string.Empty;
        if (string.IsNullOrWhiteSpace(contents))
        {
            return false;
        }

        var startIndex = contents.IndexOf(HeaderStart, StringComparison.Ordinal);
        if (startIndex != 0)
        {
            return false;
        }

        var delimiter = "\n" + HeaderEnd + "\n";
        var endIndex = contents.IndexOf(delimiter, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            delimiter = "\r\n" + HeaderEnd + "\r\n";
            endIndex = contents.IndexOf(delimiter, StringComparison.Ordinal);
        }

        if (endIndex < 0)
        {
            return false;
        }

        var headerSection = contents.Substring(HeaderStart.Length, endIndex - HeaderStart.Length);
        var lines = headerSection.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var equalsIndex = line.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = line.Substring(0, equalsIndex).Trim();
            var value = line.Substring(equalsIndex + 1);
            switch (key)
            {
                case "Name":
                    sessionName = value;
                    break;
                case "Group":
                    sessionGroup = value;
                    break;
                case "WhereTheyMeet":
                    sessionMeetingPlace = value;
                    break;
                case "DayTheyMet":
                    sessionMeetingDay = value;
                    break;
                case "WhereWeMeet":
                    sessionMeetingPlace = value;
                    break;
                case "DayWeMet":
                    sessionMeetingDay = value;
                    break;
                case "Relationship":
                    sessionRelationship = value;
                    break;
            }
        }

        plugin.Configuration.SessionName = sessionName;
        plugin.Configuration.SessionGroup = sessionGroup;
        plugin.Configuration.SessionMeetingPlace = sessionMeetingPlace;
        plugin.Configuration.SessionMeetingDay = sessionMeetingDay;
        plugin.Configuration.SessionRelationship = sessionRelationship;
        plugin.Configuration.Save();

        var bodyStart = endIndex + delimiter.Length;
        bodyText = contents.Substring(bodyStart);
        return true;
    }

    private static string SanitizeFileName(string input)
    {
        var trimmed = Path.GetFileName((input ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        var sanitized = new string(chars);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitized)))
        {
            sanitized += ".txt";
        }

        return sanitized;
    }
}
