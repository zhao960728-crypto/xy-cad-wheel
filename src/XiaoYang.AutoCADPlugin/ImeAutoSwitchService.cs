#if ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using AcApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadDocument = ZwSoft.ZwCAD.ApplicationServices.Document;
using CadDocumentCollectionEventArgs = ZwSoft.ZwCAD.ApplicationServices.DocumentCollectionEventArgs;
using CadCommandEventArgs = ZwSoft.ZwCAD.ApplicationServices.CommandEventArgs;
using CadLispWillStartEventArgs = ZwSoft.ZwCAD.ApplicationServices.LispWillStartEventArgs;
using CadOpenMode = ZwSoft.ZwCAD.DatabaseServices.OpenMode;
using CadPromptStatus = ZwSoft.ZwCAD.EditorInput.PromptStatus;
using CadDbText = ZwSoft.ZwCAD.DatabaseServices.DBText;
using CadMText = ZwSoft.ZwCAD.DatabaseServices.MText;
using CadBlockReference = ZwSoft.ZwCAD.DatabaseServices.BlockReference;
using CadObjectId = ZwSoft.ZwCAD.DatabaseServices.ObjectId;
using CadObjectEventArgs = ZwSoft.ZwCAD.DatabaseServices.ObjectEventArgs;
#else
using Autodesk.AutoCAD.ApplicationServices;
#if AUTOCAD_PRE2013
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif
using CadDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using CadDocumentCollectionEventArgs = Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs;
using CadCommandEventArgs = Autodesk.AutoCAD.ApplicationServices.CommandEventArgs;
using CadLispWillStartEventArgs = Autodesk.AutoCAD.ApplicationServices.LispWillStartEventArgs;
using CadOpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;
using CadPromptStatus = Autodesk.AutoCAD.EditorInput.PromptStatus;
using CadDbText = Autodesk.AutoCAD.DatabaseServices.DBText;
using CadMText = Autodesk.AutoCAD.DatabaseServices.MText;
using CadBlockReference = Autodesk.AutoCAD.DatabaseServices.BlockReference;
using CadObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using CadObjectEventArgs = Autodesk.AutoCAD.DatabaseServices.ObjectEventArgs;
#endif

namespace XiaoYang.AutoCADPlugin;

internal sealed class ImeAutoSwitchService : IDisposable
{
    private readonly Func<PluginConfig> _configProvider;
    private readonly Func<IntPtr> _mainWindowProvider;
    private readonly Func<string, IReadOnlyList<string>> _equivalentCommandNamesProvider;
    private readonly HashSet<CadDocument> _documents = new();
    private readonly List<CommandModeFrame> _commandFrames = new();
    private readonly List<LispExecutionFrame> _lispExecutions = new();
    private readonly Dictionary<CadDocument, int> _documentCommandDepths = new();
    private CadDocument _currentDocument;
    private bool _started;
    private bool? _lastAppliedChinese;
    private IntPtr _lastInputTarget;
    private int _activeTextCommandDepth;
    private int _activeOtherCommandDepth;
    private int _activeCadCommandDepth;
    private int _appliedSwitchCount;
    private int _skippedDuplicateCount;
    private bool _hasAppliedChinese;
    private bool _hasAppliedEnglish;
    private DateTime _nextFocusCheckUtc = DateTime.MinValue;
    private DateTime _nextModeReapplyUtc = DateTime.MinValue;
    private int _pendingModeReapplyCount;
    private bool _pendingModeReapplyChinese;
    private int _modeReapplyCount;
    private int _skippedBackgroundCount;
    private int _modeDetectionCount;
    private int _modeTransitionCount;
    private int _profileActivationCount;
    private int _profileActivationFailureCount;
    private int _raceBlockedCount;
    private int _contentAwareChineseCommandCount;
    private int _contentAwareEnglishCommandCount;
    private bool _pendingConfigurationApply;
#if AUTOCAD_WIN32
    private bool _processingIdle;
#endif
    private string _lastInputMethodFallbackSignature = "";
    private string _lastModeErrorSignature = "";
    private ImeErrorReportWindow? _errorReportWindow;

    public ImeAutoSwitchService(Func<PluginConfig> configProvider, Func<IntPtr> mainWindowProvider,
        Func<string, IReadOnlyList<string>> equivalentCommandNamesProvider = null)
    {
        _configProvider = configProvider;
        _mainWindowProvider = mainWindowProvider;
        _equivalentCommandNamesProvider = equivalentCommandNamesProvider
                                          ?? (command => new[] { ImeCommandCatalog.Normalize(command) });
    }

