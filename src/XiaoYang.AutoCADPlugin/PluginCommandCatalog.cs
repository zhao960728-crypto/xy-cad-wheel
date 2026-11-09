namespace XiaoYang.AutoCADPlugin;

internal static class PluginCommandCatalog
{
    public const string GroupName = "XYCADMENU";
    public const string CategoryName = "小阳轮盘";

    public static IReadOnlyList<CadCommandInfo> All { get; } = new[]
    {
        new CadCommandInfo
        {
            Id = "XYCAD_IME_SETTINGS",
            DisplayName = "输入法自动切换设置",
            GlobalName = "XYLPIME",
            Macro = "XYLPIME",
            Help = "打开 CAD 输入法自动切换配置，并可添加到轮盘。",
            Group = GroupName
        },
        new CadCommandInfo
        {
            Id = "XYCAD_RESTORE_LOCAL_COMMANDS",
            DisplayName = "重新读取本地命令",
            GlobalName = "XYRESTORELOCALCMDS",
            Macro = "XYRESTORELOCALCMDS",
            Help = "重新读取当前主 CUI、已加载局部 CUIX 和 PGP 命令快照；PGP 始终只读，不修改任何快捷键或用户配置。",
            Group = GroupName
        },
    };
}
