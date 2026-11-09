using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class CellDialogRegression
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check, string output)
    {
        Directory.CreateDirectory(output);
        var config = new PluginConfig(); ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Global];
        scene.Rings[0].Slots = 5;
        scene.Rings[0].Items = Enumerable.Range(0, 5).Select(slot => new MenuItemConfig {
            Slot = slot, Value = "COMMAND" + slot, Label = "测试命令 " + (slot + 1), FontSizePx = 12 + slot,
            FontFamily = "宋体", FontBold = slot % 2 == 0, TextEffect = TextEffects.Shadow,
            TextColor = slot == 0 ? "#224466" : "#AABBCC", ImageMode = "Background", IconData = "" }).ToList();
        var input = JsonCompat.Serialize(config);
        var saves = 0;
        using (var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => saves++, loadCatalog: false))
        {
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(-20000, -20000);
            settings.ShowInTaskbar = false;
            var draft = Field<PluginConfig>(settings, "_config");
            var wheel = draft.Menus[SceneKeys.Global];
            var ring = wheel.Rings[0];
            var expected = ring.Items.ToDictionary(item => item.Value, Clone);
            Invoke(settings, "ShowBatchCellAppearance");
            var preview = Field<RadialMenuControl>(settings, "_preview");
            var batch = Field<Panel>(settings, "_batchAppearancePanel");
            var apply = Field<Action>(settings, "_applyBatchAppearance");
            apply();
            check(preview.BatchSelectionMode, "inline batch rejects empty selection without opening dialog");
            preview.BatchSelection.Add((0, 0)); preview.BatchSelection.Add((0, 2));
            Descendants(batch).OfType<CheckBox>().Single(c => c.Text == "字号").Checked = true;
            Descendants(batch).OfType<CheckBox>().Single(c => c.Text == "自动字号").Checked = false;
            Descendants(batch).OfType<NumericUpDown>().Single(n => n.Maximum == 72).Value = 26;
            Descendants(batch).OfType<CheckBox>().Single(c => c.Text == "文字颜色").Checked = true;
            apply();
            check(!preview.BatchSelectionMode, "inline batch confirmation ends selection mode");
            foreach (var key in new[] { "COMMAND0", "COMMAND2" })
            { expected[key].FontSizePx = 26; expected[key].TextColor = "#000000"; }
            CheckItems(check, ring, expected, "inline batch size/color changes only checked cells and preserves unchecked styles");
            var before = JsonCompat.Serialize(draft);
            Invoke(settings, "ShowBatchCellAppearance");
            preview.BatchSelection.Add((0, 1));
            Descendants(batch).OfType<NumericUpDown>().Single(n => n.Maximum == 72).Value = 55;
            Invoke(settings, "EndBatchAppearance");
            check(JsonCompat.Serialize(draft) == before, "cancel inline batch leaves complete draft unchanged");

            object result = null;
            Modal(settings, "选择新格子的插入位置", () => result = Invoke(settings, "ChooseCellCountChange", ring, 7, 0), dialog =>
            {
                Descendants(dialog).OfType<ListBox>().Single().SelectedIndex = 2;
                Capture(dialog, output, "insert-cell-position.png");
                Button(dialog, "插入").PerformClick();
            });
            check(result is bool && (bool)result && ring.Slots == 7 && ring.Items.Select(item => item.Slot).SequenceEqual(new[] { 0, 1, 4, 5, 6 }),
                "insert dialog adds two cells before chosen third position");

            before = JsonCompat.Serialize(draft);
            Modal(settings, "选择新格子的插入位置", () => result = Invoke(settings, "ChooseCellCountChange", ring, 8, 0), dialog =>
            {
                Descendants(dialog).OfType<ListBox>().Single().SelectedIndex = 5;
                Button(dialog, "取消").PerformClick();
            });
            check(result is bool && !(bool)result && JsonCompat.Serialize(draft) == before, "cancel position insertion leaves complete draft unchanged");

            Modal(settings, "选择要删除的格子", () => result = Invoke(settings, "ChooseCellCountChange", ring, 5, 0), dialog =>
            {
                var list = Descendants(dialog).OfType<CheckedListBox>().Single();
                list.SetItemChecked(1, true);
                Button(dialog, "删除这些位置").PerformClick();
                check(dialog.DialogResult != DialogResult.OK && ring.Slots == 7, "delete dialog rejects insufficient checked positions without mutating draft");
                list.SetItemChecked(5, true);
                Capture(dialog, output, "delete-cell-positions.png");
                Button(dialog, "删除这些位置").PerformClick();
            });
            check(result is bool && (bool)result && ring.Slots == 5 && ring.Items.Select(item => item.Value).SequenceEqual(new[] { "COMMAND0", "COMMAND2", "COMMAND4" }) &&
                ring.Items.Select(item => item.Slot).SequenceEqual(new[] { 0, 3, 4 }), "delete dialog removes selected nonadjacent second and sixth positions only");
            check(wheel.UnassignedItems.Select(item => item.Value).OrderBy(value => value).SequenceEqual(new[] { "COMMAND1", "COMMAND3" }) &&
                wheel.UnassignedItems.All(item => Payload(item) == Payload(expected[item.Value])), "delete dialog preserves complete removed command appearance in unassigned list");

            before = JsonCompat.Serialize(draft);
            Modal(settings, "选择要删除的格子", () => result = Invoke(settings, "ChooseCellCountChange", ring, 4, 0), dialog =>
            {
                var list = Descendants(dialog).OfType<CheckedListBox>().Single();
                list.SetItemChecked(0, false); list.SetItemChecked(3, true);
                Button(dialog, "取消").PerformClick();
            });
            check(result is bool && !(bool)result && JsonCompat.Serialize(draft) == before, "cancel position deletion leaves complete draft and unassigned list unchanged");
            TestCompactPreviewPress(settings, check);
            check(saves == 0 && JsonCompat.Serialize(config) == input, "modal cell editing never saves or modifies supplied real configuration");
        }
    }

    private static void TestCompactPreviewPress(SettingsWindow settings, Action<bool, string> check)
    {
        settings.Size = new Size(1000, 750);
        try
        {
            settings.Show();
            Invoke(settings, "SelectPage", 1);
            Field<Button>(settings, "_previewToggle").PerformClick();
            Application.DoEvents();
            var preview = Field<RadialMenuControl>(settings, "_preview");
            check(Field<bool>(settings, "_compactPreviewOpen") && preview.Visible, "compact preview opens for selecting and dragging cells");
            Point? hit = null;
            for (var y = 0; y < preview.Height && hit == null; y += 4)
            for (var x = 0; x < preview.Width; x += 4)
                if (preview.HitCell(new Point(x, y)) != null) { hit = new Point(x, y); break; }
            check(hit.HasValue, "compact preview has a hit-testable cell");
            var mouse = new MouseEventArgs(MouseButtons.Left, 1, hit.Value.X, hit.Value.Y, 0);
            typeof(RadialMenuControl).GetMethod("OnMouseDown", Flags).Invoke(preview, new object[] { mouse });
            check(Field<bool>(settings, "_compactPreviewOpen") && preview.Visible, "pressing a cell keeps compact preview visible so dragging can start");
            typeof(RadialMenuControl).GetMethod("OnMouseUp", Flags).Invoke(preview, new object[] { mouse });
            check(!Field<bool>(settings, "_compactPreviewOpen") && !preview.Visible, "releasing a plain cell click returns to compact settings");
        }
        finally { settings.Hide(); }
    }

    // The timer runs on the same STA as ShowDialog, so no input injection or CAD
    // interaction is involved. Failures always cancel this test-owned dialog.
    private static void Modal(SettingsWindow owner, string title, Action open, Action<Form> operate)
    {
        Exception failure = null;
        var seen = false;
        using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
        {
            timer.Tick += (_, __) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form != owner && form.Text == title && form.Modal);
                if (dialog == null) return;
                seen = true; timer.Stop();
                try { dialog.PerformLayout(); operate(dialog); }
                catch (Exception exception) { failure = exception; }
                finally { if (!dialog.IsDisposed && dialog.DialogResult == DialogResult.None) dialog.DialogResult = DialogResult.Cancel; }
            };
            timer.Start();
            open();
            timer.Stop();
        }
        if (failure != null) throw new InvalidOperationException("Cell dialog automation failed: " + title, failure);
        if (!seen) throw new InvalidOperationException("Cell dialog did not open: " + title);
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static Button Button(Form dialog, string text) => Descendants(dialog).OfType<Button>().Single(control => control.Text == text);
    private static CheckBox Checkbox(Form dialog, string text) => Descendants(dialog).OfType<CheckBox>().Single(control => control.Text == text);
    private static void Capture(Form dialog, string output, string name)
    {
        using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
        { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, name), ImageFormat.Png); }
    }
    private static T Field<T>(SettingsWindow settings, string name) => (T)typeof(SettingsWindow).GetField(name, Flags).GetValue(settings);
    private static object Invoke(SettingsWindow settings, string name, params object[] args) => typeof(SettingsWindow).GetMethod(name, Flags).Invoke(settings, args);
    private static MenuItemConfig Clone(MenuItemConfig item) => JsonCompat.Deserialize<MenuItemConfig>(JsonCompat.Serialize(item));
    private static string Payload(MenuItemConfig item) { var copy = Clone(item); copy.Slot = 0; return JsonCompat.Serialize(copy); }
    private static void CheckItems(Action<bool, string> check, RingConfig ring, Dictionary<string, MenuItemConfig> expected, string message) =>
        check(ring.Items.Count == expected.Count && ring.Items.All(item => JsonCompat.Serialize(item) == JsonCompat.Serialize(expected[item.Value])), message);
}
