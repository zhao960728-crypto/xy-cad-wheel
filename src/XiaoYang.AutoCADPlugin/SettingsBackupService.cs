using System.IO;

namespace XiaoYang.AutoCADPlugin;

internal static class SettingsBackupService
{
    private const string BackupFormat = "XY-CAD-H-SettingsBackup";
    private const int MaximumEmbeddedImageBytes = 30 * 1024 * 1024;
    public static void Export(string path, PluginConfig config)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("备份路径不能为空。", nameof(path));
        ConfigStore.Normalize(config);
        var document = new SettingsBackupDocument
        {
            PluginVersion = ProductMetadata.DisplayVersion,
            ExportedAt = DateTimeOffset.Now.ToString("O"),
            Config = config
        };

        var background = config.Menus.TryGetValue(SceneKeys.Global, out var wheel)
            ? wheel.BackgroundImagePath
            : "";
        if (!string.IsNullOrWhiteSpace(background) && File.Exists(background))
        {
            var info = new FileInfo(background);
            if (info.Length > MaximumEmbeddedImageBytes)
                throw new InvalidDataException("背景图片超过 30 MB，请压缩图片后再导出设置备份。");
            document.BackgroundFileName = Path.GetFileName(background);
            document.BackgroundImageBase64 = Convert.ToBase64String(File.ReadAllBytes(background));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonCompat.Serialize(document));
        RuntimeCompat.ReplaceFile(temp, path);
    }

    public static PluginConfig Import(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到设置备份文件。", path);
        var json = File.ReadAllText(path);
        if (json.Length > 100 * 1024 * 1024)
            throw new InvalidDataException("设置备份文件过大，无法导入。");

        PluginConfig config;
        var document = JsonCompat.Deserialize<SettingsBackupDocument>(json);
        if (document?.Format == BackupFormat && document.Config != null)
        {
            config = document.Config;
            RestoreBackground(document, config);
        }
        else
        {
            // Also accept the raw autocad-2026.json file as a backup.
            config = JsonCompat.Deserialize<PluginConfig>(json)
                     ?? throw new InvalidDataException("备份文件中没有有效设置。");
        }

        ConfigStore.Normalize(config);
        return config;
    }

    public static string CreateAutomaticBackupPath(string reason)
    {
        var folder = Path.Combine(PluginPaths.DataDirectory, "Backups");
        Directory.CreateDirectory(folder);
        var safeReason = string.Concat((reason ?? "auto").Where(ch => char.IsLetterOrDigit(ch) || ch == '-'));
        if (safeReason.Length == 0) safeReason = "auto";
        return Path.Combine(folder, $"{safeReason}-{DateTime.Now:yyyyMMdd-HHmmss}.xylpbackup");
    }

    private static void RestoreBackground(SettingsBackupDocument document, PluginConfig config)
    {
        if (string.IsNullOrWhiteSpace(document.BackgroundImageBase64)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(document.BackgroundImageBase64); }
        catch (FormatException ex) { throw new InvalidDataException("备份中的背景图片数据无效。", ex); }
        if (bytes.Length > MaximumEmbeddedImageBytes)
            throw new InvalidDataException("备份中的背景图片超过 30 MB，无法恢复。");

        var extension = Path.GetExtension(document.BackgroundFileName ?? "").ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")) extension = ".png";
        var folder = Path.Combine(PluginPaths.DataDirectory, "Backgrounds");
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, $"backup-background-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
        File.WriteAllBytes(destination, bytes);
        if (config.Menus.TryGetValue(SceneKeys.Global, out var wheel))
            wheel.BackgroundImagePath = destination;
    }

    private sealed class SettingsBackupDocument
    {
        public string Format { get; set; } = BackupFormat;
        public int FormatVersion { get; set; } = 1;
        public string PluginVersion { get; set; } = "";
        public string ExportedAt { get; set; } = "";
        public PluginConfig Config { get; set; }
        public string BackgroundFileName { get; set; } = "";
        public string BackgroundImageBase64 { get; set; } = "";
    }
}
