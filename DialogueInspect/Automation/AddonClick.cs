using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using DialogueInspect.Inspection;

namespace DialogueInspect.Automation;

internal enum UiClickResult
{
    Missed,
    Disabled,
    Clicked,
}

internal static unsafe class AddonClick
{
    public static UiClickResult ClickChoice(string addonName, int index, string text)
    {
        var best = UiClickResult.Missed;
        for (var i = 1; i <= 8; i++)
        {
            var addon = Plugin.GameGui.GetAddonByName(addonName, i);
            if (addon.IsNull)
                continue;
            var unit = (AtkUnitBase*)addon.Address;
            if (!AddonReader.IsUsable(unit, true))
                continue;
            var result = ClickOn(unit, addonName, index, text);
            if (result == UiClickResult.Clicked)
                return result;
            if (result == UiClickResult.Disabled)
                best = UiClickResult.Disabled;
        }

        return best;
    }

    public static bool FireInt(AtkUnitBase* addon, int value)
    {
        if (addon == null || !addon->IsReady || !addon->IsVisible)
            return false;

        return addon->FireCallbackInt(value);
    }

    public static bool ClickYesNo(AddonSelectYesno* addon, bool yes)
        => FireInt((AtkUnitBase*)addon, yes ? 0 : 1);

    private static UiClickResult ClickOn(AtkUnitBase* addon, string addonName, int index, string text)
    {
        if (IsSelectStringFamily(addonName))
            return FireInt(addon, index) ? UiClickResult.Clicked : UiClickResult.Missed;

        if (addonName.Equals("SelectYesno", StringComparison.OrdinalIgnoreCase))
            return FireInt(addon, index) ? UiClickResult.Clicked : UiClickResult.Missed;

        var buttonResult = ClickButtonByText(addon, text);
        if (buttonResult != UiClickResult.Missed)
        {
            if (buttonResult == UiClickResult.Clicked)
                Plugin.Log.Information($"ボタンクリック: {addonName} 「{text}」");
            else
                Plugin.Log.Information($"ボタン無効: {addonName} 「{text}」");
            return buttonResult;
        }

        if (ClickListItem(addon, index, text))
        {
            Plugin.Log.Information($"リストクリック: {addonName} index={index} 「{text}」");
            return UiClickResult.Clicked;
        }

        if (ClickVisibleText(addon, text))
        {
            Plugin.Log.Information($"文字クリック: {addonName} 「{text}」");
            return UiClickResult.Clicked;
        }

        Plugin.Log.Warning($"クリック失敗: {addonName} index={index} 「{text}」");
        return UiClickResult.Missed;
    }

    private static bool IsSelectStringFamily(string name)
        => name.Equals("SelectString", StringComparison.OrdinalIgnoreCase)
           || name.Equals("SelectIconString", StringComparison.OrdinalIgnoreCase)
           || name.Equals("CutSceneSelectString", StringComparison.OrdinalIgnoreCase);

    private static UiClickResult ClickButtonByText(AtkUnitBase* addon, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return UiClickResult.Missed;

        AtkComponentButton* found = null;
        var disabled = false;
        for (uint id = 1; id <= 300; id++)
        {
            var button = addon->GetComponentButtonById(id);
            if (button == null || !TextEquals(ButtonText(button), text))
                continue;
            if (button->IsEnabled)
            {
                found = button;
                break;
            }

            disabled = true;
        }

        if (found == null)
        {
            var walked = WalkFindButton(addon, text, out var walkedDisabled);
            if (walked != null)
                found = walked;
            else if (walkedDisabled)
                disabled = true;
        }

        if (found == null)
            return disabled ? UiClickResult.Disabled : UiClickResult.Missed;
        if (!found->IsEnabled)
            return UiClickResult.Disabled;
        return SendButtonClick(addon, found) ? UiClickResult.Clicked : UiClickResult.Missed;
    }

    private static AtkComponentButton* WalkFindButton(AtkUnitBase* addon, string text, out bool disabled)
    {
        disabled = false;
        AtkComponentButton* enabled = null;
        var count = addon->UldManager.NodeListCount;
        for (var i = 0; i < count; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
                continue;
            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;

            var type = componentNode->Component->GetComponentType();
            if (type is not (ComponentType.Button or ComponentType.RadioButton or ComponentType.HoldButton))
                continue;

            var button = (AtkComponentButton*)componentNode->Component;
            if (!TextEquals(ButtonText(button), text))
                continue;
            if (button->IsEnabled)
                enabled = button;
            else
                disabled = true;
        }

        return enabled;
    }

