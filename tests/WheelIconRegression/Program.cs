using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiaoYang.AutoCADPlugin;

internal static class Program
{
    private sealed class CaptureHost : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            var output = Path.GetFullPath(args.Length > 0 ? args[0] : "work/wheel-icons");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.Combine(output, "png"));
            var entries = WheelIconCatalog.Entries;
            Check(entries.Count >= 1288, "Expanded library includes 161 unique base designs");
            foreach (var sample in new[] {
                ("批量焊接", "", "", "weld"), ("XYZ", "计算所选实体重量", "", "weight"),
                ("工具", "", "电气", "circuit"), ("Laser cutting", "", "", "laser-cut"),
                ("二维码生成", "", "", "qrcode"), ("自动编号", "", "", "numbering"),
                ("展开钣金", "", "", "sheet-unfold"), ("轴承设计", "", "", "bearing"),
                ("图像处理", "", "", "image"), ("零件套料", "", "", "nesting") })
                Check(WheelIconCatalog.TryGetSemanticIconId("VENDOR-UNKNOWN", sample.Item1, sample.Item2, sample.Item3, out var id) && id == sample.Item4,
                    "Unknown vendor metadata match " + sample.Item1);
            Check(WheelIconCatalog.TryGetSemanticIconId("LINE", "导出图片", "", "", out var exact) && exact == "line", "Execution identity precedes metadata");
            Check(!WheelIconCatalog.TryGetSemanticIconId("MYLINE", "MYLINE", "", "", out _), "No embedded English keyword false positive");
            Check(!WheelIconCatalog.TryGetSemanticIconId("VENDOR-UNKNOWN", null, null, null, out _), "No metadata safely retains fallback");
            Check(entries.Select(e => e.Id).Distinct().Count() == entries.Count, "Unique IDs");
            for (int number = 1; number <= 242; number++)
                Check(WheelIconCatalog.TryGetCommandIconId("SUN-" + number.ToString("000"), out _), "WangZhe command " + number);
            for (int number = 0; number <= 15; number++)
                Check(WheelIconCatalog.TryGetCommandIconId("SS" + number.ToString("000"), out _), "WangZhe panel command " + number);
            foreach (var pair in new[] {
                ("SUN-009", "bolt"), ("SUN-034", "erase"), ("SUN-150", "dimangular"),
                ("SS201", "array"), ("SUN", "toolbox"), ("SUN-KJJ", "toolbox"),
                ("XYBZ", "sheet-unfold"), ("XYUNFOLD", "sheet-unfold"), ("MCXY", "toolbox"), ("XYCUTBOX", "toolbox"),
                ("XYCUT-CAD-TO-EXCEL", "export"), ("XYCUT-EXCEL-TO-CAD", "import"),
                ("XYCUT-BATCH-PRINT", "plot"), ("XYCUT-DIM-CHAIN", "dimcontinue"),
                ("XYCUT-LAYER-ISOLATE", "layer"), ("XYCUT-SIMSUN", "text"),
                ("XYCUT-UNFOLD", "sheet-unfold"), ("XYCUT-FLAT-EXTRACT", "purge"),
                ("XYCUT-FLAT-QUANTITY-SUMMARY", "table"), ("XYCUT-SIMPLE-ANNOTATION", "dimlinear"),
                ("^C^C_.xycut-unfold;", "sheet-unfold"), ("^C^C_.SUN-018;", "sheet-unfold") })
                Check(WheelIconCatalog.TryGetCommandIconId(pair.Item1, out var iconId) && iconId == pair.Item2,
                    "Semantic plugin command mapping " + pair.Item1);
            var hashes = new HashSet<string>();
            foreach (var entry in entries)
            {
                using var bitmap = WheelIconCatalog.Render(entry.Id, 256);
                Check(bitmap.Width == 256 && bitmap.Height == 256 && bitmap.GetPixel(0, 0).A == 0, "Transparent square " + entry.Id);
                var encoded = CommandIconImage.Encode(bitmap, new RectangleF(0, 0, 256, 256));
                using var decoded = CommandIconImage.Decode(encoded);
                Check(decoded.Width == 256, "Existing PNG storage roundtrip " + entry.Id);
                Check(hashes.Add(Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(encoded)))), "Duplicate image " + entry.Id);
                bitmap.Save(Path.Combine(output, "png", entry.Id + ".png"), ImageFormat.Png);
                using var small = WheelIconCatalog.Render(entry.Id, 32);
                Check(HasInk(small), "Nonempty small icon " + entry.Id);
            }
            using (var surface = new Bitmap(80, 80))
            using (var graphics = Graphics.FromImage(surface))
            {
                graphics.TranslateTransform(2, 3);
                var before = graphics.Transform.Elements;
                foreach (var command in new[] { "LINE", "_CIRCLE", "^C^C_.MOVE", "ROTATE", "DIMLINEAR", "ZOOM", "LAYER", "EXTRUDE", "MTEXT", "QSAVE" })
                    Check(WheelIconCatalog.TryDrawCommand(graphics, command, new PointF(32, 32), 32), "CAD match " + command);
                foreach (var command in new[] { "", "YX_Q", "LINE_MYPLUGIN", "DIMWHATEVER", "(c:line)", "SUN-999", "SS099", "XYCUT-UNFOLD-CUSTOM", "MYSUN-018" })
                    Check(!WheelIconCatalog.TryDrawCommand(graphics, command, new PointF(32, 32), 32), "Unknown command unchanged " + command);
                Check(before.SequenceEqual(graphics.Transform.Elements), "Caller transform preserved");
            }
            using (var host = new CaptureHost { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, Size = new Size(920, 700) })
            using (var dialog = new WheelIconLibraryDialog())
            {
                // A child form creates its controls without activating a desktop
                // window. Hidden top-level forms otherwise capture as blank.
                dialog.TopLevel = false;
                host.Controls.Add(dialog);
                host.Show();
                dialog.Show();
                Application.DoEvents();
                dialog.CreateControl();
                dialog.PerformLayout();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var grid = (FlowLayoutPanel)typeof(WheelIconLibraryDialog).GetField("_grid", flags).GetValue(dialog);
                var search = (TextBox)typeof(WheelIconLibraryDialog).GetField("_search", flags).GetValue(dialog);
                var category = (ComboBox)typeof(WheelIconLibraryDialog).GetField("_category", flags).GetValue(dialog);
                Check(grid.Controls.Count == 48, "Only one thumbnail page allocated");
                using var capture = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                capture.Save(Path.Combine(output, "library-dialog.png"));
                search.Text = "unlikely_no_such_icon_12345";
                Check(grid.Controls.Count == 0, "Empty search clears page");
                search.Text = "";
                for (int i = 1; i < category.Items.Count; i++)
                {
                    category.SelectedIndex = i;
                    Check(grid.Controls.Count > 0 && grid.Controls.Count <= 48, "Category " + i);
                }
                search.Text = "直线";
                category.SelectedIndex = 0;
                Check(grid.Controls.Count > 0, "Chinese search");
                search.Text = "QSAVE";
                Check(grid.Controls.Count > 0 && grid.Controls[0].Text.Contains("保存"), "CAD command keyword search");
                var button = (Button)grid.Controls[0];
                var expected = entries.First(x => x.Name == button.Text).Id;
                // PerformClick requires a visible form; invoke the button click
                // without activating any user's foreground window.
                typeof(Button).GetMethod("OnClick", flags).Invoke(button, new object[] { EventArgs.Empty });
                Check(dialog.SelectedIconId == expected && dialog.DialogResult == DialogResult.OK, "Selection commits correct icon");
            }
            var manifest = entries.Select(x => new { x.Id, x.Name, x.Category, x.Keywords, png = "png/" + x.Id + ".png" });
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            var cards = new StringBuilder();
            foreach (var entry in entries)
                cards.Append("<a class='card' href='png/").Append(entry.Id).Append(".png' data-search='")
                    .Append(System.Net.WebUtility.HtmlEncode(entry.Name + " " + entry.Category + " " + entry.Keywords))
                    .Append("'><img loading='lazy' src='png/").Append(entry.Id).Append(".png'><span>")
                    .Append(System.Net.WebUtility.HtmlEncode(entry.Name)).Append("</span><small>")
                    .Append(System.Net.WebUtility.HtmlEncode(entry.Category)).Append("</small></a>");
            File.WriteAllText(Path.Combine(output, "index.html"), """
<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>小阳 CAD · 轮盘图标库</title><style>
body{margin:0;background:#eef2f6;color:#203247;font:15px system-ui}header{position:sticky;top:0;background:#eef2f6ed;padding:24px 4vw;backdrop-filter:blur(12px)}h1{font-size:26px;margin:0 0 10px}p{margin:8px 0;color:#536578}input{padding:12px;border:1px solid #b9c8d6;border-radius:8px;width:min(440px,75vw)}button{padding:12px;cursor:pointer}main{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:12px;padding:12px 4vw 40px}.card{display:flex;align-items:center;flex-direction:column;gap:8px;background:white;padding:18px 6px;border-radius:12px;color:inherit;text-decoration:none}.card:hover{outline:2px solid #109baa}img{width:64px;height:64px}small{color:#64768a}body.dark{background:#172431;color:#e8f1f8}.dark header{background:#172431ed}.dark .card{background:#283b4b}.dark small,.dark p{color:#b3c5d4}[hidden]{display:none!important}
</style><header><h1>小阳 CAD · 轮盘图标库</h1><p>透明 PNG · 256 × 256 · 点击图标打开原图</p><input id="search" placeholder="搜索中文名称、分类或 CAD 命令"><button id="theme">切换深浅背景</button><p id="count"></p></header><main>
""" + cards + """
</main><script>const cards=[...document.querySelectorAll('.card')];function filter(){let n=0;const q=document.querySelector('#search').value.trim().toLowerCase();cards.forEach(c=>{c.hidden=!c.dataset.search.toLowerCase().includes(q);if(!c.hidden)n++});document.querySelector('#count').textContent=n+' 个图标'}document.querySelector('#search').oninput=filter;document.querySelector('#theme').onclick=()=>document.body.classList.toggle('dark');filter();</script></html>
""", new UTF8Encoding(false));
            using (var sheet = new Bitmap(1024, 1024))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(245, 247, 250));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Original symbols occur first in the catalog.
                for (var i = 0; i < Math.Min(128, entries.Count); i++)
                {
                    using var icon = WheelIconCatalog.Render(entries[i].Id, 48);
                    var x = (i % 8) * 128; var y = (i / 8) * 64;
                    g.DrawImage(icon, x + 6, y + 8);
                    using var font = new Font("Microsoft YaHei UI", 7);
                    g.DrawString(entries[i].Name, font, Brushes.DarkSlateGray, new RectangleF(x + 58, y + 12, 69, 50));
                }
                sheet.Save(Path.Combine(output, "overview.png"));
            }
            File.WriteAllText(Path.Combine(output, "verification.txt"), $"PASS {_checks} checks; {entries.Count} icons; {hashes.Count} distinct PNG files.");
            Console.WriteLine($"PASS {_checks} checks; {entries.Count} icons; {output}");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static bool HasInk(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).A > 0) return true;
        return false;
    }
}
