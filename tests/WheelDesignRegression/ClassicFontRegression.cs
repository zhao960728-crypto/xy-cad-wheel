using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using XiaoYang.AutoCADPlugin;

internal static class ClassicFontRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var draw = typeof(RadialMenuControl).GetMethod("DrawFittedText", BindingFlags.NonPublic | BindingFlags.Instance);
        var pathBuilder = typeof(RadialMenuControl).GetMethod("CreateSlotPath", BindingFlags.NonPublic | BindingFlags.Static);
        var boundsGetter = typeof(RadialMenuControl).GetMethod("GetMenuBounds", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var label in new[] { "移", "移动", "局部放大视图", "完整显示长命令名称与尺寸标注" })
        {
            RadialMenuControl.TextLayoutDiagnostic Render(bool preview, int requested = 0)
            {
                using var control = new RadialMenuControl(Color.White) { Size = preview ? new Size(220, 220) : new Size(600, 600), ScaleMenuForPreview = preview };
                control.SetMenu(new PluginConfig(), new SceneMenuConfig { MenuSizePx = 600 });
                var bounds = (RectangleF)boundsGetter.Invoke(control, null);
                var scale = preview ? bounds.Width / 600f : 1f;
                var center = new PointF(control.Width / 2f, control.Height / 2f);
                using var path = (GraphicsPath)pathBuilder.Invoke(null, new object[] { center, 120 * scale, 200 * scale, -22.5f, 45f });
                using var bitmap = new Bitmap(control.Width, control.Height);
                using var graphics = Graphics.FromImage(bitmap);
                draw.Invoke(control, new object[] { graphics, label, Color.Black, false, TextEffects.None,
                    new PointF(center.X + 160 * scale, center.Y), 160 * scale, 80 * scale, 8, requested, "宋体", path, center, 120 * scale, 200 * scale, 0f });
                return control.TextDiagnostics.Single();
            }
            foreach (var requested in new[] { 6, 8, 32, 48 })
            foreach (var isPreview in new[] { false, true })
            {
                var manual = Render(isPreview, requested);
                check(manual.LogicalFontSizePx <= requested + .01f && manual.FullText && manual.FitsBounds,
                    $"classic manual {requested} px fits complete text at preview={isPreview}: {label}");
                if (requested <= 8 && label.Length <= 2)
                    check(Math.Abs(manual.LogicalFontSizePx - requested) < .01f,
                        $"classic fitting manual {requested} px remains unchanged at preview={isPreview}: {label}");
            }
            var actual = Render(false);
            if (label.Length <= 2)
                check(actual.LogicalFontSizePx > 22, $"classic spacious automatic font exceeds 22 px: {label}");
            var preview = Render(true);
            check(actual.FullText && actual.FitsBounds && preview.FullText && preview.FitsBounds,
                $"classic full text fits actual and small preview: {label}");
            check(Math.Abs(actual.LogicalFontSizePx - preview.LogicalFontSizePx) <= 2.5f,
                $"classic automatic logical font avoids double preview scaling: {label} actual={actual.LogicalFontSizePx:F2} preview={preview.LogicalFontSizePx:F2}");
        }
    }
}
