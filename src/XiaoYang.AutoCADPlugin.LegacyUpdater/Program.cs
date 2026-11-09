using System;
using System.Threading;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var mutex = new Mutex(true, @"Local\XiaoYangCAD.LegacyUpdater", out var owns);
        if (!owns) { MessageBox.Show("更新窗口已打开，请先完成当前更新。", "小阳轮盘 · 一键更新"); return; }
        try { using var dialog = new PluginUpdateDialog(); dialog.StartPosition = FormStartPosition.CenterScreen; Application.Run(dialog); }
        finally { mutex.ReleaseMutex(); }
    }
}

internal static class CadHostInfo
{
    public const string PluginDisplayName = "小阳CAD轮盘插件";
    public const string ConfigFilePrefix = "autocad";
    public const string ProductYear = "2010-2012";
}
