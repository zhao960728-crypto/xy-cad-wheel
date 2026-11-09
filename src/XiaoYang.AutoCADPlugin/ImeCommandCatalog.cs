namespace XiaoYang.AutoCADPlugin;

internal static class ImeCommandCatalog
{
    public static IReadOnlyList<string> Recommended { get; } = new[]
    {
        "TEXT", "DTEXT", "MTEXT",
        "TEXTEDIT", "DDEDIT", "MTEDIT",
        "ATTEDIT", "EATTEDIT", "ATTIPEDIT", "BATTMAN",
        "TABLEDIT", "TABLE", "TEDIT", "FIELD", "FIND",
        "MLEADERCONTENTEDIT", "DIMEDIT", "DIMTEDIT"
    };

    // Store the same canonical name that runtime events use. This lets users
    // paste either COMMAND or AutoLISP's C:COMMAND form without creating two
    // entries or losing the command during configuration normalization.
    public static string Normalize(string value) => NormalizeForMatch(value);

    public static bool IsTextCommand(ImeSwitchConfig config, string commandName,
        IEnumerable<string> equivalentNames = null)
    {
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddNormalized(observed, commandName);
        foreach (var equivalent in equivalentNames ?? Array.Empty<string>()) AddNormalized(observed, equivalent);
        return observed.Count > 0
               && config?.TextCommands?.Any(command => observed.Contains(NormalizeForMatch(command))) == true;
    }

    private static void AddNormalized(ISet<string> values, string commandName)
    {
        var normalized = NormalizeForMatch(commandName);
        if (normalized.Length > 0) values.Add(normalized);
    }

    private static string NormalizeForMatch(string value)
    {
        var normalized = CadCommandCatalog.NormalizeCommandName(value);
        // CAD hosts and third-party dispatchers can report the same command with
        // global/local prefixes, a command-line variant, or AutoLISP's C: name.
        // Canonicalize every command through one general rule; no command gets a
        // product-specific special case.
        var changed = true;
        while (changed && normalized.Length > 0)
        {
            changed = false;
            while (normalized.Length > 0 && normalized[0] is '.' or '_' or '\'')
            {
                normalized = normalized.Substring(1);
                changed = true;
            }
            while (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
                changed = true;
            }
            if (normalized.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(2);
                changed = true;
            }
        }
        // MT is the common PGP alias entered by users; AutoCAD normally reports
        // GlobalCommandName=MTEXT, but treating both as one command also covers
        // third-party dispatchers that forward the alias name unchanged.
        return normalized.Equals("MT", StringComparison.OrdinalIgnoreCase) ? "MTEXT" : normalized;
    }

    internal static string ExtractLispCommand(string firstLine)
    {
        var text = (firstLine ?? "").Trim();
        if (text.Length == 0) return "";

        var wrapped = text[0] == '(';
        if (wrapped) text = text.Substring(1).TrimStart();
        while (text.Length > 0 && text[0] is '\'' or '`') text = text.Substring(1).TrimStart();
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end])
                                 && text[end] != '(' && text[end] != ')') end++;
        if (end == 0) return "";
        var token = text.Substring(0, end);

        // LispWillStart also reports arbitrary expressions and DEFUN bodies.
        // Only a C: command symbol (or an unwrapped command-name event from a
        // compatible CAD host) is allowed into the command preference rules.
        if (token.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) return NormalizeForMatch(token);
        if (!wrapped && CadCommandCatalog.IsSafeCommandName(token)) return NormalizeForMatch(token);
        return "";
    }

    public static IReadOnlyList<string> CustomCommands(ImeSwitchConfig config)
    {
        var recommended = new HashSet<string>(Recommended, StringComparer.OrdinalIgnoreCase);
        return (config?.TextCommands ?? new List<string>())
            .Select(Normalize)
            .Where(command => command.Length > 0 && !recommended.Contains(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
