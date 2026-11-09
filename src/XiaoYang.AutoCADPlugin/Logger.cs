using System.Diagnostics;
using System.IO;

namespace XiaoYang.AutoCADPlugin;

internal static class Logger
{
    private static readonly object Sync = new();
    private const long MaxLogBytes = 2 * 1024 * 1024;
    public static string LogPath => PluginPaths.MainLogPath;
    public static string ErrorLogPath => PluginPaths.ErrorLogPath;

    public static void Info(string message) => Write("INFO", message, null);
    public static void Trace(string message) => Debug.WriteLine("[XY-CAD] " + message);
    public static void Error(string message) => Write("ERROR", message, null);
    public static void Error(string message, Exception ex) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                PluginPaths.EnsureRuntimeDirectories();
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] " +
                            $"[PID {System.Diagnostics.Process.GetCurrentProcess().Id}] {message}" +
                            $"{(exception == null ? "" : Environment.NewLine + exception)}{Environment.NewLine}";
                AppendWithRotation(LogPath, entry);
                if (level == "ERROR") AppendWithRotation(ErrorLogPath, entry);
            }
        }
        catch { }
    }

    private static void AppendWithRotation(string path, string entry)
    {
        if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
        {
            var previous = path + ".1";
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(path, previous);
        }
        File.AppendAllText(path, entry);
    }
}
