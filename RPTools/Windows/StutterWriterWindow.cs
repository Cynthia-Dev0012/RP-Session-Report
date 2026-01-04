using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace RPTools.Windows;

public class StutterWriterWindow : Window, IDisposable
{
    private const int MaxTextChars = 200_000;
    private const int MaxChatChars = 500;
    private const double DraftSaveIntervalSeconds = 2.0;
    private const string DraftFileName = "stutter-writer-draft.txt";
    private const string SpacedWrapMarker = "\r\r";
    private const string NoSpaceWrapMarker = "\r";
    private static readonly string[] ChatModes = { "/s", "/p", "/em" };
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private string inputText = string.Empty;
    private string outputText = string.Empty;
    private readonly List<string> outputChunks = new();
    private int nextChunkIndex;
    private bool outputDirty = true;
    private int lastSettingsHash;
    private float lastWrapWidth;
    private bool ignoreTextEdit;
    private bool draftDirty;
    private double lastDraftSaveTime;
    private bool draftLoaded;

    public StutterWriterWindow(Plugin plugin)
        : base("Stutter Writer##StutterWriter")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        configuration = plugin.Configuration;
        IsOpen = configuration.IsStutterWriterWindowOpen;
        RespectCloseHotkey = false;
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.MenuBar;
    }

    public void Dispose() { }

    public override void OnOpen()
    {
        configuration.IsStutterWriterWindowOpen = true;
        configuration.Save();
    }

    public override void OnClose()
    {
        configuration.IsStutterWriterWindowOpen = false;
        configuration.Save();
    }

    public override void Draw()
    {
        var settings = configuration.StutterWriterSettings ??= new StutterSettings();
        if (!draftLoaded)
        {
            LoadDraft();
            draftLoaded = true;
            outputDirty = true;
        }

        var settingsHash = ComputeSettingsHash(settings);
        if (settingsHash != lastSettingsHash)
        {
            lastSettingsHash = settingsHash;
            outputDirty = true;
        }

        DrawMenuBar(settings);

        DrawChatModeSelector();

        ImGui.TextUnformatted("Stutters only apply inside quotes.");
        var inputPlain = UnwrapText(inputText);
        var outputPlain = outputText ?? string.Empty;
        var currentChunkLength = outputChunks.Count > 0
            ? outputChunks[^1].Length
            : Math.Min(outputPlain.Length, MaxChatChars);
        var chunkLabel = outputChunks.Count > 1 ? $" (chunks {outputChunks.Count})" : string.Empty;
        ImGui.TextUnformatted($"Input: {Math.Min(inputPlain.Length, MaxTextChars)}/{MaxTextChars} chars   Output: {currentChunkLength}/{MaxChatChars} chars{chunkLabel}");
        var available = ImGui.GetContentRegionAvail();
        var outputHeight = Math.Max(170f, available.Y * 0.45f);
        var inputHeight = Math.Max(170f, available.Y * 0.35f);

        ImGui.TextUnformatted($"Output {(outputChunks.Count > 0 ? $"({nextChunkIndex + 1}/{outputChunks.Count})" : string.Empty)}");
        if (ImGui.BeginChild("StutterOutputPreview", new Vector2(-1f, outputHeight), true))
        {
            DrawOutputChunks();
            ImGui.EndChild();
        }

        ImGui.Separator();

        ImGui.TextUnformatted("Input");
        var inputSize = new Vector2(-1f, inputHeight);
        var wrapWidth = GetWrapWidth(available.X);
        if (Math.Abs(wrapWidth - lastWrapWidth) > 0.1f)
        {
            lastWrapWidth = wrapWidth;
            var cursor = inputText.Length;
            ignoreTextEdit = true;
            inputText = WrapText(inputText, wrapWidth, ref cursor);
            outputDirty = true;
        }

            if (ImGui.InputTextMultiline(
                    "##StutterInput",
                    ref inputText,
                    MaxTextChars,
                    inputSize,
                    ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackEdit,
                    OnTextCallback))
            {
                outputDirty = true;
                draftDirty = true;
            }

        DrawFooterButtons(settings);

        if (settings.LivePreview && outputDirty)
        {
            UpdateOutput(settings, false);
        }

        AutoSaveDraft(settings);
    }

    private void UpdateOutput(StutterSettings settings, bool force)
    {
        if (!force && !outputDirty)
        {
            return;
        }

        var unwrapped = UnwrapText(inputText);
        outputText = StutterTransform.Transform(unwrapped, settings);
        BuildOutputChunks();
        outputDirty = false;
        draftDirty = true;
    }

    private void BuildOutputChunks()
    {
        outputChunks.Clear();
        nextChunkIndex = 0;
        var text = outputText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var rawChunks = SplitIntoChunks(text, MaxChatChars);
        if (rawChunks.Count <= 1)
        {
            foreach (var chunk in rawChunks)
            {
                outputChunks.Add(ApplyChatHeader(chunk));
            }
            return;
        }

        var markerTemplate = $" ({rawChunks.Count}/{rawChunks.Count})";
        var markerChars = markerTemplate.Length;
        var budget = Math.Max(1, MaxChatChars - markerChars);
        var finalChunks = SplitIntoChunks(text, budget);
        for (var i = 0; i < finalChunks.Count; i++)
        {
            var marker = $" ({i + 1}/{finalChunks.Count})";
            outputChunks.Add(ApplyChatHeader(finalChunks[i] + marker));
        }
    }

    private static List<string> SplitIntoChunks(string text, int maxChars)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Substring(start);
            var chunkLength = GetChunkLength(remaining, maxChars);
            if (chunkLength <= 0)
            {
                break;
            }

            var chunk = remaining.Substring(0, chunkLength).Trim();
            if (!string.IsNullOrEmpty(chunk))
            {
                chunks.Add(chunk);
            }

            start += chunkLength;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        return chunks;
    }

    private static int GetChunkLength(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (text.Length <= maxChars)
        {
            return text.Length;
        }

        var max = Math.Min(text.Length, maxChars);
        var lastSpace = -1;
        var lastSentence = -1;

        for (var i = 0; i < max; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                lastSpace = i;
            }
            else if (c is '.' or '!' or '?' or ';')
            {
                lastSentence = i + 1;
            }

            // character-based limit; no UTF-8 byte checks
        }

        var sentenceThreshold = (int)(maxChars * 0.6f);
        if (lastSentence >= sentenceThreshold && lastSentence > 0)
        {
            return lastSentence;
        }

        if (lastSpace > 0)
        {
            return lastSpace;
        }

        return Math.Max(max, 1);
    }

    private void DrawOutputChunks()
    {
        if (outputChunks.Count == 0)
        {
            ImGui.TextWrapped(ApplyChatHeader(outputText ?? string.Empty));
            return;
        }

        for (var i = 0; i < outputChunks.Count; i++)
        {
            if (i > 0)
            {
                ImGui.Separator();
            }

            ImGui.TextWrapped(outputChunks[i]);
        }
    }

    private void DrawFooterButtons(StutterSettings settings)
    {
        if (ImGui.Button("Generate"))
        {
            UpdateOutput(settings, true);
        }

        ImGui.SameLine();
        if (ImGui.Button("<"))
        {
            if (outputChunks.Count > 0)
            {
                nextChunkIndex = (nextChunkIndex - 1 + outputChunks.Count) % outputChunks.Count;
            }
        }

        ImGui.SameLine();
        var copyLabel = outputChunks.Count > 1 ? $"Copy Output ({nextChunkIndex + 1}/{outputChunks.Count})" : "Copy Output";
        if (ImGui.Button(copyLabel))
        {
            if (outputChunks.Count > 0)
            {
                ImGui.SetClipboardText(outputChunks[nextChunkIndex]);
            }
            else
            {
                ImGui.SetClipboardText(ApplyChatHeader(outputText ?? string.Empty));
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(">"))
        {
            if (outputChunks.Count > 0)
            {
                nextChunkIndex = (nextChunkIndex + 1) % outputChunks.Count;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            inputText = string.Empty;
            outputText = string.Empty;
            outputChunks.Clear();
            nextChunkIndex = 0;
            outputDirty = true;
            draftDirty = true;
            if (settings.LivePreview)
            {
                UpdateOutput(settings, true);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Load Draft"))
        {
            LoadDraft();
            outputDirty = true;
            draftDirty = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Draft"))
        {
            ClearDraft();
        }
    }

    private void DrawChatModeSelector()
    {
        if (!ImGui.BeginTable("StutterChatHeader", 2))
        {
            return;
        }

        ImGui.TableSetupColumn("ChatMode", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("LockMode", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableNextColumn();

        var selectedIndex = Array.IndexOf(ChatModes, configuration.StutterWriterChatMode);
        if (selectedIndex < 0)
        {
            selectedIndex = 2;
        }

        if (configuration.StutterWriterChatModeLocked)
        {
            ImGui.BeginDisabled();
        }

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##StutterChatMode", ref selectedIndex, "/s\0/p\0/em\0"))
        {
            configuration.StutterWriterChatMode = ChatModes[selectedIndex];
            configuration.Save();
            outputDirty = true;
        }

        if (configuration.StutterWriterChatModeLocked)
        {
            ImGui.EndDisabled();
        }

        ImGui.TableNextColumn();
        var locked = configuration.StutterWriterChatModeLocked;
        if (ImGui.Checkbox("Lock", ref locked))
        {
            configuration.StutterWriterChatModeLocked = locked;
            configuration.Save();
        }

        ImGui.EndTable();

        var settings = configuration.StutterWriterSettings ??= new StutterSettings();
        var presetIndex = (int)settings.StrengthPreset;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("Preset", ref presetIndex, "Off\0Light\0Medium\0Heavy\0Custom\0"))
        {
            settings.StrengthPreset = (StutterStrengthPreset)presetIndex;
            configuration.StutterWriterSettings = settings;
            configuration.Save();
            outputDirty = true;
        }
    }

    private string ApplyChatHeader(string text)
    {
        var header = configuration.StutterWriterChatMode;
        if (string.IsNullOrWhiteSpace(header))
        {
            return text;
        }

        if (text.StartsWith(header, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return $"{header} {text}";
    }

    private void DrawMenuBar(StutterSettings settings)
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("Text"))
        {
            if (ImGui.MenuItem("Generate"))
            {
                UpdateOutput(settings, true);
            }

            if (ImGui.MenuItem("Copy Output"))
            {
                ImGui.SetClipboardText(outputText ?? string.Empty);
            }

            if (ImGui.MenuItem("Clear"))
            {
                inputText = string.Empty;
                outputText = string.Empty;
                outputDirty = true;
            }

            if (ImGui.MenuItem("Load Draft"))
            {
                LoadDraft();
                outputDirty = true;
                draftDirty = true;
            }

            if (ImGui.MenuItem("Clear Draft"))
            {
                ClearDraft();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Settings"))
        {
            if (ImGui.MenuItem("Open Plugin Settings"))
            {
                plugin.ToggleConfigUi();
            }

            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private unsafe int OnTextCallback(ImGuiInputTextCallbackDataPtr data)
    {
        if (data.EventFlag == ImGuiInputTextFlags.CallbackAlways)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
            {
                while (data.CursorPos < data.BufTextLen && data.Buf[(data.CursorPos - 1 > 0 ? data.CursorPos - 1 : 0)] == '\r')
                {
                    data.CursorPos++;
                }
            }

            if (data.CursorPos > 0)
            {
                while (data.CursorPos > 0 && data.Buf[data.CursorPos - 1] == '\r')
                {
                    data.CursorPos--;
                }
            }

            return 0;
        }

        return OnTextEdit(data);
    }

    private unsafe int OnTextEdit(ImGuiInputTextCallbackDataPtr data)
    {
        if (ignoreTextEdit)
        {
            ignoreTextEdit = false;
            return 0;
        }

        if (lastWrapWidth <= 0f)
        {
            return 0;
        }

        var text = data.BufTextLen >= 0 ? Encoding.UTF8.GetString(data.Buf, data.BufTextLen) : string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        var cursor = data.CursorPos;
        text = WrapText(text, lastWrapWidth, ref cursor);
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var i = 0; i < bytes.Length; i++)
        {
            data.Buf[i] = bytes[i];
        }

        data.Buf[bytes.Length] = 0;
        data.BufTextLen = bytes.Length;
        data.CursorPos = cursor;
        data.BufDirty = true;
        outputDirty = true;
        return 0;
    }

    private static string WrapText(string text, float width, ref int cursorPos)
    {
        if (text.Length == 0 || width <= 0f)
        {
            return text;
        }

        text = text.TrimEnd('\r');

        while (text.Contains(SpacedWrapMarker + '\n'))
        {
            var idx = text.IndexOf(SpacedWrapMarker + '\n', StringComparison.Ordinal);
            text = text[..idx] + " " + text[(idx + (SpacedWrapMarker + '\n').Length)..];
            if (cursorPos > idx)
            {
                cursorPos -= SpacedWrapMarker.Length;
            }
        }

        while (text.Contains(NoSpaceWrapMarker + '\n'))
        {
            var idx = text.IndexOf(NoSpaceWrapMarker + '\n', StringComparison.Ordinal);
            text = text[..idx] + text[(idx + (NoSpaceWrapMarker + '\n').Length)..];
            if (cursorPos > idx)
            {
                cursorPos -= (NoSpaceWrapMarker + '\n').Length;
            }
        }

        while (text.Contains('\r'))
        {
            var idx = text.IndexOf('\r');
            text = text[..idx] + text[(idx + 1)..];
            if (cursorPos > idx)
            {
                cursorPos -= 1;
            }
        }

        var lastSpace = 0;
        var offset = 0;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lastSpace = i;
                offset = i + 1;
                continue;
            }

            if (text[i] == ' ')
            {
                lastSpace = i;
            }

            var segmentWidth = ImGui.CalcTextSize(text.Substring(offset, i - offset)).X;
            if (segmentWidth + 10f > width)
            {
                var sb = new StringBuilder(text);
                if (lastSpace > offset)
                {
                    sb.Remove(lastSpace, 1);
                    sb.Insert(lastSpace, SpacedWrapMarker + '\n');
                    offset = lastSpace + SpacedWrapMarker.Length;
                    i += SpacedWrapMarker.Length;
                    if (lastSpace < cursorPos)
                    {
                        cursorPos += SpacedWrapMarker.Length;
                    }
                }
                else
                {
                    sb.Insert(i, NoSpaceWrapMarker + '\n');
                    offset = i + NoSpaceWrapMarker.Length;
                    i += NoSpaceWrapMarker.Length;
                    if (cursorPos > i - NoSpaceWrapMarker.Length)
                    {
                        cursorPos += NoSpaceWrapMarker.Length + 1;
                    }
                }

                text = sb.ToString();
            }
        }

        return text;
    }

    private static string UnwrapText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        text = text.Trim();
        text = text.Replace(SpacedWrapMarker + "\n", " ").Replace(NoSpaceWrapMarker + "\n", "");
        return text.Replace("\r", "");
    }


    private static float GetWrapWidth(float inputWidth)
    {
        var style = ImGui.GetStyle();
        return inputWidth - (style.FramePadding.X * 2f + style.ScrollbarSize);
    }

    private void AutoSaveDraft(StutterSettings settings)
    {
        if (!draftDirty)
        {
            return;
        }

        var now = ImGui.GetTime();
        if (now - lastDraftSaveTime < DraftSaveIntervalSeconds)
        {
            return;
        }

        if (settings.LivePreview && outputDirty)
        {
            UpdateOutput(settings, false);
        }

        SaveDraft();
        lastDraftSaveTime = now;
        draftDirty = false;
    }

    private void SaveDraft()
    {
        var path = GetDraftPath();
        var inputPlain = UnwrapText(inputText);
        var sb = new StringBuilder();
        sb.AppendLine("---INPUT---");
        sb.AppendLine(inputPlain);
        sb.AppendLine("---OUTPUT---");
        sb.AppendLine(outputText ?? string.Empty);
        File.WriteAllText(path, sb.ToString());
    }

    private void LoadDraft()
    {
        var path = GetDraftPath();
        if (!File.Exists(path))
        {
            return;
        }

        var contents = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(contents))
        {
            return;
        }

        var inputMarker = "---INPUT---";
        var outputMarker = "---OUTPUT---";
        var inputIndex = contents.IndexOf(inputMarker, StringComparison.Ordinal);
        var outputIndex = contents.IndexOf(outputMarker, StringComparison.Ordinal);
        if (inputIndex < 0)
        {
            inputText = contents;
            return;
        }

        if (outputIndex < 0)
        {
            var inputOnly = contents.Substring(inputIndex + inputMarker.Length).TrimStart('\r', '\n');
            inputText = inputOnly;
            return;
        }

        var inputStart = inputIndex + inputMarker.Length;
        var inputContent = contents.Substring(inputStart, outputIndex - inputStart).Trim('\r', '\n');
        var outputContent = contents.Substring(outputIndex + outputMarker.Length).Trim('\r', '\n');
        inputText = inputContent;
        outputText = outputContent;
    }

    private void ClearDraft()
    {
        var path = GetDraftPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        inputText = string.Empty;
        outputText = string.Empty;
        outputChunks.Clear();
        nextChunkIndex = 0;
        outputDirty = true;
        draftDirty = false;
    }

    private static string GetDraftPath()
    {
        return Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, DraftFileName);
    }

    private static int ComputeSettingsHash(StutterSettings settings)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (int)settings.StrengthPreset;
            hash = hash * 31 + (int)settings.Mode;
            hash = hash * 31 + (int)(settings.WordStutterChance * 1000);
            hash = hash * 31 + (int)(settings.ConsonantBias * 1000);
            hash = hash * 31 + (int)(settings.VowelRepeatChance * 1000);
            hash = hash * 31 + (int)(settings.ConsonantRepeatChance * 1000);
            hash = hash * 31 + settings.MaxRepeatsPerWord;
            hash = hash * 31 + settings.MinWordLength;
            hash = hash * 31 + (settings.RespectExistingStutters ? 1 : 0);
            hash = hash * 31 + (settings.AlwaysStutterFirstWord ? 1 : 0);
            hash = hash * 31 + settings.MaxStuttersPerQuote;
            hash = hash * 31 + (settings.StableSeed ? 1 : 0);
            return hash;
        }
    }
}

