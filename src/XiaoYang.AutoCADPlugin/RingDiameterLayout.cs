namespace XiaoYang.AutoCADPlugin;

internal static class RingDiameterLayout
{
    internal const int MinimumDiameterStepPx = 16;

    public static void Normalize(SceneMenuConfig scene)
    {
        if (scene?.Rings == null) return;

        var rings = scene.Rings
            .Where(ring => ring != null && ring.Enabled)
            .OrderBy(ring => ring.Index)
            .ToList();
        if (rings.Count == 0) return;

        var wheelDiameter = RuntimeCompat.Clamp(scene.MenuSizePx, 260, 900);
        var centerDiameter = RuntimeCompat.Clamp(scene.CenterRadiusPx, 20, wheelDiameter / 3) * 2;
        var available = Math.Max(rings.Count, wheelDiameter - centerDiameter);
        var step = Math.Max(1, Math.Min(MinimumDiameterStepPx, available / rings.Count));

        // A missing diameter identifies an old configuration. Convert its
        // cumulative size weights once, then persist real pixel diameters.
        if (rings.Any(ring => ring.DiameterPx <= 0))
        {
            var totalWeight = Math.Max(1, rings.Sum(ring => Math.Max(1, ring.SizeWeight)));
            var cumulative = 0;
            foreach (var ring in rings)
            {
                cumulative += Math.Max(1, ring.SizeWeight);
                ring.DiameterPx = centerDiameter
                    + (int)Math.Round((wheelDiameter - centerDiameter) * cumulative / (double)totalWeight);
            }
        }

        var previous = centerDiameter;
        for (var index = 0; index < rings.Count; index++)
        {
            var remaining = rings.Count - index - 1;
            var minimum = previous + step;
            var maximum = wheelDiameter - remaining * step;
            rings[index].DiameterPx = index == rings.Count - 1
                ? wheelDiameter
                : RuntimeCompat.Clamp(rings[index].DiameterPx, minimum, maximum);
            previous = rings[index].DiameterPx;
        }
    }

    public static void SplitOutermostRing(SceneMenuConfig scene, int previousCount)
    {
        if (scene?.Rings == null) return;
        var rings = scene.Rings.Where(ring => ring.Enabled).OrderBy(ring => ring.Index).ToList();
        if (rings.Count <= previousCount || rings.Count < 2)
        {
            Normalize(scene);
            return;
        }

        Normalize(scene);
        var oldOuterIndex = rings.Count - 2;
        var innerDiameter = oldOuterIndex > 0
            ? rings[oldOuterIndex - 1].DiameterPx
            : scene.CenterRadiusPx * 2;
        rings[oldOuterIndex].DiameterPx = innerDiameter
            + Math.Max(MinimumDiameterStepPx, (scene.MenuSizePx - innerDiameter) / 2);
        rings[rings.Count - 1].DiameterPx = scene.MenuSizePx;
        Normalize(scene);
    }
}
