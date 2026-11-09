namespace XiaoYang.AutoCADPlugin;

internal sealed class CustomCommandConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "自定义命令";
    public string DisplayNameOverride { get; set; } = "";
    public string CommandName { get; set; } = "";
    public string Category { get; set; } = CommandCategoryCatalog.DefaultCustomCategory;
    public List<string> Arguments { get; set; } = new();
    public string IconPath { get; set; } = "";
    public bool ShowIconOnly { get; set; }
    public bool Silent { get; set; } = true;
    public bool DisableDialogs { get; set; }
}
