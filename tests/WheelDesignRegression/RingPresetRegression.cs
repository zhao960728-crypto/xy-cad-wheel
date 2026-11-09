using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class RingPresetRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        check(WheelRingPresets.ForStyle("classic").Select(x => string.Join("+", x)).SequenceEqual(new[] { "8+8", "8+8+16", "8+8+16+16" }), "normal wheel has exactly three requested presets");
        check(WheelRingPresets.ForStyle("orbit").Length == 0, "independent wheel has no recommended presets");
        check(WheelRingPresets.ForStyle("06").Length == 0, "node wheel has no recommended presets");
        check(WheelRingPresets.ForStyle("11").Length == 0, "group panel has no recommended presets");
        foreach (var style in new[] { "classic", "01", "11", "06", "orbit" })
        foreach (var preset in WheelRingPresets.ForStyle(style))
        {
            var scene = new SceneMenuConfig { StyleId = style, MenuSizePx = 520, CenterRadiusPx = 44,
                Rings = Enumerable.Range(0, 8).Select(i => new RingConfig { Index = i, Slots = 32, Name = "圈" + i,
                    NodeInnerSizePercent = 115, Items = Enumerable.Range(0, 32).Select(s => new MenuItemConfig { Slot = s, Label = "命令" + i + ":" + s, Value = "CMD" + i + ":" + s }).ToList() }).ToList() };
            var allBindings = scene.Rings.SelectMany(r => r.Items).Select(x => JsonCompat.Serialize(x)).OrderBy(x => x).ToArray();
            WheelRingPresets.Apply(scene, preset);
            check(scene.Rings.Where(r => r.Enabled).Select(r => r.Slots).SequenceEqual(preset), style + " preset applies ring capacities");
            check(scene.Rings.SelectMany(r => r.Items).Concat(scene.UnassignedItems).Select(x => JsonCompat.Serialize(x)).OrderBy(x => x).SequenceEqual(allBindings), style + " preset preserves all 256 bindings");
            check(scene.Rings.All(r => r.NodeInnerSizePercent == 115) && scene.Rings[7].Items.Count == 32, style + " preset preserves appearance and hidden outer rings");
            var json = JsonCompat.Serialize(scene);
            WheelRingPresets.Apply(scene, preset);
            check(JsonCompat.Serialize(scene) == json, style + " repeated preset does not duplicate unassigned commands");
        }
        var config = new PluginConfig(); ConfigStore.Normalize(config);
        foreach (var style in new[] { "classic", "01" })
        {
            var used = new SceneMenuConfig { StyleId = style, MenuSizePx = 520, CenterRadiusPx = 44,
                Rings = Enumerable.Range(0, 6).Select(i => new RingConfig { Index = i, Enabled = true, SizeWeight = 1 }).ToList() };
            RingDiameterLayout.Normalize(used);
            check(used.Rings.Select(r => r.DiameterPx).SequenceEqual(new[] { 160, 232, 304, 376, 448, 520 }), "reproduce existing six-ring geometry");
            WheelRingPresets.Apply(used, new[] { 8, 8, 16 });
            check(used.Rings.Where(r => r.Enabled).Select(r => r.DiameterPx).SequenceEqual(new[] { 232, 376, 520 }), style + " three-ring recommendation redistributes existing diameters");
        }
        config.Menus[SceneKeys.Global].StyleId = "01";
        var original = JsonCompat.Serialize(config);
        using (var canceled = new WheelRingPresetDialog(config, new[] { 8, 8, 16 }))
        {
            check(JsonCompat.Serialize(config) == original, "opening preset preview does not change source");
            canceled.DialogResult = DialogResult.Cancel;
        }
        check(JsonCompat.Serialize(config) == original, "canceling preset preview leaves original settings intact");
        using var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => throw new InvalidOperationException("Preset test must not save"), loadCatalog: false);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(SettingsWindow).GetMethod("ApplyRingPreset", flags).Invoke(settings, new object[] { new[] { 8, 8, 16 } });
        var draft = (PluginConfig)typeof(SettingsWindow).GetField("_config", flags).GetValue(settings);
        var count = (NumericUpDown)typeof(SettingsWindow).GetField("_ringCount", flags).GetValue(settings);
        var preview = (RadialMenuControl)typeof(SettingsWindow).GetField("_preview", flags).GetValue(settings);
        check(count.Value == 3 && count.Maximum == 8 && draft.Menus[SceneKeys.Global].Rings.Where(r => r.Enabled).Select(r => r.Slots).SequenceEqual(new[] { 8, 8, 16 }), "preset UI synchronizes counts while retaining manual eight-ring limit");
        var previewScene = (SceneMenuConfig)typeof(RadialMenuControl).GetField("_scene", flags).GetValue(preview);
        check(previewScene.Rings.Where(r => r.Enabled).Select(r => r.Slots).SequenceEqual(new[] { 8, 8, 16 }), "confirmed preset immediately updates preview capacities");
        check(JsonCompat.Serialize(config) == original, "preset editing leaves original config unchanged until save");
        var stylePicker = (ComboBox)typeof(SettingsWindow).GetField("_wheelStyle", flags).GetValue(settings);
        stylePicker.SelectedIndex = 0;
        var stable = JsonCompat.Serialize(draft);
        foreach (var other in new[] { 1, 2, 3, 4 })
        {
            stylePicker.SelectedIndex = other;
            stylePicker.SelectedIndex = 0;
            check(JsonCompat.Serialize(draft) == stable, "style round trip " + other + " preserves complete settings");
            check(ReferenceEquals(previewScene, draft.Menus[SceneKeys.Global]), "style round trip retains synchronized preview scene");
        }
        settings.Size = new Size(1480, 900);
        typeof(SettingsWindow).GetMethod("SelectPage", flags).Invoke(settings, new object[] { 3 });
        settings.StartPosition = FormStartPosition.Manual; settings.Location = new Point(-20000, -20000); settings.ShowInTaskbar = false;
        settings.Show(); Application.DoEvents();
        using (var beforeSwitch = new Bitmap(preview.Width, preview.Height))
        {
            preview.DrawToBitmap(beforeSwitch, new Rectangle(Point.Empty, preview.Size));
            foreach (var other in new[] { 1, 2, 3, 4 })
            {
                stylePicker.SelectedIndex = other;
                Application.DoEvents();
                stylePicker.SelectedIndex = 0;
                Application.DoEvents();
                using var afterSwitch = new Bitmap(preview.Width, preview.Height);
                preview.DrawToBitmap(afterSwitch, new Rectangle(Point.Empty, preview.Size));
                using var beforeBytes = new MemoryStream();
                using var afterBytes = new MemoryStream();
                beforeSwitch.Save(beforeBytes, System.Drawing.Imaging.ImageFormat.Png);
                afterSwitch.Save(afterBytes, System.Drawing.Imaging.ImageFormat.Png);
                check(beforeBytes.ToArray().SequenceEqual(afterBytes.ToArray()), "visible style round trip " + other + " preserves rendered layout");
            }
        }
        using var image = new Bitmap(settings.Width, settings.Height);
        settings.DrawToBitmap(image, new Rectangle(Point.Empty, settings.Size));
        image.Save(Path.Combine(output, "ring-preset-8-8-16.png"));
        draft.Menus[SceneKeys.Global].StyleId = "orbit";
        typeof(SettingsWindow).GetMethod("UpdateStyleEditors", flags).Invoke(settings, null);
        var presetButton = (Button)typeof(SettingsWindow).GetField("_ringPresetButton", flags).GetValue(settings);
        check(!presetButton.Visible, "independent wheel hides recommendation entry");
        draft.Menus[SceneKeys.Global].StyleId = "06";
        typeof(SettingsWindow).GetMethod("UpdateStyleEditors", flags).Invoke(settings, null);
        check(!presetButton.Visible, "node wheel hides recommendation entry");
        draft.Menus[SceneKeys.Global].StyleId = "11";
        typeof(SettingsWindow).GetMethod("UpdateStyleEditors", flags).Invoke(settings, null);
        check(!presetButton.Visible, "group panel hides recommendation entry");
        settings.Hide();
        typeof(SettingsWindow).GetMethod("SelectPage", flags).Invoke(settings, new object[] { 2 });
        settings.Show(); Application.DoEvents();
        foreach (var field in new[] { "_customName", "_commandName" })
        {
            var input = (TextBox)typeof(SettingsWindow).GetField(field, flags).GetValue(settings);
            input.Text = "SUN-173";
            input.Height = 1;
            check(input.Height >= input.PreferredHeight && input.ClientSize.Height >= input.Font.Height, field + " has full input height");
            using var largerFont = new Font(input.Font.FontFamily, 14);
            var originalFont = input.Font;
            input.Font = largerFont;
            check(input.MinimumSize.Height >= input.PreferredHeight, field + " follows changed font metrics");
            input.Font = originalFont;
        }
        using var inputImage = new Bitmap(settings.Width, settings.Height);
        settings.DrawToBitmap(inputImage, new Rectangle(Point.Empty, settings.Size));
        inputImage.Save(Path.Combine(output, "custom-inputs.png"));
        settings.Hide();
    }
}