    public static bool IsChoiceSelected(string addonName, string text)
    {
        for (var i = 1; i <= 8; i++)
        {
            var addon = Plugin.GameGui.GetAddonByName(addonName, i);
            if (addon.IsNull)
                continue;
            var unit = (AtkUnitBase*)addon.Address;
            if (!AddonReader.IsUsable(unit, true))
                continue;
            if (IsChoiceSelected(unit, text))
                return true;
        }

        return false;
    }

    public static int ReadPartyCount(string addonName)
    {
        for (var i = 1; i <= 8; i++)
        {
            var addon = Plugin.GameGui.GetAddonByName(addonName, i);
            if (addon.IsNull)
                continue;
            var unit = (AtkUnitBase*)addon.Address;
            if (!AddonReader.IsUsable(unit, true))
                continue;
            var count = ReadPartyCount(unit);
            if (count >= 0)
                return count;
        }

        return -1;
    }

    private static bool IsChoiceSelected(AtkUnitBase* addon, string text)
    {
        foreach (var ptr in CollectLists(addon))
        {
            if (ListHasSelected((AtkComponentList*)ptr, text))
                return true;
        }

        return false;
    }

    private static bool ListHasSelected(AtkComponentList* list, string text)
    {
        if (list == null || string.IsNullOrWhiteSpace(text))
            return false;

        var count = ListCount(list);
        var selected = list->SelectedItemIndex;
        if (selected >= 0 && selected < count && ItemMatches(list, selected, text))
            return true;

        for (var i = 0; i < count; i++)
        {
            var renderer = SafeRenderer(list, i);
            if (renderer == null || !renderer->IsChecked)
                continue;
            if (ItemMatches(list, i, text))
                return true;
        }

        return false;
    }

    private static int ReadPartyCount(AtkUnitBase* addon)
    {
        var nodeCount = addon->UldManager.NodeListCount;
        for (var i = 0; i < nodeCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
                continue;
            var textNode = node->GetAsAtkTextNode();
            if (textNode == null)
                continue;
            var text = Normalize(textNode->NodeText.ToString());
            var slash = text.IndexOf('/');
            if (slash <= 0 || slash >= text.Length - 1)
                continue;
            if (int.TryParse(text[..slash], out var current) &&
                int.TryParse(text[(slash + 1)..], out var max) &&
                max is > 0 and <= 20 && current >= 0 && current <= max)
                return current;
        }

        return -1;
    }

    private static List<nint> CollectLists(AtkUnitBase* addon)
    {
        var lists = new List<nint>();
        var seen = new HashSet<nint>();
        if (addon == null)
            return lists;

        var nodeCount = addon->UldManager.NodeListCount;
        if (nodeCount <= 0 || addon->UldManager.NodeList == null)
            return lists;

        for (var i = 0; i < nodeCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
                continue;
            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;
            var type = componentNode->Component->GetComponentType();
            if (type is not (ComponentType.List or ComponentType.TreeList))
                continue;
            var list = (AtkComponentList*)componentNode->Component;
            var ptr = (nint)list;
            if (!seen.Add(ptr))
                continue;
            lists.Add(ptr);
        }

        return lists;
    }

    private static bool ClickListItem(AtkUnitBase* addon, int index, string text)
    {
        AtkComponentList* best = null;
        var bestTarget = -1;
        var bestCount = -1;

        foreach (var ptr in CollectLists(addon))
        {
            var list = (AtkComponentList*)ptr;
            var count = ListCount(list);
            var target = FindItemIndex(list, text);
            if (target < 0 || count < bestCount)
                continue;
            best = list;
            bestTarget = target;
            bestCount = count;
        }

        if (best == null)
            return false;

        Plugin.Log.Information($"リスト対象: {bestCount}件中 {bestTarget}番目 「{text}」（フローindex={index}）");
        return ClickListIndex(best, bestTarget);
    }

    private static int ListCount(AtkComponentList* list)
    {
        if (list == null)
            return 0;
        var count = list->GetItemCount();
        if (count <= 0)
            count = list->ListLength;
        if (count <= 0 || count > 256)
            return 0;
        return count;
    }

    private static int FindItemIndex(AtkComponentList* list, string text)
    {
        var count = ListCount(list);
        var found = MatchVisibleItem(list, count, text);
        if (found >= 0)
            return found;

        var step = Math.Max(1, Math.Min(8, count / 4));
        for (var i = 0; i < count; i += step)
        {
            TryScrollTo(list, i);
            found = MatchVisibleItem(list, count, text);
            if (found >= 0)
                return found;
        }

        return -1;
    }

