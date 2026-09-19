using Dalamud.Utility;
using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using DialogueInspect.Models;
using InteropGenerator.Runtime;
using CsAtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace DialogueInspect.Inspection;

internal static unsafe class AddonReader
{
    public static bool IsUsable(AtkUnitBase* addon, bool requireVisible)
    {
        if (addon == null)
            return false;
        if (!requireVisible)
            return true;
        if (addon->IsVisible)
            return true;
        var root = addon->RootNode;
        return root != null && root->Width > 0 && root->Height > 0 && (root->NodeFlags & NodeFlags.Visible) != 0;
    }

    public static List<ChoiceRow> ReadNamed(string addonName, AtkUnitBase* addon)
    {
        if (addon == null)
            return [];

        return addonName switch
        {
            "SelectString" => ReadSelectString((AddonSelectString*)addon),
            "SelectIconString" => ReadSelectIconString((AddonSelectIconString*)addon),
            "CutSceneSelectString" => ReadCutSceneSelectString((AddonCutSceneSelectString*)addon),
            "SelectYesno" => ReadYesNo((AddonSelectYesno*)addon),
            _ => ReadGeneric(addon),
        };
    }

    private static List<ChoiceRow> ReadGeneric(AtkUnitBase* addon)
    {
        var texts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            AddUnique(texts, seen, ReadNestedTexts(addon));
        }
        catch
        {
            // ネストした文字の読み取りに失敗しても、他の文字は拾う
        }

        try
        {
            AddUnique(texts, seen, ReadButtons(addon));
        }
        catch
        {
            // ボタン読み取りに失敗しても、他の文字は拾う
        }

        try
        {
            AddUnique(texts, seen, ReadAtkValueStrings(addon, -1));
        }
        catch
        {
        }

