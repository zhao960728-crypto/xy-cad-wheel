using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

// Networking and package verification run in a private CLR 4 process.
// The CAD process continues running CLR 2 and its config is never rewritten.
internal sealed class PluginUpdateDialog : Form
{
    public new DialogResult ShowDialog(IWin32Window owner)
    {
#if AUTOCAD_WIN32
        // The public update feed contains the separate x64 product. Never hand
        // that installer to a 32-bit host or require CLR 4 for this legacy build.
        MessageBox.Show(owner, "当前为 AutoCAD 2010 Win32 独立兼容版。\r\n请使用标注 AutoCAD2010-Win32 的独立安装包更新，保留原配置。\r\n此版本不连接 64 位版自动更新源。",
            "小阳轮盘 · Win32 更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return DialogResult.OK;
#else
        try
        {
            var helper = Path.Combine(Path.GetDirectoryName(typeof(PluginUpdateDialog).Assembly.Location), "XYCAD.LegacyUpdater.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("旧版 CAD 更新组件缺失，请重新安装完整安装包。", helper);
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(helper).FileVersion;
            if (new Version(version) != typeof(PluginUpdateDialog).Assembly.GetName().Version) throw new InvalidDataException("旧版 CAD 更新组件版本不匹配，请重新安装完整安装包。");
            using var process = Process.Start(new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true });
            if (process == null) throw new IOException("无法打开更新窗口，请稍后重试。");
            return DialogResult.OK;
        }
        catch (Exception ex)
        {
            Logger.Error("无法打开旧版 CAD 独立更新窗口。", ex);
            MessageBox.Show(owner, ex.Message, "小阳轮盘 · 一键更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return DialogResult.Cancel;
        }
#endif
    }
}
