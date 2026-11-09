using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class SettingsAllPagesRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool, string> check, string output)
    {
        Directory.CreateDirectory(output);
        foreach (var sample in new[] { (Width: 1600, Font: 9f), (Width: 1000, Font: 13.5f) })
        {
            var input = new PluginConfig(); ConfigStore.Normalize(input);
            var original = JsonCompat.Serialize(input);
            using var window = new SettingsWindow(input, new CadCommandCatalog(), _ => throw new InvalidOperationException("Page layout check must not save"), loadCatalog: false);
            using var font = new Font("Microsoft YaHei UI", sample.Font);
            window.Font = font; window.ClientSize = new Size(sample.Width, 900);
            window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-20000, -20000); window.ShowInTaskbar = false;
            var snapshot = JsonCompat.Serialize(Field<PluginConfig>(window, "_config"));
            try
            {
                window.Show(); Application.DoEvents();
                var pages = Field<TabControl>(window, "_pages");
                // Visit maintenance first and again after every other feature,
                // reproducing navigation out of the initially selected editor.
                foreach (int index in new[] { 6, 3, 1, 2, 0, 4, 5, 7, 8, 6 })
                {
                    Call(window, "SelectPage", index); window.PerformLayout(); Application.DoEvents();
                    var page = pages.TabPages[index];
                    check(page.Visible && page.ClientSize.Width > 100 && page.ClientSize.Height > 100,
                        sample.Width + " page " + index + " becomes usable after navigation");
                    foreach (var container in Children(page).Where(c => c is TableLayoutPanel || c is FlowLayoutPanel))
                    {
                        int events = 0; LayoutEventHandler handler = (_, __) => events++;
                        container.Layout += handler;
                        try { container.PerformLayout(); }
                        finally { container.Layout -= handler; }
                        check(events > 0, sample.Width + " page " + index + " " + container.GetType().Name + " is not left suspended");
                    }
                    if (index == 6) CheckMaintenance(check, page, sample.Width);
                    if (index == 3)
                    {
                        foreach (var tabControl in Children(page).OfType<TabControl>().ToArray())
                        foreach (TabPage nested in tabControl.TabPages)
                        {
                            tabControl.SelectedTab = nested; window.PerformLayout(); Application.DoEvents();
                            check(nested.ClientSize.Width > 100 && nested.ClientSize.Height > 50,
                                sample.Width + " wheel section " + nested.Text + " keeps allocated layout after switching");
                        }
                    }
                    using var image = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(image, new Rectangle(Point.Empty, window.Size));
                    image.Save(Path.Combine(output, "settings-page-" + sample.Width + "-" + index + ".png"), ImageFormat.Png);
                }
                check(JsonCompat.Serialize(Field<PluginConfig>(window, "_config")) == snapshot && JsonCompat.Serialize(input) == original,
                    sample.Width + " page navigation and font changes do not alter wheel data");
            }
            finally { window.Hide(); }
        }
    }

    private static void CheckMaintenance(Action<bool, string> check, TabPage page, int width)
    {
        var fields = page.Controls.OfType<TableLayoutPanel>().Single();
        var caption = fields.GetControlFromPosition(0, 0);
        var audit = fields.GetControlFromPosition(1, 0);
        var backup = fields.GetControlFromPosition(0, 1);
        check(caption != null && audit != null && backup != null && !caption.Bounds.IntersectsWith(audit.Bounds),
            width + " maintenance label and audit button occupy separate columns");
        check(backup.Top >= Math.Max(caption.Bottom, audit.Bottom) && backup.Height >= 30,
            width + " maintenance backup actions occupy their own row instead of the origin");
        var buttons = backup.Controls.OfType<Button>().ToArray();
        check(buttons.Length == 4 && buttons.Any(b => b.Name == "OneClickUpdate") &&
            buttons.All(b => b.Width > 40 && b.Height > 20 && b.Left >= 0 && b.Right <= backup.ClientSize.Width),
            width + " maintenance export/import/reset/update actions remain reachable");
        for (int i = 0; i < buttons.Length; i++)
            for (int j = i + 1; j < buttons.Length; j++)
                check(!buttons[i].Bounds.IntersectsWith(buttons[j].Bounds), width + " maintenance actions do not overlap " + i + "/" + j);
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
    private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Private).Invoke(instance, args);
    private static System.Collections.Generic.IEnumerable<Control> Children(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
}