    public bool IsStarted => _started;
    public bool UsesFocusAwareReapply => true;
    public bool UsesLispCommandEvents => _started && _documents.Count > 0;
    public bool? LastAppliedChinese => _lastAppliedChinese;
    public int AppliedSwitchCount => _appliedSwitchCount;
    public int SkippedDuplicateCount => _skippedDuplicateCount;
    public bool HasAppliedChinese => _hasAppliedChinese;
    public bool HasAppliedEnglish => _hasAppliedEnglish;
    public int ModeReapplyCount => _modeReapplyCount;
    public int SkippedBackgroundCount => _skippedBackgroundCount;
    public int ModeDetectionCount => _modeDetectionCount;
    public int ModeTransitionCount => _modeTransitionCount;
    public int ProfileActivationCount => _profileActivationCount;
    public int ProfileActivationFailureCount => _profileActivationFailureCount;
    public int RaceBlockedCount => _raceBlockedCount;
    public int ContentAwareChineseCommandCount => _contentAwareChineseCommandCount;
    public int ContentAwareEnglishCommandCount => _contentAwareEnglishCommandCount;
    public string LastModeBackend { get; private set; } = "";
    public bool HasActiveCadCommand => Volatile.Read(ref _activeCadCommandDepth) > 0;

    public void Start()
    {
        if (_started) return;
        _started = true;
        AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
        AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        AcApp.Idle += OnApplicationIdle;
        Attach(AcApp.DocumentManager.MdiActiveDocument);
        RecalculateCommandDepths();
        ApplyConfiguration();
        Logger.Info($"CAD 输入法自动切换服务已就绪；启用={Current.Enabled}；仅切换同一输入法的中英文模式。");
    }

    public void ApplyConfiguration()
    {
        if (!_started) return;
        Attach(AcApp.DocumentManager.MdiActiveDocument);
        var config = Current;
        // A modeless settings change can reclassify an in-flight command.
        // Discard depths from the old rule set so they cannot become stale.
        _activeTextCommandDepth = 0;
        _activeOtherCommandDepth = 0;
        _commandFrames.Clear();
        _lispExecutions.Clear();
        _pendingModeReapplyCount = 0;
        _lastInputMethodFallbackSignature = "";
        _lastModeErrorSignature = "";
        if (!config.Enabled || !config.ShowErrorReport)
        {
            try { _errorReportWindow?.Close(); } catch { }
            _errorReportWindow = null;
        }
        if (!config.Enabled)
        {
            _pendingConfigurationApply = false;
            return;
        }
        _lastInputTarget = IntPtr.Zero;
        _pendingConfigurationApply = config.DirectInputUsesEnglish || config.DocumentActivatedUsesEnglish;
#if !AUTOCAD_WIN32
        if (_pendingConfigurationApply)
            _pendingConfigurationApply = !Switch(chinese: false, "配置应用/命令行直接输入待命");
#endif
    }

    public void Dispose() => Dispose(hostTerminating: false);

    internal void Dispose(bool hostTerminating)
    {
        if (!_started) return;
        _started = false;
        Interlocked.Exchange(ref _activeCadCommandDepth, 0);
#if AUTOCAD_CLR2
        if (!hostTerminating)
#endif
        {
            try { AcApp.DocumentManager.DocumentActivated -= OnDocumentActivated; } catch { }
            try { AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed; } catch { }
            try { AcApp.Idle -= OnApplicationIdle; } catch { }
            foreach (var document in _documents.ToArray()) Detach(document);
        }
        _documents.Clear();
        _documentCommandDepths.Clear();
        _commandFrames.Clear();
        _lispExecutions.Clear();
        _currentDocument = null;
        try { _errorReportWindow?.Close(); } catch { }
        _errorReportWindow = null;
        Logger.Info("CAD 输入法自动切换服务已释放。");
    }

    private ImeSwitchConfig Current => _configProvider()?.ImeSwitch ?? new ImeSwitchConfig();

    private void OnDocumentActivated(object sender, CadDocumentCollectionEventArgs e)
    {
        if (!_started) return;
        Attach(e.Document);
        // Suspended commands belong to their drawing and must survive an MDI switch.
        RecalculateCommandDepths();
        _lastInputTarget = IntPtr.Zero;
        _pendingModeReapplyCount = 0;
        var config = Current;
        if (!config.Enabled) return;
        var preference = CurrentCommandPreference;
        if (preference.HasValue)
            ApplyPreferredMode(preference.Value, "切换图纸/恢复当前命令输入模式");
        else if (config.DirectInputUsesEnglish || config.DocumentActivatedUsesEnglish)
            ApplyPreferredMode(chinese: false, "切换图纸/命令行直接输入待命");
    }

