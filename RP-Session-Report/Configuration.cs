using Dalamud.Configuration;
using System;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public string LastNoteText { get; set; } = string.Empty;
    public string CurrentNoteFileName { get; set; } = string.Empty;
    public bool AutoSaveEnabled { get; set; } = false;
    public int AutoSaveIntervalSeconds { get; set; } = 60;
    public string SessionName { get; set; } = string.Empty;
    public string SessionGroup { get; set; } = string.Empty;
    public string SessionMeetingPlace { get; set; } = string.Empty;
    public string SessionMeetingDay { get; set; } = string.Empty;
    public string SessionRelationship { get; set; } = string.Empty;
    public string LastSeenChangelogId { get; set; } = string.Empty;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
