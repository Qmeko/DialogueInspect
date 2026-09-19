namespace DialogueInspect.Models;

public sealed class ChoiceList
{
    public string AddonName { get; set; } = string.Empty;
    public string TabTitle { get; set; } = string.Empty;
    public List<ChoiceRow> Rows { get; set; } = [];
    public List<ChoiceMark> Marks { get; set; } = [];

    public string Fingerprint()
        => AddonName + "|" + string.Join('\n', Rows.Select(r => r.Text));
}
