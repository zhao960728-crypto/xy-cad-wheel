using System.Drawing;
using XiaoYang.AutoCADPlugin;

internal static class EightRingRegression
{
    internal static void Run(Action<bool, string> check)
    {
        using var icon = new Bitmap(16, 16);
        using var iconStream = new MemoryStream();
        icon.SetPixel(8, 8, Color.Teal);
        icon.Save(iconStream, System.Drawing.Imaging.ImageFormat.Png);
        var iconData = Convert.ToBase64String(iconStream.ToArray());
        PluginConfig Fixture(int count = 8) => new() {
            Menus = new Dictionary<string, SceneMenuConfig> {
                [SceneKeys.Global] = new SceneMenuConfig {
                    MenuSizePx = 520, CenterRadiusPx = 44,
                    Rings = Enumerable.Range(0, count).Select(i => new RingConfig {
                        Index = i, Slots = 8, Name = "圈" + (i + 1),
                        Items = new List<MenuItemConfig> {
                            new() { Slot = 7, Value = "TEST" + i, Label = "第" + (i + 1) + "圈", IconData = iconData }
                        }
                    }).ToList()
                }
            }
        };

        var config = Fixture();
        ConfigStore.Normalize(config);
        var wheel = config.Menus[SceneKeys.Global];
        check(wheel.Rings.Count == 8 && wheel.Rings.All(r => r.Enabled), "eight rings survive normalization");
        var saved = JsonCompat.Serialize(config);
        var restored = JsonCompat.Deserialize<PluginConfig>(saved);
        ConfigStore.Normalize(restored);
        check(JsonCompat.Serialize(restored) == saved, "eight rings and binding metadata survive serialization and normalization");
        foreach (var ring in wheel.Rings) ring.Enabled = ring.Index < 2;
        ConfigStore.Normalize(config);
        check(wheel.Rings.Count == 8 && wheel.Rings[7].Items[0].Value == "TEST7", "reducing visible count preserves disabled outer ring commands");
        foreach (var ring in wheel.Rings) ring.Enabled = true;
        ConfigStore.Normalize(config);
        check(wheel.Rings.Count(r => r.Enabled) == 8 && wheel.Rings[7].Items[0].IconData == iconData, "eight rings restore with icon metadata intact");

        var backup = Path.Combine(Path.GetTempPath(), "XY-EightRing-" + Guid.NewGuid().ToString("N") + ".xylpbackup");
        try {
            SettingsBackupService.Export(backup, config);
            var imported = SettingsBackupService.Import(backup);
            check(JsonCompat.Serialize(imported) == JsonCompat.Serialize(config), "eight ring backup export and import preserve configuration");
        }
        finally { if (File.Exists(backup)) File.Delete(backup); }

        var legacy = Fixture(1);
        legacy.Menus[SceneKeys.Model] = Fixture().Menus[SceneKeys.Global];
        ConfigStore.Normalize(legacy);
        check(legacy.Menus[SceneKeys.Global].Rings.Count == 8 && legacy.Menus[SceneKeys.Global].Rings[7].Items[0].Value == "TEST7",
            "legacy scene merge preserves commands in eighth ring");
        var old = Fixture(4);
        ConfigStore.Normalize(old);
        check(old.Menus[SceneKeys.Global].Rings.Count == 4, "existing four ring configuration does not gain default rings");

        foreach (var size in new[] { 260, 520, 900 }) {
            wheel.MenuSizePx = size;
            RingDiameterLayout.Normalize(wheel);
            check(wheel.Rings[0].DiameterPx > wheel.CenterRadiusPx * 2 &&
                wheel.Rings.Zip(wheel.Rings.Skip(1)).All(p => p.Second.DiameterPx > p.First.DiameterPx) &&
                wheel.Rings[7].DiameterPx == size, "eight concentric ring diameters are ordered at size " + size);
        }

        foreach (var style in new[] { "classic", "01", "orbit", "06", "11" }) {
            wheel.MenuSizePx = 520;
            wheel.StyleId = style;
            RingDiameterLayout.Normalize(wheel);
            using var control = new RadialMenuControl(Color.White) { Size = new Size(520, 520), EnableCellSelection = true };
            control.SetMenu(config, wheel);
            var selected = -1;
            control.CellSelected += (_, e) => selected = e.RingIndex;
            control.SelectCell(7, 7);
            check(selected == 7, style + " selects eighth ring last slot");
            var reached = new HashSet<int>();
            for (var y = 1; y < 520; y += 3)
            for (var x = 1; x < 520; x += 3) {
                var hit = control.HitCell(new Point(x, y));
                if (hit?.RingIndex == 7) reached.Add(hit.Slot);
            }
            check(reached.Count == 8, style + " eighth ring all eight commands remain reachable");
        }
    }
}
