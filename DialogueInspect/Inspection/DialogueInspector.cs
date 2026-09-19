using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using DialogueInspect.Models;

namespace DialogueInspect.Inspection;

internal sealed class DialogueInspector
{
    private readonly HashSet<string> seenAddons = new(StringComparer.OrdinalIgnoreCase)
    {
        "SelectString",
        "SelectIconString",
        "CutSceneSelectString",
        "SelectYesno",
        "SelectOk",
        "Shop",
        "InclusionShop",
        "ShopExchangeItem",
        "JournalResult",
        "JournalAccept",
        "Request",
        "ContextIconMenu",
        "ContextMenu",
        "InputNumeric",
        "ContentsFinderConfirm",
        "XBMStageList",
        "XBMStageDetailList",
        "XBMPetParty",
        "XBMResult",
        "XBMContentsMainHUD",
        "XBMContentsTreasure",
    };

    public string TalkSpeaker { get; private set; } = string.Empty;
    public string TalkBody { get; private set; } = string.Empty;
    public bool TalkVisible { get; private set; }
    public List<LiveAddon> Live { get; } = [];

    public void Register()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnAnyAddon);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, OnAnyAddon);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostShow, OnAnyAddon);
    }

    public void Unregister()
        => Plugin.AddonLifecycle.UnregisterListener(OnAnyAddon);

    public void Tick()
    {
        ReadTalk();
        RefreshLive();
    }

    public IGameObject? CurrentTarget() => Plugin.Targets.Target;

    public NpcRef? TargetAsNpc()
    {
        var target = CurrentTarget();
        if (target == null)
            return null;
        return new NpcRef
        {
            Name = target.Name.TextValue,
            BaseId = target.BaseId,
            ObjectKind = target.ObjectKind.ToString(),
        };
    }

    private void OnAnyAddon(AddonEvent type, AddonArgs args)
    {
        var name = args.AddonName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || AddonReader.IsHudName(name) || name == "Talk")
            return;
        seenAddons.Add(name);
    }

    private void RefreshLive()
    {
        Live.Clear();
        var names = new HashSet<string>(seenAddons, StringComparer.OrdinalIgnoreCase);
        CollectLoadedAddonNames(names);

        foreach (var name in names)
        {
            if (AddonReader.IsHudName(name) || name.Equals("Talk", StringComparison.OrdinalIgnoreCase))
                continue;

            for (var i = 1; i <= 8; i++)
            {
                try
                {
                    if (!TryReadLive(name, i, out var live))
                        continue;
                    Live.Add(live);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, $"選択窓の読み取りに失敗: {name}");
                }
            }
        }
    }

    private static bool TryReadLive(string name, int index, out LiveAddon live)
    {
        live = null!;
        AtkUnitBasePtr ptr;
        try
        {
            ptr = Plugin.GameGui.GetAddonByName(name, index);
        }
        catch
        {
            return false;
        }

        if (ptr.IsNull)
            return false;

        unsafe
        {
            var addon = (AtkUnitBase*)ptr.Address;
            if (!AddonReader.IsUsable(addon, true))
                return false;

            List<ChoiceRow> rows;
            try
            {
                rows = AddonReader.ReadNamed(name, addon);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"選択肢の読み取りに失敗: {name}");
                rows = [];
            }

            if (rows.Count == 0)
            {
                try
                {
                    rows = AddonReader.ReadAtkValueRows(addon);
                }
                catch
                {
                    rows = [];
                }
            }

            if (rows.Count == 0)
                return false;

            live = new LiveAddon
            {
                Name = name,
                Rows = rows,
            };
            return true;
        }
    }

    private static unsafe void CollectLoadedAddonNames(HashSet<string> names)
    {
        try
        {
            var module = RaptureAtkModule.Instance();
            if (module == null)
                return;

            var list = module->RaptureAtkUnitManager.AllLoadedUnitsList;
            var count = Math.Min((int)list.Count, list.Entries.Length);
            for (var i = 0; i < count; i++)
            {
                var addon = list.Entries[i].Value;
                if (addon == null)
                    continue;
                var name = addon->NameString;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "開いている窓の一覧取得に失敗");
        }
    }

    private void ReadTalk()
    {
        TalkVisible = false;
        TalkSpeaker = string.Empty;
        TalkBody = string.Empty;
        unsafe
        {
            var addon = Plugin.GameGui.GetAddonByName<AddonTalk>("Talk");
            if (!AddonReader.IsUsable((AtkUnitBase*)addon, requireVisible: true))
                return;
            TalkVisible = true;
            (TalkSpeaker, TalkBody) = AddonReader.ReadTalk(addon);
        }
    }

    internal sealed class LiveAddon
    {
        public string Name { get; set; } = string.Empty;
        public List<ChoiceRow> Rows { get; set; } = [];

        public string ButtonLabel()
        {
            var preview = string.Join(" / ", Rows.Take(3).Select(r => r.Text));
            if (Rows.Count > 3)
                preview += " / ...";
            return string.IsNullOrWhiteSpace(preview) ? Name : $"{Name}: {preview}";
        }

        public string ListKey() => FlowStep.MakeListKey(Rows);
    }
}
