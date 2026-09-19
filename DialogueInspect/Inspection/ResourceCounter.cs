using Dalamud.Game.Inventory;
using Lumina.Excel.Sheets;

namespace DialogueInspect.Inspection;

internal sealed class ResourceCounter
{
    public IReadOnlyList<CurrencyInfo> ListHeldCurrencies()
    {
        var result = new List<CurrencyInfo>();
        foreach (var item in Plugin.GameInventory.GetInventoryItems(GameInventoryType.Currency))
        {
            if (item.IsEmpty || item.ItemId == 0 || item.Quantity <= 0)
                continue;
            result.Add(new CurrencyInfo(item.ItemId, ItemName(item.ItemId), item.Quantity));
        }

        return result;
    }

    public int Count(uint itemId)
    {
        if (itemId == 0)
            return 0;

        var total = 0;
        foreach (var item in Plugin.GameInventory.GetInventoryItems(GameInventoryType.Currency))
        {
            if (!item.IsEmpty && item.ItemId == itemId)
                total += item.Quantity;
        }

        return total;
    }

    public static string ItemName(uint itemId)
    {
        if (itemId == 0)
            return "(なし)";
        if (Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var row))
            return row.Name.ToString();
        return $"Item#{itemId}";
    }

    public readonly record struct CurrencyInfo(uint ItemId, string Name, int Quantity);
}
