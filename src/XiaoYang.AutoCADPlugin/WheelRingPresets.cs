namespace XiaoYang.AutoCADPlugin;

internal static class WheelRingPresets
{
    internal static int[][] ForStyle(string style) => style == "classic" || style == "01"
        ? new[] { new[] { 8, 8 }, new[] { 8, 8, 16 }, new[] { 8, 8, 16, 16 } }
        : Array.Empty<int[]>();

    internal static void Apply(SceneMenuConfig scene, int[] slots)
    {
        if (scene == null || slots == null || !ForStyle(scene.StyleId).Any(p => p.SequenceEqual(slots)))
            throw new ArgumentException("请选择当前轮盘的推荐设置。");
        while (scene.Rings.Count < slots.Length)
            scene.Rings.Add(new RingConfig { Index = scene.Rings.Count });
        for (int index = 0; index < scene.Rings.Count; index++)
        {
            var ring = scene.Rings[index];
            ring.Index = index;
            ring.Enabled = index < slots.Length;
            if (!ring.Enabled) continue;
            ring.Slots = slots[index];
            // A confirmed recommendation defines a fresh, evenly spaced layout.
            // Retaining old inner diameters compresses these rings when the
            // previous layout had more rings. Keep hidden ring geometry intact.
            var diameter = RuntimeCompat.Clamp(scene.MenuSizePx, 260, 900);
            var centerDiameter = RuntimeCompat.Clamp(scene.CenterRadiusPx, 20, diameter / 3) * 2;
            ring.DiameterPx = centerDiameter
                + (int)Math.Round((diameter - centerDiameter) * (index + 1) / (double)slots.Length);
            // Reduced capacity moves bindings to the existing unassigned pool.
            // Hidden outer rings keep their commands, icons and appearance.
            scene.UnassignedItems.AddRange(ring.Items.Where(item => item.Slot >= ring.Slots));
            ring.Items.RemoveAll(item => item.Slot >= ring.Slots);
        }
        RingDiameterLayout.Normalize(scene);
    }
}
