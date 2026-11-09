namespace XiaoYang.AutoCADPlugin;

internal sealed class MenuItemConfig
{
    public int Slot { get; set; }
    public string Label { get; set; } = "";
    public string IconData { get; set; } = "";
    public string ImageMode { get; set; } = "Icon";
    public string ContentMode { get; set; } = "Inherit";
    public int CellSizePercent { get; set; } = 100;
    public string CellShape { get; set; } = "Circle";
    public double? FreeX { get; set; }
    public double? FreeY { get; set; }
    public int IconRatioPercent { get; set; } = 55;
    public string Kind { get; set; } = ActionKinds.CadCommand;
    public string Value { get; set; } = "";
    public int FontSizePx { get; set; }
    public string FontFamily { get; set; } = "";
    public bool? FontBold { get; set; }
    public string TextEffect { get; set; } = "";
    public string TextColor { get; set; } = "";
}
