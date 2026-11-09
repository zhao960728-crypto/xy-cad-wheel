using System.Drawing;
using System.Reflection;
using XiaoYang.AutoCADPlugin;

internal static class CardFontRegression
{
    internal static void Run(Action<bool, string> check)
    {
        ClassicFontRegression.Run(check);
        using var image = new Bitmap(600, 600);
        using var graphics = Graphics.FromImage(image);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip | StringFormatFlags.NoWrap;
        var fit = typeof(RadialMenuControl).GetMethod("FitCardFontSize", BindingFlags.NonPublic | BindingFlags.Static);
        var wrap = typeof(RadialMenuControl).GetMethod("WrapCardText", BindingFlags.NonPublic | BindingFlags.Static);
        float Fit(string label, int requested, float width, float height, float scale = 1) =>
            (float)fit.Invoke(null, new object[] { graphics, label, "宋体", false, requested, new SizeF(width, height), scale, format });
        check(Math.Abs(Fit("字号", 48, 200, 200) - 48) < .01f, "manual card font retains 48 px instead of capping at 22");
        check(Math.Abs(Fit("字号", 8, 200, 200) - 8) < .01f, "legacy manual card font below 12 px remains explicit");
        check(Math.Abs(Fit("字号", 48, 100, 100, .5f) - 24) < .01f, "manual card font scales with preview geometry");
        check(Fit("字号", 0, 160, 160) > Fit("字号", 0, 40, 40) * 2, "automatic font grows with usable cell area");
        check(Fit("字号", 0, 12, 12) < 12, "automatic dense cell can fit below old physical 12 px floor");
        check(Fit("字号", 0, 90, 90) > 22, "automatic spacious cell can grow beyond old 22 px cap");
        // Auto content decisions must be identical for a full-size cell and
        // its scaled preview. Compare actual drawing against the explicit mode.
        var draw = typeof(RadialMenuControl).GetMethod("DrawItemCard", BindingFlags.NonPublic | BindingFlags.Instance);
        using var control = new RadialMenuControl(Color.White);
        foreach (var scale in new[] { 1f, .5f })
        {
            Bitmap Render(string mode)
            {
                var bitmap = new Bitmap(120, 120);
                using var g = Graphics.FromImage(bitmap);
                using var clip = new System.Drawing.Drawing2D.GraphicsPath();
                var bounds = new RectangleF(5, 5, 80 * scale, 60 * scale);
                clip.AddRectangle(bounds);
                draw.Invoke(control, new object[] { g, new MenuItemConfig { Label = "移动", Value = "MOVE", FontSizePx = 0 }, bounds, clip, mode, scale });
                return bitmap;
            }
            using var automatic = Render("Auto");
            using var explicitMode = Render("IconAndText");
            var equal = true;
            for (var x = 0; x < automatic.Width; x++)
            for (var y = 0; y < automatic.Height; y++)
                equal &= automatic.GetPixel(x, y) == explicitMode.GetPixel(x, y);
            check(equal, $"Auto content keeps icon and text at preview scale {scale}");
        }
        foreach (var scale in new[] { 1f, .75f, .5f })
        {
            Bitmap RenderDense(string mode)
            {
                var bitmap = new Bitmap(60, 60);
                using var g = Graphics.FromImage(bitmap);
                using var clip = new System.Drawing.Drawing2D.GraphicsPath();
                var bounds = new RectangleF(2, 2, 30 * scale, 30 * scale);
                clip.AddRectangle(bounds);
                draw.Invoke(control, new object[] { g, new MenuItemConfig { Label = "局部放大视图", Value = "ZOOM", FontSizePx = 48 }, bounds, clip, mode, scale });
                return bitmap;
            }
            using var automatic = RenderDense("Auto");
            using var text = RenderDense("TextOnly");
            var equal = true;
            for (var x = 0; x < automatic.Width; x++)
            for (var y = 0; y < automatic.Height; y++)
                equal &= automatic.GetPixel(x, y) == text.GetPixel(x, y);
            check(equal, $"dense Auto preserves the label at display scale {scale}");
        }
        foreach (var label in new[] { "移动", "空位 21", "标注文字更新" })
        {
            var actual = Fit(label, 0, 80, 80);
            var preview = Fit(label, 0, 40, 40, .5f) / .5f;
            check(Math.Abs(actual - preview) <= 2, $"automatic logical font stays within two px in half-size preview: {label}");
        }
        foreach (var label in new[] { "移动", "空位 21", "标注文字更新", "局部放大视图", "MOVE", "A\nB", "完整显示长命令名称与尺寸标注" })
        foreach (var width in new[] { 12f, 28f, 64f, 140f })
        foreach (var requested in new[] { 0, 8, 48, 72 })
        {
            var size = Fit(label, requested, width, width);
            using var font = new Font("宋体", size, FontStyle.Regular, GraphicsUnit.Pixel);
            var lines = (string[])wrap.Invoke(null, new object[] { graphics, label, font, width, format });
            var height = graphics.MeasureString("国", font, 32767, format).Height * lines.Length;
            check(height <= width + .01f && lines.All(line => graphics.MeasureString(line, font, 32767, format).Width <= width + .01f),
                $"card preferred font {requested} fits complete text '{label.Replace("\n", "/")}' in {width} px");
            check(requested == 0 || size <= requested, "manual card font never exceeds the configured preference");
        }
    }
}
