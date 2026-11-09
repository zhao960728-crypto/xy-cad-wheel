using System.Reflection;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class CatalogIconRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        check(WheelIconCatalog.TryGetCommandIconId("^C^C_.LINE", out var lineId) && lineId == "line", "catalog uses same macro-aware command matcher as wheel");
        check(WheelIconCatalog.TryGetCommandIconId("ZOOM;_E", out var zoomId) && zoomId == "extents", "catalog preserves command variant matching");
        check(!WheelIconCatalog.TryGetCommandIconId("MYLINE", out _), "catalog does not match third-party command substrings");
        var known = new CadCommandInfo { GlobalName = "HATCHEDIT", DisplayName = "编辑图案填充", IsCoreCommand = true };
        var unknown = new CadCommandInfo { GlobalName = "UNKNOWN-CAD", IsCoreCommand = true };
        var external = new CadCommandInfo { GlobalName = "SUN-999", Aliases = new[] { "LINE" } };
        check(SettingsWindow.CatalogIconId(known) == "hatch", "CAD catalog command receives semantic icon");
        check(SettingsWindow.CatalogIconId(unknown) == "gear", "unknown CAD command receives fallback icon");
        check(SettingsWindow.CatalogIconId(external) == "toolbox", "third-party fallback ignores conflicting alias");
        check(SettingsWindow.CatalogIconId(new CustomCommandConfig { CommandName = "CIRCLE" }) == "circle", "custom known execution command shares wheel icon");
        check(SettingsWindow.CatalogIconId(new CustomCommandConfig { CommandName = "MYLINE" }) == "pencil", "custom unknown command receives fallback icon");

        check(SettingsWindow.CatalogIconId(new CadCommandInfo { GlobalName = "ACME-007", DisplayName = "焊接工具" }) == "weld", "third-party function name receives semantic icon");
        check(SettingsWindow.CatalogIconId(new CadCommandInfo { GlobalName = "OTHER-123", Help = "计算实体重量" }) == "weight", "third-party description receives semantic icon");
        check(SettingsWindow.CatalogIconId(new CadCommandInfo { GlobalName = "OTHER-456", Group = "电气" }) == "circuit", "third-party category receives semantic icon");
        check(SettingsWindow.CatalogIconId(new CustomCommandConfig { CommandName = "ACME-42", Name = "自动编号" }) == "numbering", "custom function name receives semantic icon");

        var config = new PluginConfig();
        ConfigStore.Normalize(config);
        config.CustomCommands.Add(new CustomCommandConfig { Name = "测试圆", CommandName = "CIRCLE" });
        var original = JsonCompat.Serialize(config);
        var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => throw new InvalidOperationException("Catalog icons must not save"), loadCatalog: false);
        ImageList images;
        bool disposed = false;
        try
        {
            typeof(SettingsWindow).GetField("_allCommands", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, new[] { known, unknown, external });
            var tree = (TreeView)typeof(SettingsWindow).GetField("_catalogTree", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
            var filter = typeof(SettingsWindow).GetMethod("FilterCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            filter.Invoke(settings, null);
            images = tree.ImageList;
            check(images != null && images.Images.Count > 0, "catalog refresh attaches populated image list");
            check(images.Images.Count < 10, "catalog renders only distinct required artwork, not all 1024 entries");
            check(tree.Nodes.Cast<TreeNode>().All(node => node.ImageKey == "folder"), "catalog categories show folder artwork");
            var leaves = tree.Nodes.Cast<TreeNode>().SelectMany(root => root.Nodes.Cast<TreeNode>()).ToArray();
            check(leaves.Length == 4 && leaves.All(node => node.ImageKey.Length > 0 && node.ImageKey == node.SelectedImageKey), "all catalog rows retain icons when selected");
            var count = images.Images.Count;
            for (var i = 0; i < 25; i++) filter.Invoke(settings, null);
            check(ReferenceEquals(images, tree.ImageList) && images.Images.Count == count, "repeated catalog refresh reuses image list and bitmap cache");
            check(images.ImageSize.Width == Math.Max(16, SettingsWindow.ScaleLogicalForDpi(20, settings.DeviceDpi)), "catalog artwork follows DPI");
            check(JsonCompat.Serialize(config) == original, "catalog icon refresh leaves supplied configuration unchanged");
            var draft = (PluginConfig)typeof(SettingsWindow).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
            check(JsonCompat.Serialize(draft) == original, "catalog icon refresh leaves draft execution values and configuration unchanged");
            typeof(SettingsWindow).GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] { 1 });
            tree.ExpandAll();
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(-20000, -20000);
            settings.Size = new Size(1480, 860);
            settings.ShowInTaskbar = false;
            try
            {
                settings.Show();
                Application.DoEvents();
                settings.PerformLayout();
                check(tree.Visible && tree.Nodes.Cast<TreeNode>().All(node => node.IsExpanded), "catalog screenshot shows expanded command rows");
                using var bitmap = new Bitmap(settings.Width, settings.Height);
                settings.DrawToBitmap(bitmap, new Rectangle(Point.Empty, settings.Size));
                bitmap.Save(Path.Combine(output, "catalog-icons.png"), ImageFormat.Png);
            }
            finally { settings.Hide(); }
            images.Disposed += (_, _) => disposed = true;
        }
        finally { settings.Dispose(); }
        check(disposed, "disposing settings releases catalog image list");
    }
}
