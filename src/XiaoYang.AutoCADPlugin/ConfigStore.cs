using System.IO;

namespace XiaoYang.AutoCADPlugin;

internal static class ConfigStore
{
    private static readonly object Sync = new();
    public static string BaseDirectory => PluginPaths.DataDirectory;

    public static string ConfigPath => Path.Combine(BaseDirectory,
        $"{CadHostInfo.ConfigFilePrefix}-{CadHostInfo.ProductYear}.json");

    public static PluginConfig Load()
    {
        lock (Sync)
        {
            PluginPaths.MigrateLegacyRuntimeFiles();
            if (!File.Exists(ConfigPath))
            {
                var initial = CreateDefault();
                SaveCore(initial);
                return initial;
            }

            try
            {
                var config = JsonCompat.Deserialize<PluginConfig>(File.ReadAllText(ConfigPath))
                             ?? CreateDefault();
                var requiresMigration = config.Menus == null
                    || !config.Menus.ContainsKey(SceneKeys.Global)
                    || config.Menus.Count != 1;
                if (requiresMigration)
                {
                    var backup = ConfigPath + ".multi-wheel-backup.json";
                    if (!File.Exists(backup)) File.Copy(ConfigPath, backup, false);
                }
                Normalize(config);
                if (requiresMigration) SaveCore(config);
                return config;
            }
            catch (Exception ex)
            {
                Logger.Error("配置读取失败，已使用默认配置。", ex);
                var backup = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(ConfigPath, backup, false); } catch { }
                return CreateDefault();
            }
        }
    }

    public static void Save(PluginConfig config)
    {
        lock (Sync)
        {
            Normalize(config);
            SaveCore(config);
        }
    }

    private static void SaveCore(PluginConfig config)
    {
        PluginPaths.EnsureRuntimeDirectories();
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonCompat.Serialize(config));
        RuntimeCompat.ReplaceFile(temp, ConfigPath);
    }

    internal static void Normalize(PluginConfig config)
    {
        config.TriggerMode = TriggerModes.Normalize(config.TriggerMode);
        config.HoldDelayMs = RuntimeCompat.Clamp(config.HoldDelayMs, 100, 3000);
        config.MenuSizePx = RuntimeCompat.Clamp(config.MenuSizePx, 260, 900);
        config.CenterRadiusPx = RuntimeCompat.Clamp(config.CenterRadiusPx, 20, config.MenuSizePx / 3);
        config.FontSizePx = RuntimeCompat.Clamp(config.FontSizePx, 8, 28);
        config.FontFamily = NormalizeFontFamily(config.FontFamily, "宋体");
        config.TextEffect = TextEffects.Normalize(config.TextEffect);
        config.OpacityPercent = RuntimeCompat.Clamp(config.OpacityPercent, 20, 100);
        config.TextColor = NormalizeColor(config.TextColor, "#1E2935");
        config.ImeSwitch ??= new ImeSwitchConfig();
        config.DrawingMerge ??= new DrawingMergeConfig();
        config.DrawingMerge.ArrangementGap = RuntimeCompat.Clamp(config.DrawingMerge.ArrangementGap, 0, 1000000);
        config.DrawingMerge.DrawingNumberFrameDistance = RuntimeCompat.Clamp(config.DrawingMerge.DrawingNumberFrameDistance, 0, 1000000);
        config.DrawingMerge.NumberTextHeight = Math.Max(0.1d, Math.Min(1000000d, config.DrawingMerge.NumberTextHeight));
        config.DrawingMerge.FrameLengthRatioPercent = Math.Max(0.01d, Math.Min(100d, config.DrawingMerge.FrameLengthRatioPercent));
        config.DrawingMerge.LastDirectory = (config.DrawingMerge.LastDirectory ?? "").Trim();
        config.DrawingMerge.FileType = NormalizeDrawingMergeFileType(config.DrawingMerge.FileType);
        config.DrawingMerge.ExpansionDrawingPosition = NormalizeDrawingMergeExpansionPosition(config.DrawingMerge.ExpansionDrawingPosition);
        config.DrawingMerge.InputBehavior = NormalizeDrawingMergeInputBehavior(config.DrawingMerge.InputBehavior);
        var selectedImeLayout = string.IsNullOrWhiteSpace(config.ImeSwitch.InputMethodLayoutId)
            ? config.ImeSwitch.ChineseLayoutId
            : config.ImeSwitch.InputMethodLayoutId;
        var selectedImeProfile = string.IsNullOrWhiteSpace(config.ImeSwitch.InputMethodProfileId)
            ? config.ImeSwitch.ChineseProfileId
            : config.ImeSwitch.InputMethodProfileId;
        selectedImeLayout = ImeNativeBridge.NormalizeLayoutId(selectedImeLayout, "00000804");
        selectedImeProfile = (selectedImeProfile ?? "").Trim();
        config.ImeSwitch.InputMethodLayoutId = selectedImeLayout;
        config.ImeSwitch.InputMethodProfileId = selectedImeProfile;
        // English and Chinese are two conversion modes of one IME. Keeping the
        // old properties synchronized preserves old backup-file compatibility
        // without ever selecting a separate US keyboard for English input.
        config.ImeSwitch.EnglishLayoutId = selectedImeLayout;
        config.ImeSwitch.ChineseLayoutId = selectedImeLayout;
        config.ImeSwitch.EnglishProfileId = selectedImeProfile;
        config.ImeSwitch.ChineseProfileId = selectedImeProfile;
        config.ImeSwitch.RestoreOnShutdown = false;
        // The old independent switch is no longer shown. Preserve its disabled
        // meaning once by converting it to an empty command list, then let the
        // list alone decide which commands prefer Chinese.
        if (!config.ImeSwitch.TextCommandsUseChinese)
            config.ImeSwitch.TextCommands = new List<string>();
        config.ImeSwitch.TextCommandsUseChinese = true;
        config.ImeSwitch.TextCommands ??= ImeCommandCatalog.Recommended.ToList();
        config.ImeSwitch.TextCommands = config.ImeSwitch.TextCommands
            .Select(ImeCommandCatalog.Normalize)
            .Where(command => command.Length > 0 && CadCommandCatalog.IsSafeCommandName(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToList();
        config.Menus ??= new Dictionary<string, SceneMenuConfig>(StringComparer.OrdinalIgnoreCase);
        config.CustomCommands ??= new List<CustomCommandConfig>();
        config.ManagedExtensions ??= new List<ManagedExtensionConfig>();
        config.ManagedExtensions = config.ManagedExtensions.Where(item => item != null).ToList();
        var extensionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in config.ManagedExtensions)
        {
            if (string.IsNullOrWhiteSpace(extension.Id) || !extensionIds.Add(extension.Id))
            {
                extension.Id = Guid.NewGuid().ToString("N");
                extensionIds.Add(extension.Id);
            }
            extension.FilePath ??= "";
            extension.Name ??= "";
            extension.StartupCommand = (extension.StartupCommand ?? "").Trim();
        }
        config.CommandCategories ??= new List<string>();
        config.CommandCategoryOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        config.PluginAppearance ??= new WheelAppearance();
        config.PluginAppearance.Normalize();
        config.CommandDisplayNameOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var scene = SelectSingleWheel(config.Menus) ?? CreateScene(SceneKeys.Global);
        scene.Key = SceneKeys.Global;
        if (scene.StyleId != "06" && scene.StyleId != "11" && scene.StyleId != "01" && scene.StyleId != "orbit" && scene.StyleId != "free") scene.StyleId = "classic";
        scene.SurfaceOpacityPercent = RuntimeCompat.Clamp(scene.SurfaceOpacityPercent, 5, 100);
        if (scene.ContentMode != "IconOnly" && scene.ContentMode != "TextOnly" && scene.ContentMode != "IconAndText" && scene.ContentMode != "ImageOnly" && scene.ContentMode != "ImageAndText") scene.ContentMode = "Auto";
        scene.Appearance ??= new WheelAppearance();
        scene.Appearance.Normalize();
        scene.UnassignedItems ??= new List<MenuItemConfig>();
        scene.BackgroundImagePath ??= "";
        scene.BackgroundOpacityPercent = RuntimeCompat.Clamp(scene.BackgroundOpacityPercent, 0, 100);
        scene.MenuSizePx = scene.MenuSizePx <= 0 ? config.MenuSizePx : RuntimeCompat.Clamp(scene.MenuSizePx, 260, 900);
        scene.CenterRadiusPx = scene.CenterRadiusPx <= 0
            ? config.CenterRadiusPx
            : RuntimeCompat.Clamp(scene.CenterRadiusPx, 20, scene.MenuSizePx / 3);
        scene.Rings ??= new List<RingConfig>();
        if (scene.Rings.Count == 0) scene.Rings.Add(new RingConfig());
        scene.Rings = scene.Rings.Take(RingConfig.MaximumRingCount).Select((ring, index) =>
        {
            ring.Index = index;
            ring.Slots = RuntimeCompat.Clamp(ring.Slots, 1, 32);
            ring.NodeInnerSizePercent = RuntimeCompat.Clamp(ring.NodeInnerSizePercent, 50, 150);
            ring.NodeOuterSizePercent = RuntimeCompat.Clamp(ring.NodeOuterSizePercent, 50, 150);
            ring.SizeWeight = RuntimeCompat.Clamp(ring.SizeWeight, 1, 12);
            ring.Items ??= new List<MenuItemConfig>();
            scene.UnassignedItems.AddRange(ring.Items.Where(item => item != null &&
                (item.Slot < 0 || item.Slot >= ring.Slots) && !string.IsNullOrWhiteSpace(item.Value)));
            ring.Items = ring.Items
                .Where(item => item != null && item.Slot >= 0 && item.Slot < ring.Slots)
                .Select(item =>
                {
                    item.Label = (item.Label ?? "").Trim();
                    item.ImageMode = item.ImageMode == "Background" ? "Background" : "Icon";
                    item.ContentMode = NormalizeCellContentMode(item.ContentMode);
                    item.CellSizePercent = RuntimeCompat.Clamp(item.CellSizePercent, 25, 250);
                    item.IconRatioPercent = RuntimeCompat.Clamp(item.IconRatioPercent, 10, 90);
                    item.FontSizePx = item.FontSizePx <= 0 ? 0 : RuntimeCompat.Clamp(item.FontSizePx, 6, 72);
                    item.FontFamily = NormalizeFontFamily(item.FontFamily, "");
                    item.TextEffect = string.IsNullOrWhiteSpace(item.TextEffect)
                        ? ""
                        : TextEffects.Normalize(item.TextEffect);
                    item.TextColor = NormalizeColor(item.TextColor, "");
                    return item;
                })
                .GroupBy(item => item.Slot)
                .Select(group => group.Last())
                .OrderBy(item => item.Slot)
                .ToList();
            return ring;
        }).ToList();
        foreach (var item in scene.UnassignedItems.Where(item => item != null))
        {
            item.ContentMode = NormalizeCellContentMode(item.ContentMode);
                    item.CellSizePercent = RuntimeCompat.Clamp(item.CellSizePercent, 25, 250);
                    item.IconRatioPercent = RuntimeCompat.Clamp(item.IconRatioPercent, 10, 90);
        }
        RingDiameterLayout.Normalize(scene);
        config.Menus = new Dictionary<string, SceneMenuConfig>(StringComparer.OrdinalIgnoreCase)
        {
            [SceneKeys.Global] = scene
        };

        config.CustomCommands = config.CustomCommands
            .Where(command => command != null && !string.IsNullOrWhiteSpace(command.CommandName))
            .GroupBy(command => command.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Select(command =>
            {
                command.Name = string.IsNullOrWhiteSpace(command.Name) ? command.CommandName : command.Name.Trim();
                command.CommandName = CadCommandCatalog.NormalizeCommandName(command.CommandName);
                command.Category = CommandCategoryCatalog.NormalizeName(command.Category);
                if (command.Category.Length == 0) command.Category = CommandCategoryCatalog.DefaultCustomCategory;
                command.IconPath ??= "";
                command.Arguments ??= new List<string>();
                // Keep the legacy flag exactly as stored for backup compatibility.
                // Command execution ignores it and never writes AutoCAD system variables.
                return command;
            })
            .ToList();

        config.CommandCategories = config.CommandCategories
            .Concat(config.CustomCommands.Select(command => command.Category))
            .Append(CommandCategoryCatalog.DefaultCustomCategory)
            .Select(CommandCategoryCatalog.NormalizeName)
            .Where(name => name.Length > 0 && !CommandCategoryCatalog.IsBuiltIn(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        config.CommandCategoryOverrides = config.CommandCategoryOverrides
            .Where(pair => CadCommandCatalog.IsSafeCommandName(pair.Key))
            .Select(pair => new KeyValuePair<string, string>(
                CadCommandCatalog.NormalizeCommandName(pair.Key),
                CommandCategoryCatalog.NormalizeName(pair.Value)))
            .Where(pair => pair.Value.Length > 0)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

        config.CommandDisplayNameOverrides = config.CommandDisplayNameOverrides
            .Where(pair => CadCommandCatalog.IsSafeCommandName(pair.Key))
            .Select(pair => new KeyValuePair<string, string>(
                CadCommandCatalog.NormalizeCommandName(pair.Key),
                (pair.Value ?? "").Trim()))
            .Where(pair => pair.Value.Length > 0)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFontFamily(string value, string fallback)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0) return fallback;
        return normalized.Length <= 80 ? normalized : normalized.Substring(0, 80);
    }

    private static string NormalizeDrawingMergeFileType(string value) =>
        string.Equals(value, "DWG", StringComparison.OrdinalIgnoreCase) ? "DWG"
        : string.Equals(value, "DXF", StringComparison.OrdinalIgnoreCase) ? "DXF"
        : "DWG_DXF";

    private static string NormalizeDrawingMergeExpansionPosition(string value) =>
        string.Equals(value, "Above", StringComparison.OrdinalIgnoreCase) ? "Above"
        : string.Equals(value, "Below", StringComparison.OrdinalIgnoreCase) ? "Below"
        : "Ignore";

    private static string NormalizeDrawingMergeInputBehavior(string value) =>
        string.Equals(value, "SelectImmediately", StringComparison.OrdinalIgnoreCase) ? "SelectImmediately"
        : string.Equals(value, "SettingsOnly", StringComparison.OrdinalIgnoreCase) ? "SettingsOnly"
        : "SettingsThenSelect";

    internal static PluginConfig CreateDefault()
    {
        // 新安装只提供空轮盘。命令目录来自用户当前 AutoCAD，自定义命令由
        // 用户导入/创建；安装包不预放示例命令或测试格子。
        var config = new PluginConfig();
        config.Menus[SceneKeys.Global] = CreateScene(SceneKeys.Global);
        Normalize(config);
        return config;
    }

    private static SceneMenuConfig SelectSingleWheel(Dictionary<string, SceneMenuConfig> menus)
    {
        if (menus.TryGetValue(SceneKeys.Global, out var global) && global != null)
            return MergeLegacyWheels(global, menus.Values);

        if (menus.TryGetValue(SceneKeys.Model, out var model)
            && model?.Rings?.Any(ring => ring.Items?.Count > 0) == true)
            return MergeLegacyWheels(model, menus.Values);

        var selected = menus.Values
            .Where(value => value != null)
            .OrderByDescending(value => value.Rings?.Sum(ring => ring.Items?.Count ?? 0) ?? 0)
            .FirstOrDefault();
        return selected == null ? null : MergeLegacyWheels(selected, menus.Values);
    }

    private static SceneMenuConfig MergeLegacyWheels(SceneMenuConfig target, IEnumerable<SceneMenuConfig> sources)
    {
        target.Rings ??= new List<RingConfig>();
        foreach (var source in sources.Where(source => source != null && !ReferenceEquals(source, target)))
        {
            foreach (var sourceRing in source.Rings ?? new List<RingConfig>())
            {
                if (sourceRing.Index < 0 || sourceRing.Index >= RingConfig.MaximumRingCount) continue;
                while (target.Rings.Count <= sourceRing.Index)
                    target.Rings.Add(new RingConfig { Index = target.Rings.Count, Slots = 8 });
                var targetRing = target.Rings[sourceRing.Index];
                targetRing.Slots = Math.Max(targetRing.Slots, sourceRing.Slots);
                targetRing.Items ??= new List<MenuItemConfig>();
                foreach (var item in sourceRing.Items ?? new List<MenuItemConfig>())
                {
                    if (targetRing.Items.All(existing => existing.Slot != item.Slot))
                        targetRing.Items.Add(item);
                }
            }
        }
        return target;
    }

    private static SceneMenuConfig CreateScene(string key) => new()
    {
        Key = key,
        MenuSizePx = 520,
        CenterRadiusPx = 44,
        Rings = new List<RingConfig> { new() { Index = 0, Slots = 8, Enabled = true } }
    };

    internal static string NormalizeCellContentMode(string value) =>
        value == "IconOnly" || value == "TextOnly" || value == "IconAndText" || value == "ImageOnly" || value == "ImageAndText" ? value : "Inherit";

    private static string NormalizeColor(string value, string fallback)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return fallback;
        try
        {
            var color = System.Drawing.ColorTranslator.FromHtml(text);
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch { return fallback; }
    }
}
