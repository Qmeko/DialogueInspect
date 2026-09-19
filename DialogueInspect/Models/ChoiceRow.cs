namespace DialogueInspect.Models;

public sealed class ChoiceRow
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
    public uint ItemId { get; set; }
}
