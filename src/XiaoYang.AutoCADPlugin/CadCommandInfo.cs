namespace XiaoYang.AutoCADPlugin;

internal sealed class CadCommandInfo
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string GlobalName { get; init; } = "";
    public string Macro { get; init; } = "";
    public string Help { get; init; } = "";
    public string Group { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public bool IsCoreCommand { get; init; }
    public bool CanCallDirectly => !string.IsNullOrWhiteSpace(GlobalName);
}
