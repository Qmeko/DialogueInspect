namespace DialogueInspect.Models;

public sealed class FlowStep
{
    public string AddonName { get; set; } = string.Empty;
    public string ListKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public MarkKind Kind { get; set; } = MarkKind.DialogueText;
    public float WaitSeconds { get; set; }

    public bool IsWait => Kind == MarkKind.Wait;
    public bool IsInteract => Kind == MarkKind.Interact;
    public bool IsAction => IsWait || IsInteract;

    public static FlowStep CreateWait(float seconds)
    {
        seconds = Math.Clamp(seconds, 0.1f, 300f);
        return new FlowStep
        {
            Kind = MarkKind.Wait,
            AddonName = "Wait",
            Text = $"{seconds:0.#}秒",
            WaitSeconds = seconds,
        };
    }

    public static FlowStep CreateInteract()
        => new()
        {
            Kind = MarkKind.Interact,
            AddonName = "Interact",
            Text = "NPCアクセス",
        };

    public static string MakeListKey(IEnumerable<ChoiceRow> rows)
        => string.Join('\n', rows.Select(r => r.Text));

    public bool Matches(string addonName, List<ChoiceRow> rows)
    {
        if (IsAction)
            return false;
        if (!AddonName.Equals(addonName, StringComparison.OrdinalIgnoreCase))
            return false;
        return FindIndex(rows) != null;
    }

    public int? FindIndex(List<ChoiceRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (Kind == MarkKind.DialogueText && rows[i].Text.Equals(Text, StringComparison.Ordinal))
                return i;
            if (Kind == MarkKind.ItemId && ItemId != 0 && rows[i].ItemId == ItemId)
                return i;
        }

        return null;
    }
}