    private void OnDocumentToBeDestroyed(object sender, CadDocumentCollectionEventArgs e)
    {
        if (!_started) return;
        if (e?.Document == null) return;
        Detach(e.Document);
        _documentCommandDepths.Remove(e.Document);
        _commandFrames.RemoveAll(frame => ReferenceEquals(frame.Document, e.Document));
        _lispExecutions.RemoveAll(frame => ReferenceEquals(frame.Document, e.Document));
        RecalculateCommandDepths();
    }

    private void Attach(CadDocument document)
    {
        if (!_started || document == null || !_documents.Add(document)) return;
        document.CommandWillStart += OnCommandWillStart;
        document.CommandEnded += OnCommandFinished;
        document.CommandCancelled += OnCommandFinished;
        document.CommandFailed += OnCommandFinished;
        document.LispWillStart += OnLispWillStart;
        document.LispEnded += OnLispFinished;
        document.LispCancelled += OnLispFinished;
        document.Database.ObjectOpenedForModify += OnObjectOpenedForModify;
    }

    private void Detach(CadDocument document)
    {
        if (document == null || !_documents.Remove(document)) return;
        try { document.CommandWillStart -= OnCommandWillStart; } catch { }
        try { document.CommandEnded -= OnCommandFinished; } catch { }
        try { document.CommandCancelled -= OnCommandFinished; } catch { }
        try { document.CommandFailed -= OnCommandFinished; } catch { }
        try { document.LispWillStart -= OnLispWillStart; } catch { }
        try { document.LispEnded -= OnLispFinished; } catch { }
        try { document.LispCancelled -= OnLispFinished; } catch { }
        try { document.Database.ObjectOpenedForModify -= OnObjectOpenedForModify; } catch { }
    }

    private void OnLispWillStart(object sender, CadLispWillStartEventArgs e)
    {
        if (!_started) return;
        var document = sender as CadDocument ?? AcApp.DocumentManager.MdiActiveDocument;
        TrackCommandDepth(document, 1);
        var config = Current;
        if (!config.Enabled)
        {
            _lispExecutions.Add(new LispExecutionFrame(document, null, selectedForChinese: false));
            return;
        }

        var command = ImeCommandCatalog.ExtractLispCommand(e?.FirstLine);
        if (command.Length == 0)
        {
            // Loading/evaluating arbitrary Lisp code is not a user command and
            // must not change the input mode. Keep a marker only so nested Lisp
            // end/cancel events remain balanced.
            _lispExecutions.Add(new LispExecutionFrame(document, null, selectedForChinese: false));
            return;
        }

        var selectedForChinese = ImeCommandCatalog.IsTextCommand(config, command,
            _equivalentCommandNamesProvider(command));
        if (selectedForChinese)
            Logger.Info($"已匹配需要中文输入的 LISP/第三方命令：收到={e.FirstLine}；规范={command}。");
        var parentPreference = GetCommandPreference(document);
        var preferredChinese = ResolveCommandPreference(null, selectedForChinese, parentPreference, config);
        var modeFrame = new CommandModeFrame(document, command, preferredChinese, contentAwareCandidate: false);
        _commandFrames.Add(modeFrame);
        _lispExecutions.Add(new LispExecutionFrame(document, modeFrame, selectedForChinese));
        RecalculateCommandDepths();
        if (!ReferenceEquals(document, AcApp.DocumentManager.MdiActiveDocument)) return;

        if (preferredChinese == true)
        {
            var reason = selectedForChinese ? "已勾选的 LISP/第三方命令" : "继承上层中文命令";
            var switched = ApplyPreferredMode(chinese: true, $"{reason}开始：{command}");
            if (selectedForChinese && switched)
                Logger.Info($"LISP/第三方命令输入法已应用中文模式：命令={command}；后端={LastModeBackend}。");
        }
        else if (preferredChinese == false)
        {
            ApplyPreferredMode(chinese: false, $"普通 LISP/第三方命令开始：{command}");
        }
    }

    private void OnLispFinished(object sender, EventArgs e)
    {
        if (!_started) return;
        var document = sender as CadDocument ?? AcApp.DocumentManager.MdiActiveDocument;
        TrackCommandDepth(document, -1);
        var execution = TakeLispExecution(document);
        if (execution?.ModeFrame == null) return;
        _commandFrames.Remove(execution.ModeFrame);
        RecalculateCommandDepths();
        if (!ReferenceEquals(document, AcApp.DocumentManager.MdiActiveDocument)) return;
        _pendingModeReapplyCount = 0;

        var config = Current;
        if (!config.Enabled) return;
        var command = execution.ModeFrame.Command;
        var parentPreference = CurrentCommandPreference;
        if (parentPreference.HasValue)
        {
            ApplyPreferredMode(parentPreference.Value, $"返回上层命令：{command}");
        }
        else if (!HasCommandFrames(document) && (config.DirectInputUsesEnglish || config.CommandEndUsesEnglish))
        {
            var switched = ApplyPreferredMode(chinese: false, $"LISP/第三方命令结束或取消，恢复直接输入英文：{command}");
            if (execution.SelectedForChinese && switched)
                Logger.Info($"LISP/第三方命令结束后已恢复英文模式：命令={command}；后端={LastModeBackend}。");
        }
    }

