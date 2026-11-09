using System.IO.Compression;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace XiaoYang.AutoCADPlugin;

internal sealed record CommandImportRow(string Name, string CommandName, string Category, int SourceRow);

internal sealed class CommandImportResult
{
    public List<CommandImportRow> Rows { get; } = new();
    public List<string> Errors { get; } = new();
}

internal static class CommandImportService
{
    public const string TemplateFileName = "批量导入CAD快捷键模板.xlsx";
    private static readonly string[] FixedHeaders = { "执行命令", "功能描述", "归属分类" };

    public static CommandImportResult Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("找不到导入文件。", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".xlsx")
            throw new NotSupportedException($"请使用固定的 Excel 模板“{TemplateFileName}”，只支持 .xlsx 文件。");
        var rows = ReadXlsx(path);
        return ConvertRows(rows);
    }

    public static void WriteTemplate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("模板保存路径不能为空。", nameof(path));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        using var file = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);
        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="命令导入" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="2"><font><sz val="11"/><name val="Microsoft YaHei"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Microsoft YaHei"/></font></fonts>
              <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF0092AE"/><bgColor indexed="64"/></patternFill></fill></fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs>
              <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
            </styleSheet>
            """);
        WriteEntry(archive, "xl/worksheets/sheet1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>
              <cols><col min="1" max="1" width="22" customWidth="1"/><col min="2" max="2" width="28" customWidth="1"/><col min="3" max="3" width="22" customWidth="1"/></cols>
              <sheetData><row r="1">
                <c r="A1" s="1" t="inlineStr"><is><t>执行命令</t></is></c>
                <c r="B1" s="1" t="inlineStr"><is><t>功能描述</t></is></c>
                <c r="C1" s="1" t="inlineStr"><is><t>归属分类</t></is></c>
              </row></sheetData>
              <autoFilter ref="A1:C1"/>
            </worksheet>
            """);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static CommandImportResult ConvertRows(IReadOnlyList<IReadOnlyList<string>> source)
    {
        var result = new CommandImportResult();
        if (source.Count == 0) return result;

        if (source.Count == 0 || source[0].Count < FixedHeaders.Length)
            throw InvalidTemplate();
        for (var column = 0; column < FixedHeaders.Length; column++)
        {
            if (!NormalizeHeader(GetCell(source[0], column)).Equals(NormalizeHeader(FixedHeaders[column]), StringComparison.OrdinalIgnoreCase))
                throw InvalidTemplate();
        }

        for (var index = 1; index < source.Count; index++)
        {
            var row = source[index];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var command = GetCell(row, 0).Trim();
            var name = GetCell(row, 1).Trim();
            var category = GetCell(row, 2).Trim();
            if (command.Length == 0)
            {
                result.Errors.Add($"第 {index + 1} 行：执行命令为空");
                continue;
            }
            if (name.Length == 0) name = command;
            result.Rows.Add(new CommandImportRow(name, command, category, index + 1));
        }
        return result;
    }

    private static InvalidDataException InvalidTemplate() => new(
        $"表格模板不正确。第一行必须固定为：A1“{FixedHeaders[0]}”、B1“{FixedHeaders[1]}”、C1“{FixedHeaders[2]}”。请使用插件提供的“{TemplateFileName}”。");

    private static IReadOnlyList<IReadOnlyList<string>> ReadXlsx(string path)
    {
        // Excel keeps workbooks open while editing. FileShare.ReadWrite/Delete allows a
        // consistent read of the current saved content without asking the user to close it.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        var sharedStrings = ReadSharedStrings(archive);
        var sheet = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? throw new InvalidDataException("表格中没有可读取的工作表。");
        using var stream = sheet.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var rows = new List<IReadOnlyList<string>>();
        foreach (var rowElement in document.Descendants(ns + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in rowElement.Elements(ns + "c"))
            {
                var reference = (string)cell.Attribute("r") ?? "A1";
                var column = ColumnIndex(reference);
                var type = (string)cell.Attribute("t") ?? "";
                var raw = cell.Element(ns + "v")?.Value ?? "";
                var value = type switch
                {
                    "s" when int.TryParse(raw, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count => sharedStrings[sharedIndex],
                    "inlineStr" => string.Concat(cell.Descendants(ns + "t").Select(item => item.Value)),
                    _ => raw
                };
                cells[column] = value;
            }
            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var values = Enumerable.Range(0, width).Select(index => cells.TryGetValue(index, out var value) ? value : "").ToList();
            rows.Add(values);
        }
        return rows;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return new List<string>();
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToList();
    }

    private static int ColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var ch in cellReference.TakeWhile(char.IsLetter))
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return Math.Max(0, index - 1);
    }

    private static string NormalizeHeader(string value) =>
        new string((value ?? "").TrimStart('\uFEFF').Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-').ToArray()).ToLowerInvariant();

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] ?? "" : "";
}
