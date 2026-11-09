namespace XiaoYang.AutoCADPlugin;

internal static class ImeTextEditPolicy
{
    private static readonly HashSet<string> ExistingTextEditCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXTEDIT", "DDEDIT", "MTEDIT", "TEDIT",
        "ATTEDIT", "EATTEDIT", "ATTIPEDIT",
        "TABLEDIT", "MLEADERCONTENTEDIT", "DIMEDIT", "DIMTEDIT"
    };

    public static bool IsExistingTextEditCommand(string commandName)
    {
        var command = ImeCommandCatalog.Normalize(commandName);
        while (command.StartsWith("-", StringComparison.Ordinal)) command = command.Substring(1);
        return ExistingTextEditCommands.Contains(command);
    }

    public static bool? PreferChineseForExistingContents(IEnumerable<string> contents)
    {
        var hasReadableContent = false;
        foreach (var content in contents ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            hasReadableContent = true;
            if (ContainsChinese(content)) return true;
        }
        return hasReadableContent ? false : null;
    }

    public static bool ContainsChinese(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (var index = 0; index < value.Length; index++)
        {
            int codePoint = value[index];
            if (char.IsHighSurrogate(value[index])
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                codePoint = char.ConvertToUtf32(value[index], value[++index]);
            }
            if (IsChineseCodePoint(codePoint)) return true;
        }
        return false;
    }

    private static bool IsChineseCodePoint(int codePoint) =>
        (codePoint >= 0x3400 && codePoint <= 0x4DBF)
        || (codePoint >= 0x4E00 && codePoint <= 0x9FFF)
        || (codePoint >= 0xF900 && codePoint <= 0xFAFF)
        || (codePoint >= 0x20000 && codePoint <= 0x2FA1F);
}
