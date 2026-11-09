namespace XiaoYang.AutoCADPlugin;

internal sealed class ImeSwitchConfig
{
    public bool Enabled { get; set; }
    public bool DirectInputUsesEnglish { get; set; } = true;
    // Legacy setting retained only so older settings/backups remain readable.
    // Selected TextCommands now always determine which commands prefer Chinese.
    public bool TextCommandsUseChinese { get; set; } = true;
    public bool OtherCommandsUseEnglish { get; set; } = true;
    public bool CommandEndUsesEnglish { get; set; } = true;
    public bool DocumentActivatedUsesEnglish { get; set; } = true;
    // Error details are always written to the plug-in log. The optional
    // non-activating text report stays off unless the user explicitly enables it.
    public bool ShowErrorReport { get; set; }
    public bool RestoreOnShutdown { get; set; }
    public string InputMethodLayoutId { get; set; } = "00000804";
    public string InputMethodProfileId { get; set; } = "";
    // Legacy fields are retained so existing settings/backups remain readable.
    // Normalize() converges both modes onto the single selected input method.
    public string EnglishLayoutId { get; set; } = "00000804";
    public string ChineseLayoutId { get; set; } = "00000804";
    public string EnglishProfileId { get; set; } = "";
    public string ChineseProfileId { get; set; } = "";
    public List<string> TextCommands { get; set; } = ImeCommandCatalog.Recommended.ToList();
}