        return ToRows(texts, addon);
    }

    private static void AddUnique(List<string> texts, HashSet<string> seen, List<string> source)
    {
        foreach (var text in source)
        {
            if (string.IsNullOrWhiteSpace(text) || !seen.Add(text))
                continue;
            texts.Add(text);
        }
    }

    public static List<ChoiceRow> ReadSelectString(AddonSelectString* addon)
        => ReadMenu((PopupMenu*)&addon->PopupMenu, (AtkUnitBase*)addon, skipLeadingPrompt: true);

    public static List<ChoiceRow> ReadSelectIconString(AddonSelectIconString* addon)
        => ReadMenu((PopupMenu*)&addon->PopupMenu, (AtkUnitBase*)addon, skipLeadingPrompt: true);

    public static List<ChoiceRow> ReadCutSceneSelectString(AddonCutSceneSelectString* addon)
    {
        var fromList = ReadComponentList(addon->OptionList);
        if (fromList.Count > 0)
            return ToRows(fromList, (AtkUnitBase*)addon);
        return ReadAtkValueRows((AtkUnitBase*)addon);
    }

    public static List<ChoiceRow> ReadYesNo(AddonSelectYesno* addon)
    {
        var yes = ButtonText(addon->YesButton);
        var no = ButtonText(addon->NoButton);
        if (string.IsNullOrWhiteSpace(yes))
            yes = "はい";
        if (string.IsNullOrWhiteSpace(no))
            no = "いいえ";

        var itemId = 0u;
        if (addon->CollectibleAtkValuesAvailable)
            itemId = addon->CollectibleTypedAtkValues->ItemId.UInt;

        return
        [
            new ChoiceRow { Index = 0, Text = Normalize(yes), ItemId = itemId },
            new ChoiceRow { Index = 1, Text = Normalize(no) },
        ];
    }

    public static (string Speaker, string Body) ReadTalk(AddonTalk* addon)
    {
        var speaker = NodeText(addon->GetTextNodeById(2));
        var body = NodeText(addon->GetTextNodeById(3));
        if (string.IsNullOrWhiteSpace(speaker))
            speaker = NodeText(addon->AtkTextNode220);
        if (string.IsNullOrWhiteSpace(body))
            body = NodeText(addon->AtkTextNode228);
        return (Normalize(speaker), Normalize(body));
    }

    public static List<ChoiceRow> ReadFromValuePtrs(IEnumerable<AtkValuePtr> values)
    {
        var texts = new List<string>();
        foreach (var value in values)
        {
            var text = ValuePtrToText(value);
            if (!string.IsNullOrWhiteSpace(text))
                texts.Add(text);
        }

        return ToRows(texts, null);
    }

    public static bool IsHudName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('_'))
            return true;

        ReadOnlySpan<string> skip =
        [
            "ChatLog", "ChatLogPanel", "NamePlate", "Tooltip", "Cursor",
            "Inventory", "InventoryGrid", "InventoryExpansion", "Config",
            "Character", "HudLayout", "PartyList", "EnemyList", "AllianceList",
            "NaviMap", "AreaMap", "ToDoList", "ScenarioTree", "RecipeNote",
            "FlyText", "ScreenText", "CastBar", "ParameterWidget", "Status",
            "Exp", "Money", "BagWidget", "Notification", "MainCommand",
            "ActionBar", "CrossHotbar", "JobHud", "LimitBreak", "FadeMiddle",
            "FadeBack", "NowLoading", "SystemMenu", "GoldSaucer",
        ];
        foreach (var prefix in skip)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool LooksLikeChoice(string name)
    {
        ReadOnlySpan<string> keys =
        [
            "Select", "Yesno", "YesNo", "Shop", "Journal", "Request",
            "Context", "Inclusion", "Materialize", "InputNumeric",
            "GrandCompany", "RetainerTask", "ItemInspection", "Difficulty",
            "ContentsFinderConfirm", "IconString", "CutScene",
        ];
        foreach (var key in keys)
        {
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ValuePtrToText(AtkValuePtr value)
    {
        if (value.IsNull)
            return string.Empty;
        try
        {
            return value.GetValue() is string boxed ? Normalize(boxed) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static List<ChoiceRow> ReadAtkValueRows(AtkUnitBase* addon, bool skipLeadingPrompt = false)
    {
        var texts = ReadAtkValueStrings(addon, skipLeadingPrompt ? 0 : -1);
        return ToRows(texts, addon);
    }

    private static List<ChoiceRow> ReadMenu(PopupMenu* menu, AtkUnitBase* owner, bool skipLeadingPrompt)
    {
        // 画面上のリストと、今セットされた AtkValues を優先する。
        // EntryNames は窓の使い回し後に古い文言のまま残ることがある。
        var fromList = ReadComponentList(menu != null ? menu->List : null);
        var expected = fromList.Count;
        if (expected <= 0 && menu != null)
            expected = menu->EntryCount;
        var fromAtk = ReadAtkValueStrings(owner, skipLeadingPrompt ? expected : -1);
        var fromNames = ReadEntryNames(menu);

        var texts = PreferCurrent(fromList, fromAtk, fromNames);
        return ToRows(texts, owner);
    }

    private static List<string> PreferCurrent(List<string> fromList, List<string> fromAtk, List<string> fromNames)
    {
        // EntryNames は古い内容が残るので、AtkValues / 画面リストがあるときは使わない。
        if (fromAtk.Count == 0 && fromList.Count == 0)
            return fromNames;
        if (fromAtk.Count >= fromList.Count && fromAtk.Count > 0)
            return fromAtk;
        return fromList.Count > 0 ? fromList : fromAtk;
    }

    private static List<string> ReadEntryNames(PopupMenu* menu)
    {
        var texts = new List<string>();
        if (menu == null || menu->EntryNames == null || menu->EntryCount <= 0)
            return texts;

        for (var i = 0; i < menu->EntryCount; i++)
        {
            var text = ReadCString(menu->EntryNames[i]);
            if (!string.IsNullOrWhiteSpace(text))
                texts.Add(text);
        }

        return texts;
    }

    private static List<string> ReadComponentList(AtkComponentList* list)
    {
        var texts = new List<string>();
        if (list == null)
            return texts;

        var count = list->GetItemCount();
        if (count <= 0)
            count = list->ListLength;
        if (count <= 0)
            return texts;

        if (count > 256)
            return texts;

        for (var i = 0; i < count; i++)
        {
            var renderer = list->GetItemRenderer(i);
            if (renderer == null)
                continue;
            var text = NodeText(renderer->ButtonTextNode);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            texts.Add(Normalize(text));
        }

        return texts;
    }

    private static List<string> ReadAtkValueStrings(AtkUnitBase* addon, int expectedChoices)
    {
        var texts = new List<string>();
        if (addon == null || addon->AtkValues == null)
            return texts;

        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var value = addon->AtkValues[i];
            if (value.Type is not (CsAtkValueType.String or CsAtkValueType.ManagedString or CsAtkValueType.ConstString))
                continue;
            var text = Normalize(value.String.ToString());
            if (!string.IsNullOrWhiteSpace(text))
                texts.Add(text);
        }

        if (expectedChoices > 0 && texts.Count == expectedChoices + 1)
            texts.RemoveAt(0);

        return texts;
    }

    private static List<ChoiceRow> ToRows(List<string> texts, AtkUnitBase* owner)
    {
        var itemIds = ReadItemIds(owner, texts.Count);
        var rows = new List<ChoiceRow>(texts.Count);
        for (var i = 0; i < texts.Count; i++)
        {
            rows.Add(new ChoiceRow
            {
                Index = i,
                Text = texts[i],
                ItemId = i < itemIds.Count ? itemIds[i] : 0,
            });
        }

        return rows;
    }

    private static List<uint> ReadItemIds(AtkUnitBase* addon, int expectedCount)
    {
        var ids = new List<uint>();
        if (addon == null || addon->AtkValues == null || expectedCount <= 0)
            return ids;

        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var value = addon->AtkValues[i];
            uint id = 0;
            if (value.Type == CsAtkValueType.UInt)
                id = value.UInt;
            else if (value.Type == CsAtkValueType.Int && value.Int > 0)
                id = (uint)value.Int;

            if (LooksLikeItemId(id))
                ids.Add(id);
        }

        if (ids.Count == expectedCount)
            return ids;
        if (ids.Count == 1)
            return Enumerable.Repeat(ids[0], expectedCount).ToList();
        return [];
    }

    private static bool LooksLikeItemId(uint id)
        => id is > 0 and < 2_000_000;

    private static List<string> ReadButtons(AtkUnitBase* addon)
    {
        var texts = new List<string>();
        if (addon == null || addon->UldManager.NodeList == null)
            return texts;

        var nodeCount = addon->UldManager.NodeListCount;
        for (var i = 0; i < nodeCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (node->NodeFlags & NodeFlags.Visible) == 0)
                continue;
            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;
            var type = componentNode->Component->GetComponentType();
            if (type is not (ComponentType.Button or ComponentType.RadioButton or ComponentType.HoldButton))
                continue;

            var text = ButtonText((AtkComponentButton*)componentNode->Component);
            if (string.IsNullOrWhiteSpace(text) || text.Length > 48)
                continue;
            texts.Add(text);
        }

        return texts;
    }

    private static List<string> ReadNestedTexts(AtkUnitBase* addon)
    {
        var texts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (addon != null)
            CollectUldTexts(&addon->UldManager, texts, seen, 0);
        return texts;
    }

    private static void CollectUldTexts(AtkUldManager* uld, List<string> texts, HashSet<string> seen, int depth)
    {
        if (uld == null || uld->NodeList == null || depth > 6)
            return;

        var count = uld->NodeListCount;
        if (count <= 0 || count > 2000)
            return;

        for (var i = 0; i < count; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;

            var textNode = node->GetAsAtkTextNode();
            if (textNode != null)
            {
                var text = NodeText(textNode);
                if (IsChoiceText(text) && seen.Add(text))
                    texts.Add(text);
                continue;
            }

            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;
            CollectUldTexts(&componentNode->Component->UldManager, texts, seen, depth + 1);
        }
    }

    private static bool IsChoiceText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length is < 2 or > 24)
            return false;
        if (text.Contains('。') || text.Contains('%') || text.Contains('/'))
            return false;
        if (text.Contains("特殊効果") || text.Contains("オートアタック") || text.Contains("ダメージ"))
            return false;
        return true;
    }

    private static string ButtonText(AtkComponentButton* button)
    {
        if (button == null)
            return string.Empty;
        var text = NodeText(button->ButtonTextNode);
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        var count = button->UldManager.NodeListCount;
        if (count <= 0 || button->UldManager.NodeList == null)
            return string.Empty;
        for (var i = 0; i < count; i++)
        {
            var node = button->UldManager.NodeList[i];
            if (node == null)
                continue;
            var textNode = node->GetAsAtkTextNode();
            if (textNode == null)
                continue;
            text = NodeText(textNode);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    private static string NodeText(AtkTextNode* node)
    {
        if (node == null)
            return string.Empty;
        return Normalize(node->NodeText.ToString());
    }

    private static string ReadCString(CStringPointer pointer)
    {
        try
        {
            return Normalize(pointer.ExtractText());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Normalize(string? text)
        => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}
