#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadDocument = ZwSoft.ZwCAD.ApplicationServices.Document;
using CadEditor = ZwSoft.ZwCAD.EditorInput.Editor;
using CadDocumentEventArgs = ZwSoft.ZwCAD.ApplicationServices.DocumentCollectionEventArgs;
#else
#if AUTOCAD_PRE2013
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif
using CadDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using CadEditor = Autodesk.AutoCAD.EditorInput.Editor;
using CadDocumentEventArgs = Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs;
#endif

namespace XiaoYang.AutoCADPlugin;

internal sealed class CadCommandExecutor : IDisposable
{
    private readonly CadCommandCatalog _catalog;
    private readonly Func<PluginConfig> _configProvider;
    // Created during plug-in initialization on the CAD UI thread. Overlay and
    // modifier-wait threads must never read the editor or CAD system variables.
    private readonly System.Windows.Forms.Control _dispatcher = new();
    private volatile bool _disposed;
    private long _documentEpoch;
    private CadDocument _wheelDocument;
    private long _wheelEpoch;
    private bool _hasWheelOrigin;

    public CadCommandExecutor(CadCommandCatalog catalog, Func<PluginConfig> configProvider)
    {
        _catalog = catalog;
        _configProvider = configProvider;
        _ = _dispatcher.Handle;
        AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
    }

    private void OnDocumentActivated(object sender, CadDocumentEventArgs e)
    {
        if (!_disposed) Interlocked.Increment(ref _documentEpoch);
    }

    internal long DocumentEpoch => Interlocked.Read(ref _documentEpoch);

    internal void PostToCadThread(Action action)
    {
        if (_disposed) return;
        try
        {
            _dispatcher.BeginInvoke((Action)(() =>
            {
                if (_disposed) return;
                try { action(); }
                catch (Exception ex) { Logger.Error("轮盘主线程操作失败。", ex); }
            }));
        }
        catch (InvalidOperationException ex)
        {
            if (!_disposed) Logger.Error("无法提交轮盘主线程操作。", ex);
        }
    }

    internal void BeginWheel()
    {
        _wheelDocument = AcApp.DocumentManager.MdiActiveDocument;
        _wheelEpoch = _documentEpoch;
        _hasWheelOrigin = true;
    }

    public void Dispose() => Dispose(hostTerminating: false);

    internal void Dispose(bool hostTerminating)
    {
        if (_disposed) return;
        _disposed = true;
#if AUTOCAD_CLR2
        if (!hostTerminating)
#endif
        AcApp.DocumentManager.DocumentActivated -= OnDocumentActivated;
        _wheelDocument = null;
        _dispatcher.Dispose();
    }

    public void Queue(MenuItemConfig item) => PostToCadThread(() => QueueOnCadThread(item));

