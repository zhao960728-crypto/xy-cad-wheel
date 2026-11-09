using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class SettingsStartupPerformanceRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool, string> check, string output)
    {
        Directory.CreateDirectory(output);
        var report = new StringBuilder("scenario,run,constructor_ms,show_ms,first_pump_ms,responsive_5_ms,filter_ms,first_directory_ms\n");
        var snapshot = new CadCommandInfo[2000];
        for (int i = 0; i < snapshot.Length; i++) snapshot[i] = new CadCommandInfo
        { GlobalName = "BENCH_VENDOR_" + i, DisplayName = "批量零件统计 " + i, Help = "统计所选零件的重量面积和表格", Group = "测试工具", IsCoreCommand = false };
        foreach (bool populated in new[] { false, true })
        for (int run = 0; run < 3; run++)
        {
            var config = new PluginConfig(); ConfigStore.Normalize(config);
            var original = JsonCompat.Serialize(config);
            var catalog = new CadCommandCatalog();
            if (populated)
            {
                typeof(CadCommandCatalog).GetField("_cached", Private).SetValue(catalog, snapshot);
                typeof(CadCommandCatalog).GetProperty("RefreshedAt").GetSetMethod(true).Invoke(catalog, new object[] { DateTime.Now });
            }
            var timer = Stopwatch.StartNew();
            using var window = new SettingsWindow(config, catalog, _ => throw new InvalidOperationException("Performance probe must not save"), loadCatalog: populated);
            long constructor = timer.ElapsedMilliseconds;
            window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-20000, -20000); window.ShowInTaskbar = false;
            try
            {
                timer.Restart(); window.Show(); long show = timer.ElapsedMilliseconds;
                timer.Restart(); Application.DoEvents(); long pump = timer.ElapsedMilliseconds;
                timer.Restart();
                typeof(SettingsWindow).GetMethod("SelectPage", Private).Invoke(window, new object[] { 1 }); Application.DoEvents();
                long firstDirectory = timer.ElapsedMilliseconds;
                if (populated) check(((TreeView)typeof(SettingsWindow).GetField("_catalogTree", Private).GetValue(window)).GetNodeCount(true) >= 2000,
                    "deferred command directory is fully available " + run);
                timer.Restart();
                for (int i = 0; i < 5; i++) typeof(SettingsWindow).GetMethod("ApplyResponsiveSettingsLayout", Private).Invoke(window, null);
                long responsive = timer.ElapsedMilliseconds;
                timer.Restart(); typeof(SettingsWindow).GetMethod("FilterCatalog", Private).Invoke(window, null); long filter = timer.ElapsedMilliseconds;
                check(JsonCompat.Serialize(config) == original, "startup benchmark preserves caller configuration " + populated + "/" + run);
                report.AppendLine((populated ? "cached-2000" : "no-catalog") + "," + run + "," + constructor + "," + show + "," + pump + "," + responsive + "," + filter + "," + firstDirectory);
                Console.WriteLine(report.ToString().Trim().Split('\n').Last());
            }
            finally { window.Hide(); }
        }
        File.WriteAllText(Path.Combine(output, "startup-timing.csv"), report.ToString());
    }
}
