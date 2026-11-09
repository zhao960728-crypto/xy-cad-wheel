using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class FreeEditorUiRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check) => Run(check, null);

    internal static void Run(Action<bool, string> check, string output)
    {
        var input = new PluginConfig();
        ConfigStore.Normalize(input);
        input.Menus[SceneKeys.Global].StyleId = "orbit";
        input.Menus[SceneKeys.Global].Rings = new List<RingConfig>
        {
            new RingConfig { Index = 0, Enabled = true, Slots = 4, DiameterPx = 420, Items = new List<MenuItemConfig>
            {
                new MenuItemConfig { Slot = 0, Label = "原功能", Value = "LINE", FontFamily = "宋体", FontSizePx = 19,
                    TextColor = "#123456", CellShape = "Heart", CellSizePercent = 75, FreeX = -.23, FreeY = .17, IconRatioPercent = 65 }
            } },
            new RingConfig { Index = 1, Enabled = true, Slots = 3, DiameterPx = 620, Items = new List<MenuItemConfig>
            { new MenuItemConfig { Slot = 0, Label = "其他圈", Value = "CIRCLE", FontSizePx = 21 } } }
        };
        var original = JsonCompat.Serialize(input);
        var saves = 0;
        using var window = new SettingsWindow(input, new CadCommandCatalog(), _ => saves++, loadCatalog: false);
        window.CreateControl(); window.ClientSize = new Size(1600, 1000); window.PerformLayout();
        var draft = Field<PluginConfig>(window, "_config");
        var wheel = draft.Menus[SceneKeys.Global];
        var preview = Field<RadialMenuControl>(window, "_preview");
        preview.Size = new Size(640, 640); preview.CreateControl(); preview.SelectCell(0, 0);
        var first = wheel.Rings[0].Items.Single();
        var other = JsonCompat.Serialize(wheel.Rings[1]);
        var before = JsonCompat.Serialize(draft);
        var formCount = Application.OpenForms.Count;
        Invoke(window, "ShowBatchCellAppearance");
        var tools = Field<FlowLayoutPanel>(window, "_batchSelectionTools");
        var panel = Field<Panel>(window, "_batchAppearancePanel");
        var fields = panel.Controls.OfType<TableLayoutPanel>().Single();
        check(preview.BatchSelectionMode && Application.OpenForms.Count == formCount && panel.FindForm() == window,
            "batch editor stays embedded in settings without opening a selection dialog");
        Click(tools, "✓ 应用");
        check(preview.BatchSelectionMode && JsonCompat.Serialize(draft) == before, "empty batch apply leaves draft intact and selection open");
        var point = FindCell(preview, 0, 0);
        Mouse(preview, point);
        check(preview.BatchSelection.Contains((0, 0)), "preview click adds cell to batch highlight selection");
        Mouse(preview, point);
        check(!preview.BatchSelection.Contains((0, 0)), "second preview click removes cell highlight");
        Click(tools, "当前圈");
        check(preview.BatchSelection.Count == 4 && preview.BatchSelection.All(cell => cell.Item1 == 0)
            && preview.BatchSelection.Contains((0, 3)), "current-ring selection includes unbound positions and excludes other rings");
        ((CheckBox)fields.GetControlFromPosition(0, 5)).Checked = true;
        ((NumericUpDown)fields.GetControlFromPosition(1, 5)).Value = 80;
        Click(tools, "✕ 取消");
        check(!preview.BatchSelectionMode && preview.BatchSelection.Count == 0 && JsonCompat.Serialize(draft) == before,
            "cancel discards pending batch edits and clears selection without mutation");

        Invoke(window, "ShowBatchCellAppearance");
        Click(tools, "当前圈");
        ((CheckBox)fields.GetControlFromPosition(0, 4)).Checked = true;
        var batchMode = (ComboBox)fields.GetControlFromPosition(1, 4);
        check(batchMode.Items.Count == 6 && batchMode.Items[4].ToString().Contains("纯图片") && batchMode.Items[5].ToString().Contains("图片与文字"),
            "batch display choices include both full-cell image modes");
        batchMode.SelectedIndex = 5;
        Click(tools, "✓ 应用");
        check(!preview.BatchSelectionMode && preview.BatchSelection.Count == 0 && wheel.Rings[0].Items.Count == 4
            && wheel.Rings[0].Items.All(i => i.ContentMode == "ImageAndText" && i.IconRatioPercent == 80),
            "confirm applies checked image and ratio fields to selected occupied and empty cells");
        check(first.Value == "LINE" && first.FontFamily == "宋体" && first.FontSizePx == 19 && first.TextColor == "#123456"
            && JsonCompat.Serialize(wheel.Rings[1]) == other, "batch application preserves unchecked properties, commands and other rings");
        check(wheel.Rings[0].Items.Where(i => i.Slot != 0).All(i => string.IsNullOrEmpty(i.Value)), "styling empty positions does not invent executable commands");

        preview.SelectCell(0, 0);
        var globalMode = Field<ComboBox>(window, "_contentMode");
        var cellMode = Field<ComboBox>(window, "_cellContentMode");
        globalMode.SelectedIndex = 4;
        check(wheel.ContentMode == "ImageOnly", "global pure-image choice updates draft");
        globalMode.SelectedIndex = 5;
        check(wheel.ContentMode == "ImageAndText", "global image-and-text choice updates draft");
        cellMode.SelectedIndex = 4;
        check(first.ContentMode == "ImageOnly", "current-cell pure-image choice updates only selected cell");
        Field<NumericUpDown>(window, "_cellIconRatio").Value = 35;
        check(first.IconRatioPercent == 35 && wheel.Rings[0].Items.Where(i => i.Slot != 0).All(i => i.IconRatioPercent == 80),
            "single-cell ratio editor does not change neighbouring ratios");
        var styles = Field<ComboBox>(window, "_wheelStyle");
        check(styles.Items.Cast<object>().Any(item => item.ToString().Contains("自设计")), "style selector exposes free design mode");
        styles.SelectedIndex = 5;
        check(wheel.StyleId == "free", "free style selection updates draft");
        check(Descendants(window).OfType<Button>().Any(b => b.Text == "拖入新格")
            && Descendants(window).OfType<ComboBox>().Any(c => c.Items.Cast<object>().Any(i => i.ToString() == "爱心")),
            "free design toolbar exposes shape choices and draggable new cell");
        var resize = Descendants(window).OfType<Button>().Single(b => b.Text == "调整单格大小");
        Click(resize);
        check(preview.ResizeMode, "single-cell resize button enters handle mode");
        Click(resize);
        check(!preview.ResizeMode, "resize confirmation leaves handle mode");

        preview.SelectCell(0, 0);
        var geometry = (first.FreeX, first.FreeY, first.CellShape, first.CellSizePercent, first.IconRatioPercent);
        typeof(SettingsWindow).GetField("_allCommands", Private).SetValue(window, new[]
        { new CadCommandInfo { GlobalName = "ACME_BIND_TEST", DisplayName = "绑定新功能" } });
        Field<TextBox>(window, "_cellCommandSearch").Text = "ACME_BIND_TEST";
        var matches = Field<ListBox>(window, "_cellCommandMatches");
        check(matches.Items.Count == 1, "selected-cell search locates exact replacement command");
        matches.SelectedIndex = 0;
        Invoke(window, "BindSelectedCellCommand");
        var replacement = wheel.Rings[0].Items.Single(i => i.Slot == 0);
        check(replacement.Value == "ACME_BIND_TEST" && replacement.Label == "绑定新功能"
            && (replacement.FreeX, replacement.FreeY, replacement.CellShape, replacement.CellSizePercent, replacement.IconRatioPercent) == geometry,
            "rebinding command preserves free coordinates, shape, individual size and icon ratio");
        check(wheel.UnassignedItems.Any(i => i.Value == "LINE"), "replaced binding remains recoverable in unassigned commands");
        check(saves == 0 && JsonCompat.Serialize(input) == original, "editor interactions remain draft-only until explicit save");
        if (!string.IsNullOrWhiteSpace(output)) CaptureEditor(window, check, output);
    }

    private static void CaptureEditor(SettingsWindow window, Action<bool, string> check, string output)
    {
        Directory.CreateDirectory(output);
        window.StartPosition = FormStartPosition.Manual;
        window.Location = new Point(-20000, -20000);
        window.ShowInTaskbar = false;
        window.ClientSize = new Size(1600, 1000);
        try
        {
            window.Show();
            typeof(SettingsWindow).GetMethod("SelectPage", Private).Invoke(window, new object[] { 3 });
            var iconPage = Descendants(window).OfType<TabPage>().Single(p => p.Text == "图标与文字");
            ((TabControl)iconPage.Parent).SelectedTab = iconPage;
            var typography = Descendants(iconPage).OfType<TabPage>().Single(p => p.Text == "文字与图片");
            ((TabControl)typography.Parent).SelectedTab = typography;
            window.PerformLayout(); Application.DoEvents();
            var preview = Field<RadialMenuControl>(window, "_preview");
            preview.SelectCell(0, 0);
            check(typography.Visible && preview.Visible && Descendants(window).OfType<Button>().Single(b => b.Text == "拖入新格").Visible,
                "free screenshot shows selected typography tab and free design toolbar");
            Capture(window, Path.Combine(output, "free-editor-tools.png"));
            Invoke(window, "ShowBatchCellAppearance");
            Click(Field<FlowLayoutPanel>(window, "_batchSelectionTools"), "当前圈");
            window.PerformLayout(); Application.DoEvents();
            var batch = Field<Panel>(window, "_batchAppearancePanel");
            check(batch.Visible && preview.BatchSelection.Count > 0 && Descendants(batch).OfType<CheckBox>().Any(c => c.Text == "字号" && c.Visible),
                "batch screenshot shows highlighted cells and embedded appearance properties");
            Capture(window, Path.Combine(output, "free-editor-batch.png"));
        }
        finally
        {
            Invoke(window, "EndBatchAppearance");
            window.Hide();
        }
    }

    private static void Capture(Form window, string path)
    {
        using var bitmap = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
    private static void Invoke(object instance, string name) => instance.GetType().GetMethod(name, Private).Invoke(instance, null);
    private static void Click(Control parent, string text) => Click(Descendants(parent).OfType<Button>().Single(b => b.Text == text));
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", Private).Invoke(button, new object[] { EventArgs.Empty });
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Mouse(RadialMenuControl preview, Point point)
    {
        var args = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
        typeof(RadialMenuControl).GetMethod("OnMouseDown", Private).Invoke(preview, new object[] { args });
        typeof(RadialMenuControl).GetMethod("OnMouseUp", Private).Invoke(preview, new object[] { args });
    }
    private static Point FindCell(RadialMenuControl preview, int ring, int slot)
    {
        for (var y = 0; y < preview.Height; y += 2)
            for (var x = 0; x < preview.Width; x += 2)
            { var hit = preview.HitCell(new Point(x, y)); if (hit?.RingIndex == ring && hit.Slot == slot) return new Point(x, y); }
        throw new InvalidOperationException("Missing preview cell " + ring + "/" + slot);
    }
}
