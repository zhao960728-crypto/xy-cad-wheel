using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace XiaoYang.AutoCADPlugin;

internal static class NumericCommandRegression
{
    internal static void VerifyWorkbook(Action<bool, string> check, string path)
    {
        var parsed = CommandImportService.Read(path);
        check(parsed.Errors.Count == 0, "provided XLSX parses without row errors");
        check(parsed.Rows.Count > 0, "provided XLSX contains commands: " + parsed.Rows.Count);
        foreach (var row in parsed.Rows)
            check(CadCommandCatalog.IsSafeCommandName(row.CommandName), "provided XLSX row " + row.SourceRow + ": " + row.CommandName);
    }

    internal static void Run(Action<bool, string> check, string output)
    {
        var names = new[] { "0", "1", "2", "3", "01", "3DORBIT", "123ABC", "SUN-173", "MY_CMD", "-LAYER", "+VPORTS" };
        foreach (var name in names)
        {
            check(CadCommandCatalog.IsSafeCommandName(name), "accept single command " + name);
            check(CadCommandExecutor.BuildInteractiveCommandLine(name, false) == name + " ", "preserve CAD resolution and numeric spelling " + name);
        }
        foreach (var name in new[] { "", " ", "+", "-", "1 2", "0;ERASE", "0\nERASE", "0\tERASE", "(load x)", "^C^C0", "0/1", "0,1", "0.1", "0\"", "0\0", "0\\1" })
            check(!CadCommandCatalog.IsSafeCommandName(name), "reject multiple commands/script " + name);

        // Exercise real XLSX parsing: numeric cells as Excel writes them, plus
        // text commands whose leading zeros must not be coerced into numbers.
        var path = Path.Combine(output, "numeric-command-fixture.xlsx");
        CommandImportService.WriteTemplate(path);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);
            XNamespace ns = doc.Root.Name.Namespace;
            var data = doc.Root.Element(ns + "sheetData");
            data.Elements(ns + "row").Skip(1).Remove();
            for (var i = 0; i < names.Length; i++)
            {
                var cell = i < 4
                    ? new XElement(ns + "c", new XAttribute("r", "A" + (i + 2)), new XElement(ns + "v", names[i]))
                    : new XElement(ns + "c", new XAttribute("r", "A" + (i + 2)), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", names[i])));
                data.Add(new XElement(ns + "row", new XAttribute("r", i + 2), cell));
            }
            entry.Delete();
            using (var stream = archive.CreateEntry("xl/worksheets/sheet1.xml").Open()) doc.Save(stream);
        }
        var parsed = CommandImportService.Read(path);
        check(parsed.Errors.Count == 0 && parsed.Rows.Select(r => r.CommandName).SequenceEqual(names), "XLSX numeric/text cells and leading zeros preserved");
        check(parsed.Rows.All(r => CadCommandCatalog.IsSafeCommandName(r.CommandName)), "all parsed numeric commands pass import validation");
        var config = ConfigStore.CreateDefault();
        config.CustomCommands.Clear();
        foreach (var row in parsed.Rows)
        {
            config.CustomCommands.Add(new CustomCommandConfig { Name = row.Name, CommandName = row.CommandName, Category = "数字插件" });
            config.CommandDisplayNameOverrides[row.CommandName] = "名称" + row.CommandName;
            config.CommandCategoryOverrides[row.CommandName] = "数字插件";
        }
        ConfigStore.Normalize(config);
        var reloaded = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));
        ConfigStore.Normalize(reloaded);
        check(reloaded.CustomCommands.Select(c => c.CommandName).SequenceEqual(names), "save/reload preserves numeric commands and leading zeros");
        check(names.All(n => reloaded.CommandDisplayNameOverrides[n] == "名称" + n && reloaded.CommandCategoryOverrides[n] == "数字插件"), "save/reload preserves numeric display names and categories");
    }
}
