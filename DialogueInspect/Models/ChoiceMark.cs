namespace DialogueInspect.Models;

public sealed class ChoiceMark
{
    public MarkKind Kind { get; set; } = MarkKind.DialogueText;
    public string Text { get; set; } = string.Empty;
    public uint ItemId { get; set; }
}
