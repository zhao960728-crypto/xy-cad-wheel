using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
#if ZWCAD
using ZwSoft.ZwCAD.Runtime;
using CadResultBuffer = ZwSoft.ZwCAD.DatabaseServices.ResultBuffer;
using CadPromptStatus = ZwSoft.ZwCAD.EditorInput.PromptStatus;
using AcApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadDocument = ZwSoft.ZwCAD.ApplicationServices.Document;
using CadDocumentEventArgs = ZwSoft.ZwCAD.ApplicationServices.DocumentCollectionEventArgs;
using CadObjectId = ZwSoft.ZwCAD.DatabaseServices.ObjectId;
#else
using Autodesk.AutoCAD.Runtime;
using CadResultBuffer = Autodesk.AutoCAD.DatabaseServices.ResultBuffer;
using CadPromptStatus = Autodesk.AutoCAD.EditorInput.PromptStatus;
#if AUTOCAD_PRE2013
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif
using CadDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using CadDocumentEventArgs = Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs;
using CadObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
#endif
using SystemException = System.Exception;

[assembly: CommandClass(typeof(XiaoYang.AutoCADPlugin.ExtensionLoadService))]

namespace XiaoYang.AutoCADPlugin;

// Uses the host's supported LOAD / NETLOAD / ARXLOAD entry points, preserving
// SECURELOAD and trusted-location checks. No support paths, startup groups,
// registry entries or process-wide dependency resolvers are installed here.
internal sealed class ExtensionLoadService : IDisposable
{
    private static readonly ConcurrentDictionary<string, string> StatusById = new(StringComparer.OrdinalIgnoreCase);
    private static ExtensionLoadService? _current;
    private readonly HashSet<string> _processAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _processLoaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CadDocument, HashSet<string>> _documentAttempts = new();
    private readonly Dictionary<CadDocument, HashSet<string>> _documentLoaded = new();
    private readonly Dictionary<string, bool> _lastEnabled = new(StringComparer.OrdinalIgnoreCase);
    private Func<PluginConfig>? _configProvider;
    private PendingLoad? _pending;
    private bool _started;
    private DateTime _nextCheck;
    private long _documentEpoch;

    private sealed class PendingLoad
    {
        public string Token = Guid.NewGuid().ToString("N");
        public string Id = "";
        public string Path = "";
        public bool LaunchRequested;
        public string StartupCommand = "";
        public CadDocument Document = null!;
        public DateTime QueuedAt = DateTime.UtcNow;
        public long DocumentEpoch;
        public CadObjectId[] Selection = Array.Empty<CadObjectId>();
        public bool RequiresSelectionReentry;
    }

    public void Start(Func<PluginConfig> configProvider)
    {
        if (_started) return;
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _current = this;
        AcApp.Idle += OnIdle;
        AcApp.DocumentManager.DocumentCreated += OnDocumentCreated;
        AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentClosing;
        AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
        _started = true;
    }

