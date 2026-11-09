namespace XiaoYang.AutoCADPlugin;

internal static class TriggerModes
{
    public const string RightHold = "right-hold";
    public const string MiddleHold = "middle-hold";
    public const string XButton1Hold = "xbutton1-hold";
    public const string XButton2Hold = "xbutton2-hold";
    public const string ShiftRightHold = "shift-right-hold";
    public const string AltRightHold = "alt-right-hold";
    public const string CtrlRightHold = "ctrl-right-hold";

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        [RightHold] = "右键长按",
        [MiddleHold] = "中键长按",
        [XButton1Hold] = "鼠标侧键 1 长按",
        [XButton2Hold] = "鼠标侧键 2 长按",
        [ShiftRightHold] = "Shift + 右键长按",
        [AltRightHold] = "Alt + 右键长按",
        [CtrlRightHold] = "Ctrl + 右键长按"
    };

    public static IReadOnlyDictionary<string, string> All => Names;

    public static string Normalize(string value)
    {
        var text = (value ?? "").Trim();
        return Names.ContainsKey(text) ? text : ShiftRightHold;
    }

    public static string Display(string value) =>
        Names.TryGetValue(value ?? "", out var display) ? display : Names[ShiftRightHold];
}
