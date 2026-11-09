using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using XiaoYang.AutoCADPlugin;

internal static class OrbitSizingRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        ReportedLayouts(check, output);
        var config = new PluginConfig { MenuSizePx = 520, CenterRadiusPx = 44 };
        var scene = new SceneMenuConfig { StyleId = "orbit", MenuSizePx = 520, CenterRadiusPx = 44,
            Rings = new List<RingConfig>() };
        config.Menus[SceneKeys.Global] = scene;
        for (int i = 0; i < 4; i++) scene.Rings.Add(new RingConfig { Index = i, Enabled = true, Slots = 8, DiameterPx = 196 + 108 * i });
        var before = Rectangles(config, scene, 520, false);
        scene.Rings[0].NodeInnerSizePercent = 125;
        var after = Rectangles(config, scene, 520, false);
        check(after[0].Width / before[0].Width > 1.15f && after[0].Width / before[0].Width <= 1.251f, "reported four-ring 100 to 125 percent visibly grows cells within safe available space: " + before[0].Width + " -> " + after[0].Width);
        check(after[8].Width / before[8].Width > .95f, "four-ring size edit needs at most a small fit adjustment to neighbours");
        foreach (var panelSize in new[] { 280, 340, 640 })
        {
            var preview = Rectangles(config, scene, panelSize, true);
            var ratio = preview[0].Width / after[0].Width;
            check(after.Zip(preview, (a, p) => Math.Abs((a.X + a.Width / 2 - 260) * ratio - (p.X + p.Width / 2 - panelSize / 2f)) < .02 &&
                Math.Abs((a.Y + a.Height / 2 - 260) * ratio - (p.Y + p.Height / 2 - panelSize / 2f)) < .02 && Math.Abs(a.Width * ratio - p.Width) < .02).All(x => x),
                "independent-cell preview scales actual geometry " + panelSize);
        }
        foreach (var count in new[] { 1, 4, 8 })
        foreach (var slots in new[] { 1, 8, 32 })
        foreach (var size in new[] { 260, 520, 900 })
        foreach (var percent in new[] { 50, 100, 125, 150 })
        {
            scene.MenuSizePx = config.MenuSizePx = size;
            scene.Rings = Enumerable.Range(0, count).Select(i => new RingConfig { Index = i, Enabled = true, Slots = slots,
                DiameterPx = 88 + (size - 88) * (i + 1) / count, NodeInnerSizePercent = percent }).ToList();
            var circles = Rectangles(config, scene, size, false);
            var safe = circles.All(r => r.Width > 0 && r.Left >= -.01 && r.Top >= -.01 && r.Right <= size + .01 && r.Bottom <= size + .01);
            for (int i = 0; i < circles.Length; i++) for (int j = i + 1; j < circles.Length; j++)
            {
                var a = circles[i]; var b = circles[j];
                var dx = a.X + a.Width / 2 - b.X - b.Width / 2;
                var dy = a.Y + a.Height / 2 - b.Y - b.Height / 2;
                safe &= Math.Sqrt(dx * dx + dy * dy) - (a.Width + b.Width) / 2 >= 7.95;
            }
            check(safe && circles.Length == count * slots + 1, $"orbit bounds and spacing rings={count} slots={slots} size={size} percent={percent}");
        }
        scene.MenuSizePx = config.MenuSizePx = 520;
        scene.Rings = Enumerable.Range(0, 4).Select(i => new RingConfig { Index = i, Enabled = true, Slots = 8, DiameterPx = 196 + 108 * i }).ToList();
        using var sheet = new Bitmap(1040, 560);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.White);
        for (int sample = 0; sample < 2; sample++)
        {
            scene.Rings[0].NodeInnerSizePercent = sample == 0 ? 100 : 125;
            using var control = new RadialMenuControl(Color.FromArgb(43, 48, 56)) { Size = new Size(520, 520) };
            control.SetMenu(config, scene);
            using var bitmap = new Bitmap(520, 520);
            control.DrawToBitmap(bitmap, new Rectangle(0, 0, 520, 520));
            g.DrawImage(bitmap, sample * 520, 40);
            using var font = new Font("Microsoft YaHei UI", 14);
            g.DrawString(sample == 0 ? "第1圈 100%" : "第1圈 125%", font, Brushes.Black, sample * 520 + 20, 7);
        }
        sheet.Save(Path.Combine(output, "orbit-100-vs-125.png"), ImageFormat.Png);
    }

    private static void ReportedLayouts(Action<bool, string> check, string output)
    {
        foreach (var count in new[] { 4, 8 })
        {
            var diameters = count == 4 ? new[] { 164, 289, 351, 600 } : new[] { 152, 264, 320, 376, 432, 488, 544, 600 };
            var scene = new SceneMenuConfig { StyleId = "orbit", MenuSizePx = 600, CenterRadiusPx = 20, ContentMode = "TextOnly",
                Rings = Enumerable.Range(0, count).Select(i => new RingConfig { Index = i, Enabled = true,
                    Slots = i == 0 ? 9 : i == 1 ? 15 : i == 2 ? 21 : 8, DiameterPx = diameters[i] }).ToList() };
            foreach (var ring in scene.Rings)
                ring.Items = Enumerable.Range(0, ring.Slots).Select(i => new MenuItemConfig {
                    Slot = i, Label = new[] { "直线", "多段线", "移动", "快速标注" }[i % 4], Value = "LINE", FontSizePx = 0 }).ToList();
            var config = new PluginConfig { MenuSizePx = 600, CenterRadiusPx = 20 };
            config.Menus[SceneKeys.Global] = scene;
            var snapshot = JsonCompat.Serialize(scene);
            var circles = Rectangles(config, scene, 600, false);
            check(circles.Take(circles.Length - 1).All(r => Math.Abs(r.Width - circles[0].Width) < .01), count + " reported uneven rings retain equal 100 percent cell sizes");
            check(circles[0].Width >= (count == 4 ? 50 : 24), count + " reported mixed capacity layout avoids narrow-ring size collapse");
            for (int i = 0; i < circles.Length; i++) for (int j = i + 1; j < circles.Length; j++)
            {
                var a = circles[i]; var b = circles[j];
                var dx = a.X + a.Width / 2 - b.X - b.Width / 2;
                var dy = a.Y + a.Height / 2 - b.Y - b.Height / 2;
                check(Math.Sqrt(dx * dx + dy * dy) - (a.Width + b.Width) / 2 >= 7.95,
                    $"reported {count} rings gap {i}/{j}");
            }
            using var sheet = new Bitmap(960, 640);
            using var graphics = Graphics.FromImage(sheet);
            graphics.Clear(Color.White);
            foreach (var size in new[] { 600, 340 })
            {
                using var control = new RadialMenuControl(Color.FromArgb(43, 48, 56)) {
                    Size = new Size(size, size), ScaleMenuForPreview = size == 340 };
                control.SetMenu(config, scene);
                using var bitmap = new Bitmap(size, size);
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                graphics.DrawImageUnscaled(bitmap, size == 600 ? 0 : 610, 30);
            }
            sheet.Save(Path.Combine(output, "reported-" + count + "-rings.png"), ImageFormat.Png);
            check(JsonCompat.Serialize(scene) == snapshot, count + " reported layout drawing preserves configured diameters and bindings");
        }
    }

    private static RectangleF[] Rectangles(PluginConfig config, SceneMenuConfig scene, int size, bool preview)
    {
        using var control = new RadialMenuControl(Color.White) { Size = new Size(size, size), ScaleMenuForPreview = preview };
        control.SetMenu(config, scene);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var bounds = typeof(RadialMenuControl).GetMethod("GetMenuBounds", flags).Invoke(control, null);
        var cells = (System.Collections.IEnumerable)typeof(RadialMenuControl).GetMethod("BuildStyleCells", flags).Invoke(control, new[] { bounds });
        return cells.Cast<object>().Select(x => (RectangleF)x.GetType().GetField("Bounds").GetValue(x)).ToArray();
    }
}