    private static int MatchVisibleItem(AtkComponentList* list, int count, string text)
    {
        for (var i = 0; i < count; i++)
        {
            if (ItemMatches(list, i, text))
                return i;
        }

        return -1;
    }

    private static bool ClickListIndex(AtkComponentList* list, int target)
    {
        if (list == null || target < 0)
            return false;

        TryScrollTo(list, target);
        list->DispatchItemEvent(target, AtkEventType.ListItemClick);
        list->DispatchItemEvent(target, AtkEventType.ListItemSelect);
        return true;
    }

    private static void TryScrollTo(AtkComponentList* list, int index)
    {
        if (list == null || index < 0 || index > short.MaxValue)
            return;
        list->ScrollToItem((short)index);
    }

    private static bool ClickVisibleText(AtkUnitBase* addon, string text)
    {
        if (addon == null || string.IsNullOrWhiteSpace(text))
            return false;
        return ClickTextInManager(&addon->UldManager, addon, text, 0);
    }

    private static bool ClickTextInManager(AtkUldManager* uld, AtkUnitBase* addon, string text, int depth)
    {
        if (uld == null || uld->NodeList == null || depth > 6)
            return false;

        var count = uld->NodeListCount;
        if (count <= 0 || count > 2000)
            return false;

        for (var i = 0; i < count; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;

            var textNode = node->GetAsAtkTextNode();
            if (textNode != null && TextEquals(NodeText(textNode), text) && ClickNodeOrParents(addon, node))
                return true;

            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;
            if (ClickTextInManager(&componentNode->Component->UldManager, addon, text, depth + 1))
                return true;
        }

        return false;
    }

    private static bool ClickNodeOrParents(AtkUnitBase* addon, AtkResNode* node)
    {
        for (var current = node; current != null; current = current->ParentNode)
        {
            if (FireRegisteredEvents(addon, current))
                return true;
        }

        return false;
    }

    private static bool SendButtonClick(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (addon == null || button == null)
            return false;

        if (button->OwnerNode != null && FireRegisteredEvents(addon, (AtkResNode*)button->OwnerNode))
            return true;

        for (var i = 0; i < button->UldManager.NodeListCount; i++)
        {
            var node = button->UldManager.NodeList[i];
            if (node == null)
                continue;
            if (node->GetAsAtkCollisionNode() == null)
                continue;
            if (FireRegisteredEvents(addon, node))
                return true;
        }

        return button->AtkResNode != null && FireRegisteredEvents(addon, button->AtkResNode);
    }

    private static bool FireRegisteredEvents(AtkUnitBase* addon, AtkResNode* node)
    {
        if (addon == null || node == null)
            return false;

        var evt = node->AtkEventManager.Event;
        if (evt == null)
            return false;

        var fired = false;
        for (var current = evt; current != null; current = current->NextEvent)
        {
            var type = current->State.EventType;
            if (type is not (AtkEventType.MouseClick or AtkEventType.MouseDown or AtkEventType.ButtonClick
                or AtkEventType.ButtonPress or AtkEventType.ButtonRelease or AtkEventType.ListItemClick))
                continue;
            addon->ReceiveEvent(type, (int)current->Param, current, null);
            fired = true;
        }

        if (fired)
            return true;

        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt, null);
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

    private static AtkComponentListItemRenderer* SafeRenderer(AtkComponentList* list, int index)
    {
        if (list == null || index < 0)
            return null;
        return list->GetItemRenderer(index);
    }

    private static bool ItemMatches(AtkComponentList* list, int index, string text)
    {
        var renderer = SafeRenderer(list, index);
        if (renderer == null)
            return false;
        if (TextEquals(NodeText(renderer->ButtonTextNode), text))
            return true;

        var nodeCount = renderer->UldManager.NodeListCount;
        if (nodeCount <= 0 || renderer->UldManager.NodeList == null)
            return false;
        for (var i = 0; i < nodeCount; i++)
        {
            var node = renderer->UldManager.NodeList[i];
            if (node == null)
                continue;
            var textNode = node->GetAsAtkTextNode();
            if (textNode == null)
                continue;
            if (TextEquals(NodeText(textNode), text))
                return true;
        }

        return false;
    }

    private static string NodeText(AtkTextNode* node)
    {
        if (node == null)
            return string.Empty;
        return Normalize(node->NodeText.ToString());
    }

    private static bool TextEquals(string left, string right)
        => Normalize(left).Equals(Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? text)
        => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}
