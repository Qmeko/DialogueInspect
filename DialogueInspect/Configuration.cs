using Dalamud.Configuration;

namespace DialogueInspect;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string SelectedPresetName { get; set; } = string.Empty;
    public List<Models.DialoguePreset> Presets { get; set; } = [];

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public Models.DialoguePreset? Find(string name)
        => Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Upsert(Models.DialoguePreset preset)
    {
        var existing = Find(preset.Name);
        if (existing != null)
            Presets.Remove(existing);
        Presets.Add(preset);
        SelectedPresetName = preset.Name;
        Save();
    }

    public bool Remove(string name)
    {
        var existing = Find(name);
        if (existing == null)
            return false;
        Presets.Remove(existing);
        if (SelectedPresetName.Equals(name, StringComparison.OrdinalIgnoreCase))
            SelectedPresetName = Presets.FirstOrDefault()?.Name ?? string.Empty;
        Save();
        return true;
    }
}