    public static bool IsSupported(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath ?? "").ToLowerInvariant();
            return ext == ".lsp" || ext == ".fas" || ext == ".vlx" || ext == ".dll"
#if ZWCAD
                || ext == ".zrx";
#else
                || ext == ".arx";
#endif
        }
        catch (SystemException) { return false; }
    }

    public static string GetStatus(string id) => StatusById.TryGetValue(id ?? "", out var status) ? status : "待应用 / 待加载";

    public string Status(string path)
    {
        var item = _configProvider?.Invoke()?.ManagedExtensions?.FirstOrDefault(e => SamePath(e.FilePath, path));
        return item == null ? "未登记" : GetStatus(item.Id);
    }

    internal const string StartupEntryCommand = "XYLPSTARTEXTENSION";

    internal static bool IsMatchingRequest(ManagedExtensionConfig item, string id, string path, string command)
    {
        if (item == null || !item.Enabled || !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) || !SamePath(item.FilePath, path)) return false;
        var saved = (item.StartupCommand ?? "").Trim();
        return command == null || (saved.Length == 0 || ExtensionCommandDiscovery.IsCommandName(saved)) && string.Equals(saved, command, StringComparison.OrdinalIgnoreCase);
    }

    internal static CustomCommandConfig CreateStartupCommand(ManagedExtensionConfig extension)
    {
        BuildStartupEntryLine(extension.Id);
        var command = (extension.StartupCommand ?? "").Trim();
        if (command.Length > 0 && !ExtensionCommandDiscovery.IsCommandName(command)) throw new ArgumentException("启动命令格式无效。");
        return new CustomCommandConfig
        {
            Id = "extension-" + extension.Id,
            Name = extension.Name + (command.Length == 0 ? " · 加载" : " · " + command),
            CommandName = StartupEntryCommand,
            Arguments = new List<string> { extension.Id },
            DisableDialogs = false,
            Silent = false,
            Category = CommandCategoryCatalog.DefaultCustomCategory
        };
    }
    internal static string BuildStartupEntryLine(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("扩展入口 ID 无效。");
        return StartupEntryCommand + " " + id + " ";
    }

    internal static void QueueStartupEntry(string id, string path, string command)
    {
        var service = _current ?? throw new InvalidOperationException("扩展加载服务尚未启动。");
        if (!service._configProvider().ManagedExtensions.Any(x => IsMatchingRequest(x, id, path, command)))
            throw new InvalidOperationException("请先勾选启用并点击“应用并保存”，保存当前启动命令后再运行。");
        var doc = AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("请先打开 CAD 图纸。");
        if (!IsIdleForeground(doc)) throw new InvalidOperationException("请等待当前 CAD 命令结束后重试。");
        doc.SendStringToExecute(BuildStartupEntryLine(id), false, false, true);
    }

    [CommandMethod(StartupEntryCommand, CommandFlags.Redraw)]
    public static void StartExtensionCommand()
    {
        var service = _current;
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (service == null || doc == null || !IsHostForeground()) return;
        var input = doc.Editor.GetString("\n扩展入口 ID: ");
        if (input.Status != CadPromptStatus.OK) return;
        var id = (input.StringResult ?? "").Trim();
        try
        {
            BuildStartupEntryLine(id);
            var entry = service._configProvider().ManagedExtensions.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (entry == null || !entry.Enabled) throw new InvalidOperationException("扩展不存在或未启用，请在功能管理器启用并保存。");
            var command = (entry.StartupCommand ?? "").Trim();
            if (command.Length > 0 && !ExtensionCommandDiscovery.IsCommandName(command)) throw new InvalidOperationException("启动命令无效。");
            var path = NormalizePath(entry.FilePath);
            if (!IsSupported(path) || !File.Exists(path)) throw new InvalidOperationException("扩展文件不存在或类型不支持。");
            if (service._pending != null) throw new InvalidOperationException("已有扩展正在加载，请完成或取消后重试。");
            var selected = doc.Editor.SelectImplied();
            var request = new PendingLoad
            {
                Id = entry.Id, Path = path, Document = doc, LaunchRequested = true, StartupCommand = command,
                DocumentEpoch = service._documentEpoch,
                Selection = selected.Status == CadPromptStatus.OK && selected.Value != null
                    ? selected.Value.GetObjectIds() : Array.Empty<CadObjectId>()
            };
            if (service.IsLoaded(path, doc)) service.QueueStartup(request);
            else
            {
                // LOAD/NETLOAD/ARXLOAD can consume PickFirst and can run arbitrary
                // extension initialization, including prompts and commands. There
                // is no safe way to distinguish that from a user's deselection.
                // Never restore an old selection across this boundary. Loading
                // still completes; a second invocation uses the Redraw entry above.
                request.RequiresSelectionReentry = request.Selection.Length > 0;
                service._pending = request;
                SetStatus(id, "等待原生信任检查，加载成功后启动所选命令");
                doc.SendStringToExecute(BuildLoadExpression(request.Token, path), false, false, false);
            }
        }
        catch (SystemException ex) { SetStatus(id, "启动未完成：" + ex.Message); doc.Editor.WriteMessage("\n" + ex.Message); }
    }

    private void QueueStartup(PendingLoad request)
    {
        if (!_started || request.DocumentEpoch != _documentEpoch || AcApp.DocumentManager.MdiActiveDocument != request.Document || !IsHostForeground() || !_configProvider().ManagedExtensions.Any(x => IsMatchingRequest(x, request.Id, request.Path, request.StartupCommand)))
        {
            SetStatus(request.Id, "已加载；启动请求因配置或当前文档改变而取消");
            return;
        }
        if (request.StartupCommand.Length == 0) return;
        var current = request.Document.Editor.SelectImplied();
        var selection = current.Status == CadPromptStatus.OK && current.Value != null
            ? current.Value.GetObjectIds() : Array.Empty<CadObjectId>();
        if (request.RequiresSelectionReentry || request.Selection.Any(id => !id.IsValid || id.IsErased)
            || !selection.SequenceEqual(request.Selection))
        {
            const string message = "扩展已加载；为避免使用加载前的旧选择，请重新选择对象后再次启动命令。";
            SetStatus(request.Id, message);
            request.Document.Editor.WriteMessage("\n" + message);
            return;
        }
        // A single validated command token, preserving the host's interactive command resolution.
        request.Document.SendStringToExecute(request.StartupCommand + " ", false, false, true);
        SetStatus(request.Id, "已加载，已请求启动 " + request.StartupCommand);
    }
    private void OnDocumentCreated(object sender, CadDocumentEventArgs e)
    {
        if (_started) _nextCheck = DateTime.MinValue;
    }

    private void OnDocumentActivated(object sender, CadDocumentEventArgs e)
    {
        if (_started) _documentEpoch++;
    }

    private void OnDocumentClosing(object sender, CadDocumentEventArgs e)
    {
        if (!_started) return;
        _documentAttempts.Remove(e.Document);
        _documentLoaded.Remove(e.Document);
        if (_pending?.Document == e.Document)
        {
            SetStatus(_pending.Id, "图纸已关闭，本次加载取消");
            _pending = null;
        }
    }

    private void OnIdle(object sender, EventArgs e)
    {
        if (!_started || DateTime.UtcNow < _nextCheck) return;
        _nextCheck = DateTime.UtcNow.AddMilliseconds(600);
        try
        {
            var config = _configProvider?.Invoke();
            var entries = config?.ManagedExtensions?.Where(x => x != null).ToArray() ?? Array.Empty<ManagedExtensionConfig>();
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            foreach (var item in entries)
            {
                string path;
                try { path = NormalizePath(item.FilePath); }
                catch (SystemException ex) { SetStatus(item.Id, "路径无效：" + ex.Message); continue; }
                var wasEnabled = _lastEnabled.TryGetValue(item.Id, out var enabled) && enabled;
                _lastEnabled[item.Id] = item.Enabled;
                if (!item.Enabled)
                {
                    SetStatus(item.Id, IsLoaded(path, doc) ? "已停用；本次会话已加载，重启后不再自动加载" : "已停用");
                    continue;
                }
                if (!wasEnabled && !IsLoaded(path, doc))
                {
                    _processAttempts.Remove(path);
                    foreach (var attempts in _documentAttempts.Values) attempts.Remove(path);
                }
            }
            if (doc == null || !IsIdleForeground(doc)) return;
            if (_pending != null)
            {
                // A cancelled LISP expression has no result callback. Expire it
                // only at an idle boundary; never cancel the user's command.
                if (DateTime.UtcNow - _pending.QueuedAt > TimeSpan.FromSeconds(45))
                {
                    SetStatus(_pending.Id, "加载未完成或被取消；停用后重新启用可重试");
                    _pending = null;
                }
                return;
            }
            foreach (var item in entries.Where(x => x.Enabled))
            {
                string path;
                try
                {
                    path = NormalizePath(item.FilePath);
                    if (!IsSupported(path)) { SetStatus(item.Id, "当前宿主不支持此扩展格式"); continue; }
                    if (!File.Exists(path)) { SetStatus(item.Id, "文件不存在，请重新选择文件"); continue; }
                }
                catch (SystemException ex) { SetStatus(item.Id, "路径无效：" + ex.Message); continue; }
                if (IsLoaded(path, doc)) { SetStatus(item.Id, IsLisp(path) ? "当前图纸已加载" : "当前会话已加载"); continue; }
                var attempts = IsLisp(path) ? GetSet(_documentAttempts, doc) : _processAttempts;
                if (!attempts.Add(path)) continue;
                _pending = new PendingLoad { Id = item.Id, Path = path, Document = doc };
                SetStatus(item.Id, "等待 CAD 原生加载 / 信任检查");
                // The first expression checks the exact saved entry again before
                // executing, so a queued request cannot outlive unchecking/removal.
                doc.SendStringToExecute(BuildLoadExpression(_pending.Token, path), false, false, false);
                break;
            }
        }
        catch (SystemException ex)
        {
            if (_pending != null) { SetStatus(_pending.Id, "加载失败：" + ex.Message); _pending = null; }
            Logger.Error("功能管理器加载检查失败。", ex);
        }
    }

    private static bool IsIdleForeground(CadDocument doc)
    {
        if (!doc.Editor.IsQuiescent || !string.IsNullOrEmpty(doc.CommandInProgress)) return false;
        if (Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")) != 0 || !string.IsNullOrEmpty(Convert.ToString(AcApp.GetSystemVariable("CMDNAMES")))) return false;
        return IsHostForeground();
    }

    private static bool IsHostForeground()
    {
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var processId);
        using var current = Process.GetCurrentProcess();
        return processId == current.Id;
    }

    private static bool IsLisp(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".lsp", StringComparison.OrdinalIgnoreCase) || ext.Equals(".fas", StringComparison.OrdinalIgnoreCase) || ext.Equals(".vlx", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLoaded(string path, CadDocument? doc) => IsLisp(path)
        ? doc != null && _documentLoaded.TryGetValue(doc, out var loaded) && loaded.Contains(path)
        : _processLoaded.Contains(path) || Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) && IsAssemblyLoaded(path);

    private static bool IsAssemblyLoaded(string path)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { if (!assembly.IsDynamic && SamePath(assembly.Location, path)) return true; }
            catch (SystemException) { }
        }
        return false;
    }

    private static HashSet<string> GetSet(Dictionary<CadDocument, HashSet<string>> dictionary, CadDocument doc)
    {
        if (!dictionary.TryGetValue(doc, out var set)) dictionary[doc] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return set;
    }

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)) throw new ArgumentException("请选择有效的完整文件路径。");
        if (!Path.IsPathRooted(path)) throw new ArgumentException("扩展路径必须为绝对路径。");
        return Path.GetFullPath(path.Trim());
    }

    private static bool SamePath(string first, string second)
    {
        try { return NormalizePath(first).Equals(NormalizePath(second), StringComparison.OrdinalIgnoreCase); }
        catch (SystemException) { return false; }
    }

    internal static string LispString(string value)
    {
        if (value == null || value.Any(char.IsControl)) throw new ArgumentException("LISP 字符串不能含控制字符。");
        return "\"" + value.Replace("\\", "/").Replace("\"", "\\\"") + "\"";
    }

    internal static string BuildLoadExpression(string token, string path)
    {
        var tokenLiteral = LispString(token);
        var pathLiteral = LispString(NormalizePath(path));
        var operation = IsLisp(path) ? "(vl-catch-all-apply 'load (list " + pathLiteral + "))"
            : Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                ? "(vl-catch-all-apply '(lambda () (command \"_.NETLOAD\" " + pathLiteral + ")) nil)"
                : "(vl-catch-all-apply 'arxload (list " + pathLiteral + "))";
        return "(if (= 1 (XYLP-EXTENSION-ALLOWED " + tokenLiteral + ")) "
            + "((lambda (r) (XYLP-EXTENSION-RESULT " + tokenLiteral + " (if (vl-catch-all-error-p r) (vl-catch-all-error-message r) \"\"))) " + operation + ") "
            + "(XYLP-EXTENSION-RESULT " + tokenLiteral + " \"请求已取消\")) (princ)\n";
    }

    [LispFunction("XYLP-EXTENSION-ALLOWED")]
    public static int CanLoad(CadResultBuffer args)
    {
        try
        {
            var service = _current;
            var request = service?._pending;
            var token = args?.AsArray().FirstOrDefault().Value as string;
            if (request == null || token != request.Token || AcApp.DocumentManager.MdiActiveDocument != request.Document || !IsHostForeground()) return 0;
            return service!._configProvider!().ManagedExtensions.Any(x => IsMatchingRequest(x, request.Id, request.Path, request.LaunchRequested ? request.StartupCommand : null)) ? 1 : 0;
        }
        catch (SystemException) { return 0; }
    }

    [LispFunction("XYLP-EXTENSION-RESULT")]
    public static int Complete(CadResultBuffer args)
    {
        var service = _current;
        var request = service?._pending;
        try
        {
            var values = args?.AsArray();
            if (request == null || values == null || values.Length < 2 || (values[0].Value as string) != request.Token) return 0;
            var error = Convert.ToString(values[1].Value) ?? "";
            if (error.Length == 0 && Path.GetExtension(request.Path).Equals(".dll", StringComparison.OrdinalIgnoreCase) && !IsAssemblyLoaded(request.Path))
                error = "NETLOAD 未确认加载成功，请检查宿主提示、信任位置及版本兼容性";
            if (error.Length == 0)
            {
                if (IsLisp(request.Path)) GetSet(service!._documentLoaded, request.Document).Add(request.Path);
                else service!._processLoaded.Add(request.Path);
                SetStatus(request.Id, IsLisp(request.Path) ? "当前图纸已加载" : "当前会话已加载");
                if (request.LaunchRequested) service.QueueStartup(request);
            }
            else SetStatus(request.Id, "加载失败：" + error + "；停用后重新启用可重试");
            service!._pending = null;
            return error.Length == 0 ? 1 : 0;
        }
        catch (SystemException ex)
        {
            if (request != null) SetStatus(request.Id, "加载结果检查失败：" + ex.Message);
            if (service != null) service._pending = null;
            return 0;
        }
    }

    private static void SetStatus(string id, string value)
    {
        if (!string.IsNullOrWhiteSpace(id)) StatusById[id] = value.Length > 500 ? value.Substring(0, 500) : value;
    }

    public void Dispose() => Dispose(hostTerminating: false);

    internal void Dispose(bool hostTerminating)
    {
        if (!_started) return;
        _started = false;
#if AUTOCAD_CLR2
        // The CLR 2 host can destroy its native reactors before Terminate.
        // Removing their managed events then asserts inside reactorRelease;
        // try/catch cannot protect against that native assertion.
        if (!hostTerminating)
#endif
        {
            AcApp.Idle -= OnIdle;
            AcApp.DocumentManager.DocumentCreated -= OnDocumentCreated;
            AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentClosing;
            AcApp.DocumentManager.DocumentActivated -= OnDocumentActivated;
        }
        if (_pending != null) SetStatus(_pending.Id, "服务已停止，本次加载取消");
        _pending = null;
        _documentAttempts.Clear();
        _documentLoaded.Clear();
        _configProvider = null;
        if (ReferenceEquals(_current, this)) _current = null;
    }
}


