using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class PreviewMagnifierRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        foreach (var style in new[] { "classic", "01", "orbit", "06", "11" })
        foreach (var size in new[] { new Size(240, 320), new Size(360, 500), new Size(640, 640) })
        {
            var scene = new SceneMenuConfig { StyleId = style, MenuSizePx = 600,
                Rings = new List<RingConfig> { new() { Index = 0, Enabled = true, Slots = 8, DiameterPx = 600,
                    Items = new List<MenuItemConfig> { new() { Slot = 0, Label = "局部放大视图完整名称", Value = "ZOOM", FontSizePx = 48 } } } } };
            var config = new PluginConfig();
            var before = JsonCompat.Serialize(scene);
            using var preview = new RadialMenuControl(Color.FromArgb(42, 47, 54))
                { Size = size, ScaleMenuForPreview = true, EnableCellSelection = true };
            preview.SetMenu(config, scene);
            preview.SelectCell(0, 0);
            preview.CreateControl();
            Point? hit = null;
            for (int x = 0; x < size.Width && hit == null; x += 3)
            for (int y = 0; y < size.Height && hit == null; y += 3)
            {
                var cell = preview.HitCell(new Point(x, y));
                if (cell?.RingIndex == 0 && cell.Slot == 0) hit = new Point(x, y);
            }
            check(hit.HasValue, $"magnifier finds {style} cell at {size}");
            preview.UpdateCursor(hit.Value);
            using var bitmap = new Bitmap(size.Width, size.Height);
            preview.DrawToBitmap(bitmap, preview.ClientRectangle);
            check(!preview.PreviewMagnifierBounds.IsEmpty && preview.ClientRectangle.Contains(preview.PreviewMagnifierBounds),
                $"magnifier stays inside {style} preview at {size}");
            check(preview.HitCell(hit.Value)?.Slot == 0 && JsonCompat.Serialize(scene) == before,
                $"magnifier preserves hit testing and configuration for {style} at {size}");
            if (size.Width == 360) bitmap.Save(Path.Combine(output, "magnifier-" + style + ".png"));
            typeof(RadialMenuControl).GetMethod("OnMouseLeave", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(preview, new object[] { EventArgs.Empty });
            preview.DrawToBitmap(bitmap, preview.ClientRectangle);
            check(preview.PreviewMagnifierBounds.IsEmpty, "magnifier disappears on mouse leave " + style);
            preview.ScaleMenuForPreview = false;
            preview.UpdateCursor(hit.Value);
            preview.DrawToBitmap(bitmap, preview.ClientRectangle);
            check(preview.PreviewMagnifierBounds.IsEmpty, "actual wheel never shows preview magnifier " + style);
        }
    }
}