    private void OnCommandWillStart(object sender, CadCommandEventArgs e)
    {
        if (!_started) return;
        var document = sender as CadDocument ?? AcApp.DocumentManager.MdiActiveDocument;
        TrackCommandDepth(document, 1);
        var config = Current;
        if (!config.Enabled) return;
        var command = ImeCommandCatalog.Normalize(e.GlobalCommandName);
        var isExistingTextEdit = ImeTextEditPolicy.IsExistingTextEditCommand(command);
        var existingTextPreference = TryGetExistingTextPreference(document, command);
        var selectedForChinese = ImeCommandCatalog.IsTextCommand(config, command,
            _equivalentCommandNamesProvider(command));
        if (selectedForChinese)
            Logger.Info($"已匹配需要中文输入的命令：收到={e.GlobalCommandName}；规范={command}。");
        var parentPreference = GetCommandPreference(document);
        var preferredChinese = ResolveCommandPreference(existingTextPreference, selectedForChinese,
            parentPreference, config);
        _commandFrames.Add(new CommandModeFrame(document, command, preferredChinese,
            isExistingTextEdit));
        RecalculateCommandDepths();
        if (!ReferenceEquals(document, AcApp.DocumentManager.MdiActiveDocument)) return;
        if (existingTextPreference == true) _contentAwareChineseCommandCount++;
        else if (existingTextPreference == false) _contentAwareEnglishCommandCount++;
        if (preferredChinese == true)
        {
            var reason = existingTextPreference == true
                ? "编辑现有中文文字"
                : selectedForChinese
                    ? "已勾选的文字命令"
                    : parentPreference == true
                        ? "继承上层中文命令"
                        : "中文命令";
            ApplyPreferredMode(chinese: true, $"{reason}开始：{command}");
        }
        else if (preferredChinese == false)
        {
            var reason = existingTextPreference == false ? "编辑现有数字/英文文字" : "普通命令";
            ApplyPreferredMode(chinese: false, $"{reason}开始：{command}");
        }
    }

    internal static bool? ResolveCommandPreference(bool? existingTextPreference, bool selectedForChinese,
        bool? parentPreference, ImeSwitchConfig config)
    {
        if (existingTextPreference.HasValue) return existingTextPreference.Value;
        if (selectedForChinese) return true;
        // Third-party/LISP commands frequently start native child commands. Once
        // the user-selected parent command has switched to Chinese, its children
        // must inherit that preference instead of immediately forcing English.
        if (parentPreference == true) return true;
        return config?.OtherCommandsUseEnglish == true || config?.DirectInputUsesEnglish == true
            ? false
            : null;
    }

    private void OnCommandFinished(object sender, CadCommandEventArgs e)
    {
        if (!_started) return;
        var document = sender as CadDocument ?? AcApp.DocumentManager.MdiActiveDocument;
        TrackCommandDepth(document, -1);
        var config = Current;
        if (!config.Enabled) return;
        var command = ImeCommandCatalog.Normalize(e.GlobalCommandName);
        var frame = TakeCommandFrame(document, command);
        if (frame == null) return;
        RecalculateCommandDepths();
        if (!ReferenceEquals(document, AcApp.DocumentManager.MdiActiveDocument)) return;
        _pendingModeReapplyCount = 0;
        var parentPreference = CurrentCommandPreference;
        if (parentPreference.HasValue)
        {
            ApplyPreferredMode(parentPreference.Value, $"返回上层命令：{command}");
        }
        else if (!HasCommandFrames(document) && (config.DirectInputUsesEnglish || config.CommandEndUsesEnglish))
        {
            ApplyPreferredMode(chinese: false, $"命令结束/取消/失败，恢复直接输入英文：{command}");
        }
    }

