namespace XiaoYang.AutoCADPlugin;

internal sealed class PluginConfig
{
    public string TriggerMode { get; set; } = TriggerModes.ShiftRightHold;
    public int HoldDelayMs { get; set; } = 220;
    public int MenuSizePx { get; set; } = 520;
    public int CenterRadiusPx { get; set; } = 44;
    public int FontSizePx { get; set; } = 14;
    public string FontFamily { get; set; } = "宋体";
    public bool FontBold { get; set; }
    public string TextEffect { get; set; } = TextEffects.None;
    public int OpacityPercent { get; set; } = 94;
    public string TextColor { get; set; } = "#1E2935";
    public bool PluginAppearanceEnabled { get; set; }
    public WheelAppearance PluginAppearance { get; set; } = new();
    public bool ExpandSearchCommandTree { get; set; }
    public ImeSwitchConfig ImeSwitch { get; set; } = new();
    public DrawingMergeConfig DrawingMerge { get; set; } = new();
    public Dictionary<string, SceneMenuConfig> Menus { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CustomCommandConfig> CustomCommands { get; set; } = new();
    public List<ManagedExtensionConfig> ManagedExtensions { get; set; } = new();
    public List<string> CommandCategories { get; set; } = new() { CommandCategoryCatalog.DefaultCustomCategory };
    public Dictionary<string, string> CommandCategoryOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CommandDisplayNameOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
