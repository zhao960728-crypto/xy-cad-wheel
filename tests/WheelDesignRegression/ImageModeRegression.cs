using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using XiaoYang.AutoCADPlugin;

internal static class ImageModeRegression
{
    internal static void Run(Action<bool, string> check, string output = null)
    {
        using var source = new Bitmap(240, 80);
        using (var g = Graphics.FromImage(source)) g.Clear(Color.Magenta);
        var region = new RectangleF(0, 0, 240, 80);
        using var icon = CommandIconImage.Decode(CommandIconImage.Encode(source, region));
        var data = CommandIconImage.Encode(source, region, true);
        using var background = CommandIconImage.Decode(data);
        check(icon.Width == icon.Height && icon.GetPixel(0, 0).A == 0, "legacy icon retains transparent padding");
        check(background.Width > background.Height * 2.9 && background.GetPixel(0, 0).A > 240, "background keeps source aspect without letterboxing");
        check(JsonCompat.Deserialize<MenuItemConfig>("{\"Value\":\"LINE\"}").ImageMode == "Icon", "missing image mode defaults to Icon");
        var item = new MenuItemConfig { Value = "LINE", IconData = data, ImageMode = "Background" };
        check(JsonCompat.Deserialize<MenuItemConfig>(JsonCompat.Serialize(item)).ImageMode == "Background", "image mode survives JSON copy");
        using var control = new RadialMenuControl(Color.White);
        var draw = typeof(RadialMenuControl).GetMethod("DrawCellBackground", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var round in new[] { false, true })
        {
            using var surface = new Bitmap(100, 100);
            using var g = Graphics.FromImage(surface);
            using var clip = new GraphicsPath();
            if (round) clip.AddEllipse(10, 10, 80, 80); else clip.AddRectangle(new Rectangle(10, 10, 80, 80));
            draw.Invoke(control, new object[] { g, item, clip });
            check(surface.GetPixel(50, 12).R > 240 && surface.GetPixel(50, 12).B > 240, "background covers edge without transparent letterboxing round=" + round);
            check(surface.GetPixel(2, 2).A == 0, "background stays clipped round=" + round);
            if (round) check(surface.GetPixel(11, 11).A == 0, "round corners remain transparent");
        }
        foreach (var style in new[] { "classic", "01", "06", "11", "orbit" })
        {
            var counts = new List<int>();
            foreach (var mode in new[] { "Icon", "Background" })
            {
                var config = new PluginConfig(); ConfigStore.Normalize(config);
                var scene = config.Menus[SceneKeys.Global]; scene.StyleId = style; scene.ContentMode = "IconAndText";
                foreach (var ring in scene.Rings) ring.Enabled = ring.Index == 0;
                scene.Rings[0].Slots = 4;
                scene.Rings[0].Items = Enumerable.Range(0, 4).Select(slot => new MenuItemConfig {
                    Slot = slot, Value = "LINE", Label = "直线", IconData = data, ImageMode = mode }).ToList();
                using var wheelControl = new RadialMenuControl(Color.White) { Size = new Size(600, 600), EnableCellSelection = true };
                wheelControl.SetMenu(config, scene); wheelControl.SelectCell(0, 0);
                using var bitmap = new Bitmap(600, 600);
                wheelControl.DrawToBitmap(bitmap, new Rectangle(0, 0, 600, 600));
                var magenta = 0;
                for (var y = 0; y < 600; y++) for (var x = 0; x < 600; x++)
                { var pixel = bitmap.GetPixel(x, y); if (pixel.R > 200 && pixel.B > 200 && pixel.G < 90) magenta++; }
                counts.Add(magenta);
                if (output != null) bitmap.Save(System.IO.Path.Combine(output, "image-mode-" + style + "-" + mode + ".png"));
            }
            check(counts[0] > 30 && counts[1] > counts[0] * 1.5, style + " icon and cell-background render distinct coverage");
        }
        foreach (var style in new[] { "01", "06", "11", "orbit" })
        foreach (var command in new[] { "SUN-173", "XYCUT-UNFOLD" })
        {
            var config = new PluginConfig(); ConfigStore.Normalize(config);
            config.CustomCommands.Add(new CustomCommandConfig { Id = "private-command-id", Name = "功能", CommandName = command });
            var scene = config.Menus[SceneKeys.Global]; scene.StyleId = style;
            foreach (var ring in scene.Rings) ring.Enabled = ring.Index == 0;
            scene.Rings[0].Slots = 4;
            var target = new MenuItemConfig { Slot = 0, Label = "功能", Value = command };
            scene.Rings[0].Items = new List<MenuItemConfig> { target };
            byte[] Render()
            {
                using var view = new RadialMenuControl(Color.White) { Size = new Size(300, 300), EnableCellSelection = true, ScaleMenuForPreview = true };
                view.SetMenu(config, scene); view.SelectCell(0, 0);
                check(view.ResolveIconCommand(target) == command, style + " resolves actual icon command " + target.Kind);
                using var bitmap = new Bitmap(300, 300);
                view.DrawToBitmap(bitmap, new Rectangle(0, 0, 300, 300));
                using var bytes = new System.IO.MemoryStream(); bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png); return bytes.ToArray();
            }
            var native = Render();
            target.Kind = ActionKinds.CustomCommand; target.Value = "private-command-id";
            var before = JsonCompat.Serialize(config);
            check(native.SequenceEqual(Render()), style + " custom ID and native command render identical semantic icon " + command);
            check(before == JsonCompat.Serialize(config), "icon resolution does not rewrite custom binding " + style);
        }
    }
}
