using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class CellContentModeRegression
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool, string> check, string output = null)
    {
        check(JsonCompat.Deserialize<MenuItemConfig>("{\"Value\":\"LINE\"}").ContentMode == "Inherit", "legacy cell inherits display mode");
        var config = new PluginConfig(); ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Global]; scene.ContentMode = "IconOnly";
        foreach (var ring in scene.Rings) ring.Enabled = ring.Index == 0;
        scene.Rings[0].Slots = 4;
        using var source = new Bitmap(40, 40);
        using (var g = Graphics.FromImage(source)) g.Clear(Color.Magenta);
        var icon = CommandIconImage.Encode(source, new RectangleF(0, 0, 40, 40));
        scene.Rings[0].Items = new List<MenuItemConfig> {
            new MenuItemConfig { Slot = 0, Value = "LINE", Label = "直线", IconData = icon },
            new MenuItemConfig { Slot = 1, Value = "CIRCLE", Label = "圆", ContentMode = "TextOnly" } };
        scene.UnassignedItems.Add(new MenuItemConfig { Value = "ARC", ContentMode = "bad-mode" });
        ConfigStore.Normalize(config);
        check(scene.UnassignedItems[0].ContentMode == "Inherit", "unassigned invalid cell mode normalizes safely");
        foreach (var style in new[] { "classic", "01", "06", "11", "orbit" })
        {
            scene.StyleId = style;
            var target = scene.Rings[0].Items[0];
            var iconPixels = new List<int>();
            foreach (var mode in new[] { "TextOnly", "IconAndText", "IconOnly" })
            {
                target.ContentMode = mode;
                using var control = new RadialMenuControl(Color.White) { Size = new Size(600, 600), EnableCellSelection = true };
                control.SetMenu(config, scene); control.SelectCell(0, 0);
                check(control.ResolveCellContentMode(target) == mode, style + " resolves explicit cell mode " + mode);
                using var image = new Bitmap(600, 600);
                control.DrawToBitmap(image, new Rectangle(0, 0, 600, 600));
                var magenta = 0;
                for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
                { var pixel = image.GetPixel(x, y); if (pixel.R > 200 && pixel.B > 200 && pixel.G < 90) magenta++; }
                iconPixels.Add(magenta);
                check(scene.ContentMode == "IconOnly" && scene.Rings[0].Items[1].ContentMode == "TextOnly", style + " cell override leaves global and neighbor unchanged");
            }
            check(iconPixels[0] == 0 && iconPixels[1] > 20 && iconPixels[2] > 20, style + " per-cell text suppresses image and image modes render it");
        }
        scene.StyleId = "orbit";
        var original = JsonCompat.Serialize(config);
        var saves = 0;
        using var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => saves++, loadCatalog: false);
        var draft = (PluginConfig)typeof(SettingsWindow).GetField("_config", Flags).GetValue(settings);
        var modeBox = (ComboBox)typeof(SettingsWindow).GetField("_cellContentMode", Flags).GetValue(settings);
        var targetEditor = typeof(SettingsWindow).GetField("_catalogTarget", Flags).GetValue(settings);
        ((NumericUpDown)targetEditor.GetType().GetProperty("Ring").GetValue(targetEditor)).Value = 1;
        ((NumericUpDown)targetEditor.GetType().GetProperty("Slot").GetValue(targetEditor)).Value = 1;
        typeof(SettingsWindow).GetMethod("LoadSelectedCellAppearance", Flags).Invoke(settings, null);
        modeBox.SelectedIndex = 2;
        check(draft.Menus[SceneKeys.Global].Rings[0].Items[0].ContentMode == "TextOnly" && draft.Menus[SceneKeys.Global].ContentMode == "IconOnly", "current-cell dropdown edits only selected draft cell");
        draft.CustomCommands.Add(new CustomCommandConfig { Id = "cell-mode-rebind", Name = "显示测试命令", CommandName = "LINE" });
        ((TextBox)typeof(SettingsWindow).GetField("_cellCommandSearch", Flags).GetValue(settings)).Text = "显示测试命令";
        typeof(SettingsWindow).GetMethod("SearchCellCommands", Flags).Invoke(settings, null);
        ((ListBox)typeof(SettingsWindow).GetField("_cellCommandMatches", Flags).GetValue(settings)).SelectedIndex = 0;
        typeof(SettingsWindow).GetMethod("BindSelectedCellCommand", Flags).Invoke(settings, null);
        check(draft.Menus[SceneKeys.Global].Rings[0].Items.First(item => item.Slot == 0).ContentMode == "TextOnly", "rebinding cell function retains display override");
        modeBox.SelectedIndex = 0;
        check(draft.Menus[SceneKeys.Global].Rings[0].Items.First(item => item.Slot == 0).ContentMode == "Inherit", "current-cell dropdown can restore inheritance");
        check(saves == 0 && original == JsonCompat.Serialize(config), "cell display edit waits for apply and preserves caller config");
        var clone = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(draft)); ConfigStore.Normalize(clone);
        check(clone.Menus[SceneKeys.Global].Rings[0].Items[1].ContentMode == "TextOnly", "cell display override survives clone and normalization");
        WheelCellEditing.Remove(clone.Menus[SceneKeys.Global], clone.Menus[SceneKeys.Global].Rings[0], new[] { 1 });
        check(clone.Menus[SceneKeys.Global].UnassignedItems.Last().ContentMode == "TextOnly", "removed command preserves cell display in unassigned library");
    }
}
