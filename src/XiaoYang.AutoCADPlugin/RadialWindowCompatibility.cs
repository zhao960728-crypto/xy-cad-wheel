using System;

namespace XiaoYang.AutoCADPlugin;

internal static class RadialWindowCompatibility
{
    // Windows 7 can render a WinForms window that combines TransparencyKey and
    // Opacity as a solid black rectangle, especially when desktop composition
    // is disabled or an older graphics driver is in use. Use one opaque,
    // ellipse-shaped window there; Windows 8+ keeps the normal two-layer style.
    public static bool UseLegacySafeRenderer => RequiresLegacySafeRenderer(Environment.OSVersion.Version);

    internal static bool RequiresLegacySafeRenderer(Version? version)
    {
        if (version == null) return false;
        return version.Major < 6 || (version.Major == 6 && version.Minor <= 1);
    }

    internal static double EffectiveWindowOpacity(int configuredPercent) =>
        UseLegacySafeRenderer
            ? 1.0
            : RuntimeCompat.Clamp(configuredPercent, 20, 100) / 100.0;
}