    private void OnObjectOpenedForModify(object sender, CadObjectEventArgs e)
    {
        if (!_started) return;
        var config = Current;
        if (!config.Enabled || e?.DBObject == null || _commandFrames.Count == 0) return;
        var document = AcApp.DocumentManager.MdiActiveDocument;
        var frame = _commandFrames.LastOrDefault(candidate => ReferenceEquals(candidate.Document, document));
        if (frame == null || !frame.ContentAwareCandidate || document == null
                          || !ReferenceEquals(sender, document.Database)) return;
        var objectId = e.DBObject.ObjectId;
        if (!objectId.IsNull && frame.LastResolvedObjectId.Equals(objectId)) return;
        var preference = TryGetDatabaseObjectTextPreference(e.DBObject);
        if (!preference.HasValue) return;
        frame.LastResolvedObjectId = objectId;
        frame.PreferredChinese = preference;
        RecalculateCommandDepths();
        if (preference.Value) _contentAwareChineseCommandCount++;
        else _contentAwareEnglishCommandCount++;
        ApplyPreferredMode(preference.Value, $"已选择待编辑的{(preference.Value ? "中文" : "数字/英文")}文字：{frame.Command}");
    }

    private void OnApplicationIdle(object sender, EventArgs e)
    {
#if AUTOCAD_WIN32
        if (!_started || _processingIdle) return;
        _processingIdle = true;
        try { ProcessApplicationIdle(); }
        finally { _processingIdle = false; }
#else
        ProcessApplicationIdle();
#endif
    }

    private void ProcessApplicationIdle()
    {
        if (!_started || DateTime.UtcNow < _nextFocusCheckUtc) return;
        _nextFocusCheckUtc = DateTime.UtcNow.AddMilliseconds(100);
        SynchronizeCurrentDocumentCommandState();
        var config = Current;
        if (!config.Enabled) return;
        var mainWindow = _mainWindowProvider();
#if AUTOCAD_WIN32
        // SAVEAS/file dialogs disable the owner. Never activate an input method
        // while the legacy host is inside that modal native operation.
        if (mainWindow == IntPtr.Zero || !NativeMethods.IsWindowEnabled(mainWindow)) return;
#endif
        if (!ImeNativeBridge.IsCurrentProcessForeground(mainWindow))
        {
            // CAD is in the background. Forget the stale CAD focus so returning
            // to CAD is treated as a fresh target, but never touch another app.
            _lastInputTarget = IntPtr.Zero;
            _pendingModeReapplyCount = 0;
            try { _errorReportWindow?.Hide(); } catch { }
            return;
        }
        var target = ImeNativeBridge.ResolveInputTarget(mainWindow);
        if (target == IntPtr.Zero) return;
        var targetChanged = target != _lastInputTarget;
        // Remember the observed CAD target even when an IME API cannot confirm
        // the requested mode. Otherwise Idle retries every 100 ms forever.
        _lastInputTarget = target;
        if (_pendingConfigurationApply)
        {
            // The settings dialog can own the foreground while ApplyConfiguration
            // runs. Retry after focus returns to CAD, then use the same short
            // bounded settle window as command input contexts when needed.
            _pendingConfigurationApply = false;
            var configurationPreference = CurrentCommandPreference ?? false;
            ApplyPreferredMode(configurationPreference, "设置窗口关闭后应用输入法配置");
            return;
        }
        var activePreference = CurrentCommandPreference;
        if (targetChanged)
        {
            if (activePreference.HasValue)
            {
                ApplyPreferredMode(activePreference.Value, "当前命令输入焦点已建立");
            }
            else if (config.DirectInputUsesEnglish)
            {
                ApplyPreferredMode(chinese: false, "命令行直接输入焦点已建立");
            }
        }
        else if (_pendingModeReapplyCount > 0 && DateTime.UtcNow >= _nextModeReapplyUtc)
        {
            if (!ShouldRunPendingModeReapply(activePreference, _pendingModeReapplyChinese,
                    config.DirectInputUsesEnglish || config.CommandEndUsesEnglish
                                                  || config.DocumentActivatedUsesEnglish))
            {
                _pendingModeReapplyCount = 0;
            }
            else if (Switch(_pendingModeReapplyChinese, "等待输入控件就绪后重试"))
            {
                _modeReapplyCount++;
                _pendingModeReapplyCount = 0;
            }
            else
            {
                _pendingModeReapplyCount--;
                _nextModeReapplyUtc = DateTime.UtcNow.AddMilliseconds(120);
            }
        }
    }

    internal static bool ShouldRunPendingModeReapply(bool? activePreference, bool pendingChinese,
        bool idleEnglishPreferred)
    {
        if (activePreference.HasValue) return activePreference.Value == pendingChinese;
        return idleEnglishPreferred && !pendingChinese;
    }

    private void ScheduleModeReapply(bool chinese)
    {
        _pendingModeReapplyChinese = chinese;
        // Third-party text editors and the direct command line can establish their
        // real input context several Idle cycles after a command event. Retry for
        // a short bounded window; this is not continuous enforcement and stops
        // immediately on success.
        _pendingModeReapplyCount = 6;
        _nextModeReapplyUtc = DateTime.UtcNow.AddMilliseconds(80);
    }