    private void QueueOnCadThread(MenuItemConfig item)
    {
        if (item == null) return;
        var document = _hasWheelOrigin ? _wheelDocument : AcApp.DocumentManager.MdiActiveDocument;
        var epoch = _hasWheelOrigin ? _wheelEpoch : _documentEpoch;
        _hasWheelOrigin = false;
        _wheelDocument = null;
        if (!CanSubmit(document, epoch))
        {
            Logger.Info("CAD/LISP/第三方命令正在运行，轮盘本次提交已温和让行，不发送取消键。 ");
            return;
        }
        if (IsTriggerModifierDown())
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (!WaitForTriggerModifiersRelease(3000))
                {
                    Logger.Info("轮盘命令提交时 Shift/Alt 长时间未释放，本次命令已取消。");
                    return;
                }
                Thread.Sleep(50);
                PostToCadThread(() => Submit(item, document, epoch));
            });
            return;
        }
        Submit(item, document, epoch);
    }

    private bool CanSubmit(CadDocument document, long epoch) =>
        !_disposed && document != null && epoch == _documentEpoch
        && ReferenceEquals(document, AcApp.DocumentManager.MdiActiveDocument)
        && IsCadCommandIdle();

    private void Submit(MenuItemConfig item, CadDocument document, long epoch)
    {
        if (!CanSubmit(document, epoch))
        {
            Logger.Info("轮盘提交前图纸或命令状态已改变，本次操作已取消。");
            return;
        }
        // All configured commands use the host's native parser, including aliases,
        // LISP and third-party commands with arguments. No anonymous command may
        // consume PickFirst before the actual target command starts.
        Execute(item);
    }
    internal static bool UsesPhysicalModifierReleaseGuard => true;

    internal static bool PreservesActiveCommands => true;

    private static bool IsCadCommandIdle()
    {
        try
        {
            var names = Convert.ToString(AcApp.GetSystemVariable("CMDNAMES"))?.Trim() ?? "";
            var active = Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE"));
            return names.Length == 0 && active == 0;
        }
        catch { return false; }
    }

    private static bool IsTriggerModifierDown() =>
        (NativeMethods.GetAsyncKeyState(16) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(17) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(18) & 0x8000) != 0;

    private static bool WaitForTriggerModifiersRelease(int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
        while (DateTime.UtcNow < deadline)
        {
            if (!IsTriggerModifierDown()) return true;
            Thread.Sleep(10);
        }
        return !IsTriggerModifierDown();
    }

    public void ExecuteCustomById(string id)
    {
        var config = _configProvider();
        var command = config.CustomCommands.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException("自定义命令不存在或已删除。");
        if (!CadCommandCatalog.IsSafeCommandName(command.CommandName))
            throw new InvalidOperationException("自定义命令格式无效，只允许英文、数字、连字符和下划线。");
        if (command.CommandName.Equals(ExtensionLoadService.StartupEntryCommand, StringComparison.OrdinalIgnoreCase))
        {
            if (command.Arguments == null || command.Arguments.Count != 1)
                throw new InvalidOperationException("扩展启动入口参数无效，请重新生成入口。");
            var line = ExtensionLoadService.BuildStartupEntryLine(command.Arguments[0]);
            var document = AcApp.DocumentManager.MdiActiveDocument
                ?? throw new InvalidOperationException("没有活动图纸。");
            document.SendStringToExecute(line, activate: true, wrapUpInactiveDoc: false, echoCommand: true);
            return;
        }
        if (command.Arguments == null || command.Arguments.Count == 0)
        {
            QueueInteractiveCommand(command.CommandName, validateCommand: false, useGlobalPrefix: false);
            return;
        }
        ExecuteCommand(command.CommandName, command.Arguments, command.Silent, command.DisableDialogs,
            validateCommand: false, useGlobalPrefix: false);
    }

    internal void ExecuteForSelfTest(string commandName) =>
        ExecuteCommand(commandName, Array.Empty<string>(), true, true, validateCommand: false);

    private void Execute(MenuItemConfig item)
    {
        if (item.Kind.Equals(ActionKinds.CustomCommand, StringComparison.OrdinalIgnoreCase))
        {
            ExecuteCustomById(item.Value);
            return;
        }

        // 不使用 ._ 前缀强制绕过用户环境。让 AutoCAD 按当前已加载的
        // 命令重定义、LISP、第三方插件和本地命令环境解析该命令名。
        // 已放入轮盘的命令属于用户配置。执行时直接交给当前 CAD 的
        // CUI/PGP/LISP/第三方插件环境解析，不能再用启动快照拦截。
        QueueInteractiveCommand(item.Value, validateCommand: false, useGlobalPrefix: false);
    }

    private void QueueInteractiveCommand(string commandName, bool validateCommand, bool useGlobalPrefix)
    {
        var document = AcApp.DocumentManager.MdiActiveDocument
                       ?? throw new InvalidOperationException("当前没有打开的图纸。");
        var normalized = CadCommandCatalog.NormalizeCommandName(commandName);
        if (normalized.Length == 0) throw new InvalidOperationException("命令名为空。");
        if (validateCommand && !_catalog.IsCommandDefined(normalized))
            throw new InvalidOperationException($"{CadHostInfo.DisplayName} 当前未注册命令：{normalized}");

        var commandLine = BuildInteractiveCommandLine(normalized, useGlobalPrefix);
        Logger.Info($"进入 {CadHostInfo.ProductName} 原生交互命令：{normalized}，强制原始全局命令={useGlobalPrefix}；保留当前命令解析环境、捕捉、动态输入和系统变量。 ");
        document.SendStringToExecute(commandLine, activate: true, wrapUpInactiveDoc: false, echoCommand: true);
    }

    internal static string BuildInteractiveCommandLine(string commandName, bool useGlobalPrefix)
    {
        var normalized = CadCommandCatalog.NormalizeCommandName(commandName);
        if (normalized.Length == 0) throw new InvalidOperationException("命令名为空。");
        // Never prepend Ctrl+C. The trigger and queue both require an idle CAD
        // command stack, so a wheel action cannot cancel an active LSP/CUI/ARX
        // command even if one starts during the gesture.
        return (useGlobalPrefix ? "._" : "") + normalized + " ";
    }

    internal static bool ConfiguredWheelCommandsUseCurrentCadResolution => true;

    private void ExecuteCommand(
        string commandName,
        IReadOnlyList<string> arguments,
        bool silent,
        bool disableDialogs,
        bool validateCommand = true,
        bool useGlobalPrefix = true)
    {
        var document = AcApp.DocumentManager.MdiActiveDocument
                       ?? throw new InvalidOperationException("当前没有打开的图纸。");
        var normalized = CadCommandCatalog.NormalizeCommandName(commandName);
        if (normalized.Length == 0) throw new InvalidOperationException("命令名为空。");
        if (validateCommand && !_catalog.IsCommandDefined(normalized))
            throw new InvalidOperationException($"{CadHostInfo.DisplayName} 当前未注册命令：{normalized}");
        if (!useGlobalPrefix)
        {
            QueueScriptedCommand(document, normalized, arguments ?? Array.Empty<string>(), useGlobalPrefix: false);
            return;
        }

#if AUTOCAD_LEGACY_EDITOR
        if (arguments != null && arguments.Count > 0)
        {
            QueueScriptedCommand(document, normalized, arguments, useGlobalPrefix);
            return;
        }
#endif

        var tokens = new List<object> { useGlobalPrefix ? "._" + normalized : normalized };
        foreach (var argument in arguments ?? Array.Empty<string>())
        {
            if (string.Equals(argument, "<ENTER>", StringComparison.OrdinalIgnoreCase))
                tokens.Add("");
            else if (string.Equals(argument, "<PAUSE>", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("静默命令不允许 <PAUSE>，请把命令所需参数完整写入配置。");
            else
                tokens.Add(argument ?? "");
        }

        Logger.Info($"执行 CAD 命令：{normalized}，参数数={tokens.Count - 1}，静默={silent}，直接模式={!useGlobalPrefix}；所有系统变量沿用用户当前值。 ");
        RunEditorCommand(document.Editor, tokens.ToArray());
    }
    private static void RunEditorCommand(CadEditor editor, object[] tokens)
    {
#if AUTOCAD_LEGACY_EDITOR
        var method = typeof(CadEditor).GetMethod(
            "RunCommand",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (method == null) throw new MissingMethodException($"{CadHostInfo.DisplayName} Editor.RunCommand");
        method.Invoke(editor, new object[] { tokens });
#else
        editor.Command(tokens);
#endif
    }

    private static void QueueScriptedCommand(
        CadDocument document,
        string commandName,
        IReadOnlyList<string> arguments,
        bool useGlobalPrefix)
    {
        var parts = new List<string> { BuildInteractiveCommandLine(commandName, useGlobalPrefix).TrimEnd() };
        foreach (var argument in arguments)
        {
            if (string.Equals(argument, "<PAUSE>", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("脚本命令不允许 <PAUSE>，请把命令所需参数完整写入配置。");
            if (string.Equals(argument, "<ENTER>", StringComparison.OrdinalIgnoreCase))
                parts.Add("");
            else
                parts.Add(QuoteCommandLineArgument(argument ?? ""));
        }
        var commandLine = string.Join(" ", parts) + " ";
        Logger.Info($"通过 {CadHostInfo.DisplayName} 原生命令队列执行带参数命令：{commandName}，参数数={arguments.Count}");
        document.SendStringToExecute(commandLine, activate: true, wrapUpInactiveDoc: false, echoCommand: true);
    }

    private static string QuoteCommandLineArgument(string value) =>
        value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

}
