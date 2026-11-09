using System.Drawing;
using System.Reflection;
using XiaoYang.AutoCADPlugin;

internal static class NodeLayoutRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var layout = typeof(RadialMenuControl).GetMethod("BuildNodePositions", BindingFlags.Static | BindingFlags.NonPublic);
        List<RectangleF> Build(float size, int slots, int percent) =>
            (List<RectangleF>)layout.Invoke(null, new object[] {
                new PointF(size / 2, size / 2), size,
                new RingConfig { Index = 2, Slots = slots, NodeInnerSizePercent = percent }
            });

        // Crossing twelve commands must not silently switch to a smaller size
        // baseline. At 50%, both populations fit without capacity reduction.
        var twelve = Build(520, 12, 50);
        var thirteen = Build(520, 13, 50);
        check(Math.Abs(twelve[0].Width - thirteen[0].Width) < .01f,
            "node size does not collapse when slot count crosses twelve");
        var thirdRing = Build(520, 16, 100);
        check(thirdRing[0].Width >= 85,
            "third ring sixteen commands keep readable default node diameter");
        check(thirdRing[0].Width > Build(520, 16, 50)[0].Width,
            "third ring current ring size changes actual node diameter");

        foreach (var size in new[] { 260f, 340f, 520f, 900f })
        foreach (var slots in new[] { 4, 8, 12, 13, 16, 20, 32 })
        foreach (var percent in new[] { 50, 100, 150 })
        {
            var cells = Build(size, slots, percent);
            var center = new PointF(size / 2, size / 2);
            float Distance(PointF a, PointF b) =>
                (float)Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            PointF Center(RectangleF cell) => new(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
            var valid = cells.Count == slots;
            for (var i = 0; i < cells.Count; i++)
            {
                var radius = cells[i].Width / 2;
                valid &= Math.Abs(cells[i].Width - cells[0].Width) < .01f;
                valid &= Distance(Center(cells[i]), center) + radius <= size / 2 - 7.9f;
                valid &= Distance(Center(cells[i]), center) - radius - size * .055f >= 7.9f;
                for (var j = i + 1; j < cells.Count; j++)
                    valid &= Distance(Center(cells[i]), Center(cells[j])) - radius - cells[j].Width / 2 >= 7.9f;
            }
            check(valid, $"node bounds and eight pixel spacing size={size} slots={slots} percent={percent}");
        }

        var getBounds = typeof(RadialMenuControl).GetMethod("GetMenuBounds", BindingFlags.Instance | BindingFlags.NonPublic);
        var getCells = typeof(RadialMenuControl).GetMethod("BuildStyleCells", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var slots in new[] { 12, 13, 16, 32 })
        foreach (var percent in new[] { 50, 100, 150 })
        {
            var config = new PluginConfig();
            var scene = new SceneMenuConfig {
                MenuSizePx = 520, StyleId = "06",
                Rings = new List<RingConfig> { new() { Index = 2, Slots = slots, NodeInnerSizePercent = percent } }
            };
            RectangleF[] NormalizedCells(bool preview)
            {
                using var control = new RadialMenuControl(Color.White) {
                    Size = preview ? new Size(340, 340) : new Size(520, 520),
                    ScaleMenuForPreview = preview, EnableCellSelection = true
                };
                control.SetMenu(config, scene);
                control.SelectCell(2, 0);
                var bounds = (RectangleF)getBounds.Invoke(control, null);
                var cells = (System.Collections.IEnumerable)getCells.Invoke(control, new object[] { bounds });
                return cells.Cast<object>().Select(cell => {
                    var r = (RectangleF)cell.GetType().GetField("Bounds").GetValue(cell);
                    return new RectangleF((r.X - bounds.X) / bounds.Width, (r.Y - bounds.Y) / bounds.Width,
                        r.Width / bounds.Width, r.Height / bounds.Width);
                }).ToArray();
            }
            var actual = NormalizedCells(false);
            var preview = NormalizedCells(true);
            check(actual.Length == slots + 1 && actual.Length == preview.Length && actual.Zip(preview).All(pair =>
                Math.Abs(pair.First.X - pair.Second.X) < .00001f &&
                Math.Abs(pair.First.Y - pair.Second.Y) < .00001f &&
                Math.Abs(pair.First.Width - pair.Second.Width) < .00001f),
                $"node preview preserves actual topology and size slots={slots} percent={percent}");
        }
    }
}
