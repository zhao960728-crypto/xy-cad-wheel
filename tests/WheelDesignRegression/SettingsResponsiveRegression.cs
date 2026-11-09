using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class SettingsResponsiveRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check, string output)
    {
        Directory.CreateDirectory(output);
        // Emulate authored controls at 150/200% with explicit pixel/font scaling.
        // This does not change the host process awareness, monitor DPI or OS settings.
        foreach (var sample in new[] { (Dpi: 96, Width: 1000, Text: 1f), (Dpi: 144, Width: 1440, Text: 1f),
            (Dpi: 192, Width: 1680, Text: 1f), (Dpi: 192, Width: 1000, Text: 1.25f) })
        {
            var config = new PluginConfig(); ConfigStore.Normalize(config);
            config.Menus[SceneKeys.Global].StyleId = "orbit";
            var original = JsonCompat.Serialize(config);
            using var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => throw new InvalidOperationException("DPI layout must not save"), loadCatalog: false);
            settings.StartPosition = FormStartPosition.Manual; settings.Location = new Point(-20000, -20000); settings.ShowInTaskbar = false;
            var baselineCaptionWidth = Field<TableLayoutPanel>(settings, "_currentFields").ColumnStyles[0].Width;
            float scale = sample.Dpi / 96f;
            settings.Scale(new SizeF(scale, scale));
            using var font = new Font("Microsoft YaHei UI", 9f * scale * sample.Text);
            settings.Font = font;
            settings.ClientSize = new Size((int)(sample.Width * scale), (int)(860 * scale));
            var draft = Field<PluginConfig>(settings, "_config"); var snapshot = JsonCompat.Serialize(draft);
            var name = sample.Dpi + "dpi-" + sample.Width + "-text" + sample.Text.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                settings.Show();
                Call(settings, "SelectPage", 3);
                SelectTab(settings, "图标与文字"); SelectTab(settings, "文字与图片");
                Call(settings, "ApplyResponsiveSettingsLayout"); settings.PerformLayout(); Application.DoEvents();
                var fields = Field<TableLayoutPanel>(settings, "_currentFields");
                var page = (TabPage)fields.Parent;
                check(fields.Visible && fields.Width <= page.ClientSize.Width, name + " editor uses available width without horizontal clipping");
                foreach (var field in new[] { "_fontFamily", "_applyCurrentFont", "_applyAllFonts", "_resetFontFamily", "_fontBold", "_textEffect",
                    "_applyCurrentTextStyle", "_applyAllTextStyles", "_applyPlainTextStyle", "_chooseTextColor", "_chooseAllTextColor", "_cellIconRatio", "_cellContentMode" })
                {
                    var control = Field<Control>(settings, field);
                    page.ScrollControlIntoView(control); Application.DoEvents();
                    var visible = page.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    check(control.Visible && visible.Left >= -1 && visible.Right <= page.ClientSize.Width + 1 && visible.Top >= -1 && visible.Bottom <= page.ClientSize.Height + 1,
                        name + " " + field + " is reachable and fully visible");
                    if (control is Button || control is LinkLabel || control is CheckBox)
                    {
                        var measured = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine);
                        check(control.Width >= measured.Width && control.Height >= measured.Height, name + " " + field + " retains readable full caption");
                    }
                    if (control is NumericUpDown numeric) check(numeric.Height >= numeric.PreferredHeight, name + " numeric editor keeps native text height");
                }
                page.AutoScrollPosition = Point.Empty; settings.PerformLayout(); Application.DoEvents();
                Save(settings, Path.Combine(output, "responsive-" + name + ".png"));
                Call(settings, "ShowBatchCellAppearance");
                Call(settings, "ApplyResponsiveSettingsLayout"); Application.DoEvents();
                var batch = Field<Panel>(settings, "_batchAppearancePanel");
                foreach (var label in Children(batch).OfType<CheckBox>())
                {
                    var measured = TextRenderer.MeasureText(label.Text, label.Font);
                    check(label.Width >= measured.Width && label.Height >= measured.Height, name + " batch caption " + label.Text + " is readable");
                }
                Save(settings, Path.Combine(output, "responsive-batch-" + name + ".png"));
                Call(settings, "EndBatchAppearance");
                settings.Scale(new SizeF(1 / scale, 1 / scale));
                using var restoredFont = new Font("Microsoft YaHei UI", 9f);
                settings.Font = restoredFont;
                settings.MinimumSize = new Size(980, 620);
                settings.ClientSize = new Size(1000, 800);
                Call(settings, "ApplyResponsiveSettingsLayout"); settings.PerformLayout(); Application.DoEvents();
                check(fields.ColumnStyles[0].Width <= baselineCaptionWidth + 1,
                    name + " returning to 100 percent restores caption column width");
                var family = Field<ComboBox>(settings, "_fontFamily");
                check(family.Width <= 260 && Field<Button>(settings, "_applyCurrentFont").MinimumSize.Height < 60,
                    name + " returning to normal font restores usable compact controls");
                check(JsonCompat.Serialize(draft) == snapshot && JsonCompat.Serialize(config) == original, name + " layout and font adaptation preserve wheel configuration");
            }
            finally { settings.Hide(); }
        }
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
    private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Private).Invoke(instance, args);
    private static void SelectTab(Control parent, string caption)
    {
        var page = Children(parent).OfType<TabPage>().Single(p => p.Text == caption);
        ((TabControl)page.Parent).SelectedTab = page;
    }
    private static System.Collections.Generic.IEnumerable<Control> Children(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static void Save(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path, ImageFormat.Png);
    }
}
