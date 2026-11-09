using System.Reflection;
using System.Text.RegularExpressions;
using System.IO;
#if ZWCAD
using System.IO.Compression;
using System.Xml.Linq;
using ZwSoft.ZwCAD.DatabaseServices;
using AcApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#else
using Autodesk.AutoCAD.Customization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
#if AUTOCAD_PRE2013
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif
#endif

namespace XiaoYang.AutoCADPlugin;

internal sealed class CadCommandCatalog
{
    internal const bool StartupIncludesLoadedPartialCuiFiles = true;
    internal const bool LocalCommandRestoreIsReadOnly = true;
    private static readonly Regex CommandToken = new(
        // Third-party commands and aliases may be numeric (0, 1, 01) or
        // start with a digit (3D...). Keep the single-token character boundary.
        @"^(?<name>[+-]?[A-Za-z0-9][A-Za-z0-9_-]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PgpAliasPattern = new(
        @"^\s*(?<alias>[^;,\s]+)\s*,\s*\*?(?<command>[^,;\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
#if !ZWCAD
    private static readonly MethodInfo CoreCommandMethod = ResolveInternalUtilsMethod("IsCoreCommand");
#endif

    private readonly object _sync = new();
    private IReadOnlyList<CadCommandInfo> _cached = Array.Empty<CadCommandInfo>();
    private IReadOnlyList<CadCommandInfo> _indexedSnapshot;
    private HashSet<string> _commandNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _aliasesByAlias =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _aliasesByCommand =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public string CuiPath { get; private set; } = "";
    public string PgpPath { get; private set; } = "";
    public IReadOnlyList<string> SourcePaths { get; private set; } = Array.Empty<string>();
    public int PgpAliasCount
    {
        get { lock (_sync) return _aliasesByAlias.Count; }
    }
    public DateTime RefreshedAt { get; private set; }

    public bool IsLoaded
    {
        get
        {
            lock (_sync) return RefreshedAt != default;
        }
    }

    public IReadOnlyList<CadCommandInfo> GetAll(bool forceRefresh = false)
    {
        lock (_sync)
        {
            if (!forceRefresh && (RefreshedAt != default || _cached.Count > 0)) return _cached;
            // Mechanical and other vertical products expose many built-in commands
            // through loaded partial CUIX files. Read those sources on the first
            // snapshot as well as on a manual refresh; forceRefresh only controls
            // whether the existing snapshot may be reused.
            CacheSnapshot(ReadCommandSnapshot(includeLoadedPartials: StartupIncludesLoadedPartialCuiFiles));
            return _cached;
        }
    }

    public LocalCommandRestoreResult RestoreNativeCommands()
    {
        lock (_sync)
        {
            // This command is deliberately read-only. Earlier releases rewrote the
            // user's acad.pgp by guessing which targets were native; that could
            // remove valid Mechanical and dimension aliases. A local restore now
            // means "re-read the current host command sources" and never edits,
            // reloads or normalizes any user PGP/CUI/LSP file.
            CacheSnapshot(ReadCommandSnapshot(includeLoadedPartials: true));

            var nativeCount = _cached.Count(IsNativeOrPluginCommand);

            return new LocalCommandRestoreResult
            {
                Commands = _cached,
                CommandCount = _cached.Count,
                NativeCommandCount = nativeCount,
                ThirdPartyCommandCount = _cached.Count - nativeCount
            };
        }
    }

    public bool IsCommandDefined(string name)
    {
        name = NormalizeCommandName(name);
        if (name.Length == 0) return false;
        // 轮盘执行阶段只查询启动/手动刷新时形成的快照，绝不在用户点击格子时
        // 临时扫描 CUI，也不再使用各 AutoCAD 版本行为不一致的内部注册检查。
        lock (_sync)
        {
            // Publish the index with each completed snapshot. Also recognize a
            // replaced snapshot so offline fixtures and future snapshot sources
            // cannot retain command names from an earlier directory.
            if (!ReferenceEquals(_indexedSnapshot, _cached)) RebuildCommandNameIndex();
            return _commandNames.Contains(name);
        }
    }

    private void CacheSnapshot(IReadOnlyList<CadCommandInfo> snapshot)
    {
        _cached = snapshot;
        RebuildCommandNameIndex();
        RefreshedAt = DateTime.Now;
    }

    private void RebuildCommandNameIndex()
    {
        _commandNames = new HashSet<string>(_cached.Select(command => command.GlobalName),
            StringComparer.OrdinalIgnoreCase);
        _indexedSnapshot = _cached;
    }

    public IReadOnlyList<string> GetEquivalentCommandNames(string commandName)
    {
        var normalized = NormalizeCommandName(commandName);
        if (normalized.Length == 0) return Array.Empty<string>();
        lock (_sync)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized };
            if (_aliasesByAlias.TryGetValue(normalized, out var target))
            {
                result.Add(target);
                if (_aliasesByCommand.TryGetValue(target, out var targetAliases))
                    foreach (var alias in targetAliases) result.Add(alias);
            }
            if (_aliasesByCommand.TryGetValue(normalized, out var aliases))
                foreach (var alias in aliases) result.Add(alias);
            return result.ToArray();
        }
    }

    public IReadOnlyDictionary<string, string> GetPgpAliases()
    {
        lock (_sync)
            return _aliasesByAlias.ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<CadCommandInfo> ReadCommandSnapshot(bool includeLoadedPartials)
    {
        CuiPath = ResolveMainCuiPath();
        if (CuiPath.Length == 0 || !File.Exists(CuiPath))
            throw new FileNotFoundException($"找不到当前 {CadHostInfo.ProductName} 使用的主 CUIX 文件。", CuiPath);

#if ZWCAD
        var result = ReadZwcadMenuGroup(CuiPath);
        SourcePaths = new[] { CuiPath };
#else
        var mainSection = new CustomizationSection(CuiPath);
        var result = ReadAutoCadMenuGroup(mainSection, CuiPath, coreOnly: false);
        var sourcePaths = new List<string> { CuiPath };
        for (var index = 0; includeLoadedPartials && index < mainSection.PartialCuiFiles.Count; index++)
        {
            var partialPath = ResolveMenuFilePath(mainSection.PartialCuiFiles[index], CuiPath);
            if (partialPath.Length == 0
                || sourcePaths.Contains(partialPath, StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                var partial = new CustomizationSection(partialPath);
                result.AddRange(ReadAutoCadMenuGroup(partial, partialPath, coreOnly: false));
                sourcePaths.Add(partialPath);
            }
            catch (System.Exception ex)
            {
                Logger.Trace($"跳过无法读取的已加载局部 CUIX：{partialPath}；{ex.Message}");
            }
        }
        SourcePaths = sourcePaths.ToArray();
#endif

        PgpPath = ResolveActivePgpPath(CuiPath);
        _aliasesByAlias = ReadPgpAliases(PgpPath);
        _aliasesByCommand = _aliasesByAlias
            .GroupBy(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<string>)group.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        // 同一个全局命令在主/局部 CUI 中通常会出现多次（功能区、工具栏、旧版平板菜单等）。
        // 优先保留当前语言 CUI 中的中文、无预置参数的原生命令项，避免旧英文名称或
        // TABLET 宏覆盖用户真正看到的功能名称。
        var selected = result
            .Concat(PluginCommandCatalog.All)
            .Where(item => item.GlobalName.Length > 0)
            .GroupBy(item => item.GlobalName, StringComparer.OrdinalIgnoreCase)
            .Select(SelectBestLocalizedCandidate)
            .Select(AttachAliases)
            .ToList();

        // A PGP target can be valid even when a third-party command has no CUI
        // item. Keep it callable, but do not invent a localized name or source.
        var selectedNames = new HashSet<string>(selected.Select(command => command.GlobalName),
            StringComparer.OrdinalIgnoreCase);
        foreach (var target in _aliasesByCommand.Keys)
        {
            if (!selectedNames.Add(target)) continue;
            selected.Add(AttachAliases(CreatePgpOnlyCommand(target, PgpPath, IsCoreCommand(target))));
        }

        return selected
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.GlobalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

#if !ZWCAD
    private static List<CadCommandInfo> ReadAutoCadMenuGroup(CustomizationSection section, string sourcePath,
        bool coreOnly)
    {
        var result = new List<CadCommandInfo>();
        foreach (MacroGroup group in section.MenuGroup.MacroGroups)
        {
            foreach (MenuMacro menuMacro in group.MenuMacros)
            {
                foreach (Macro macro in menuMacro.Macros)
                {
                    var globalName = ExtractGlobalName(macro.CLICommand, macro.Command);
                    var isCore = globalName.Length > 0 && IsCoreCommand(globalName);
                    if (coreOnly && !isCore) continue;
                    result.Add(new CadCommandInfo
                    {
                        Id = string.IsNullOrWhiteSpace(menuMacro.ElementID)
                            ? $"{section.MenuGroupName}:{group.Name}:{macro.Name}:{globalName}"
                            : menuMacro.ElementID,
                        DisplayName = CleanDisplayName(macro.Name, globalName),
                        GlobalName = globalName,
                        Macro = macro.Command ?? "",
                        Help = macro.HelpString ?? "",
                        Group = section.MenuGroupName,
                        SourcePath = sourcePath,
                        IsCoreCommand = isCore
                    });
                }
            }
        }
        return result;
    }
#endif

    private CadCommandInfo AttachAliases(CadCommandInfo command)
    {
        var globalName = NormalizeCommandName(command.GlobalName);
        var aliases = _aliasesByCommand.TryGetValue(globalName, out var values)
            ? values
            : Array.Empty<string>();
        return new CadCommandInfo
        {
            Id = command.Id,
            DisplayName = command.DisplayName,
            GlobalName = globalName,
            Macro = command.Macro,
            Help = command.Help,
            Group = command.Group,
            SourcePath = command.SourcePath,
            Aliases = aliases,
            IsCoreCommand = command.IsCoreCommand
        };
    }

#if ZWCAD
    private static List<CadCommandInfo> ReadZwcadMenuGroup(string cuixPath)
    {
        using var archive = ZipFile.OpenRead(cuixPath);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.FullName.Equals("MenuGroup.cui", StringComparison.OrdinalIgnoreCase)
            || item.FullName.EndsWith("/MenuGroup.cui", StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new InvalidDataException("中望 CUIX 中缺少 MenuGroup.cui。");

        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("中望 CUIX 的 MenuGroup.cui 内容为空。");
        var menuGroupName = AttributeValue(root, "Name", "ZWCAD");
        var result = new List<CadCommandInfo>();
        foreach (var menuMacro in root.Descendants().Where(element => element.Name.LocalName == "MenuMacro"))
        {
            var group = menuMacro.Ancestors().FirstOrDefault(element => element.Name.LocalName == "MacroGroup");
            var groupName = group == null ? menuGroupName : AttributeValue(group, "Name", menuGroupName);
            var id = AttributeValue(menuMacro, "UID", "");
            foreach (var macro in menuMacro.Descendants().Where(element => element.Name.LocalName == "Macro"))
            {
                var name = ElementValue(macro, "Name");
                var cliCommand = ElementValue(macro, "CLICommand");
                var command = ElementValue(macro, "Command");
                var help = ElementValue(macro, "HelpString");
                var globalName = ExtractGlobalName(cliCommand, command);
                if (globalName.Length == 0) continue;
                result.Add(new CadCommandInfo
                {
                    Id = id.Length > 0 ? id : $"{groupName}:{name}:{globalName}",
                    DisplayName = CleanDisplayName(name, globalName),
                    GlobalName = globalName,
                    Macro = command,
                    Help = help,
                    Group = groupName,
                    SourcePath = cuixPath,
                    IsCoreCommand = IsCoreCommand(globalName)
                });
            }
        }
        return result;
    }

    private static string ElementValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value?.Trim() ?? "";

    private static string AttributeValue(XElement element, string localName, string fallback) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value?.Trim() ?? fallback;
#endif

    private static string ResolveMainCuiPath()
    {
        var value = Convert.ToString(AcApp.GetSystemVariable("MENUNAME"))?.Trim() ?? "";
        if (value.Length == 0) return "";
        if (!value.EndsWith(".cuix", StringComparison.OrdinalIgnoreCase)) value += ".cuix";
        if (File.Exists(value)) return Path.GetFullPath(value);

        try
        {
            var found = HostApplicationServices.Current.FindFile(value, HostApplicationServices.WorkingDatabase, FindFileHint.Default);
            return found ?? value;
        }
        catch { return value; }
    }

    private static string ResolveMenuFilePath(string value, string mainCuiPath)
    {
        var text = Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"'));
        if (text.Length == 0) return "";
        var candidates = new List<string>();
        if (Path.IsPathRooted(text)) candidates.Add(text);
        else
        {
            candidates.Add(Path.Combine(Path.GetDirectoryName(mainCuiPath) ?? "", text));
            foreach (var supportPath in CadHostInfo.GetSupportSearchPaths())
                candidates.Add(Path.Combine(supportPath, text));
        }
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        try
        {
            var found = HostApplicationServices.Current.FindFile(text,
                HostApplicationServices.WorkingDatabase, FindFileHint.Default);
            return !string.IsNullOrWhiteSpace(found) && File.Exists(found) ? Path.GetFullPath(found) : "";
        }
        catch { return ""; }
    }

    private static string ResolveActivePgpPath(string mainCuiPath)
    {
        try
        {
            var found = HostApplicationServices.Current.FindFile(CadHostInfo.PgpFileName,
                HostApplicationServices.WorkingDatabase, FindFileHint.Default);
            if (!string.IsNullOrWhiteSpace(found) && File.Exists(found)) return Path.GetFullPath(found);
        }
        catch { }

        var candidates = new List<string>();
        try
        {
            var roamingRoot = Convert.ToString(CadHostInfo.GetSystemVariable("ROAMABLEROOTPREFIX"))?.Trim() ?? "";
            if (roamingRoot.Length > 0)
            {
                candidates.Add(Path.Combine(roamingRoot, "Support", CadHostInfo.PgpFileName));
                candidates.Add(Path.Combine(roamingRoot, CadHostInfo.PgpFileName));
            }
        }
        catch { }
        candidates.AddRange(CadHostInfo.GetSupportSearchPaths()
            .Select(path => Path.Combine(path, CadHostInfo.PgpFileName)));
        candidates.Add(Path.Combine(Path.GetDirectoryName(mainCuiPath) ?? "", CadHostInfo.PgpFileName));
        return candidates.FirstOrDefault(File.Exists) is { } path ? Path.GetFullPath(path) : "";
    }

    private static IReadOnlyDictionary<string, string> ReadPgpAliases(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
            while (reader.ReadLine() is { } line)
            {
                if (line.TrimStart().StartsWith(";", StringComparison.Ordinal)) continue;
                var match = PgpAliasPattern.Match(line);
                if (!match.Success) continue;
                var alias = NormalizeCommandName(match.Groups["alias"].Value);
                var target = NormalizeCommandName(match.Groups["command"].Value);
                if (alias.Length == 0 || target.Length == 0) continue;
                // AutoCAD uses the last definition when an alias appears more than once.
                result[alias] = target;
            }
        }
        catch (System.Exception ex)
        {
            Logger.Error($"读取当前 {CadHostInfo.ProductName} PGP 快捷命令失败：{path}", ex);
        }
        return result;
    }

    internal static CadCommandInfo CreatePgpOnlyCommand(string target, string sourcePath, bool isCoreCommand)
    {
        target = NormalizeCommandName(target);
        return new CadCommandInfo
        {
            Id = "PGP:" + target,
            DisplayName = target,
            GlobalName = target,
            Macro = target,
            Help = "当前 PGP 快捷键",
            Group = target.StartsWith("YX_", StringComparison.OrdinalIgnoreCase) ? "YANXIU" : "PGP",
            SourcePath = sourcePath ?? "",
            IsCoreCommand = isCoreCommand
        };
    }

    private static string ExtractGlobalName(string cliCommand, string macro)
    {
        var direct = NormalizeCommandName(cliCommand);
        if (LooksLikeCommandName(direct)) return direct;

        var text = (macro ?? "").Trim();
        while (text.StartsWith("^C", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("^P", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2).TrimStart();

        if (text.Length == 0 || text[0] is '$' or '^' or '(' or ';') return "";
        while (text.Length > 0 && text[0] is '\'' or '.' or '_') text = text.Substring(1);
        var match = CommandToken.Match(text);
        return match.Success ? NormalizeCommandName(match.Groups["name"].Value) : "";
    }

    internal static string NormalizeCommandName(string value)
    {
        var name = (value ?? "").Trim();
        while (name.Length > 0 && (name[0] == '.' || name[0] == '_' || name[0] == '\'')) name = name.Substring(1);
        var separator = name.IndexOfAny(new[] { ' ', '\t', '\r', '\n', ';' });
        if (separator >= 0) name = name.Substring(0, separator);
        return name.Trim().ToUpperInvariant();
    }

    internal static bool IsSafeCommandName(string value)
    {
        var text = (value ?? "").Trim();
        var match = CommandToken.Match(text);
        return match.Success && match.Length == text.Length;
    }

    private static bool LooksLikeCommandName(string name) =>
        name.Length > 0 && name.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '+');

    private static string CleanDisplayName(string name, string globalName)
    {
        var display = (name ?? "").Replace("&", "").Trim();
        if (display.Length == 0) return globalName;
        return Regex.Replace(display, @"\s*[,，]\s*", " · ");
    }

    internal static CadCommandInfo SelectBestLocalizedCandidate(IEnumerable<CadCommandInfo> candidates) =>
        candidates
            .OrderByDescending(GetDisplayNameQuality)
            .ThenBy(item => item.DisplayName.Length)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .First();

    internal static IReadOnlyList<CadCommandInfo> SelectNativeCommands(IEnumerable<CadCommandInfo> commands) =>
        (commands ?? Array.Empty<CadCommandInfo>())
        .Where(IsNativeOrPluginCommand)
        .Where(command => command.GlobalName.Length > 0)
        .GroupBy(command => command.GlobalName, StringComparer.OrdinalIgnoreCase)
        .Select(SelectBestLocalizedCandidate)
        .OrderBy(command => command.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(command => command.GlobalName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static bool IsNativeOrPluginCommand(CadCommandInfo command)
    {
        if (command == null) return false;
        if (command.Group.Equals(PluginCommandCatalog.GroupName, StringComparison.OrdinalIgnoreCase)) return true;
        return command.IsCoreCommand && !CommandCategoryCatalog.HasKnownThirdPartyIdentity(command);
    }

    internal static int GetDisplayNameQuality(CadCommandInfo item)
    {
        var display = item?.DisplayName?.Trim() ?? "";
        var globalName = item?.GlobalName?.Trim() ?? "";
        var help = item?.Help?.Trim() ?? "";
        var score = 0;

        if (ContainsCjk(display)) score += 200;
        if (ContainsCjk(help)) score += 25;
        if (IsBareCommandMacro(item?.Macro, globalName)) score += 80;
        if (display.Length is >= 2 and <= 32) score += 10;
        if (display.Equals(globalName, StringComparison.OrdinalIgnoreCase)) score -= 80;
        if (display.IndexOf("TABLET", StringComparison.OrdinalIgnoreCase) >= 0
            || display.IndexOf("^C", StringComparison.OrdinalIgnoreCase) >= 0
            || display.StartsWith("TB_", StringComparison.OrdinalIgnoreCase)
            || display.StartsWith("ID_", StringComparison.OrdinalIgnoreCase))
            score -= 120;

        return score;
    }

    internal static bool ShouldReplaceStoredDisplayName(string label, string globalName)
    {
        var display = (label ?? "").Trim();
        var command = NormalizeCommandName(globalName);
        if (display.Length == 0) return true;
        if (NormalizeCommandName(display).Equals(command, StringComparison.OrdinalIgnoreCase)) return true;
        return display.IndexOf("TABLET", StringComparison.OrdinalIgnoreCase) >= 0
               || display.IndexOf("^C", StringComparison.OrdinalIgnoreCase) >= 0
               || display.StartsWith("TB_", StringComparison.OrdinalIgnoreCase)
               || display.StartsWith("ID_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCjk(string value) =>
        (value ?? "").Any(ch => ch is >= '\u3400' and <= '\u9FFF');

    private static bool IsBareCommandMacro(string macro, string globalName)
    {
        var text = (macro ?? "").Trim();
        while (text.StartsWith("^C", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("^P", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2).TrimStart();
        while (text.Length > 0 && text[0] is '\'' or '.' or '_') text = text.Substring(1);

        var match = CommandToken.Match(text);
        if (!match.Success
            || !NormalizeCommandName(match.Groups["name"].Value).Equals(globalName, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = text.Substring(match.Length).Trim();
        return remainder.Length == 0 || remainder.All(ch => ch == ';');
    }

    private static bool IsCoreCommand(string name)
    {
        if (CommandCategoryCatalog.IsKnownThirdPartyCommandName(name)) return false;
#if ZWCAD
        return true;
#else
        try
        {
            return CoreCommandMethod != null
                   && (bool)CoreCommandMethod.Invoke(null, new object[] { name });
        }
        catch { return false; }
#endif
    }

    private static MethodInfo ResolveInternalUtilsMethod(string name)
    {
#if ZWCAD
        return null;
#else
        var type = Type.GetType("Autodesk.AutoCAD.Internal.Utils, AcMgd", false);
        return type?.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(string) }, null);
#endif
    }
}

internal sealed class LocalCommandRestoreResult
{
    public IReadOnlyList<CadCommandInfo> Commands { get; init; } = Array.Empty<CadCommandInfo>();
    public int CommandCount { get; init; }
    public int NativeCommandCount { get; init; }
    public int ThirdPartyCommandCount { get; init; }
}
