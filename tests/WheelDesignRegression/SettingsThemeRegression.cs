using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class SettingsThemeRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        var config = new PluginConfig();
        ConfigStore.Normalize(config);
        using var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => throw new InvalidOperationException("Theme verification must not save"), loadCatalog: false);
        settings.Size = new Size(1480, 860);
        settings.CreateControl();
        _ = settings.Handle;
        var draft = Field<PluginConfig>(settings, "_config");
        var preview = Field<Control>(settings, "_preview");
        var swatch = Field<Control>(settings, "_textColorSwatch");
        var previewColor = preview.BackColor;
        var swatchColor = swatch.BackColor;
        var tabs = Descendants(settings).OfType<TabControl>().ToArray();
        var originalModes = tabs.ToDictionary(tab => tab, tab => tab.DrawMode);
        var previewPane = Descendants(settings).Single(control => Equals(control.Tag, "preview"));
        var originalPane = previewPane.BackColor;
        var wheelJson = JsonCompat.Serialize(draft.Menus);

        foreach (var mode in new[] { "Solid", "Gradient" })
        {
            draft.PluginAppearanceEnabled = true;
            draft.PluginAppearance = new WheelAppearance { FillMode = mode, StartColor = "#142232", EndColor = "#405B73", SelectedColor = "#FFBB55" };
            Call(settings, "ApplyPluginAppearance");
            var pages = Field<TabControl>(settings, "_pages");
            for (var page = 0; page < pages.TabCount; page++)
            {
                Call(settings, "SelectPage", page);
                check(pages.TabPages[page].BackColor == settings.BackColor, mode + " settings page " + page + " receives global palette");
                var labels = Descendants(pages.TabPages[page]).OfType<Label>().Where(label => !label.IsDisposed).ToArray();
                check(labels.All(label => label.ForeColor == Color.White), mode + " page " + page + " labels keep contrast");
            }
            check(previewPane.BackColor == settings.BackColor, mode + " preview surrounding panel follows settings theme");
            check(preview.BackColor == previewColor && swatch.BackColor == swatchColor, mode + " wheel content and color sample preserved");
            check(tabs.All(tab => tab.DrawMode == TabDrawMode.OwnerDrawFixed), mode + " all settings tabs follow palette");
            var editorTabs = tabs.First(tab => !ReferenceEquals(tab, pages));
            using (var tabBitmap = new Bitmap(160, 36))
            using (var graphics = Graphics.FromImage(tabBitmap))
            {
                var draw = new DrawItemEventArgs(graphics, editorTabs.Font, new Rectangle(0, 0, 160, 36), editorTabs.SelectedIndex, DrawItemState.Selected);
                typeof(TabControl).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editorTabs, new object[] { draw });
                check(tabBitmap.GetPixel(1, 1).ToArgb() == draft.PluginAppearance.GetSelectedColor().ToArgb(), mode + " selected tab paints chosen palette");
            }
            var status = Field<Label>(settings, "_imeBasicStatus");
            status.ForeColor = Color.FromArgb(99, 115, 129);
            check(status.ForeColor == Color.White, mode + " status refresh cannot reintroduce unreadable default text");
            using var dynamicPanel = new Panel { BackColor = Color.White };
            using var dynamicLabel = new Label { Text = "动态状态", ForeColor = Color.Black };
            dynamicPanel.Controls.Add(dynamicLabel);
            pages.TabPages[0].Controls.Add(dynamicPanel);
            Application.DoEvents();
            check(dynamicPanel.BackColor == settings.BackColor && dynamicLabel.ForeColor == Color.White, mode + " dynamically added controls receive palette");
            pages.TabPages[0].Controls.Remove(dynamicPanel);
            Call(settings, "SelectPage", 3);
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(-20000, -20000);
            settings.ShowInTaskbar = false;
            try
            {
                settings.Show();
                Application.DoEvents();
                settings.PerformLayout();
                check(editorTabs.Visible && editorTabs.TabPages[editorTabs.SelectedIndex].Controls.Cast<Control>().Any(control => control.Visible), mode + " theme screenshot contains live editor controls");
                using var bitmap = new Bitmap(settings.Width, settings.Height);
                settings.DrawToBitmap(bitmap, new Rectangle(Point.Empty, settings.Size));
                bitmap.Save(Path.Combine(output, "settings-whole-theme-" + mode.ToLowerInvariant() + ".png"), ImageFormat.Png);
            }
            finally { settings.Hide(); }
        }
        check(JsonCompat.Serialize(draft.Menus) == wheelJson, "whole settings palette leaves wheel configuration unchanged");
        draft.PluginAppearanceEnabled = false;
        Call(settings, "ApplyPluginAppearance");
        check(tabs.All(tab => tab.DrawMode == originalModes[tab]), "reset restores original tab rendering modes");
        check(previewPane.BackColor == originalPane, "reset restores preview pane default color");
        var restoredStatus = Field<Label>(settings, "_imeBasicStatus");
        restoredStatus.ForeColor = Color.Red;
        check(restoredStatus.ForeColor == Color.Red, "reset detaches palette enforcement handlers");
    }

    private static T Field<T>(SettingsWindow settings, string name) => (T)typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
    private static void Call(SettingsWindow settings, string name, params object[] args) => typeof(SettingsWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, args);
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
