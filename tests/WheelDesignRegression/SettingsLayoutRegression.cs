using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class SettingsLayoutRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        TestAppearanceDraft(check);
        foreach (var dpi in new[] { 96, 144, 192 })
        {
            check(SettingsWindow.UseCompactSettingsLayout(SettingsWindow.ScaleLogicalForDpi(1260, dpi), dpi),
                $"settings keeps full-width editor at former clipped breakpoint at {dpi} DPI");
            check(SettingsWindow.UseCompactSettingsLayout(SettingsWindow.ScaleLogicalForDpi(1359, dpi), dpi)
                && !SettingsWindow.UseCompactSettingsLayout(SettingsWindow.ScaleLogicalForDpi(1360, dpi), dpi),
                $"settings preview breakpoint scales consistently at {dpi} DPI");
        }
        foreach (var dpi in new[] { 96, 120, 144, 192 })
        foreach (var area in new[] { new Rectangle(0, 0, 1366, 728), new Rectangle(-1920, 0, 1920, 1040), new Rectangle(0, 0, 3840, 2080) })
        {
            var bounds = SettingsWindow.InitialSettingsBounds(area, dpi);
            check(area.Contains(bounds) && bounds.Width <= area.Width * .86 && bounds.Height <= area.Height * .86,
                $"settings initial window retains desktop margin at {dpi} DPI in {area.Size}");
            check(Math.Abs(bounds.Left + bounds.Width / 2 - (area.Left + area.Width / 2)) <= 1
                && Math.Abs(bounds.Top + bounds.Height / 2 - (area.Top + area.Height / 2)) <= 1,
                $"settings initial window centered at {dpi} DPI in {area}");
        }
        var config = new PluginConfig();
        ConfigStore.Normalize(config);
        config.Menus[SceneKeys.Global].StyleId = "orbit";
        using var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => throw new InvalidOperationException("Layout check must not save"), loadCatalog: false);
        settings.StartPosition = FormStartPosition.Manual;
        settings.Location = new Point(-20000, -20000);
        settings.ShowInTaskbar = false;
        try
        {
            settings.Show();
            Application.DoEvents();
            var count = Field<NumericUpDown>(settings, "_ringCount");
            var fields = Field<TableLayoutPanel>(settings, "_layoutFields");
            var rings = Field<TableLayoutPanel>(settings, "_ringEditors");
            count.Value = 4;
            Application.DoEvents();
            check(count.Parent.Height < 60 * settings.DeviceDpi / 96d, "ring count row shrinks to content instead of retaining empty 100px flow panel");
            var caption = fields.GetControlFromPosition(0, 3);
            check(Math.Abs(caption.Top - rings.Top) <= 2, "ring settings caption aligns with first ring rather than middle of all rings");
            count.Value = 8;
            settings.Size = new Size(1000, 640);
            Application.DoEvents();
            var scrollPage = (TabPage)fields.Parent;
            scrollPage.ScrollControlIntoView(rings.GetControlFromPosition(6, 7));
            Application.DoEvents();
            var last = rings.GetControlFromPosition(6, 7);
            var bottom = scrollPage.PointToClient(last.PointToScreen(new Point(0, last.Height))).Y;
            check(last.Visible && bottom <= scrollPage.ClientSize.Height, "eight-ring editor scrolls last size input into visible page");
            var draft = Field<PluginConfig>(settings, "_config");
            var beforeResize = JsonCompat.Serialize(draft);
            foreach (var logicalWidth in new[] { 1000, 1260, 1320, 1359, 1360, 1440 })
            {
                settings.ClientSize = new Size(SettingsWindow.ScaleLogicalForDpi(logicalWidth, settings.DeviceDpi),
                    SettingsWindow.ScaleLogicalForDpi(800, settings.DeviceDpi));
                Application.DoEvents();
                foreach (Control input in rings.Controls)
                {
                    if (!input.Visible) continue;
                    var visibleBounds = scrollPage.RectangleToClient(input.RectangleToScreen(input.ClientRectangle));
                    check(input.Left >= 0 && input.Right <= rings.ClientSize.Width
                        && visibleBounds.Left >= 0 && visibleBounds.Right <= scrollPage.ClientSize.Width,
                        $"ring editor column {rings.GetColumn(input)} remains horizontally visible at {logicalWidth} logical px");
                }
            }
            settings.Size = new Size(1320, 800);
            Application.DoEvents();
            check(JsonCompat.Serialize(draft) == beforeResize, "window resize leaves wheel configuration unchanged");
            using var bitmap = new Bitmap(settings.Width, settings.Height);
            settings.DrawToBitmap(bitmap, new Rectangle(Point.Empty, settings.Size));
            bitmap.Save(Path.Combine(output, "settings-layout-eight-rings.png"));
        }
        finally { settings.Hide(); }
    }

    private static T Field<T>(SettingsWindow settings, string name) => (T)typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);

    private static void TestAppearanceDraft(Action<bool, string> check)
    {
        var original = new PluginConfig();
        ConfigStore.Normalize(original);
        original.Menus[SceneKeys.Global].Rings[0].Items = new List<MenuItemConfig> { new() { Slot = 0, Label = "测试", Value = "LINE" } };
        var snapshot = JsonCompat.Serialize(original);
        var saves = 0;
        PluginConfig applied = null;
        using (var settings = new SettingsWindow(original, new CadCommandCatalog(), value => { saves++; applied = value; }, loadCatalog: false))
        {
            EditAppearance(settings);
            check(saves == 0 && JsonCompat.Serialize(original) == snapshot, "font size family style and color edits stay in draft until apply");
        }
        check(saves == 0 && JsonCompat.Serialize(original) == snapshot, "discarded appearance draft leaves caller configuration and persistence untouched");
        using (var settings = new SettingsWindow(original, new CadCommandCatalog(), value => { saves++; applied = value; }, loadCatalog: false))
        {
            EditAppearance(settings);
            Call(settings, "SaveAllPendingSettings");
            check(saves == 1 && applied.Menus[SceneKeys.Global].Rings[0].Items[0].FontSizePx == 30
                && applied.Menus[SceneKeys.Global].Rings[0].Items[0].TextColor == "#123456",
                "explicit apply persists pending font size and color together");
        }
    }

    private static void EditAppearance(SettingsWindow settings)
    {
        Field<RadialMenuControl>(settings, "_preview").SelectCell(0, 0);
        Field<CheckBox>(settings, "_autoFitCellFont").Checked = false;
        Field<NumericUpDown>(settings, "_cellFontSize").Value = 30;
        Call(settings, "ApplySelectedCellFontFamily");
        Field<CheckBox>(settings, "_fontBold").Checked = true;
        Call(settings, "ApplySelectedCellTextStyle");
        Call(settings, "ApplyTextColor", "#123456", false);
    }

    private static void Call(SettingsWindow settings, string name, params object[] args) => typeof(SettingsWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, args);
}
