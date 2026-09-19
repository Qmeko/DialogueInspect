namespace DialogueInspect.Models;

public sealed class DialoguePreset
{
    public string Name { get; set; } = string.Empty;
    public NpcRef Parent { get; set; } = new();
    public List<FlowStep> Flow { get; set; } = [];
    public bool OnceOnly { get; set; } = true;
    public ResourceLimit Resource { get; set; } = new();

    public DialoguePreset Clone()
    {
        return new DialoguePreset
        {
            Name = Name,
            OnceOnly = OnceOnly,
            Parent = new NpcRef
            {
                Name = Parent.Name,
                BaseId = Parent.BaseId,
                ObjectKind = Parent.ObjectKind,
            },
            Resource = new ResourceLimit
            {
                Enabled = Resource.Enabled,
                ItemId = Resource.ItemId,
                MinRemaining = Resource.MinRemaining,
            },
            Flow = Flow.Select(step => new FlowStep
            {
                AddonName = step.AddonName,
                ListKey = step.ListKey,
                Text = step.Text,
                ItemId = step.ItemId,
                Kind = step.Kind,
                WaitSeconds = step.WaitSeconds,
            }).ToList(),
        };
    }
}
