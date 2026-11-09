using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XiaoYang.AutoCADPlugin;

internal static class ExtensionCommandDiscovery
{
    internal static bool IsCommandName(string command) => !string.IsNullOrEmpty(command) && command.Length <= 128 && command.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

    internal static List<string> Read(string path, out string status)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { status = "文件不存在；保留加载入口。"; return new List<string>(); }
            if (info.Length > 64 * 1024 * 1024) { status = "文件过大，未静态扫描；可手动填写命令或仅加载。"; return new List<string>(); }
            List<string> names;
            switch (info.Extension.ToLowerInvariant())
            {
                case ".lsp": names = ParseLisp(File.ReadAllText(path, Encoding.Default)); break;
                case ".dll": names = ExtensionDllMetadata.Read(File.ReadAllBytes(path)); break;
                default: status = "此格式不能可靠静态识别命令；可手动填写已知命令，留空仅加载。"; return new List<string>();
            }
            status = names.Count > 0 ? "静态读取到 " + names.Count + " 个命令；加载成功后才能调用。" : "未读取到可靠命令；已提供仅加载入口，可手动填写已知命令。";
            return names;
        }
        catch (Exception ex) { status = "静态读取失败：" + ex.Message + "；可仅加载或手动填写。"; return new List<string>(); }
    }

    internal static List<string> ParseLisp(string text)
    {
        // Tokenize without evaluating Lisp. Quoted lists, comments and strings cannot define commands.
        var tokens = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == ';')
            {
                if (i + 1 < text.Length && text[i + 1] == '|')
                {
                    var end = text.IndexOf("|;", i + 2, StringComparison.Ordinal); i = end < 0 ? text.Length : end + 2;
                }
                else { while (i < text.Length && text[i] != '\n') i++; }
                continue;
            }
            if (c == '"')
            {
                i++;
                while (i < text.Length) { if (text[i] == '\\') { i += 2; continue; } if (text[i++] == '"') break; }
                tokens.Add("<string>"); continue;
            }
            if (c == '(' || c == ')' || c == '\'') { tokens.Add(c.ToString()); i++; continue; }
            var start = i++;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && "();\"'".IndexOf(text[i]) < 0) i++;
            tokens.Add(text.Substring(start, i - start));
        }
        var names = new List<string>();
        var quoted = new Stack<bool>();
        var quoteNext = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "'") { quoteNext = true; continue; }
            if (tokens[i] == "(")
            {
                var isQuoted = quoteNext || quoted.Count > 0 && quoted.Peek();
                quoteNext = false;
                // (quote (...)) and (function (...)) are data, too.
                if (i + 1 < tokens.Count && (tokens[i + 1].Equals("quote", StringComparison.OrdinalIgnoreCase) || tokens[i + 1].Equals("function", StringComparison.OrdinalIgnoreCase))) isQuoted = true;
                quoted.Push(isQuoted);
                if (!isQuoted && i + 3 < tokens.Count && tokens[i + 1].Equals("defun", StringComparison.OrdinalIgnoreCase) && tokens[i + 2].StartsWith("c:", StringComparison.OrdinalIgnoreCase) && tokens[i + 3] == "(")
                {
                    var name = tokens[i + 2].Substring(2);
                    if (IsCommandName(name)) names.Add(name.ToUpperInvariant());
                }
            }
            else if (tokens[i] == ")") { if (quoted.Count > 0) quoted.Pop(); quoteNext = false; }
            else quoteNext = false;
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    }
}

