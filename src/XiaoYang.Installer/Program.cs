using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    // Payload stays here because the CAD registration points into this directory.
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        bool owns;
        using (var mutex = new Mutex(true, @"Local\XiaoYang.AutoCAD.Setup", out owns))
        {
            if (!owns) { MessageBox.Show("安装器已打开，请完成当前安装。", "小阳 CAD 轮盘"); return 2; }
            try
            {
                bool extractOnly = args.Length == 2 && args[0] == "--extract-only";
                if (args.Length != 0 && !extractOnly) throw new ArgumentException("不支持的启动参数。");
                string root = extractOnly ? Path.GetFullPath(args[1]) : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XiaoYangCAD", "Packages", Assembly.GetExecutingAssembly().GetName().Version + "-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(root) || File.Exists(root)) throw new IOException("安装目录已存在，拒绝覆盖：" + root);
                Directory.CreateDirectory(root);
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("安装文件路径无效。");
                        if (entry.Name.Length == 0) { Directory.CreateDirectory(destination); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (var input = entry.Open())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
                    }
                }
                if (extractOnly) return 0;
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"));
                start.Arguments = "-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "installer-ui.ps1") + "\"";
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardError = true;
                start.RedirectStandardOutput = true;
                using (var process = Process.Start(start))
                {
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    process.WaitForExit();
                    Task.WaitAll(error, output);
                    if (process.ExitCode != 0) throw new IOException(error.Result + "\r\n安装目录：" + root);
                }
                return 0;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "小阳 CAD 轮盘安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
