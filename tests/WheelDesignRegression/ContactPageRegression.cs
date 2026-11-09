using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class ContactPageRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check, string output)
    {
        var resources = typeof(SettingsWindow).Assembly.GetManifestResourceNames();
        check(!resources.Any(name => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)),
            "host kernel no longer embeds duplicate contact images");
        var expected = new Dictionary<string, string>
        {
            ["抖音.png"] = "9A72B04D3E263C226899530E2E11069BE3D650C4F75014A3581CA4C010FEC674",
            ["加我.png"] = "80433552A4750869489C6D60C435AABB4541A08E4DE86D6075DA8499C375D1D1"
        };
        foreach (var pair in expected)
        {
            var path = Path.Combine(PluginPaths.ContactResourceDirectory, pair.Key);
            check(File.Exists(path) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == pair.Value,
                "shared contact resource exactly preserves original attachment " + pair.Key);
        }
        foreach (var width in new[] { 1406, 1000 })
        {
            var config = new PluginConfig(); ConfigStore.Normalize(config);
            var original = JsonCompat.Serialize(config);
            using var window = new SettingsWindow(config, new CadCommandCatalog(), _ =>
                throw new InvalidOperationException("Contact page check must not save"), loadCatalog: false);
            var pictures = (List<PictureBox>)typeof(SettingsWindow).GetField("_contactPictures", Private).GetValue(window);
            check(pictures.Count == 2 && pictures.All(p => p.Image == null), "contact images remain deferred at startup " + width);
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(-20000, -20000); window.ShowInTaskbar = false;
            window.Size = new Size(width, 746);
            window.Show(); Application.DoEvents();
            var select = typeof(SettingsWindow).GetMethod("SelectPage", Private);
            select.Invoke(window, new object[] { 4 }); Application.DoEvents();
            var pages = (TabControl)typeof(SettingsWindow).GetField("_pages", Private).GetValue(window);
            var page = pages.TabPages[4];
            var captions = Children(page).OfType<Label>().Select(label => label.Text).ToArray();
            check(captions.Contains("小阳爱学习") && captions.Contains("微信联系") &&
                !captions.Contains("支付宝") && !captions.Contains("微信"), "contact page keeps the two requested cards " + width);
            check(pictures.All(p => p.Image != null && p.Width > 100 && p.Height > 100), "both contact images decode and have usable space " + width);
            foreach (var pair in expected)
            {
                using var file = new FileStream(Path.Combine(PluginPaths.ContactResourceDirectory, pair.Key),
                    FileMode.Open, FileAccess.Read, FileShare.None);
                check(file.Length > 0, "displayed contact image leaves shared file unlocked " + pair.Key + " " + width);
            }
            var images = pictures.Select(p => p.Image).ToArray();
            select.Invoke(window, new object[] { 3 });
            select.Invoke(window, new object[] { 4 }); Application.DoEvents();
            check(pictures.Select(p => p.Image).SequenceEqual(images), "contact page reuses decoded images across navigation " + width);
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
            bitmap.Save(Path.Combine(output, "contact-page-" + width + ".png"), ImageFormat.Png);
            check(JsonCompat.Serialize(config) == original, "contact navigation preserves caller configuration " + width);
            window.Hide(); window.Dispose();
            check(pictures.All(p => p.Image == null), "contact window releases its images " + width);
        }
    }

    private static IEnumerable<Control> Children(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
