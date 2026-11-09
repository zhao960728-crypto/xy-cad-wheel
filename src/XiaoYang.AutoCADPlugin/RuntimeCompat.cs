using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XiaoYang.AutoCADPlugin;

internal static class RuntimeCompat
{
    public static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    public static float Clamp(float value, float minimum, float maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    public static double Clamp(double value, double minimum, double maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    public static string ProcessPath
    {
        get
        {
            try { return Process.GetCurrentProcess().MainModule?.FileName ?? ""; }
            catch { return ""; }
        }
    }

    public static void ReplaceFile(string source, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(source, destination);
            return;
        }

        try { File.Replace(source, destination, null); }
        catch (PlatformNotSupportedException)
        {
            File.Delete(destination);
            File.Move(source, destination);
        }
    }

    public static string ResolveFinalDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try
        {
            using var handle = CreateFile(
                path,
                0,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                3,
                0x02000000,
                IntPtr.Zero);
            if (handle.IsInvalid) return path;
            var buffer = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity) return path;
            var resolved = buffer.ToString();
            if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + resolved.Substring(8);
            if (resolved.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return resolved.Substring(4);
            return resolved;
        }
        catch { return path; }
    }

    public static void SetTextBoxCueBanner(System.Windows.Forms.TextBox textBox, string text)
    {
        if (textBox == null || textBox.IsDisposed) return;
        try { SendMessage(textBox.Handle, 0x1501, new IntPtr(1), text ?? ""); }
        catch { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        int filePathSize,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);
}

#if NETFRAMEWORK
internal static class FrameworkCollectionExtensions
{
    public static bool Contains(this string source, string value, StringComparison comparison) =>
        source?.IndexOf(value ?? "", comparison) >= 0;

    public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> source, TKey key)
    {
        if (source == null) return default;
        return source.TryGetValue(key, out var value) ? value : default;
    }

    public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource> comparer) =>
        new(source, comparer);
}
#endif
