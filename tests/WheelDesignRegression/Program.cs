using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class Program
{
    private static readonly List<string> Passed = new();
    private static readonly List<string> Skipped = new();
    private static string _output;

    [STAThread]
    private static int Main(string[] args)
    {
        _output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "XY-WheelDesign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_output);
        var hostDirectory = Environment.GetEnvironmentVariable("XY_TEST_CAD_DIRECTORY");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var assemblySearchPaths = new[] { hostDirectory,
            Path.Combine(profile, ".nuget", "packages", "autocad.net.core", "25.1.0", "lib", "net8.0"),
            Path.Combine(profile, ".nuget", "packages", "autocad.net", "25.1.0", "lib", "net8.0") };
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var path = assemblySearchPaths.Where(folder => !string.IsNullOrWhiteSpace(folder))
                    .Select(folder => Path.Combine(folder, name.Name + ".dll")).FirstOrDefault(File.Exists);
                return path != null ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
        Application.EnableVisualStyles();
        try
        {
            if (args.Contains("--runtime-performance-only"))
            {
                RuntimeRenderingPerformanceRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " runtime performance checks; " + _output);
                return 0;
            }
            if (args.Contains("--optimization-only"))
            {
                ContactPageRegression.Run(Check, _output);
                CatalogSnapshotRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " optimization checks; " + _output);
                return 0;
            }
            if (args.Contains("--numeric-commands-only"))
            {
                NumericCommandRegression.Run(Check, _output);
                var sourceIndex = Array.IndexOf(args, "--source-xlsx");
                if (sourceIndex >= 0) NumericCommandRegression.VerifyWorkbook(Check, args[sourceIndex + 1]);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " numeric command checks; " + _output);
                return 0;
            }
            if (args.Contains("--update-ui-only"))
            {
                PluginUpdateUiRegression.Run(Check, _output);
                SettingsAllPagesRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " update UI checks; " + _output);
                return 0;
            }
            if (args.Contains("--editor-workflow-only"))
            {
                EditorWorkflowRegression.Run(Check);
                SettingsAllPagesRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " editor workflow checks; " + _output);
                return 0;
            }
            if (args.Contains("--extension-startup-only"))
            {
                ExtensionStartupRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " extension startup checks; " + _output);
                return 0;
            }
            if (args.Contains("--startup-performance-only"))
            {
                SettingsStartupPerformanceRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " startup checks; " + _output);
                return 0;
            }
            if (args.Contains("--wheel-resize-only"))
            {
                WheelResizeRegression.Run(Check);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " wheel resize checks; " + _output);
                return 0;
            }
            if (args.Contains("--responsive-only"))
            {
                SettingsResponsiveRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " responsive checks; " + _output);
                return 0;
            }
            if (args.Contains("--free-editor-only"))
            {
                FreeEditorUiRegression.Run(Check, _output);
                FreeDesignerRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " free editor checks; " + _output);
                return 0;
            }
            if (args.Contains("--display-only"))
            {
                PreviewMagnifierRegression.Run(Check, _output);
                PopupScreenRegression.Run(Check);
                PopupScreenRegression.RunRendering(Check, _output);
                CardFontRegression.Run(Check);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " display checks; " + _output);
                return 0;
            }
            if (args.Contains("--cell-content-only"))
            {
                CellContentModeRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " cell content checks; " + _output);
                return 0;
            }
            if (args.Contains("--count-drag-only"))
            {
                CellEditingRegression.Run(Check);
                CountDragRegression.Run(Check);
                CellContentModeRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " count drag checks; " + _output);
                return 0;
            }
            if (args.Contains("--editor-only"))
            {
                CellEditingRegression.Run(Check);
                CountDragRegression.Run(Check);
                CellContentModeRegression.Run(Check, _output);
                CellCommandEditorRegression.Run(Check);
                CellDialogRegression.Run(Check, _output);
                SettingsLayoutRegression.Run(Check, _output);
                SettingsThemeRegression.Run(Check, _output);
                File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { passed = Passed, skipped = Skipped }));
                Console.WriteLine("PASS " + Passed.Count + " editor checks; " + _output);
                return 0;
            }
            NumericCommandRegression.Run(Check, _output);
            TestAppearance();
            PreviewMagnifierRegression.Run(Check, _output);
            PopupScreenRegression.Run(Check);
            PopupScreenRegression.RunRendering(Check, _output);
            TestLegacyAndPreservation();
            CellEditingRegression.Run(Check);
            CountDragRegression.Run(Check);
            CellContentModeRegression.Run(Check, _output);
            CellCommandEditorRegression.Run(Check);
            CellDialogRegression.Run(Check, _output);
            TestBackupIcons();
            TestHitAndNavigation();
            CaptureWheels();
            TestContentModes();
            TestNodeSpacing();
            NodeLayoutRegression.Run(Check);
            TestOrbitWheel();
            OrbitSizingRegression.Run(Check, _output);
            CardFontRegression.Run(Check);
            ClassicFontRegression.Run(Check);
            ImageModeRegression.Run(Check, _output);
            EightRingRegression.Run(Check);
            RingPresetRegression.Run(Check, _output);
            CatalogIconRegression.Run(Check, _output);
            TestPluginTheme();
            SettingsThemeRegression.Run(Check, _output);
            SettingsLayoutRegression.Run(Check, _output);
            TestSwPreviewOpacity();
            using (var appearance = new WheelAppearanceDialog(new WheelAppearance { FillMode = "Gradient" }))
            { appearance.CreateControl(); SaveControl(appearance, "appearance-dialog.png"); }
            try { CaptureSettings(); }
            catch (FileNotFoundException ex) when ((ex.FileName ?? "").StartsWith("accoremgd,", StringComparison.OrdinalIgnoreCase))
            { Skipped.Add("Settings screenshots require installed AutoCAD 2026 managed runtime: " + ex.Message); }
            if (Skipped.Count == 0) TestExtensionManagerDraft();
            File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new { assembly = typeof(WheelAppearance).Assembly.Location, passed = Passed, skipped = Skipped, output = _output }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS " + Passed.Count + " checks; " + _output);
            foreach (var skipped in Skipped) Console.WriteLine("SKIP " + skipped);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(_output, "failure.txt"), ex.ToString());
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Passed.Add(name);
    }

    private static PluginConfig Fixture()
    {
        var config = new PluginConfig();
        config.Menus[SceneKeys.Global] = new SceneMenuConfig
        {
            MenuSizePx = 520, CenterRadiusPx = 38,
            Rings = new List<RingConfig>
            {
                new RingConfig { Index = 0, Slots = 6, DiameterPx = 260, Items = Enumerable.Range(0,6).Select(i => new MenuItemConfig { Slot=i, Label=new[]{"直线","多段线","圆","圆弧","矩形","填充"}[i], Value=new[]{"LINE","PLINE","CIRCLE","ARC","RECTANG","HATCH"}[i]}).ToList() },
                new RingConfig { Index = 1, Slots = 8, DiameterPx = 520, Items = Enumerable.Range(0,8).Select(i => new MenuItemConfig { Slot=i, Label=new[]{"移动","复制","旋转","镜像","偏移","修剪","延伸","删除"}[i], Value=new[]{"MOVE","COPY","ROTATE","MIRROR","OFFSET","TRIM","EXTEND","ERASE"}[i]}).ToList() }
            }
        };
        ConfigStore.Normalize(config);
        return config;
    }

    private static void TestAppearance()
    {
        foreach (var mode in new[] { "Solid", "Gradient" })
        {
            var source = new WheelAppearance { FillMode = mode, StartColor = "#203040", EndColor = "#BBDDFF", GradientAngle = 135, BorderColor = "#123456", HoverColor = "#00AABB", SelectedColor = "#AABB00" };
            var restored = JsonCompat.Deserialize<WheelAppearance>(JsonCompat.Serialize(source));
            Check(JsonCompat.Serialize(source) == JsonCompat.Serialize(restored), mode + " appearance roundtrip");
            using var brush = restored.CreateBrush(new RectangleF(0, 0, 160, 120));
            Check(mode == "Solid" ? brush is SolidBrush : brush is LinearGradientBrush, mode + " brush kind");
        }
        var bad = new WheelAppearance { FillMode = "broken", StartColor = "#oops", EndColor = null, GradientAngle = -450, BorderColor = "", HoverColor = "#xyzxyz", SelectedColor = "#112233" };
        bad.Normalize();
        Check(bad.FillMode == "Solid" && bad.StartColor == "#FFFFFF" && bad.EndColor == "#DDF4F5" && bad.GradientAngle == 270 && bad.BorderColor == "#D7DFE6" && bad.HoverColor == "#008CA2" && bad.SelectedColor == "#112233", "invalid appearance defaults");
        var clone = bad.Clone(); clone.StartColor = "#000000";
        Check(bad.StartColor == "#FFFFFF", "appearance drafts isolated");
    }

    private static void TestLegacyAndPreservation()
    {
        var legacy = JsonCompat.Deserialize<PluginConfig>("{\"Menus\":{\"Global\":{\"Rings\":[{\"Slots\":8,\"Items\":[{\"Slot\":0,\"Label\":\"直线\",\"Value\":\"LINE\"}]}]}}}");
        ConfigStore.Normalize(legacy);
        Check(legacy.Menus[SceneKeys.Global].StyleId == "classic" && legacy.Menus[SceneKeys.Global].Appearance.FillMode == "Solid", "legacy defaults to original style and solid appearance");
        var config = Fixture();
        var wheel = config.Menus[SceneKeys.Global];
        var item = wheel.Rings[0].Items.Last();
        item.FontFamily = "宋体"; item.FontBold = true; item.FontSizePx = 17; item.TextColor = "#123456"; item.IconData = IconData();
        var expected = JsonCompat.Serialize(item);
        wheel.Rings[0].Slots = 3;
        ConfigStore.Normalize(config);
        Check(wheel.UnassignedItems.Count == 3 && JsonCompat.Serialize(wheel.UnassignedItems.Single(x => x.Value == item.Value)) == expected, "capacity decrease retains binding font color and icon in unassigned pool");
        ConfigStore.Normalize(config);
        Check(wheel.UnassignedItems.Count == 3, "repeated normalization does not duplicate unassigned bindings");
        var bindings = JsonCompat.Serialize(wheel.Rings);
        foreach (var style in new[] { "classic", "01", "11", "06", "classic" })
        {
            wheel.StyleId = style; ConfigStore.Normalize(config);
            var restored = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));
            ConfigStore.Normalize(restored);
            Check(JsonCompat.Serialize(restored.Menus[SceneKeys.Global].Rings) == bindings, style + " preserves bindings through serialization");
        }
    }

    private static string IconData()
    {
        using var bitmap = new Bitmap(48, 24);
        using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); using var pen = new Pen(Color.RoyalBlue, 3); g.DrawRectangle(pen, 3, 3, 40, 17); }
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static void TestBackupIcons()
    {
        var config = Fixture(); var wheel = config.Menus[SceneKeys.Global];
        wheel.BackgroundImagePath = ""; // No runtime-directory background extraction; icons live within configuration.
        wheel.Rings[0].Items[0].IconData = IconData();
        wheel.UnassignedItems.Add(new MenuItemConfig { Label = "待分配", Value = "ZOOM", IconData = IconData() });
        wheel.Appearance.FillMode = "Gradient"; wheel.Appearance.StartColor = "#ECF7FF";
        var path = Path.Combine(_output, "icons.xylpbackup");
        SettingsBackupService.Export(path, config);
        var restored = SettingsBackupService.Import(path).Menus[SceneKeys.Global];
        Check(restored.Rings[0].Items[0].IconData == wheel.Rings[0].Items[0].IconData && restored.UnassignedItems[0].IconData == wheel.UnassignedItems[0].IconData && restored.Appearance.StartColor == "#ECF7FF", "backup roundtrip embeds assigned and unassigned icons with appearance");
        using var bytes = new MemoryStream(Convert.FromBase64String(restored.Rings[0].Items[0].IconData));
        using var image = Image.FromStream(bytes);
        Check(image.Width == 48 && image.Height == 24, "backup retains icon image dimensions");
    }

    private static void CaptureWheels()
    {
        var config = Fixture(); var wheel = config.Menus[SceneKeys.Global]; wheel.Rings[0].Items[0].IconData = IconData();
        foreach (var style in new[] { "classic", "01", "11", "06" })
        {
            wheel.StyleId = style;
            using var control = new RadialMenuControl(Color.White) { Size = new Size(620, 620), ScaleMenuForPreview = true, EnableCellSelection = true };
            control.SetMenu(config, wheel); control.CreateControl();
            SaveControl(control, "wheel-" + style + ".png");
            control.SelectCell(0, 0);
            SaveControl(control, "wheel-" + style + "-selected.png");
            Check(true, style + " offscreen drawing and selection");
        }
    }

    private static void TestContentModes()
    {
        foreach (var style in new[] { "01", "06", "11" })
        foreach (var mode in new[] { "IconOnly", "TextOnly", "IconAndText" })
        {
            var config = Fixture(); var wheel = config.Menus[SceneKeys.Global];
            config.FontSizePx = 8; config.FontFamily = "宋体"; config.TextEffect = "outline";
            wheel.StyleId = style; wheel.ContentMode = mode;
            foreach (var ring in wheel.Rings) foreach (var item in ring.Items) item.IconData = IconData();
            var restored = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));
            ConfigStore.Normalize(restored);
            Check(restored.Menus[SceneKeys.Global].ContentMode == mode, style + " " + mode + " persisted");
            using var control = new RadialMenuControl(Color.White) { Size = new Size(600, 600) };
            control.SetMenu(config, wheel); control.CreateControl();
            if (style == "06") control.UpdateCursor(new Point(300, 155));
            using var bitmap = new Bitmap(600, 600);
            control.DrawToBitmap(bitmap, new Rectangle(0, 0, 600, 600));
            bitmap.Save(Path.Combine(_output, "content-" + style + "-" + mode + ".png"));
            var dark = 0; var blue = 0;
            for (var y = 0; y < 600; y++) for (var x = 0; x < 600; x++)
            {
                if (Math.Abs(x - 300) < 45 && Math.Abs(y - 300) < 45) continue;
                var p = bitmap.GetPixel(x, y);
                if (p.R < 75 && p.G < 75 && p.B < 85) dark++;
                if (p.B > 160 && p.R < 110 && p.G < 155) blue++;
            }
            Check(mode == "TextOnly" ? blue == 0 : blue > 100, style + " " + mode + " image visibility");
            if (mode != "IconOnly") Check(dark > 250, style + " " + mode + " readable text pixels actually rendered");
            Check(config.FontSizePx == 8 && config.TextEffect == "outline", style + " keeps legacy font configuration");
        }
    }

    private static void TestNodeSpacing()
    {
        foreach (var size in new[] { 260, 600, 900 })
        foreach (var count in new[] { 1, 8, 12, 13, 20, 32 })
        foreach (var percent in new[] { 50, 100, 150 })
        {
            var config = Fixture(); var wheel = config.Menus[SceneKeys.Global];
            wheel.StyleId = "06"; wheel.MenuSizePx = size;
            var ring = wheel.Rings[0]; ring.Slots = count;
            ring.NodeInnerSizePercent = percent; ring.NodeOuterSizePercent = percent;
            using var control = new RadialMenuControl(Color.White) { Size = new Size(size, size), EnableCellSelection = true };
            control.SetMenu(config, wheel); control.CreateControl(); control.SelectCell(0, 0);
            var bounds = typeof(RadialMenuControl).GetMethod("GetMenuBounds", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, null);
            var cells = (System.Collections.IEnumerable)typeof(RadialMenuControl).GetMethod("BuildStyleCells", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new[] { bounds });
            var circles = cells.Cast<object>().Select(cell => (RectangleF)cell.GetType().GetField("Bounds").GetValue(cell)).ToArray();
            var minGap = float.MaxValue;
            for (var i = 0; i < circles.Length; i++) for (var j = i + 1; j < circles.Length; j++)
            {
                var a = circles[i]; var b = circles[j];
                var dx = a.X + a.Width / 2 - b.X - b.Width / 2;
                var dy = a.Y + a.Height / 2 - b.Y - b.Height / 2;
                minGap = Math.Min(minGap, (float)Math.Sqrt(dx * dx + dy * dy) - (a.Width + b.Width) / 2);
            }
            Check(circles.Length == count + 1 && minGap >= 7.9f, $"node safe spacing size={size} slots={count} percent={percent} gap={minGap}");
            var orbitRadii = new List<double>();
            foreach (var r in circles.Take(count))
            {
                var distance = Math.Sqrt(Math.Pow(r.X + r.Width / 2 - size / 2f, 2) + Math.Pow(r.Y + r.Height / 2 - size / 2f, 2));
                if (!orbitRadii.Any(existing => Math.Abs(existing - distance) < .25)) orbitRadii.Add(distance);
            }
            var orbitCount = orbitRadii.Count;
            Check(orbitCount >= 1 && orbitCount <= 3, $"node uses at most three concentric circles for {count} commands");
            if (size == 600 && count == 20 && percent == 100) Check(orbitCount >= 2, "20 commands use more rows to retain readable default button size");
            if (size == 600 && count == 32 && percent == 150) Check(orbitCount == 3, "larger equal buttons extend into a third circle");
            Check(circles.Take(count).Select(r => Math.Round(r.Width, 2)).Distinct().Count() == 1, "node buttons have equal size across visual orbits " + count);
            if (size == 600 && count == 32) SaveControl(control, "node-size-" + percent + ".png");
            if (size == 600 && count == 20 && percent == 100) SaveControl(control, "node-single-20.png");
        }
        var defaults = JsonCompat.Deserialize<RingConfig>("{\"Slots\":8}");
        Check(defaults.NodeInnerSizePercent == 100 && defaults.NodeOuterSizePercent == 100, "legacy node size default preserves configuration");
    }

    private static void TestOrbitWheel()
    {
        var config = Fixture(); var wheel = config.Menus[SceneKeys.Global]; wheel.StyleId = "orbit";
        ConfigStore.Normalize(config);
        Check(wheel.StyleId == "orbit" && wheel.ContentMode == "Auto", "orbit and automatic content survive normalization");
        using var control = new RadialMenuControl(Color.White) { Size = new Size(600, 600), EnableCellSelection = true };
        control.SetMenu(config, wheel); control.CreateControl();
        var bounds = typeof(RadialMenuControl).GetMethod("GetMenuBounds", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, null);
        var raw = (System.Collections.IEnumerable)typeof(RadialMenuControl).GetMethod("BuildStyleCells", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new[] { bounds });
        var cells = raw.Cast<object>().ToArray();
        Check(cells.Length == 15, "orbit shows both rings and center without entering a group");
        for (var i = 0; i < cells.Length - 1; i++)
        {
            var cell = cells[i]; var rect = (RectangleF)cell.GetType().GetField("Bounds").GetValue(cell);
            var hit = control.HitCell(Point.Round(new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
            Check(hit != null && hit.RingIndex == (i < 6 ? 0 : 1) && hit.Slot == (i < 6 ? i : i - 6), "orbit direct slot " + i);
            for (var j = i + 1; j < cells.Length; j++)
            {
                var other = (RectangleF)cells[j].GetType().GetField("Bounds").GetValue(cells[j]);
                var dx = rect.X + rect.Width / 2 - other.X - other.Width / 2;
                var dy = rect.Y + rect.Height / 2 - other.Y - other.Height / 2;
                Check(Math.Sqrt(dx * dx + dy * dy) - (rect.Width + other.Width) / 2 >= 7.9, "orbit spacing " + i + "/" + j);
            }
        }
        SaveControl(control, "orbit-all-rings.png");
    }

    private static void TestPluginTheme()
    {
        var config = Fixture();
        Check(!config.PluginAppearanceEnabled, "legacy plugin theme stays disabled");
        config.PluginAppearanceEnabled = true;
        config.PluginAppearance = new WheelAppearance { FillMode = "Gradient", StartColor = "#142232", EndColor = "#405B73", SelectedColor = "#FFBB55" };
        var oldWheel = JsonCompat.Serialize(config.Menus);
        var restored = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));
        ConfigStore.Normalize(restored);
        Check(restored.PluginAppearanceEnabled && restored.PluginAppearance.StartColor == "#142232", "plugin theme persists independently");
        var menuRequests = 0;
        using var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => throw new InvalidOperationException("Theme preview must not save"), loadCatalog:false, requestMenuRestore: () => menuRequests++);
        settings.Size = new Size(1380, 900); settings.CreateControl();
        typeof(SettingsWindow).GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] { 0 });
        SaveControl(settings, "plugin-interface-theme.png");
        var restore = Descendants(settings).OfType<Button>().Single(b => b.Text == "显示 CAD 菜单栏并恢复小阳菜单");
        Check(menuRequests == 0, "opening settings never changes MENUBAR");
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(restore, new object[] { EventArgs.Empty });
        Check(menuRequests == 1, "explicit restore menu button invokes host repair exactly once");
        Check(JsonCompat.Serialize(config.Menus) == oldWheel, "plugin theme never changes wheel colors or settings");
        var draft = (PluginConfig)typeof(SettingsWindow).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        draft.PluginAppearanceEnabled = false;
        typeof(SettingsWindow).GetMethod("ApplyPluginAppearance", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, null);
        var nav = (Dictionary<int, Button>)typeof(SettingsWindow).GetField("_navButtons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        Check(nav[0].BackColor == Color.FromArgb(0, 104, 125), "restoring plugin theme keeps current navigation selected");
    }

    private static void TestHitAndNavigation()
    {
        foreach (var style in new[] { "classic", "01", "06", "11" })
        {
            var config = Fixture(); var wheel = config.Menus[SceneKeys.Global]; wheel.StyleId = style;
            var group = wheel.Rings[0]; group.Slots = 32;
            group.Items = Enumerable.Range(0, 32).Select(i => new MenuItemConfig { Slot = i, Label = "命令 " + i, Value = "TEST" + i }).ToList();
            using var control = new RadialMenuControl(Color.White) { Size = new Size(600, 600) };
            control.SetMenu(config, wheel);
            MenuItemConfig selected = null; var cancellations = 0;
            control.ItemSelected += (_, item) => selected = item;
            control.CancelRequested += (_, _) => cancellations++;
            Check(control.HitCell(new Point(-1, -1)) == null, style + " outside is not a command");
            if (style == "classic" || style == "01")
            {
                var hits = new Dictionary<int, Point>();
                for (var y = 0; y < 600; y += 5)
                    for (var x = 0; x < 600; x += 5)
                    {
                        var hit = control.HitCell(new Point(x, y));
                        if (hit != null && hit.RingIndex == 0) hits[hit.Slot] = new Point(x, y);
                    }
                Check(hits.Count == 32, style + " all 32 slots remain reachable");
                control.CommitAt(hits[31]); Check(selected?.Value == "TEST31", style + " last slot executes its binding");
                control.CommitAt(new Point(300, 300)); Check(cancellations == 1, style + " center cancels");
                continue;
            }
            if (style == "06")
            {
                control.UpdateCursor(new Point(300, 132));
                Check(selected == null, "node group navigation never executes command");
            }
            var cells = StyleCells(control);
            Check(cells.Count == 32, style + " all 32 commands visible without paging");
            foreach (var entry in cells)
            {
                AssertHit(control, entry.Value, 0, entry.Key, style + " visible slot " + entry.Key);
                control.CommitAt(entry.Value);
                Check(selected?.Value == "TEST" + entry.Key, style + " executes slot " + entry.Key);
            }
            using var foreground = new RadialMenuControl(Color.White) { Size = control.Size, DrawBackdrop = false };
            foreground.SetMenu(config, wheel); foreground.CopyNavigationFrom(control);
            AssertHit(foreground, cells[31], 0, 31, style + " foreground last slot matches without paging");
            control.EnableCellSelection = true; control.SelectCell(0, 31);
            AssertHit(control, cells[31], 0, 31, style + " editor selection keeps all slots visible");
            SaveControl(control, "wheel-" + style + "-all32.png");
            if (style == "06")
            {
                control.CommitAt(new Point(300, 300));
                Check(StyleCells(control).Count == 0 && cancellations == 0, "node center returns to groups");
            }
        }
    }

    private static Dictionary<int, Point> StyleCells(RadialMenuControl control)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var bounds = (RectangleF)typeof(RadialMenuControl).GetMethod("GetMenuBounds", flags).Invoke(control, null);
        var raw = (System.Collections.IEnumerable)typeof(RadialMenuControl).GetMethod("BuildStyleCells", flags).Invoke(control, new object[] { bounds });
        var result = new Dictionary<int, Point>();
        foreach (var cell in raw)
        {
            var type = cell.GetType();
            var label = (string)type.GetField("Label").GetValue(cell);
            Check(!label.Contains("上一页") && !label.Contains("下一页"), "no pagination controls");
            var hit = type.GetField("Hit").GetValue(cell);
            var slot = (int)hit.GetType().GetProperty("Slot").GetValue(hit);
            if (slot < 0) continue;
            var rect = (RectangleF)type.GetField("Bounds").GetValue(cell);
            result[slot] = Point.Round(new PointF(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
        }
        return result;
    }
    private static void AssertHit(RadialMenuControl control, Point point, int ring, int slot, string name)
    {
        var hit = control.HitCell(point);
        Check(hit != null && hit.RingIndex == ring && hit.Slot == slot, name + " at " + point);
    }

    private static void CaptureSettings()
    {
        using var settings = new SettingsWindow(Fixture(), new CadCommandCatalog(), _ => throw new InvalidOperationException("Test must not save runtime settings"), loadCatalog: false);
        settings.Size = new Size(1380, 900);
        settings.CreateControl();
        var pages = (TabControl)typeof(SettingsWindow).GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        for (var i = 0; i < pages.TabPages.Count; i++)
        {
            typeof(SettingsWindow).GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] { i }); settings.PerformLayout();
            SaveControl(settings, "settings-page-" + i + ".png");
        }
        pages.SelectedIndex = 3;
        var styles = (ComboBox)typeof(SettingsWindow).GetField("_wheelStyle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        for (var i = 0; i < styles.Items.Count; i++)
        {
            styles.SelectedIndex = i;
            settings.PerformLayout();
            SaveControl(settings, "settings-style-" + i + ".png");
        }
        styles.SelectedIndex = 3;
        CaptureWheelEditorSections(settings, pages, "wide");
        var ringEditors = (TableLayoutPanel)typeof(SettingsWindow).GetField("_ringEditors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        var innerSize = (NumericUpDown)ringEditors.GetControlFromPosition(6, 0);
        innerSize.Value = 75;
        var preview = (RadialMenuControl)typeof(SettingsWindow).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        Check(StyleCells(preview).Count == 6, "node size edit keeps selected group expanded in preview");
        var draft = (PluginConfig)typeof(SettingsWindow).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        Check(draft.Menus[SceneKeys.Global].Rings[0].NodeInnerSizePercent == 75, "node size editor updates selected group draft");
        Check(((Label)ringEditors.GetControlFromPosition(5, 0)).Text == "当圈格子大小 %", "per-ring size caption explains scope");
        innerSize.Text = "120";
        Check(draft.Menus[SceneKeys.Global].Rings[0].NodeInnerSizePercent == 120, "typed node size updates draft before focus leaves");
        Check(StyleCells(preview).Count == 6, "typed node size retains expanded preview");
        Check(draft.Menus[SceneKeys.Global].Rings[1].NodeInnerSizePercent == 100, "editing current ring leaves other ring size unchanged");
        SaveControl(settings, "settings-node-size-preview.png");
        styles.SelectedIndex = 4;
        var orbitSize = (NumericUpDown)ringEditors.GetControlFromPosition(6, 0);
        orbitSize.Text = "80";
        Check(draft.Menus[SceneKeys.Global].Rings[0].NodeInnerSizePercent == 80, "typed independent-cell size updates before focus leaves");
        Check(draft.Menus[SceneKeys.Global].Rings[1].NodeInnerSizePercent == 100, "independent-cell edit leaves other rings unchanged");
        var previewBounds = typeof(RadialMenuControl).GetMethod("GetMenuBounds", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(preview, null);
        var orbitCells = ((System.Collections.IEnumerable)typeof(RadialMenuControl).GetMethod("BuildStyleCells", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(preview, new[] { previewBounds })).Cast<object>().ToArray();
        Check(orbitCells.Length == 15, "independent-cell preview keeps both rings and center visible after typing");
        SaveControl(settings, "settings-orbit-size-preview.png");
        styles.SelectedIndex = 3;
        settings.Size = new Size(980, 620);
        for (var i = 0; i < pages.TabPages.Count; i++)
        {
            typeof(SettingsWindow).GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] { i }); settings.PerformLayout();
            SaveControl(settings, "settings-compact-page-" + i + ".png");
        }
        CaptureWheelEditorSections(settings, pages, "compact");
        using (var ime = new ImeSettingsDialog(new ImeSwitchConfig(), _ => throw new InvalidOperationException("IME preview must not save")))
            SaveControl(ime, "ime-settings.png");
        Check(true, "settings and color dialog offscreen drawing without catalog reads or saving");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void CaptureWheelEditorSections(SettingsWindow settings, TabControl pages, string size)
    {
        typeof(SettingsWindow).GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] { 3 });
        var sections = Descendants(pages.TabPages[3]).OfType<TabControl>().FirstOrDefault();
        if (sections == null) return;
        for (var section = 0; section < sections.TabPages.Count; section++)
        {
            sections.SelectedIndex = section;
            SaveControl(settings, "wheel-editor-" + size + "-section-" + section + ".png");
        }
        var cellSections = Descendants(sections.TabPages[2]).OfType<TabControl>().FirstOrDefault();
        if (cellSections != null)
        {
            for (var section = 0; section < cellSections.TabPages.Count; section++)
            { cellSections.SelectedIndex = section; SaveControl(settings, "wheel-cell-editor-" + size + "-" + section + ".png"); }
        }
    }

    private static void TestSwPreviewOpacity()
    {
        var config = Fixture();
        var wheel = config.Menus[SceneKeys.Global];
        wheel.StyleId = "01";
        using var control = new RadialMenuControl(Color.FromArgb(43, 48, 56)) { Size = new Size(620, 620), ScaleMenuForPreview = true };
        control.SetMenu(config, wheel);
        using var low = new Bitmap(620, 620);
        using var high = new Bitmap(620, 620);
        wheel.SurfaceOpacityPercent = 20;
        control.DrawToBitmap(low, control.ClientRectangle);
        wheel.SurfaceOpacityPercent = 90;
        control.DrawToBitmap(high, control.ClientRectangle);
        var changed = 0; var stableInk = 0;
        for (var y = 0; y < 620; y++)
            for (var x = 0; x < 620; x++)
            {
                var a = low.GetPixel(x, y); var b = high.GetPixel(x, y);
                if (a != b) changed++;
                if (a == b && a.B > a.R + 40 && a.B > a.G + 10) stableInk++;
            }
        Check(changed > 10000 && stableInk > 50, "SW preview changes background opacity while retaining opaque icon ink");
        Check(control.DrawBackdrop && control.DrawLabels && control.DrawForeground, "SW preview restores layer flags after composing");
        high.Save(Path.Combine(_output, "sw-preview-opacity90.png"), ImageFormat.Png);
        low.Save(Path.Combine(_output, "sw-preview-opacity20.png"), ImageFormat.Png);
    }

    private static void TestExtensionManagerDraft()
    {
        var directory = Path.Combine(_output, "external-user-files", "包含依赖的原始扩展目录");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "测试扩展保留原文件.lsp");
        File.WriteAllText(path, "; temporary fixture only, never loaded\n");
        var original = Fixture(); var saves = 0; PluginConfig applied = null;
        var originalSnapshot = JsonCompat.Serialize(original);
        using var settings = new SettingsWindow(original, new CadCommandCatalog(), saved => { saves++; applied = saved; }, loadCatalog: false);
        var config = (PluginConfig)typeof(SettingsWindow).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        var add = typeof(SettingsWindow).GetMethod("AddManagedExtensions", BindingFlags.Instance | BindingFlags.NonPublic);
        add.Invoke(settings, new object[] { new[] { path } });
        Check(config.ManagedExtensions.Count == 1 && !config.ManagedExtensions[0].Enabled && config.ManagedExtensions[0].FilePath == path && saves == 0, "adding extension defaults disabled and remains an unsaved draft");
        var grid = (DataGridView)typeof(SettingsWindow).GetField("_managedExtensionGrid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        grid.Rows[0].Cells[0].Value = true;
        Check(config.ManagedExtensions[0].Enabled && saves == 0, "extension checkbox edits draft without immediate save or loading");
        Check(JsonCompat.Serialize(original) == originalSnapshot, "unsaved draft is isolated from caller configuration");
        var ordinarySave = (Action<PluginConfig>)typeof(SettingsWindow).GetField("_saved", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        ordinarySave(config);
        Check(saves == 1 && applied.ManagedExtensions.Count == 0, "ordinary command save does not apply pending extensions");
        typeof(SettingsWindow).GetMethod("SaveAllPendingSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, null);
        Check(saves == 2 && applied.ManagedExtensions.Count == 1 && applied.ManagedExtensions[0].Enabled, "explicit apply saves enabled extension draft");
        grid.Rows[0].Cells[0].Value = false;
        Check(applied.ManagedExtensions[0].Enabled, "edits after saving do not mutate runtime configuration");
        ordinarySave(config);
        Check(saves == 3 && applied.ManagedExtensions[0].Enabled, "ordinary save retains last explicitly applied extension state");
        var pages = (TabControl)typeof(SettingsWindow).GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
        typeof(SettingsWindow).GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] { pages.TabPages.Count - 1 });
        settings.Size = new Size(980, 620);
        SaveControl(settings, "settings-extension-with-file.png");
        grid.ClearSelection(); grid.Rows[0].Selected = true;
        typeof(SettingsWindow).GetMethod("RemoveManagedExtensions", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, null);
        Check(config.ManagedExtensions.Count == 0 && File.Exists(path) && saves == 3, "uninstall removes draft registration only and preserves original file");
        settings.Dispose();
        Check(JsonCompat.Serialize(original) == originalSnapshot, "discarding settings never changes original configuration object");
    }

    private static void SaveControl(Control control, string name)
    {
        var form = control as Form;
        var wasVisible = control.Visible;
        if (form != null && !wasVisible)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
        }
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        bitmap.Save(Path.Combine(_output, name), ImageFormat.Png);
        if (name == "settings-compact-page-2.png" || name == "settings-page-0.png")
        {
            var lines = new List<string>();
            void Describe(Control current, int depth)
            {
                lines.Add(new string(' ', depth) + current.GetType().Name + " " + current.Text + " " + current.Bounds + " visible=" + current.Visible + " dock=" + current.Dock);
                foreach (Control child in current.Controls) Describe(child, depth + 1);
            }
            Describe(control, 0); File.WriteAllLines(Path.Combine(_output, name + ".layout.txt"), lines);
        }
        if (form != null && !wasVisible) form.Hide();
    }
}
