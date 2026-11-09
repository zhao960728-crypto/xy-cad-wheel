using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            var sdk = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var name = new AssemblyName(e.Name).Name + ".dll";
                var path = Directory.GetFiles(sdk, name, SearchOption.AllDirectories).FirstOrDefault();
                return path == null ? null : Assembly.LoadFrom(path);
            };
            Check(Environment.Version.Major == 2, "Test must run under the actual CLR 2 runtime.");
            if (args.Length > 3) Check(IntPtr.Size == int.Parse(args[3]), "Wrong test process bitness.");
            Check(typeof(SettingsWindow).Assembly.ImageRuntimeVersion.StartsWith("v2."), "Legacy kernel must target CLR 2.");
            Check(MouseWheelTrigger.ExtendedMouseDataOffset == (IntPtr.Size == 4 ? 20 : 32), "Mouse hook layout does not match the process architecture.");
#if WIN32_REGRESSION
            var ready = false; var started = 0;
            var startup = new LegacyDeferredStartup(() => ready, () => started++);
            Check(!startup.Tick() && started == 0, "Startup initialized an unready host.");
            ready = true;
            Check(startup.Tick() && started == 1, "Ready host did not initialize.");
            Check(startup.Tick() && started == 1, "Repeated idle initialized twice.");
            startup = new LegacyDeferredStartup(() => true, () => started++);
            startup.Cancel(); Check(startup.Tick() && started == 1, "Cancelled startup initialized services.");
#endif
            Application.EnableVisualStyles();
            var config = ConfigStore.CreateDefault();
            config.MenuSizePx = 612; ConfigStore.Normalize(config);
            var serialized = JsonCompat.Serialize(config);
            var loaded = JsonCompat.Deserialize<PluginConfig>(serialized);
            Check(loaded.MenuSizePx == 612 && loaded.Menus.Count == 1, "Config serialization/normalization changed user settings.");
            var template = Path.Combine(output, "命令模板.xlsx");
            CommandImportService.WriteTemplate(template);
            var imported = CommandImportService.Read(template);
            Check(imported.Errors.Count == 0 && imported.Rows.Count == 0, "Legacy XLSX template round trip failed.");
            Check(CommandImportService.Read(args[2]).Errors.Count == 0, "Deflated XLSX from the modern template writer failed.");
            var corrupt = File.ReadAllBytes(template);
            for (var index = 0; index <= corrupt.Length - 46; index++)
            {
                if (BitConverter.ToUInt32(corrupt, index) != 0x02014b50) continue;
                var length = BitConverter.ToUInt16(corrupt, index + 28);
                if (index + 46 + length > corrupt.Length) continue;
                if (Encoding.UTF8.GetString(corrupt, index + 46, length) != "xl/worksheets/sheet1.xml") continue;
                corrupt[index + 16] ^= 1; break;
            }
            var corruptPath = Path.Combine(output, "crc-invalid.xlsx"); File.WriteAllBytes(corruptPath, corrupt);
            var blocked = false;
            try { CommandImportService.Read(corruptPath); } catch (InvalidDataException) { blocked = true; }
            Check(blocked, "Corrupted Excel ZIP data must be rejected.");
            Check(LegacyRuntime.Join(",", new[] { 2010, 2011 }) == "2010,2011", "Legacy generic join changed values.");
            Guid guid; Check(LegacyRuntime.TryParseGuidExact("5110c96a-49b4-415c-8829-773cf54a3d45", "D", out guid), "GUID adapter failed.");
            Check(!LegacyRuntime.TryParseGuid("invalid", out guid), "GUID adapter accepted invalid input.");
            using (var key = LegacyRegistry.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, LegacyRegistryView.Registry64))
            using (var software = key.OpenSubKey("SOFTWARE")) Check(software != null && software.GetSubKeyNames().Length > 0, "Legacy registry adapter cannot read the 64-bit view.");
            using (var key = LegacyRegistry.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, LegacyRegistryView.Registry32))
            using (var software = key.OpenSubKey("SOFTWARE")) Check(software != null && software.GetSubKeyNames().Length > 0, "Legacy registry adapter cannot read the 32-bit view.");
            var catalog = new CadCommandCatalog();
            typeof(CadCommandCatalog).GetField("<RefreshedAt>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(catalog, DateTime.Now);
            var settingsSaves = 0;
            using (var window = new SettingsWindow(loaded, catalog, updated => { settingsSaves++; }, loadCatalog: false))
            {
                window.Show(); Application.DoEvents();
                Check(window.Visible && window.Controls.Count > 0, "Legacy settings window did not open.");
                var pages = (TabControl)typeof(SettingsWindow).GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                for (var index = 0; index < pages.TabPages.Count; index++) { pages.SelectedIndex = index; Application.DoEvents(); Check(pages.SelectedTab.Controls.Count > 0, "Empty legacy settings page " + index); }
                pages.SelectedIndex = 3; Application.DoEvents();
                typeof(SettingsWindow).GetMethod("SaveAllPendingSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                Check(settingsSaves == 1, "Legacy settings Save/Apply did not complete exactly once.");
                using (var bitmap = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(output, "legacy-settings.png"));
                }
                window.Dispose();
            }
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + checks + " checks; CLR " + Environment.Version, Encoding.UTF8);
            Console.WriteLine("PASS " + checks + " checks; actual CLR " + Environment.Version);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
