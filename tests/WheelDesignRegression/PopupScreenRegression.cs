using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Collections;
using XiaoYang.AutoCADPlugin;

internal static class PopupScreenRegression
{
    private static object Call(RadialMenuControl control, string name, params object[] args) =>
        typeof(RadialMenuControl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, args);
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);

    internal static void RunRendering(Action<bool, string> check, string output)
    {
        foreach (var style in new[] { "classic", "01", "11", "06", "orbit" })
        foreach (var ringCount in new[] { 1, 3, 8 })
        foreach (var size in new[] { 240, 480, 720 })
        {
            var config = new PluginConfig { MenuSizePx = 900, CenterRadiusPx = 90 };
            var scene = new SceneMenuConfig { StyleId = style, MenuSizePx = 900, CenterRadiusPx = 90, ContentMode = "TextOnly",
                Rings = Enumerable.Range(0, ringCount).Select(r => new RingConfig {
                    Index = r, Enabled = true, Slots = 8, DiameterPx = 180 + 720 * (r + 1) / ringCount,
                    Items = Enumerable.Range(0, 8).Select(s => new MenuItemConfig {
                        Slot = s, Label = "移动命令", Value = "MOVE", FontSizePx = 0 }).ToList()
                }).ToList() };
            config.Menus[SceneKeys.Global] = scene;
            var snapshot = JsonCompat.Serialize(config);
            using var background = new RadialMenuControl(Color.White) { Size = new Size(size, size), ScaleMenuForPreview = false, DrawLabels = false };
            using var foreground = new RadialMenuControl(Color.White) { Size = new Size(size, size), ScaleMenuForPreview = false, DrawBackdrop = false, DrawGuides = false, DrawHover = false };
            background.SetMenu(config, scene);
            foreground.SetMenu(config, scene);
            var bounds = (RectangleF)Call(background, "GetMenuBounds");
            var title = $"runtime {style}, {ringCount} rings, 900 to {size}px";
            check(bounds == (RectangleF)Call(foreground, "GetMenuBounds") && bounds.Width == size,
                title + " background and foreground fill the same fitted bounds");
            check((int)Call(background, "EffectiveCenterRadiusPx") == (int)Math.Round(90 * size / 900d),
                title + " center radius follows runtime scale");
            var views = style is "06" or "11" ? Enumerable.Range(0, ringCount) : new[] { 0 };
            foreach (var view in views)
            {
                if (style is "06" or "11") Call(background, "SetActiveGroup", view);
                foreground.CopyNavigationFrom(background);
                if (style is "classic" or "01")
                {
                    var layouts = ((IEnumerable)Call(background, "BuildRingLayouts", bounds, scene.Rings)).Cast<object>();
                    foreach (var layout in layouts)
                    {
                        var ring = Property<RingConfig>(layout, "Ring");
                        var inner = Property<float>(layout, "InnerRadius");
                        var outer = Property<float>(layout, "OuterRadius");
                        check(inner < outer && inner >= 0 && outer <= size / 2f, title + $" ring {ring.Index} annulus inside popup");
                        for (var slot = 0; slot < ring.Slots; slot++)
                        {
                            var degrees = (slot + (style == "01" ? 0 : .5)) * 360 / ring.Slots - 90;
                            var radians = degrees * Math.PI / 180;
                            var point = Point.Round(new PointF(size / 2f + (float)Math.Cos(radians) * (inner + outer) / 2,
                                size / 2f + (float)Math.Sin(radians) * (inner + outer) / 2));
                            var hit = Call(background, "HitTest", point);
                            var labelHit = Call(foreground, "HitTest", point);
                            check(Property<int>(hit, "RingIndex") == ring.Index && Property<int>(hit, "Slot") == slot && hit.Equals(labelHit),
                                title + $" ring {ring.Index} slot {slot} rendering and both-layer hit match");
                        }
                    }
                }
                else
                {
                    var cells = ((IEnumerable)Call(background, "BuildStyleCells", bounds)).Cast<object>().ToArray();
                    var labelCells = ((IEnumerable)Call(foreground, "BuildStyleCells", bounds)).Cast<object>().ToArray();
                    check(cells.Length == labelCells.Length, title + " navigation shows same cells in both layers");
                    for (var i = 0; i < cells.Length; i++)
                    {
                        var rect = Field<RectangleF>(cells[i], "Bounds");
                        var expected = Field<object>(cells[i], "Hit");
                        var point = Point.Round(new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                        check(rect.Width > 0 && rect.Height > 0 && rect.Left >= -.01 && rect.Top >= -.01 && rect.Right <= size + .01 && rect.Bottom <= size + .01,
                            title + $" view {view} cell {i} drawn completely inside popup");
                        check(rect == Field<RectangleF>(labelCells[i], "Bounds") && expected.Equals(Call(background, "HitTest", point)) && expected.Equals(Call(foreground, "HitTest", point)),
                            title + $" view {view} cell {i} label and command hit match");
                    }
                }
                using var bitmap = new Bitmap(size, size);
                foreground.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                if (foreground.TextDiagnostics.Any(d => !d.FullText || !d.FitsBounds))
                    File.WriteAllText(Path.Combine(output, "runtime-text-failure.json"), JsonCompat.Serialize(foreground.TextDiagnostics));
                check(foreground.TextDiagnostics.Count > 0 && foreground.TextDiagnostics.All(d => d.FullText && d.FitsBounds),
                    title + $" view {view} labels draw fully in runtime cell bounds");
                if (ringCount == 8 && view == 0) bitmap.Save(Path.Combine(output, $"popup-{style}-{size}.png"));
            }
            check(JsonCompat.Serialize(config) == snapshot, title + " fitting preserves configuration");
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var areas = new[]
        {
            new Rectangle(0, 0, 1366, 728),
            new Rectangle(-1920, 40, 1920, 1040),
            new Rectangle(1920, -1080, 1280, 984),
            new Rectangle(0, 0, 3840, 2080),
            // Logical work areas also cover hosts virtualized at 150/200% DPI.
            new Rectangle(0, 0, 853, 480),
            new Rectangle(0, 0, 640, 360)
        };
        foreach (var area in areas)
        foreach (var size in new[] { 260, 600, 900 })
        foreach (var center in new[] { area.Location, new Point(area.Right - 1, area.Top),
            new Point(area.Left, area.Bottom - 1), new Point(area.Right - 1, area.Bottom - 1),
            new Point(area.Left + area.Width / 2, area.Top + area.Height / 2) })
        {
            var bounds = RadialMenuWindow.FitMenuBounds(size, center, area);
            check(area.Contains(bounds) && bounds.Width == bounds.Height,
                $"popup complete and square: size {size}, work area {area}, trigger {center}");
            check(bounds.Width == Math.Min(size, Math.Min(area.Width, area.Height) - 16),
                $"popup preserves configured size unless display requires shrinking: {size}, {area.Size}");
        }
        using var window = new RadialMenuWindow(_ => { });
        using var foreground = new RadialForegroundWindow();
        check(window.AutoScaleMode == AutoScaleMode.None && foreground.AutoScaleMode == AutoScaleMode.None,
            "popup background and labels share explicit pixel scaling across host DPI settings");
    }
}
