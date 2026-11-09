namespace XiaoYang.AutoCADPlugin;

internal sealed class SceneMenuConfig
{
    public string Key { get; set; } = SceneKeys.Global;
    public string StyleId { get; set; } = "classic";
    public string ContentMode { get; set; } = "Auto";
    public int SurfaceOpacityPercent { get; set; } = 65;
    public WheelAppearance Appearance { get; set; } = new();
    public List<MenuItemConfig> UnassignedItems { get; set; } = new();
    public string BackgroundImagePath { get; set; } = "";
    public int BackgroundOpacityPercent { get; set; } = 65;
    public int MenuSizePx { get; set; }
    public int CenterRadiusPx { get; set; }
    public List<RingConfig> Rings { get; set; } = new();
}
