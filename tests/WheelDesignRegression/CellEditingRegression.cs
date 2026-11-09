using XiaoYang.AutoCADPlugin;

internal static class CellEditingRegression
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var position in new[] { 0, 2, 5 })
        foreach (var count in new[] { 1, 3 })
        {
            var ring = new RingConfig { Slots = 5, Items = new() { Item("first", 0), Item("middle", 2), Item("last", 4) } };
            var before = ring.Items.Select(item => (Item: item, Slot: item.Slot, Payload: Payload(item))).ToArray();
            WheelCellEditing.Insert(ring, position, count);
            check(ring.Slots == 5 + count && ring.Items.Count == 3, $"insert {count} at {position} creates only empty positions");
            check(before.All(old => ring.Items.Contains(old.Item) && old.Item.Slot == old.Slot + (old.Slot >= position ? count : 0) && Payload(old.Item) == old.Payload),
                $"insert {count} at {position} preserves sparse order and complete bindings");
        }

        var wheel = new SceneMenuConfig();
        var first = new RingConfig { Index = 0, Slots = 8, Items = Enumerable.Range(0, 8).Select(slot => Item("a" + slot, slot)).ToList() };
        var second = new RingConfig { Index = 1, Slots = 4, Items = new() { Item("b0", 0), Item("b2", 2) } };
        wheel.Rings.Add(first); wheel.Rings.Add(second);
        var existingUnassigned = Item("previously-unassigned", 9);
        wheel.UnassignedItems.Add(existingUnassigned);
        var originalPayloads = wheel.Rings.SelectMany(ring => ring.Items).Concat(wheel.UnassignedItems).ToDictionary(item => item.Value, Payload);
        WheelCellEditing.Remove(wheel, first, new[] { 6, 1, 4, 1 });
        check(first.Slots == 5 && first.Items.Select(item => item.Value).SequenceEqual(new[] { "a0", "a2", "a3", "a5", "a7" }) &&
            first.Items.Select(item => item.Slot).SequenceEqual(Enumerable.Range(0, 5)), "nonadjacent deletion deduplicates positions and closes only chosen gaps");
        check(wheel.UnassignedItems.Select(item => item.Value).OrderBy(value => value).SequenceEqual(new[] { "a1", "a4", "a6", "previously-unassigned" }) &&
            ReferenceEquals(wheel.UnassignedItems[0], existingUnassigned), "deleted commands retain previous unassigned bindings without duplication");

        WheelCellEditing.Swap(first, 0, first, 4);
        check(At(first, 0) == "a7" && At(first, 4) == "a0", "same-ring swap exchanges occupied cells");
        WheelCellEditing.Swap(first, 0, second, 2);
        check(At(first, 0) == "b2" && At(second, 2) == "a7", "cross-ring swap exchanges complete commands");
        WheelCellEditing.Swap(first, 1, second, 1);
        check(At(first, 1) == null && At(second, 1) == "a2", "cross-ring swap into empty cell leaves source empty");
        WheelCellEditing.Swap(first, 1, second, 0);
        check(At(first, 1) == "b0" && At(second, 0) == null, "empty source can exchange with occupied target");
        WheelCellEditing.Swap(second, 1, second, 3);
        check(At(second, 1) == null && At(second, 3) == "a2", "same-ring occupied and empty cells exchange");
        var stable = JsonCompat.Serialize(wheel);
        WheelCellEditing.Swap(second, 0, second, 1);
        WheelCellEditing.Swap(first, 2, first, 2);
        check(JsonCompat.Serialize(wheel) == stable, "empty-empty and self swaps are no-ops");
        WheelCellEditing.Remove(wheel, second, new[] { 0 });
        check(second.Slots == 3 && wheel.UnassignedItems.Count == 4 && At(second, 2) == "a2", "deleting empty position shifts bindings without inventing an unassigned item");
        check(first.Slots == 5 && second.Slots == 3, "swaps leave capacities unchanged");
        CheckPayloads(check, wheel, originalPayloads, "position edits");

        var config = new PluginConfig { Menus = new() { [SceneKeys.Global] = wheel } };
        var expectedPlacement = Placement(wheel);
        ConfigStore.Normalize(config);
        check(Placement(config.Menus[SceneKeys.Global]) == expectedPlacement, "normalization preserves edited sparse positions and capacities");
        CheckPayloads(check, config.Menus[SceneKeys.Global], originalPayloads, "normalization");
        var restored = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));
        ConfigStore.Normalize(restored);
        check(Placement(restored.Menus[SceneKeys.Global]) == expectedPlacement, "serialized reload preserves edited positions and capacities");
        CheckPayloads(check, restored.Menus[SceneKeys.Global], originalPayloads, "serialized reload");

        foreach (var invalid in new Action[] {
            () => WheelCellEditing.Insert(first, -1, 1), () => WheelCellEditing.Insert(first, first.Slots + 1, 1),
            () => WheelCellEditing.Insert(first, 0, 0), () => WheelCellEditing.Insert(first, 0, 33),
            () => WheelCellEditing.Remove(wheel, first, Array.Empty<int>()),
            () => WheelCellEditing.Remove(wheel, first, Enumerable.Range(0, first.Slots)),
            () => WheelCellEditing.Remove(wheel, first, new[] { first.Slots }),
            () => WheelCellEditing.Swap(first, -1, second, 0), () => WheelCellEditing.Swap(first, 0, second, second.Slots) })
        {
            var before = JsonCompat.Serialize(wheel);
            var rejected = false;
            try { invalid(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected && JsonCompat.Serialize(wheel) == before, "invalid edit rejected before changing commands or capacity");
        }
    }

    private static MenuItemConfig Item(string name, int slot) => new()
    {
        Slot = slot, Label = "命令 " + name, Value = name, Kind = ActionKinds.CustomCommand,
        IconData = "data:image/png;base64,iVBORw0KGgo=", ImageMode = "Background",
        FontFamily = "宋体", FontSizePx = 23, FontBold = true, TextEffect = TextEffects.Outline, TextColor = "#123ABC"
    };

    private static string At(RingConfig ring, int slot) => ring.Items.FirstOrDefault(item => item.Slot == slot)?.Value;
    private static string Payload(MenuItemConfig item)
    {
        var copy = JsonCompat.Deserialize<MenuItemConfig>(JsonCompat.Serialize(item));
        copy.Slot = 0;
        return JsonCompat.Serialize(copy);
    }
    private static string Placement(SceneMenuConfig wheel) => string.Join("|", wheel.Rings.Select(ring =>
        ring.Index + ":" + ring.Slots + ":" + string.Join(",", ring.Items.OrderBy(item => item.Slot).Select(item => item.Slot + "=" + item.Value))));
    private static void CheckPayloads(Action<bool, string> check, SceneMenuConfig wheel, Dictionary<string, string> expected, string stage)
    {
        var items = wheel.Rings.SelectMany(ring => ring.Items).Concat(wheel.UnassignedItems).ToArray();
        check(items.Length == expected.Count && items.Select(item => item.Value).Distinct().Count() == expected.Count &&
            items.All(item => expected.TryGetValue(item.Value, out var payload) && Payload(item) == payload),
            stage + " retain every command and all image/font/color/style fields exactly once");
    }
}
