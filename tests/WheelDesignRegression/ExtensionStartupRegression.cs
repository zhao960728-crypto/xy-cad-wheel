using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XiaoYang.AutoCADPlugin;

internal static class ExtensionStartupRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        var lisp = "; (defun c:COMMENT () nil)\n;| (defun c:BLOCK () nil) |;\n"
            + "(setq s \"(defun c:STRING () nil)\")\n'(defun c:QUOTED () nil)\n"
            + "(quote (defun c:DATA () nil))\n(defun c:REAL-ONE (/ x) (princ))\n"
            + "(progn (defun C:REAL_TWO () (princ)))\n(defun c:REAL-ONE () nil)\n"
            + "(defun c:中文命令 () (princ))\n(defun c:BAD$NAME () nil)";
        var commands = ExtensionCommandDiscovery.ParseLisp(lisp);
        check(commands.Count == 3 && commands.Contains("REAL-ONE") && commands.Contains("REAL_TWO") && commands.Contains("中文命令"), "LSP 只提取真实 defun C:，排除注释、字符串、引用数据并去重");
        check(ExtensionCommandDiscovery.IsCommandName("中文命令_2-TEST"), "支持中文及英文单命令名");
        foreach (var invalid in new[] { "A B", "A\nB", "(load x)", "A;B", "A\"B", "", "C:RUN" })
            check(!ExtensionCommandDiscovery.IsCommandName(invalid), "拒绝启动命令注入: " + invalid.Replace("\n", "\\n"));
        check(ExtensionCommandDiscovery.ParseLisp("; unfinished comment").Count == 0 && ExtensionCommandDiscovery.ParseLisp(";| unfinished block").Count == 0, "未闭合注释不会误发现命令");
        var malformedRejected = false;
        try { ExtensionDllMetadata.Read(new byte[] { 0, 1, 2, 3 }); }
        catch (InvalidDataException) { malformedRejected = true; }
        check(malformedRejected, "损坏 DLL 元数据拒绝，不执行文件");
        var dllCommands = ExtensionCommandDiscovery.Read(typeof(ExtensionLoadService).Assembly.Location, out var dllStatus);
        check(dllCommands.Contains(ExtensionLoadService.StartupEntryCommand), "从程序集 PE 元数据识别真实 CommandMethod 启动入口");

        var extension = new ManagedExtensionConfig { Id = Guid.NewGuid().ToString("N"), Name = "回归扩展", FilePath = Path.Combine(Path.GetTempPath(), "XY-extension-regression.lsp"), StartupCommand = "中文命令", Enabled = true };
        check(ExtensionLoadService.IsMatchingRequest(extension, extension.Id, extension.FilePath, "中文命令"), "启用且ID路径命令一致允许请求");
        extension.Enabled = false;
        check(!ExtensionLoadService.IsMatchingRequest(extension, extension.Id, extension.FilePath, "中文命令"), "未启用扩展禁止启动/加载");
        extension.Enabled = true;
        check(!ExtensionLoadService.IsMatchingRequest(extension, Guid.NewGuid().ToString("N"), extension.FilePath, "中文命令"), "过期或其他扩展ID拒绝");
        check(!ExtensionLoadService.IsMatchingRequest(extension, extension.Id, extension.FilePath + ".changed", "中文命令"), "路径变化后旧请求拒绝");
        check(!ExtensionLoadService.IsMatchingRequest(extension, extension.Id, extension.FilePath, "OTHER"), "启动命令变化后旧请求拒绝");
        var wrapper = ExtensionLoadService.CreateStartupCommand(extension);
        check(wrapper.CommandName == ExtensionLoadService.StartupEntryCommand && wrapper.Arguments.SequenceEqual(new[] { extension.Id }), "生成入口仅携带扩展ID，运行时重新解析保存的扩展设置");
        var cell = new MenuItemConfig { Kind = ActionKinds.CustomCommand, Value = wrapper.Id, Label = wrapper.Name };
        check(cell.Value == "extension-" + extension.Id && !wrapper.CommandName.Contains("中文命令"), "轮盘绑定稳定自定义入口，不把插件命令当作固定加载宏");
        var line = ExtensionLoadService.BuildStartupEntryLine(extension.Id);
        check(line == ExtensionLoadService.StartupEntryCommand + " " + extension.Id + " ", "入口命令行仅由受控命令名与GUID构成");
        var idRejected = false;
        try { ExtensionLoadService.BuildStartupEntryLine(extension.Id + "\nERASE"); }
        catch (ArgumentException) { idRejected = true; }
        check(idRejected, "入口ID注入拒绝");
        extension.StartupCommand = "";
        var loadOnly = ExtensionLoadService.CreateStartupCommand(extension);
        check(loadOnly.Name.EndsWith(" · 加载", StringComparison.Ordinal) && loadOnly.CommandName == ExtensionLoadService.StartupEntryCommand, "无已知命令生成可用仅加载入口，不编造功能命令");
        extension.StartupCommand = "A B";
        check(!ExtensionLoadService.IsMatchingRequest(extension, extension.Id, extension.FilePath, "A B"), "保存配置中的非法命令仍无法通过启动保护");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            var fixture = Path.Combine(output, "static-discovery-only.lsp");
            File.WriteAllText(fixture, lisp, new UTF8Encoding(true));
            var fromFile = ExtensionCommandDiscovery.Read(fixture, out var lispStatus);
            check(fromFile.Count == 3 && fromFile.Contains("中文命令"), "LSP UTF8 BOM 文件静态解析保留中文命令");
            var missing = ExtensionCommandDiscovery.Read(Path.Combine(output, "does-not-exist.vlx"), out _);
            check(missing.Count == 0, "缺失或未知扩展不编造启动命令");
            File.WriteAllLines(Path.Combine(output, "extension-startup-checks.txt"), new[] { lispStatus, dllStatus, "LSP: " + string.Join(", ", commands), "DLL: " + string.Join(", ", dllCommands), "未执行、加载或启用任何测试扩展。" }, Encoding.UTF8);
        }
    }
}
