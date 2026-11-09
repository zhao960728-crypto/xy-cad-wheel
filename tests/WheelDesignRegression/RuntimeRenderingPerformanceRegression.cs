using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class RuntimeRenderingPerformanceRegression
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo CardCache = typeof(RadialMenuControl).GetField("SharedCardTextLayoutCache", BindingFlags.NonPublic | BindingFlags.Static);

    internal static void Run(Action<bool, string> check, string output)
    {
        Directory.CreateDirectory(output);
        var report = new StringBuilder("style,mode,preview,cold_ms,warm_mean_ms,warm_allocated_bytes_per_frame,hits,misses,pixel_sha256\n");
        foreach (var style in new[] { "classic", "01", "06", "11", "orbit", "free" })
        foreach (var mode in new[] { "Auto", "TextOnly", "IconAndText" })
        foreach (var preview in new[] { false, true })
        {
            ClearCaches();
            var config = MakeConfig(style, mode);
            var scene = config.Menus[SceneKeys.Global];
            var original = JsonCompat.Serialize(config);
            var side = preview ? 420 : 600;
            using var control = new RadialMenuControl(Color.White)
            {
                Size = new Size(side, side), ScaleMenuForPreview = preview,
                EnableCellSelection = style == "06",
                DrawBackdrop = preview, DrawGuides = preview, DrawHover = preview
            };
            control.SetMenu(config, scene);
            if (style == "06") control.SelectCell(0, 0);
            using var bitmap = new Bitmap(side, side);
            var timer = Stopwatch.StartNew();
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var cold = timer.Elapsed.TotalMilliseconds;
            var hash = PixelHash(bitmap);
            bitmap.Save(Path.Combine(output, $"render-{style}-{mode}-{preview}.png"));
            var misses = control.TextLayoutCacheMisses;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            const int repeats = 12;
            for (var repeat = 0; repeat < repeats; repeat++) control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var elapsed = timer.Elapsed.TotalMilliseconds / repeats;
            allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / repeats;
            check(PixelHash(bitmap) == hash, $"runtime cached paint preserves every pixel: {style}/{mode}/{preview}");
            check(JsonCompat.Serialize(config) == original, $"runtime paint preserves configuration: {style}/{mode}/{preview}");
            if (CardCache != null && style != "classic")
            {
                check(control.TextLayoutCacheHits > 0 && control.TextLayoutCacheMisses == misses,
                    $"runtime repeat reuses measured card text: {style}/{mode}/{preview}");
            }
            var line = string.Join(",", style, mode, preview,
                cold.ToString("F3", CultureInfo.InvariantCulture), elapsed.ToString("F3", CultureInfo.InvariantCulture),
                allocated, control.TextLayoutCacheHits, control.TextLayoutCacheMisses, hash);
            report.AppendLine(line);
            Console.WriteLine(line);
        }
        File.WriteAllText(Path.Combine(output, "runtime-rendering-timing.csv"), report.ToString());
        VerifyCacheInputs(check);
        VerifyNavigation(check);
    }

    private static PluginConfig MakeConfig(string style, string mode)
    {
        var labels = new[] { "移动", "完整显示长命令名称与尺寸标注", "圆弧\n命令", "旋转对象 90°", "局部放大视图", "镜像", "复制图元", "延伸" };
        var commands = new[] { "MOVE", "DIM", "ARC", "ROTATE", "ZOOM", "MIRROR", "COPY", "EXTEND" };
        var config = new PluginConfig { MenuSizePx = 600, CenterRadiusPx = 44 };
        config.Menus[SceneKeys.Global] = new SceneMenuConfig
        {
            StyleId = style, ContentMode = mode, MenuSizePx = 600, CenterRadiusPx = 44,
            Rings = Enumerable.Range(0, 3).Select(r => new RingConfig
            {
                Index = r, Enabled = true, Slots = 8, Name = "命令组 " + (r + 1),
                Items = Enumerable.Range(0, 8).Select(s => new MenuItemConfig
                {
                    Slot = s, Label = labels[s], Value = commands[s], FontSizePx = s % 3 == 0 ? 48 : 0,
                    CellShape = s % 2 == 0 ? "RoundedRectangle" : "Circle",
                    TextEffect = s % 3 == 0 ? TextEffects.Outline : TextEffects.None
                }).ToList()
            }).ToList()
        };
        return config;
    }

    private static void VerifyCacheInputs(Action<bool, string> check)
    {
        var draw = typeof(RadialMenuControl).GetMethod("DrawItemCard", Private);
        using var control = new RadialMenuControl(Color.White);
        var item = new MenuItemConfig { Label = "完整命令名称与尺寸标注", FontSizePx = 48, FontFamily = "宋体", TextEffect = TextEffects.Outline };
        var bounds = new RectangleF(5, 5, 110, 80);
        float scale = 1, dpi = 96, worldScale = 1, pageScale = 1;
        GraphicsUnit unit = GraphicsUnit.Display;
        string Render()
        {
            using var image = new Bitmap(260, 220);
            image.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(image);
            graphics.PageUnit = unit;
            graphics.PageScale = pageScale;
            graphics.ScaleTransform(worldScale, worldScale);
            using var clip = new GraphicsPath(); clip.AddRectangle(bounds);
            draw.Invoke(control, new object[] { graphics, item, bounds, clip, "TextOnly", scale });
            return PixelHash(image);
        }
        foreach (var mutation in new Action[]
        {
            () => { }, () => item.Label += " A", () => item.FontSizePx = 16,
            () => item.FontFamily = "Microsoft YaHei", () => item.FontBold = true,
            () => item.TextEffect = TextEffects.WordArt, () => bounds.Width -= 17.1234f,
            () => bounds.Height -= 15.4321f, () => scale = .75f,
            () => dpi = 144, () => unit = GraphicsUnit.Pixel,
            () => worldScale = 1.25f, () => pageScale = 1.1f
        })
        {
            mutation();
            var first = Render();
            var misses = control.TextLayoutCacheMisses;
            check(Render() == first, "card layout input produces stable warm pixels " + misses);
            if (CardCache != null) check(control.TextLayoutCacheMisses == misses, "card layout exact input is cached " + misses);
            ClearCaches();
            check(Render() == first, "card layout changed input agrees with independent cold measurement " + misses);
        }
    }

    private static void VerifyNavigation(Action<bool, string> check)
    {
        var config = MakeConfig("11", "TextOnly");
        using var source = new RadialMenuControl(Color.White) { Size = new Size(600, 600), EnableCellSelection = true };
        using var target = new RadialMenuControl(Color.White) { Size = new Size(600, 600) };
        source.SetMenu(config, config.Menus[SceneKeys.Global]); target.SetMenu(config, config.Menus[SceneKeys.Global]);
        target.CreateControl();
        int invalidated = 0;
        target.Invalidated += (_, _) => invalidated++;
        source.SelectCell(0, 0); target.CopyNavigationFrom(source);
        var afterChange = invalidated;
        for (var i = 0; i < 100; i++) target.CopyNavigationFrom(source);
        if (CardCache != null) check(afterChange > 0 && invalidated == afterChange, "unchanged drag polling avoids foreground repaint");
        source.SelectCell(1, 0); target.CopyNavigationFrom(source);
        check(invalidated > afterChange, "changed group and hover still repaint foreground");
    }

    private static void ClearCaches()
    {
        foreach (var name in new[] { "SharedTextLayoutCache", "SharedCardTextLayoutCache" })
        {
            var cache = typeof(RadialMenuControl).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            cache?.GetType().GetMethod("Clear").Invoke(cache, null);
        }
    }

    private static string PixelHash(Bitmap image)
    {
        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * image.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { image.UnlockBits(data); }
    }
}
