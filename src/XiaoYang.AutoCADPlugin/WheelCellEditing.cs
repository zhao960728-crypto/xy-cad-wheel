namespace XiaoYang.AutoCADPlugin;

// Position edits move the complete item, including user images and typography.
internal static class WheelCellEditing
{
    internal static void Insert(RingConfig ring, int position, int count)
    {
        if (count < 1 || ring.Slots + count > 32 || position < 0 || position > ring.Slots)
            throw new ArgumentOutOfRangeException(nameof(position));
        foreach (var item in ring.Items.Where(item => item.Slot >= position)) item.Slot += count;
        ring.Slots += count;
    }

    internal static void Remove(SceneMenuConfig wheel, RingConfig ring, IEnumerable<int> positions)
    {
        var slots = positions.Distinct().OrderBy(slot => slot).ToArray();
        if (slots.Length == 0 || slots.Length >= ring.Slots || slots.Any(slot => slot < 0 || slot >= ring.Slots))
            throw new ArgumentOutOfRangeException(nameof(positions));
        var removed = ring.Items.Where(item => slots.Contains(item.Slot)).ToList();
        foreach (var item in removed) { ring.Items.Remove(item); wheel.UnassignedItems.Add(item); }
        foreach (var item in ring.Items) item.Slot -= slots.Count(slot => slot < item.Slot);
        ring.Slots -= slots.Length;
    }

    internal static void Swap(RingConfig from, int source, RingConfig to, int target)
    {
        if (source < 0 || source >= from.Slots || target < 0 || target >= to.Slots)
            throw new ArgumentOutOfRangeException(nameof(source));
        if (ReferenceEquals(from, to) && source == target) return;
        var first = from.Items.FirstOrDefault(item => item.Slot == source);
        var second = to.Items.FirstOrDefault(item => item.Slot == target);
        if (first != null) from.Items.Remove(first);
        if (second != null) to.Items.Remove(second);
        if (first != null) { first.Slot = target; to.Items.Add(first); }
        if (second != null) { second.Slot = source; from.Items.Add(second); }
    }
}
