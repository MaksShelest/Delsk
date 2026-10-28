using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Globalization;

/// <summary>Only physical stock in the player's backpack; paired production chains.</summary>
public partial class ResourceInventoryHud : HFlowContainer
{
    private string _signature = "";
    private static readonly string[][] Chains =
    [
        ["wood", "plank"], ["grain", "bread"], ["milk", "cheese"],
        ["beet", "sugar"], ["pastry"]
    ];

    public void ShowInventory(InventoryView inventory, string language)
    {
        var stock = inventory.Stock.GroupBy(s => s.ItemId, StringComparer.Ordinal)
            .Select(g => (Id: g.Key, Quantity: g.Sum(s => s.Quantity)))
            .Where(s => s.Quantity > 0).ToDictionary(s => s.Id, s => s.Quantity, StringComparer.Ordinal);
        string signature = language + "|" + string.Join(";", stock.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Key + ":" + s.Value));
        if (signature == _signature) return;
        _signature = signature;
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        Visible = stock.Count > 0;
        if (!Visible) return;
        AddThemeConstantOverride("separation", 10);
        AddChild(new VSeparator());
        var known = Chains.SelectMany(c => c).ToHashSet(StringComparer.Ordinal);
        var extra = stock.Keys.Where(id => !known.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).Chunk(2);
        foreach (var chain in Chains.Concat(extra))
        {
            var visible = chain.Where(stock.ContainsKey).ToArray();
            if (visible.Length == 0) continue;
            var column = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            column.AddThemeConstantOverride("separation", 2);
            AddChild(column);
            foreach (string id in visible)
            {
                string quantity = stock[id].ToString(CultureInfo.InvariantCulture);
                string tooltip = PlayerText.Item(language, id) + ": " + quantity;
                var row = new HBoxContainer { TooltipText = tooltip };
                row.SetMeta("item", id);
                row.AddThemeConstantOverride("separation", 3);
                column.AddChild(row);
                row.AddChild(new ResourceIcon { Item = id, CustomMinimumSize = new Vector2(24, 20), TooltipText = tooltip });
                var count = new Label { Text = quantity, VerticalAlignment = VerticalAlignment.Center, TooltipText = tooltip };
                count.AddThemeFontSizeOverride("font_size", 14);
                row.AddChild(count);
            }
        }
    }
}
