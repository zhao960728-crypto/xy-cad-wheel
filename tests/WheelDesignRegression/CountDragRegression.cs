using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class CountDragRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check, string output) => Run(check);

    internal static void Run(Action<bool, string> check)
    {
        var config = Fixture();
        var original = JsonCompat.Serialize(config);
        var saves = 0;
        using (var window = new SettingsWindow(config, new CadCommandCatalog(), _ => saves++, loadCatalog: false))
        {
            window.CreateControl();
            window.ClientSize = new Size(1600, 900);
            window.PerformLayout();
            var draft = Field<PluginConfig>(window, "_config");
            var wheel = draft.Menus[SceneKeys.Global];
            var ring = wheel.Rings[0];
            var preview = Field<RadialMenuControl>(window, "_preview");
            preview.Size = new Size(640, 640);
            preview.CreateControl();
            var initial = ring.Items.ToDictionary(i => i.Value, i => JsonCompat.Serialize(i));
            // A timer closes unexpected modal UI so a regression fails instead of hanging unattended.
            var unexpectedModal = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += (_, _) => { foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
                    if (!ReferenceEquals(form, window) && form.Modal) { unexpectedModal = true; form.Close(); } };
                timer.Start();
                Field<List<NumericUpDown>>(window, "_ringSlotEditors")[0].Value = 7;
                check(!unexpectedModal && ring.Slots == 7 && ring.Items.All(i => initial[i.Value] == JsonCompat.Serialize(i)),
                    "numeric increase immediately appends empty cells without position dialog or command movement");
                Field<List<NumericUpDown>>(window, "_ringSlotEditors")[0].Value = 4;
                check(!unexpectedModal && ring.Slots == 4 && ring.Items.Count == 4 &&
                    wheel.UnassignedItems.Any(i => JsonCompat.Serialize(i) == initial["TEST4"]),
                    "numeric decrease immediately removes tail and preserves complete removed command");
            }
            var snapshot = JsonCompat.Serialize(draft);
            preview.Size = new Size(640, 640);
            var gap = preview.CellInsertionPoints().First(g => g.Ring == 0 && g.Slot > 0);
            var addPoint = Point.Round(gap.Point);
            var enter = Drag(preview, addPoint, true, "OnDragEnter");
            var over = Drag(preview, addPoint, true, "OnDragOver");
            check(enter.Effect == DragDropEffects.Copy && over.Effect == DragDropEffects.Copy,
                "plus drag enter and valid gap hover advertise insertion");
            typeof(Control).GetMethod("OnDragLeave", Private).Invoke(preview, new object[] { EventArgs.Empty });
            check(JsonCompat.Serialize(draft) == snapshot, "cancelled plus drag and hover do not mutate draft");
            var items = ring.Items.ToArray();
            var oldSlots = items.ToDictionary(i => i.Value, i => i.Slot);
            Drag(preview, addPoint, true, "OnDragDrop");
            check(ring.Slots == 5 && !ring.Items.Any(i => i.Slot == gap.Slot) && items.All(i =>
                ring.Items.Contains(i) && i.Slot == oldSlots[i.Value] + (oldSlots[i.Value] >= gap.Slot ? 1 : 0)),
                "plus drop inserts at hovered interior gap and shifts existing command objects");
            var victim = items.Last();
            var victimJson = JsonCompat.Serialize(victim);
            var victimPoint = FindCell(preview, 0, victim.Slot);
            check(Drag(preview, victimPoint, false, "OnDragOver").Effect == DragDropEffects.Copy,
                "minus hover identifies an occupied target cell");
            Drag(preview, victimPoint, false, "OnDragDrop");
            check(ring.Slots == 4 && !ring.Items.Contains(victim) && wheel.UnassignedItems.Contains(victim) &&
                JsonCompat.Serialize(victim) == victimJson, "minus drop moves complete selected command to unassigned");
            snapshot = JsonCompat.Serialize(draft);
            foreach (var add in new[] { true, false })
            {
                var outside = new Point(-500, -500);
                check(Drag(preview, outside, add, "OnDragOver").Effect == DragDropEffects.None, "outside count drag rejected: " + add);
                Drag(preview, outside, add, "OnDragDrop");
            }
            check(JsonCompat.Serialize(draft) == snapshot, "invalid count drops preserve entire draft");
            typeof(SettingsWindow).GetField("_practiceMode", Private).SetValue(window, true);
            Drag(preview, Point.Round(preview.CellInsertionPoints().First().Point), true, "OnDragDrop");
            check(JsonCompat.Serialize(draft) == snapshot, "practice mode ignores count drops");
            typeof(SettingsWindow).GetField("_practiceMode", Private).SetValue(window, false);
            Field<List<NumericUpDown>>(window, "_ringSlotEditors")[0].Value = 1;
            snapshot = JsonCompat.Serialize(draft);
            var onlyCell = FindCell(preview, 0, 0);
            check(Drag(preview, onlyCell, false, "OnDragOver").Effect == DragDropEffects.None, "one remaining cell cannot be removed");
            Drag(preview, onlyCell, false, "OnDragDrop");
            check(JsonCompat.Serialize(draft) == snapshot, "one-cell removal limit preserves draft");
            Field<List<NumericUpDown>>(window, "_ringSlotEditors")[0].Value = 32;
            snapshot = JsonCompat.Serialize(draft);
            Drag(preview, addPoint, true, "OnDragDrop");
            check(!preview.CellInsertionPoints().Any(g => g.Ring == 0) && JsonCompat.Serialize(draft) == snapshot,
                "32-cell ring exposes no insertion gaps and rejects addition");
            check(saves == 0 && original == JsonCompat.Serialize(config), "count edits remain draft until explicit save");
        }
        foreach (var style in new[] { "orbit", "classic", "01", "06", "11" })
        {
            var fixture = Fixture(); var wheel = fixture.Menus[SceneKeys.Global]; wheel.StyleId = style;
            using var control = new RadialMenuControl(Color.White) { Size = new Size(640, 640), ScaleMenuForPreview = true, EnableCellSelection = true };
            control.SetMenu(fixture, wheel); control.CreateControl(); control.SelectCell(0, 0);
            var gaps = control.CellInsertionPoints();
            check(gaps.Count > 0, style + " exposes insertion targets");
            foreach (var gap in gaps)
            {
                var hit = control.HitInsertion(Point.Round(gap.Point));
                check(hit != null && hit.RingIndex == gap.Ring && hit.Slot == gap.Slot,
                    style + " insertion target maps to correct ring/slot " + gap.Ring + "/" + gap.Slot);
            }
            check(control.HitInsertion(new Point(-500, -500)) == null, style + " rejects distant insertion");
        }
    }

    private static PluginConfig Fixture()
    {
        var config = new PluginConfig(); ConfigStore.Normalize(config);
        var wheel = config.Menus[SceneKeys.Global]; wheel.StyleId = "orbit";
        wheel.Rings = new List<RingConfig> { new RingConfig { Index = 0, Enabled = true, Slots = 5, DiameterPx = 520,
            Items = Enumerable.Range(0, 5).Select(i => new MenuItemConfig { Slot = i, Value = "TEST" + i,
                Label = "自定义显示 " + i, FontSizePx = 16 + i, FontBold = true, FontFamily = "宋体",
                TextColor = "#123456", TextEffect = TextEffects.Shadow, ImageMode = "Background" }).ToList() } };
        return config;
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
    private static DragEventArgs Drag(RadialMenuControl preview, Point point, bool add, string eventName)
    {
        var type = typeof(SettingsWindow).GetNestedType("CellCountDrag", BindingFlags.NonPublic);
        var token = Activator.CreateInstance(type, true);
        type.GetField("Add", Private | BindingFlags.Public).SetValue(token, add);
        var data = new DataObject(); data.SetData(type, token);
        var screen = preview.PointToScreen(point);
        var args = new DragEventArgs(data, 1, screen.X, screen.Y, DragDropEffects.Copy, DragDropEffects.None);
        typeof(Control).GetMethod(eventName, Private).Invoke(preview, new object[] { args });
        return args;
    }
    private static Point FindCell(RadialMenuControl preview, int ring, int slot)
    {
        for (var y = 0; y < preview.Height; y += 2)
            for (var x = 0; x < preview.Width; x += 2)
            { var hit = preview.HitCell(new Point(x, y)); if (hit?.RingIndex == ring && hit.Slot == slot) return new Point(x, y); }
        throw new InvalidOperationException("Missing preview cell " + ring + "/" + slot);
    }
}