    private bool ApplyPreferredMode(bool chinese, string reason)
    {
#if AUTOCAD_WIN32
        // Native command/database notifications and modeless settings saves are
        // reentrant host callbacks; apply the latest preference on safe Idle.
        if (!_processingIdle)
        {
            ScheduleModeReapply(chinese);
            return false;
        }
#endif
        _pendingModeReapplyCount = 0;
        var applied = Switch(chinese, reason);
        if (!applied) ScheduleModeReapply(chinese);
        return applied;
    }

    private static bool? TryGetExistingTextPreference(CadDocument document, string command)
    {
        if (document == null || !ImeTextEditPolicy.IsExistingTextEditCommand(command)) return null;
        try
        {
            var selection = document.Editor.SelectImplied();
            if (selection == null || selection.Status != CadPromptStatus.OK || selection.Value == null) return null;
            var objectIds = selection.Value.GetObjectIds();
            if (objectIds == null || objectIds.Length == 0) return null;
            var contents = new List<string>();
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                foreach (var objectId in objectIds)
                {
                    if (objectId.IsNull || objectId.IsErased) continue;
                    var databaseObject = transaction.GetObject(objectId, CadOpenMode.ForRead, false);
                    if (databaseObject is CadBlockReference block)
                    {
                        foreach (CadObjectId attributeId in block.AttributeCollection)
                        {
                            if (attributeId.IsNull || attributeId.IsErased) continue;
                            AddDatabaseObjectText(transaction.GetObject(attributeId, CadOpenMode.ForRead, false), contents);
                        }
                    }
                    else AddDatabaseObjectText(databaseObject, contents);
                }
            }
            return ImeTextEditPolicy.PreferChineseForExistingContents(contents);
        }
        catch (Exception ex)
        {
            Logger.Trace($"读取现有文字内容失败，回退到用户命令名单：{command}；{ex.Message}");
            return null;
        }
    }

    private static bool? TryGetDatabaseObjectTextPreference(object databaseObject)
    {
        var contents = new List<string>();
        AddDatabaseObjectText(databaseObject, contents);
        return ImeTextEditPolicy.PreferChineseForExistingContents(contents);
    }

    private static void AddDatabaseObjectText(object databaseObject, ICollection<string> contents)
    {
        switch (databaseObject)
        {
            case CadDbText text:
                contents.Add(text.TextString);
                return;
            case CadMText mtext:
                contents.Add(GetVisibleMText(mtext));
                return;
        }

        // Dimension and leader types vary between old AutoCAD/ZWCAD APIs.
        // Read their text through stable public property names without taking
        // a hard assembly dependency on a newer host type.
        var type = databaseObject?.GetType();
        if (type == null) return;
        foreach (var propertyName in new[] { "DimensionText", "TextString" })
        {
            try
            {
                var property = type.GetProperty(propertyName);
                if (property?.PropertyType == typeof(string)
                    && property.GetValue(databaseObject, null) is string textValue)
                {
                    contents.Add(textValue);
                    return;
                }
            }
            catch { }
        }
        try
        {
            var mtextProperty = type.GetProperty("MText");
            if (mtextProperty?.GetValue(databaseObject, null) is CadMText nestedMText)
                contents.Add(GetVisibleMText(nestedMText));
        }
        catch { }
    }

    private static string GetVisibleMText(CadMText mtext)
    {
        try
        {
            var textProperty = mtext.GetType().GetProperty("Text");
            if (textProperty?.PropertyType == typeof(string)
                && textProperty.GetValue(mtext, null) is string visibleText)
                return visibleText;
        }
        catch { }
        return mtext.Contents;
    }

    private CommandModeFrame TakeCommandFrame(CadDocument document, string command)
    {
        for (var index = _commandFrames.Count - 1; index >= 0; index--)
        {
            var frame = _commandFrames[index];
            if (!ReferenceEquals(frame.Document, document)
                || !frame.Command.Equals(command, StringComparison.OrdinalIgnoreCase)) continue;
            _commandFrames.RemoveAt(index);
            return frame;
        }
        return null;
    }

    private LispExecutionFrame TakeLispExecution(CadDocument document)
    {
        for (var index = _lispExecutions.Count - 1; index >= 0; index--)
        {
            var execution = _lispExecutions[index];
            if (!ReferenceEquals(execution.Document, document)) continue;
            _lispExecutions.RemoveAt(index);
            return execution;
        }
        return null;
    }

    private bool? CurrentCommandPreference => GetCommandPreference(AcApp.DocumentManager.MdiActiveDocument);

    private bool? GetCommandPreference(CadDocument document) =>
        _commandFrames.LastOrDefault(frame => ReferenceEquals(frame.Document, document))?.PreferredChinese;

    private bool HasCommandFrames(CadDocument document) =>
        _commandFrames.Any(frame => ReferenceEquals(frame.Document, document));

    private void TrackCommandDepth(CadDocument document, int delta)
    {
        if (document == null) return;
        _documentCommandDepths.TryGetValue(document, out var depth);
        depth = Math.Max(0, depth + delta);
        if (depth == 0) _documentCommandDepths.Remove(document);
        else _documentCommandDepths[document] = depth;
        RecalculateCommandDepths();
    }

    private void RecalculateCommandDepths()
    {
        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (!ReferenceEquals(document, _currentDocument))
        {
            _currentDocument = document;
            _lastInputTarget = IntPtr.Zero;
            _pendingModeReapplyCount = 0;
        }
        _activeTextCommandDepth = _commandFrames.Count(frame => ReferenceEquals(frame.Document, document)
                                                               && frame.PreferredChinese == true);
        _activeOtherCommandDepth = _commandFrames.Count(frame => ReferenceEquals(frame.Document, document))
                                   - _activeTextCommandDepth;
        var depth = 0;
        if (document != null) _documentCommandDepths.TryGetValue(document, out depth);
        Interlocked.Exchange(ref _activeCadCommandDepth, depth);
    }

    private void SynchronizeCurrentDocumentCommandState()
    {
        // DocumentActivated can arrive before MdiActiveDocument has changed.
        // Publish the settled document's busy state on the CAD UI thread, even
        // with IME disabled; the mouse timer only reads the atomic snapshot.
        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (!ReferenceEquals(document, _currentDocument))
        {
            Attach(document);
            RecalculateCommandDepths();
        }
        if (document == null || (!HasActiveCadCommand && _activeTextCommandDepth == 0
                                && _activeOtherCommandDepth == 0
                                && !_lispExecutions.Any(frame => ReferenceEquals(frame.Document, document))))
            return;
        try
        {
            // OPEN/NEW and third-party document creation can leave unmatched
            // notifications on the old drawing. Never clear suspended commands
            // merely because the drawing changed: require native idle evidence
            // from the now-current drawing, including the Lisp activity bits.
            if (!document.Editor.IsQuiescent || !string.IsNullOrEmpty(document.CommandInProgress)
                || Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")) != 0)
                return;
        }
        catch (System.Exception ex)
        {
            Logger.Trace("核对当前图纸命令状态失败，保留命令占用保护：" + ex.Message);
            return;
        }

        _documentCommandDepths.Remove(document);
        _commandFrames.RemoveAll(frame => ReferenceEquals(frame.Document, document));
        _lispExecutions.RemoveAll(frame => ReferenceEquals(frame.Document, document));
        _pendingModeReapplyCount = 0;
        _lastInputTarget = IntPtr.Zero;
        RecalculateCommandDepths();
        Logger.Trace("当前图纸已确认空闲，清除未配对的命令状态并恢复轮盘触发。");
    }

    private bool Switch(bool chinese, string reason)
    {
        var config = Current;
        var mainWindow = _mainWindowProvider();
        if (!ImeNativeBridge.IsCurrentProcessForeground(mainWindow))
        {
            _skippedBackgroundCount++;
            _lastInputTarget = IntPtr.Zero;
            Logger.Trace($"CAD 当前不在前台，已跳过输入法切换；原因={reason}。");
            return false;
        }
        var target = ImeNativeBridge.ResolveInputTarget(mainWindow);
        if (target == IntPtr.Zero)
        {
            _skippedBackgroundCount++;
            return false;
        }
        var requested = config.InputMethodLayoutId;
        var profileId = config.InputMethodProfileId;
        var outcome = ImeNativeBridge.TrySwitch(requested, profileId, chinese, mainWindow);
        ReportInputMethodFallback(outcome);
        if (outcome.ProfileActivationAttempted)
        {
            if (outcome.ProfileActivatedForProcess) _profileActivationCount++;
            else _profileActivationFailureCount++;
        }
        if (outcome.ModeDetected) _modeDetectionCount++;
        if (outcome.RaceBlocked) _raceBlockedCount++;
        if (!outcome.Success)
        {
            var errorSignature = $"{outcome.SelectionId}|{chinese}|{outcome.ModeBackend}";
            if (!errorSignature.Equals(_lastModeErrorSignature, StringComparison.Ordinal))
            {
                _lastModeErrorSignature = errorSignature;
                Logger.Error($"CAD 输入法内部{(chinese ? "中文" : "英文")}模式未能确认；输入法={outcome.SelectionId}；后端={outcome.ModeBackend}；阶段={outcome.ModeFailureStage}；原因={reason}。");
                ShowErrorReportIfEnabled(
                    "输入法中英文状态切换没有完成。插件已停止本次切换，Windows 当前输入法继续保持可用。\r\n\r\n" +
                    "你可以继续正常使用 CAD；如需重新选择输入法，请运行 XYLPIME。\r\n\r\n" +
                    $"记录时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                    $"CAD：{CadHostInfo.DisplayName}\r\n" +
                    $"详细日志：{Logger.ErrorLogPath}");
            }
            return false;
        }
        if (outcome.UsedFallback && !outcome.ModeDetected && string.IsNullOrWhiteSpace(outcome.SelectionId))
        {
            _skippedDuplicateCount++;
            _lastInputTarget = target;
            Logger.Trace($"已停止指定输入法切换并沿用 Windows 当前输入法；原因={reason}。");
            return true;
        }
        _lastModeErrorSignature = "";
        if (outcome.AlreadyCorrect) _skippedDuplicateCount++;
        if (outcome.ModeChanged) _modeTransitionCount++;
        _lastAppliedChinese = chinese;
        LastModeBackend = outcome.ModeBackend;
        _lastInputTarget = outcome.Target;
        _appliedSwitchCount++;
        _hasAppliedChinese |= chinese;
        _hasAppliedEnglish |= !chinese;
        Logger.Trace($"CAD 输入法内部模式已确认为{(chinese ? "中文" : "英文")}：{outcome.SelectionId}；后端={outcome.ModeBackend}；变化={outcome.ModeChanged}；原因={reason}。");
        return true;
    }

    private void ReportInputMethodFallback(ImeSwitchOutcome outcome)
    {
        if (outcome == null || string.IsNullOrWhiteSpace(outcome.FallbackReason))
        {
            if (outcome?.Success == true && !outcome.UsedFallback) _lastInputMethodFallbackSignature = "";
            return;
        }
        var signature = outcome.RequestedSelectionId + "|" + outcome.SelectionId + "|" + outcome.FallbackReason;
        if (signature.Equals(_lastInputMethodFallbackSignature, StringComparison.Ordinal)) return;
        _lastInputMethodFallbackSignature = signature;
        Logger.Error("CAD 输入法设置当前不可用，已沿用 Windows 当前输入法。");
        ShowErrorReportIfEnabled(
            "输入法设置当前不可用。插件没有改用其他输入法，已直接沿用 Windows 当前输入法。\r\n\r\n" +
            "你可以继续正常使用 CAD；如需重新选择输入法，请运行 XYLPIME。\r\n\r\n" +
            $"记录时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
            $"CAD：{CadHostInfo.DisplayName}\r\n" +
            $"详细日志：{Logger.ErrorLogPath}");
    }

    private void ShowErrorReportIfEnabled(string text)
    {
        if (Current.ShowErrorReport != true) return;
        try
        {
            if (_errorReportWindow == null || _errorReportWindow.IsDisposed)
            {
                _errorReportWindow = new ImeErrorReportWindow(() =>
                {
                    var pluginConfig = _configProvider();
                    if (pluginConfig?.ImeSwitch == null) return;
                    pluginConfig.ImeSwitch.ShowErrorReport = false;
                    ConfigStore.Save(pluginConfig);
                    Logger.Info("用户已关闭输入法文本错误报告；错误仍会写入插件日志。");
                });
                _errorReportWindow.FormClosed += (_, _) => _errorReportWindow = null;
            }
            _errorReportWindow.ShowReport(text, _mainWindowProvider());
        }
        catch (Exception ex)
        {
            Logger.Trace("显示输入法文本报告失败：" + ex.Message);
        }
    }

    private sealed class CommandModeFrame
    {
        public CommandModeFrame(CadDocument document, string command, bool? preferredChinese,
            bool contentAwareCandidate)
        {
            Document = document;
            Command = command ?? "";
            PreferredChinese = preferredChinese;
            ContentAwareCandidate = contentAwareCandidate;
        }

        public CadDocument Document { get; }
        public string Command { get; }
        public bool? PreferredChinese { get; set; }
        public bool ContentAwareCandidate { get; }
        public CadObjectId LastResolvedObjectId { get; set; }
    }

    private sealed class LispExecutionFrame
    {
        public LispExecutionFrame(CadDocument document, CommandModeFrame modeFrame, bool selectedForChinese)
        {
            Document = document;
            ModeFrame = modeFrame;
            SelectedForChinese = selectedForChinese;
        }

        public CadDocument Document { get; }
        public CommandModeFrame ModeFrame { get; }
        public bool SelectedForChinese { get; }
    }
}
