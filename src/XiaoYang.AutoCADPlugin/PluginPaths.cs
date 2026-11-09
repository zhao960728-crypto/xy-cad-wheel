using System.Reflection;
using System.IO;

namespace XiaoYang.AutoCADPlugin;

internal static class PluginPaths
{
    private const string LegacyFolderName = "小阳AutoCAD轮盘";

    public static string PluginDirectory { get; } = ResolvePluginDirectory();
    public static string DataDirectory => Path.Combine(PluginDirectory, "Data");
    public static string LogDirectory => Path.Combine(PluginDirectory, "Logs");
    public static string ContactResourceDirectory => Path.Combine(PluginDirectory, "Contents", "Resources", "Contact");
    public static string MainLogPath => Path.Combine(LogDirectory, CadHostInfo.ConfigFilePrefix + "-plugin.log");
    public static string ErrorLogPath => Path.Combine(LogDirectory, "errors.log");
    public static string BundleMenuSourcePath => Path.Combine(PluginDirectory, "Contents", "Menu", "XYCADMENU.mnu");
    public static string VersionMenuDirectory => Path.Combine(DataDirectory, "Menu", CadHostInfo.ProductYear);
    public static string VersionMenuSourcePath => Path.Combine(VersionMenuDirectory, "XYCADMENU.mnu");
    public static string VersionMenuCompiledPath => Path.Combine(VersionMenuDirectory, "XYCADMENU.cuix");
    public static string LegacyDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mr.Z", LegacyFolderName);

    public static void EnsureRuntimeDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        EnsureFile(MainLogPath);
        EnsureFile(ErrorLogPath);
    }

    public static bool EnsureVersionMenuFiles()
    {
        if (!File.Exists(BundleMenuSourcePath))
            throw new FileNotFoundException("顶部菜单源文件不存在。", BundleMenuSourcePath);
        Directory.CreateDirectory(VersionMenuDirectory);
        var changed = !File.Exists(VersionMenuSourcePath)
                      || !FilesHaveSameContent(BundleMenuSourcePath, VersionMenuSourcePath);
        if (!changed) return false;

        // Data/Menu 会随用户配置迁移。升级时只更新本插件自己的菜单源文件，
        // 由 CadMenuBar 在卸载本插件菜单组后重新生成对应版本的 CUIX。
        File.Copy(BundleMenuSourcePath, VersionMenuSourcePath, true);
        return true;
    }

    public static bool DeleteVersionMenuCompiledFile()
    {
        if (!File.Exists(VersionMenuCompiledPath)) return true;
        try
        {
            File.Delete(VersionMenuCompiledPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("删除本插件旧顶部菜单缓存失败，将直接从新菜单源重新加载。", ex);
            return false;
        }
    }

    public static void MigrateLegacyRuntimeFiles()
    {
        EnsureRuntimeDirectories();
#if !ZWCAD
        CopyIfMissing(
            Path.Combine(LegacyDirectory, "autocad-2026.json"),
            Path.Combine(DataDirectory, "autocad-2026.json"));
        CopyIfMissing(
            Path.Combine(LegacyDirectory, "autocad-2026.json.multi-wheel-backup.json"),
            Path.Combine(DataDirectory, "autocad-2026.json.multi-wheel-backup.json"));
        CopyIfMissing(
            Path.Combine(LegacyDirectory, "autocad-plugin.log"),
            Path.Combine(LogDirectory, "autocad-plugin.legacy.log"));
#endif
    }

    private static void CopyIfMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target)) return;
        try { File.Copy(source, target, false); } catch { }
    }

    private static void EnsureFile(string path)
    {
        if (File.Exists(path)) return;
        try { File.WriteAllText(path, ""); } catch { }
    }

    private static bool FilesHaveSameContent(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;

        const int bufferSize = 81920;
        var leftBuffer = new byte[bufferSize];
        var rightBuffer = new byte[bufferSize];
        using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        while (true)
        {
            var leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) return true;
            for (var index = 0; index < leftRead; index++)
                if (leftBuffer[index] != rightBuffer[index]) return false;
        }
    }

    private static string ResolvePluginDirectory()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                ?? AppContext.BaseDirectory;
        for (var directory = new DirectoryInfo(assemblyDirectory); directory != null; directory = directory.Parent)
        {
            if (directory.Name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var target = RuntimeCompat.ResolveFinalDirectoryPath(directory.FullName);
                    if (!string.IsNullOrWhiteSpace(target)) return target;
                }
                catch { }
                return directory.FullName;
            }
        }
        return assemblyDirectory;
    }
}
