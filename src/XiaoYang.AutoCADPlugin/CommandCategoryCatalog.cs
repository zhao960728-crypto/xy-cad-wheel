namespace XiaoYang.AutoCADPlugin;

internal static class CommandCategoryCatalog
{
    public const string DefaultCustomCategory = "自定义命令";
    public const string ThirdPartyCategory = "第三方命令";
    public const string YanxiuToolboxCategory = "燕秀工具箱";
    public const string YanxiuMold2dCategory = "燕秀自动出图";

    public static IReadOnlyList<string> BuiltInCategories { get; } = new[]
    {
        "常用命令", PluginCommandCatalog.CategoryName, ThirdPartyCategory,
        "绘图", "修改", "图层与特性", "标注与文字",
        "块与参照", "视图与导航", "选择与捕捉", "文件与输出", "其他"
    };

    public static bool IsBuiltIn(string name) =>
        BuiltInCategories.Contains((name ?? "").Trim(), StringComparer.CurrentCultureIgnoreCase);

    public static IReadOnlyList<string> GetAll(PluginConfig config)
    {
        var custom = (config.CommandCategories ?? new List<string>())
            .Concat((config.CustomCommands ?? new List<CustomCommandConfig>()).Select(command => command.Category))
            .Concat((config.CommandCategoryOverrides ?? new Dictionary<string, string>()).Values)
            .Append(DefaultCustomCategory)
            .Select(NormalizeName)
            .Where(name => name.Length > 0 && !IsBuiltIn(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase);
        return BuiltInCategories.Concat(custom).ToArray();
    }

    public static string NormalizeName(string value)
    {
        var text = (value ?? "").Trim();
        return text.Length > 24 ? text.Substring(0, 24) : text;
    }

    public static string Categorize(CadCommandInfo command)
    {
        if (command?.Group?.Equals(PluginCommandCatalog.GroupName, StringComparison.OrdinalIgnoreCase) == true)
            return PluginCommandCatalog.CategoryName;
        if (command == null) return "其他";
        if (!command.IsCoreCommand || HasKnownThirdPartyIdentity(command))
            return ThirdPartyCategory;
        var name = command.GlobalName.ToUpperInvariant();
        var text = (command.DisplayName + " " + command.Help).ToUpperInvariant();
        if (new[] { "LINE", "PLINE", "CIRCLE", "COPY", "MOVE", "TRIM", "OFFSET", "LAYER", "ZOOM", "QSAVE", "UNDO", "REGEN" }.Contains(name))
            return "常用命令";
        if (StartsWithAny(name, "LINE", "PLINE", "CIRCLE", "ARC", "RECT", "POLYGON", "ELLIPSE", "SPLINE", "HATCH", "DONUT", "POINT", "XLINE", "RAY", "MLINE"))
            return "绘图";
        if (StartsWithAny(name, "MOVE", "COPY", "ROTATE", "SCALE", "TRIM", "EXTEND", "OFFSET", "MIRROR", "FILLET", "CHAMFER", "STRETCH", "ARRAY", "ERASE", "EXPLODE", "JOIN", "BREAK", "ALIGN", "LENGTHEN", "PEDIT", "SPLINEDIT"))
            return "修改";
        if (StartsWithAny(name, "LAYER", "LAY", "PROPERT", "MATCHPROP", "COLOR", "LINETYPE", "LWEIGHT", "CHPROP") || text.Contains("图层") || text.Contains("特性"))
            return "图层与特性";
        if (StartsWithAny(name, "DIM", "TEXT", "MTEXT", "MLEADER", "LEADER", "TABLE", "STYLE", "TOLERANCE", "CENTER") || text.Contains("标注") || text.Contains("文字"))
            return "标注与文字";
        if (StartsWithAny(name, "INSERT", "BLOCK", "WBLOCK", "XREF", "REF", "BEDIT", "ATT", "IMAGE", "PDFATTACH", "DGNATTACH") || text.Contains("块") || text.Contains("参照"))
            return "块与参照";
        if (StartsWithAny(name, "ZOOM", "PAN", "VIEW", "VPOINT", "ORBIT", "NAV", "REGEN", "REDRAW", "UCS", "PLAN") || text.Contains("视图") || text.Contains("导航"))
            return "视图与导航";
        if (StartsWithAny(name, "SELECT", "QSELECT", "FILTER", "OSNAP", "SNAP", "DSETTINGS", "ISOLATE", "HIDEOBJECTS") || text.Contains("选择") || text.Contains("捕捉"))
            return "选择与捕捉";
        if (StartsWithAny(name, "QSAVE", "SAVE", "OPEN", "NEW", "PLOT", "PUBLISH", "EXPORT", "IMPORT", "ETRANSMIT", "RECOVER", "AUDIT", "PURGE") || text.Contains("打印") || text.Contains("输出"))
            return "文件与输出";
        return "其他";
    }

    public static string Categorize(CadCommandInfo command, PluginConfig config)
    {
        if (command?.Group?.Equals(PluginCommandCatalog.GroupName, StringComparison.OrdinalIgnoreCase) == true)
            return PluginCommandCatalog.CategoryName;
        // 第三方命令的身份高于遗留分类覆盖，避免 YX_Q 等外部命令被旧配置
        // 重新显示成 CAD 原生分类。这里只忽略显示覆盖，不删除用户配置。
        if (command == null || !command.IsCoreCommand || HasKnownThirdPartyIdentity(command))
            return command == null ? "其他" : ThirdPartyCategory;
        if (config?.CommandCategoryOverrides != null
            && config.CommandCategoryOverrides.TryGetValue(command.GlobalName, out var category))
        {
            var normalized = NormalizeName(category);
            if (normalized.Length > 0) return normalized;
        }
        return Categorize(command);
    }

    internal static bool HasKnownThirdPartyIdentity(CadCommandInfo command)
    {
        if (command == null) return false;
        var group = (command.Group ?? "").Trim();
        return IsKnownThirdPartyCommandName(command.GlobalName)
               || group.Equals("LOCAL_RUNTIME", StringComparison.OrdinalIgnoreCase)
               || group.IndexOf("YANXIU", StringComparison.OrdinalIgnoreCase) >= 0
               || group.IndexOf("燕秀", StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    internal static bool IsKnownThirdPartyCommandName(string value)
    {
        var name = CadCommandCatalog.NormalizeCommandName(value);
        return name.StartsWith("YX_", StringComparison.OrdinalIgnoreCase)
               || name.Equals("SUN", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("SUN-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithAny(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
