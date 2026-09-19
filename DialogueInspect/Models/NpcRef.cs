namespace DialogueInspect.Models;

public sealed class NpcRef
{
    public string Name { get; set; } = string.Empty;
    public uint BaseId { get; set; }
    public string ObjectKind { get; set; } = string.Empty;

    public bool IsSet => BaseId != 0;
}
