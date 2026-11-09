using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool extractOnly = args.Length == 2 && args[0] == "--extract-only";
        Application.EnableVisualStyles();
        bool owns;
        using (var mutex = new Mutex(true, @"Local\XiaoYang.AutoCAD.Setup", out owns))
        {
            if (!owns) return 2;
            try
            {
                if (args.Length != 0 && !extractOnly) throw new ArgumentException("不支持的启动参数。");
                if (IntPtr.Size != 4 || Environment.Version.Major != 2) throw new InvalidOperationException("安装器必须在 32 位 CLR 2 下运行。");
                string root = extractOnly ? Path.GetFullPath(args[1]) : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"XiaoYangCAD\Packages\AutoCAD2010-Win32-" + Assembly.GetExecutingAssembly().GetName().Version + "-" + Guid.NewGuid().ToString("N"));
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
                        if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(destination); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (var input = entry.Open())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                        {
                            var buffer = new byte[8192]; int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                        }
                    }
                }
                if (extractOnly) return 0;
                // A 32-bit process gets SysWOW64 on 64-bit Windows and System32
                // on 32-bit Windows, so registry discovery uses the CAD x86 view.
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                if (!File.Exists(powershell)) throw new FileNotFoundException("请先安装 Windows PowerShell 2.0 或更高版本。", powershell);
                var start = new ProcessStartInfo(powershell)
                {
                    Arguments = "-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "installer-ui.ps1") + "\"",
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root
                };
                using (var process = Process.Start(start)) { process.WaitForExit(); return process.ExitCode; }
            }
            catch (Exception ex)
            {
                if (!extractOnly) MessageBox.Show(ex.Message, "AutoCAD 2010 Win32 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
