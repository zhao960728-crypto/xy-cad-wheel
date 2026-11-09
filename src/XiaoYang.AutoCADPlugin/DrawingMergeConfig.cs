namespace XiaoYang.AutoCADPlugin;

internal sealed class DrawingMergeConfig
{
    public int ArrangementGap { get; set; } = 30;
    public int DrawingNumberFrameDistance { get; set; } = 500;
    public bool UseFixedNumberHeight { get; set; }
    public double NumberTextHeight { get; set; } = 10d;
    public double FrameLengthRatioPercent { get; set; } = 1d;
    public bool StartInCurrentDrawingDirectory { get; set; }
    public string LastDirectory { get; set; } = "";
    public string FileType { get; set; } = "DWG_DXF";
    public bool QuickMode { get; set; }
    public bool AddDrawingNumber { get; set; } = true;
    public bool AddFrame { get; set; }
    public bool ExplodeAfterMerge { get; set; } = true;
    public bool NormalizeScale { get; set; }
    public bool ShowProgress { get; set; } = true;
    public bool IgnoreTextAlignment { get; set; }
    public bool ProcessLayers { get; set; }
    public bool ProcessBlocks { get; set; } = true;
    public bool ProcessDimensionStyles { get; set; } = true;
    public bool ProcessTextStyles { get; set; }
    public bool ProcessAnnotationScales { get; set; }
    public bool AutoPurge { get; set; } = true;
    public string ExpansionDrawingPosition { get; set; } = "Ignore";
    public string InputBehavior { get; set; } = "SettingsThenSelect";
}
