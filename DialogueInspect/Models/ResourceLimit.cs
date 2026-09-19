namespace DialogueInspect.Models;

public sealed class ResourceLimit
{
    public bool Enabled { get; set; }
    public uint ItemId { get; set; }
    public int MinRemaining { get; set; }
}
