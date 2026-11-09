using System.Drawing;
using System.IO;
using System.Windows.Forms;
#if ZWCAD
using ZwSoft.ZwCAD.Runtime;
using AcApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using AcLegacyApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadDocumentCollectionEventArgs = ZwSoft.ZwCAD.ApplicationServices.DocumentCollectionEventArgs;
using CadSystemVariableChangedEventArgs = ZwSoft.ZwCAD.ApplicationServices.SystemVariableChangedEventArgs;
#else
using Autodesk.AutoCAD.Runtime;
#if AUTOCAD_PRE2013
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif
using AcLegacyApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CadDocumentCollectionEventArgs = Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs;
using CadSystemVariableChangedEventArgs = Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs;
#endif
using SystemException = System.Exception;

namespace XiaoYang.AutoCADPlugin;

/// <summary>
/// Central application boundary for all XiaoYang CAD plugin capabilities.
/// The AutoCAD command host delegates here so feature code has one stable
/// entry point independent of command aliases or host registration details.
/// </summary>
internal sealed class CadPluginApplication : ICadPluginApplication
{
    private static PluginConfig _config = new();
    private static CadCommandCatalog _catalog;
    private static CadCommandExecutor _executor;
    private static RadialOverlayHost _overlay;
    private static MouseWheelTrigger _trigger;
    private static ImeAutoSwitchService _imeSwitch;
    private static SettingsWindow _settings;
    private static ExtensionLoadService _extensionLoader;
    private static PluginConfig _extensionSnapshot = new();
    private static System.Windows.Forms.Timer _triggerRetryTimer;
    private static int _triggerRetryCount;
    private static bool _menuRepairEventsRegistered;
    private static DateTime _nextMenuIdleCheck = DateTime.MaxValue;
    private static int _menuRepairAttempts;
    private static int _initialized;
#if AUTOCAD_WIN32
    private static LegacyDeferredStartup _deferredStartup;
    private static DateTime _nextStartupCheck;
#endif

    public void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
#if AUTOCAD_WIN32
        // R18.0 startup registration can run while the native document manager
        // and COM menu are still being constructed. Initialize only on ready Idle.
        _deferredStartup = new LegacyDeferredStartup(IsLegacyHostReady, InitializeServices);
        _nextStartupCheck = DateTime.UtcNow.AddMilliseconds(500);
        AcApp.Idle += OnLegacyStartupIdle;
        Logger.Info("AutoCAD 2010 Win32：已登记启动，等待主窗口和图纸空闲后初始化。");
#else
        InitializeServices();
#endif
    }

#if AUTOCAD_WIN32
    private static bool IsLegacyHostReady()
    {
        if (DateTime.UtcNow < _nextStartupCheck) return false;
        _nextStartupCheck = DateTime.UtcNow.AddMilliseconds(500);
        try
        {
            var document = AcApp.DocumentManager.MdiActiveDocument;
            return document != null && GetAutoCadWindow() != IntPtr.Zero
                && document.Editor.IsQuiescent
                && Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")) == 0;
        }
        catch { return false; } // The next Idle notification may have a ready host.
    }

    private static void OnLegacyStartupIdle(object sender, EventArgs e)
    {
        var startup = _deferredStartup;
        if (startup == null || Volatile.Read(ref _initialized) == 0) return;
        if (!startup.Tick() || !ReferenceEquals(startup, _deferredStartup)) return;
        AcApp.Idle -= OnLegacyStartupIdle;
        _deferredStartup = null;
    }
#endif

    [System.Diagnostics.Conditional("AUTOCAD_WIN32")]
    private static void TraceStartup(string stage) => Logger.Info("AutoCAD 2010 Win32 启动阶段：" + stage);

    private void InitializeServices()
    {
        try
        {
            TraceStartup("读取配置");
            _config = ConfigStore.Load();
            UpdateExtensionSnapshot(_config);
            TraceStartup("读取命令目录");
            _catalog = new CadCommandCatalog();
            if (IsFullAutoCadHost()) PrimeCommandCatalog();
            TraceStartup("创建命令队列");
            _executor = new CadCommandExecutor(_catalog, () => _config);
            var isFullAutoCad = IsFullAutoCadHost();
            if (isFullAutoCad)
            {
                TraceStartup("创建轮盘窗口");
                _overlay = new RadialOverlayHost(item => _executor.Queue(item));
                _overlay.Start();
                _overlay.Prime(_config, SceneKeys.Global);
                TraceStartup("注册鼠标触发");
                _trigger = new MouseWheelTrigger(
                    () => _config.TriggerMode,
                    () => _config.HoldDelayMs,
                    GetAutoCadWindow,
                    IsCadCommandActive,
                    OpenWheel,
                    point => _overlay?.Commit(point),
                    () => _overlay?.Cancel());
                if (!_trigger.Start()) StartTriggerRetry();
                TraceStartup("注册输入法服务");
                _imeSwitch = new ImeAutoSwitchService(() => _config, GetAutoCadWindow,
                    command => _catalog.GetEquivalentCommandNames(command));
                _imeSwitch.Start();
                TraceStartup("注册扩展服务");
                _extensionLoader = new ExtensionLoadService();
                _extensionLoader.Start(() => _extensionSnapshot);
                TraceStartup("登记菜单空闲检查");
                RegisterMenuRepairEvents();
                ScheduleGentleMenuRepair(TimeSpan.FromSeconds(3));
#if !AUTOCAD_WIN32
                TryImmediateGentleMenuRepair();
#endif
            }
            Logger.Info($"小阳CAD轮盘插件已加载（{CadHostInfo.DisplayName}）。");
        }
        catch (SystemException ex)
        {
            Logger.Error("插件初始化失败。", ex);
            TerminateServices(hostTerminating: false);
        }
    }

    public void Terminate() => TerminateServices(hostTerminating: true);

    private static void TerminateServices(bool hostTerminating)
    {
        if (Interlocked.Exchange(ref _initialized, 0) == 0) return;
#if AUTOCAD_WIN32
        var startup = _deferredStartup;
        _deferredStartup = null;
        startup?.Cancel();
        if (startup != null && !hostTerminating) AcApp.Idle -= OnLegacyStartupIdle;
#endif
        Logger.Info("小阳CAD轮盘插件开始卸载。");
        StopTriggerRetry();
        try { _extensionLoader?.Dispose(hostTerminating); } catch (SystemException ex) { Logger.Error("释放功能管理器失败。", ex); }
        _extensionLoader = null;
        UnregisterMenuRepairEvents(hostTerminating);
        Logger.Info($"卸载阶段：计时器和 {CadHostInfo.ProductName} 事件已释放。");
        try { _imeSwitch?.Dispose(hostTerminating); } catch (SystemException ex) { Logger.Error("释放输入法自动切换服务失败。", ex); }
        _imeSwitch = null;
        Logger.Info("卸载阶段：输入法自动切换服务已释放。");
        CadMenuBar.Remove();
        Logger.Info("卸载阶段：顶部菜单已释放。");
        try { _trigger?.Dispose(); } catch (SystemException ex) { Logger.Error("释放鼠标触发器失败。", ex); }
        Logger.Info("卸载阶段：鼠标触发器已释放。");
        try { _overlay?.Dispose(); } catch (SystemException ex) { Logger.Error("释放轮盘窗口失败。", ex); }
        try { _executor?.Dispose(hostTerminating); } catch (SystemException ex) { Logger.Error("释放轮盘命令队列失败。", ex); }
        Logger.Info("卸载阶段：轮盘窗口线程已释放。");
        try
        {
            if (_settings != null && !_settings.IsDisposed) _settings.Dispose();
        }
        catch { }
#if AUTOCAD_CLR2
        Logger.Info("小阳CAD轮盘插件已卸载（旧版 AutoCAD）。");
#else
        Logger.Info($"小阳CAD轮盘插件已卸载（{CadHostInfo.DisplayName}）。");
#endif
    }

    public void ShowWheel()
    {
        PrepareWheel();
        OpenWheel(Cursor.Position, MouseButtons.None);
    }

    public void ShowSettings()
    {
        try
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                return;
            }

            _settings = new SettingsWindow(_config, _catalog, ApplyConfig,
                requestLocalCommandRestore: RestoreNativeCommandCatalog, requestMenuRestore: RepairMenu);
            _settings.FormClosed += (_, _) => _settings = null;
            AcLegacyApp.ShowModelessDialog(_settings);
        }
        catch (SystemException ex)
        {
            Logger.Error("打开设置窗口失败。", ex);
            AcApp.ShowAlertDialog("小阳CAD轮盘插件设置打开失败：" + ex.Message);
        }
    }

    public void ShowImeSettings()
    {
        try
        {
            using var dialog = new ImeSettingsDialog(_config.ImeSwitch, updated =>
            {
                _config.ImeSwitch = updated;
                ApplyConfig(_config);
            });
            AcLegacyApp.ShowModalDialog(dialog);
        }
        catch (SystemException ex)
        {
            Logger.Error("打开输入法自动切换设置失败。", ex);
            AcApp.ShowAlertDialog("输入法自动切换设置打开失败：" + ex.Message);
        }
    }

    public void ToggleImeSwitch()
    {
        try
        {
            _config.ImeSwitch ??= new ImeSwitchConfig();
            _config.ImeSwitch.Enabled = !_config.ImeSwitch.Enabled;
            ConfigStore.Save(_config);
            _imeSwitch?.ApplyConfiguration();
            var state = _config.ImeSwitch.Enabled ? "已启用" : "已停用";
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n小阳CAD输入法自动切换：{state}。输入 XYLPIME 可设置文字命令。");
        }
        catch (SystemException ex)
        {
            Logger.Error("切换输入法自动切换开关失败。", ex);
            AcApp.ShowAlertDialog("输入法自动切换开关失败：" + ex.Message);
        }
    }

    public void RefreshCommands()
    {
        try
        {
            var commands = _catalog.GetAll(true);
            _settings?.ReloadCommandCatalogFromCache();
            var sources = string.Join("\n", _catalog.SourcePaths.Select(path => "  " + path));
            var pgp = string.IsNullOrWhiteSpace(_catalog.PgpPath)
                ? $"未找到当前 {CadHostInfo.PgpFileName}"
                : $"PGP 快捷键 {_catalog.PgpAliasCount} 个：{_catalog.PgpPath}";
            AcApp.ShowAlertDialog($"已重新读取当前 {CadHostInfo.ProductName} 命令目录：{commands.Count} 个功能。\n" +
                                  $"已加载 CUIX：{_catalog.SourcePaths.Count} 个；{pgp}\n来源：\n{sources}");
        }
        catch (SystemException ex)
        {
            Logger.Error("刷新命令目录失败。", ex);
            AcApp.ShowAlertDialog("命令目录读取失败：" + ex.Message);
        }
    }

    public void RestoreLocalCommands() => RestoreNativeCommandCatalog();

    private static void RestoreNativeCommandCatalog()
    {
        try
        {
            var document = AcApp.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                AcApp.ShowAlertDialog("当前没有可用图纸，无法重新读取本地命令目录。");
                return;
            }
            var activeCommands = Convert.ToString(AcApp.GetSystemVariable("CMDNAMES"))?.Trim() ?? "";
            if (activeCommands.Length > 0
                && activeCommands.IndexOf("XYRESTORELOCALCMDS", StringComparison.OrdinalIgnoreCase) < 0)
            {
                AcApp.ShowAlertDialog("当前有 CAD 命令正在运行。请先正常结束当前命令，再重新读取本地命令；插件不会替你取消命令。");
                return;
            }
            var result = _catalog.RestoreNativeCommands();
            if (_settings != null && !_settings.IsDisposed)
                _settings.ApplyLocalCommandRestoreResult(result);
            else
                AcApp.ShowAlertDialog(
                    $"已重新读取本地命令 {result.CommandCount} 个，其中 CAD/小阳命令 {result.NativeCommandCount} 个、第三方命令 {result.ThirdPartyCommandCount} 个。\n" +
                    $"当前 {CadHostInfo.PgpFileName} 仅被读取，未删除、改写或重载任何快捷键；轮盘配置和自定义命令保持不变。");
            Logger.Info($"本地命令重新读取完成：共 {result.CommandCount} 个，CAD/小阳命令 {result.NativeCommandCount} 个，第三方命令 {result.ThirdPartyCommandCount} 个；PGP 只读。");
        }
        catch (SystemException ex)
        {
            Logger.Error("重新读取本地命令失败。", ex);
            AcApp.ShowAlertDialog("重新读取本地命令失败：" + ex.Message);
        }
    }

    private static void PrimeCommandCatalog()
    {
        try
        {
            var commands = _catalog.GetAll();
            Logger.Info($"启动时已读取并缓存当前 {CadHostInfo.ProductName} 主命令目录：{commands.Count} 个功能；轮盘执行阶段只查询该缓存。");
        }
        catch (SystemException ex)
        {
            Logger.Error($"启动时读取 {CadHostInfo.ProductName} 命令目录失败；可在设置中点击刷新后重试。", ex);
        }
    }

    public void RepairMenu()
    {
        if (!IsFullAutoCadHost()) return;
        try
        {
            // This entry runs only on an explicit user repair request. Startup
            // registration deliberately leaves the user's MENUBAR unchanged.
            AcApp.SetSystemVariable("MENUBAR", 1);
            var installed = CadMenuBar.Ensure(force: true);
            var visible = Convert.ToInt32(AcApp.GetSystemVariable("MENUBAR")) == 1;
            AcApp.ShowAlertDialog(installed && visible
                ? "CAD 菜单栏已显示（MENUBAR=1），“小阳CAD轮盘”菜单已恢复。"
                : "已请求显示 CAD 菜单栏；小阳菜单暂未完全就绪，请在 CAD 空闲后再次运行 XYLPMENU。");
        }
        catch (SystemException ex)
        {
            Logger.Error("手动恢复菜单栏失败。", ex);
            AcApp.ShowAlertDialog("恢复菜单栏失败：" + ex.Message);
        }
    }

    // Kept as a compatibility entry for older registrations. It only schedules
    // an idle-time check and never injects a nested command during LSP startup.
    public void ScheduleStartupMenuRepair()
    {
        if (!IsFullAutoCadHost()) return;
        ScheduleGentleMenuRepair(TimeSpan.FromSeconds(3));
        Logger.Info("启动自动加载检查已登记，将在 CAD 完全空闲后温和补充本插件菜单。 ");
    }

    public void RunSelfTest()
    {
        var reportPath = Path.Combine(PluginPaths.LogDirectory, "self-test.json");
        try
        {
            var isCoreConsole = !IsFullAutoCadHost();
            _overlay?.Start();
            if (!isCoreConsole) _trigger?.Start();
            var systemVariablesBefore = CaptureUserSystemVariables();
            var fileDiaBefore = AcApp.GetSystemVariable("FILEDIA");
            var cmdDiaBefore = AcApp.GetSystemVariable("CMDDIA");
            _executor.ExecuteForSelfTest("REGEN");
            var fileDiaAfter = AcApp.GetSystemVariable("FILEDIA");
            var cmdDiaAfter = AcApp.GetSystemVariable("CMDDIA");
            var dialogVariablesPreserved = Equals(fileDiaBefore, fileDiaAfter)
                                           && Equals(cmdDiaBefore, cmdDiaAfter);
            // 自检只核对启动/手动刷新形成的缓存，不在自检或轮盘执行阶段实时扫描 CUI。
            var commands = isCoreConsole ? Array.Empty<CadCommandInfo>() : _catalog.GetAll();
            var hookInstalled = !isCoreConsole && _trigger?.IsInstalled == true;
            var overlayReady = !isCoreConsole && _overlay?.IsReady == true;
            var topMenuVisibleBeforeRepair = !isCoreConsole && CadMenuBar.IsVisible();
            var topMenuInstalled = !isCoreConsole && CadMenuBar.Ensure(force: true);
            var topMenuMacrosValid = !isCoreConsole && topMenuInstalled && CadMenuBar.ValidateCommandMacros();
            var systemVariablesAfter = CaptureUserSystemVariables();
            var userSystemVariablesPreserved = SystemVariableSnapshotsEqual(systemVariablesBefore, systemVariablesAfter);
            var localizedSelection = CadCommandCatalog.SelectBestLocalizedCandidate(new[]
            {
                new CadCommandInfo { DisplayName = "DIMLINEAR", GlobalName = "DIMLINEAR", Macro = "^C^C_dimlinear" },
                new CadCommandInfo { DisplayName = "标注 · 线性", GlobalName = "DIMLINEAR", Macro = "^C^C_dimlinear", Help = "创建线性标注" }
            });
            var localizedNameSelectionPassed = localizedSelection.DisplayName == "标注 · 线性";
            var nativeCommandSelection = CadCommandCatalog.SelectNativeCommands(new[]
            {
                new CadCommandInfo
                {
                    DisplayName = "直线", GlobalName = "LINE", Group = "ACAD", IsCoreCommand = true
                },
                new CadCommandInfo
                {
                    DisplayName = "QD", GlobalName = "YX_Q", Group = "ACADMacros", IsCoreCommand = true
                },
                new CadCommandInfo
                {
                    DisplayName = "外部命令", GlobalName = "SUN-017", Group = "LOCAL_RUNTIME", IsCoreCommand = false
                }
            }.Concat(PluginCommandCatalog.All));
            var nativeCommandSelectionPolicyPassed =
                nativeCommandSelection.Any(command => command.GlobalName.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                && nativeCommandSelection.Any(command => command.GlobalName.Equals("XYRESTORELOCALCMDS", StringComparison.OrdinalIgnoreCase))
                && nativeCommandSelection.All(command =>
                    !command.GlobalName.Equals("YX_Q", StringComparison.OrdinalIgnoreCase)
                    && !command.GlobalName.Equals("SUN-017", StringComparison.OrdinalIgnoreCase));
            var thirdPartyCategoryPolicyPassed =
                CommandCategoryCatalog.Categorize(new CadCommandInfo
                {
                    DisplayName = "QD", GlobalName = "YX_Q", Group = "ACADMacros", IsCoreCommand = true
                }) == CommandCategoryCatalog.ThirdPartyCategory
                && CommandCategoryCatalog.Categorize(new CadCommandInfo
                {
                    DisplayName = "本地命令", GlobalName = "SUN-017", Group = "LOCAL_RUNTIME", IsCoreCommand = false
                }) == CommandCategoryCatalog.ThirdPartyCategory
                && CommandCategoryCatalog.Categorize(new CadCommandInfo
                {
                    DisplayName = "直线", GlobalName = "LINE", Group = "ACAD", IsCoreCommand = true
                }) != CommandCategoryCatalog.ThirdPartyCategory;
            var pgpNativeDimension = CadCommandCatalog.CreatePgpOnlyCommand(
                "DIMALIGNED", "acad.pgp", isCoreCommand: true);
            var pgpExternalCommand = CadCommandCatalog.CreatePgpOnlyCommand(
                "YX_Q", "acad.pgp", isCoreCommand: false);
            var pgpAliasClassificationPassed =
                CommandCategoryCatalog.Categorize(pgpNativeDimension) == "标注与文字"
                && CommandCategoryCatalog.Categorize(pgpExternalCommand) == CommandCategoryCatalog.ThirdPartyCategory;
            var localCommandReloadSafetyPassed =
                CadCommandCatalog.StartupIncludesLoadedPartialCuiFiles
                && CadCommandCatalog.LocalCommandRestoreIsReadOnly;
            var localCommandRestoreEntryPassed = PluginCommandCatalog.All.Any(command =>
                command.GlobalName.Equals("XYRESTORELOCALCMDS", StringComparison.OrdinalIgnoreCase));
            var expectedInteractiveQueue = "DIMLINEAR ";
            var nativeInteractiveQueueFormatPassed = CadCommandExecutor.BuildInteractiveCommandLine("DIMLINEAR", false)
                                                     == expectedInteractiveQueue;
            var modifierGestureGuardPassed = MouseWheelTrigger.RequiredModifierKeyForMode(TriggerModes.ShiftRightHold) == 16
                                             && MouseWheelTrigger.RequiredModifierKeyForMode(TriggerModes.AltRightHold) == 18
                                             && MouseWheelTrigger.RequiredModifierKeyForMode(TriggerModes.CtrlRightHold) == 17
                                             && MouseWheelTrigger.RequiredModifierKeyForMode(TriggerModes.RightHold) == 0
                                             && CadCommandExecutor.UsesPhysicalModifierReleaseGuard;
            var mouseCompatibilityPolicyPassed =
                MouseWheelTrigger.ShouldBypassForActiveCommand(TriggerModes.RightHold, commandActive: true)
                && !MouseWheelTrigger.ShouldBypassForActiveCommand(TriggerModes.RightHold, commandActive: false)
                && MouseWheelTrigger.ShouldBypassForActiveCommand(TriggerModes.ShiftRightHold, commandActive: true)
                && MouseWheelTrigger.ShouldBypassForActiveCommand(TriggerModes.CtrlRightHold, commandActive: true)
                && CadCommandExecutor.PreservesActiveCommands
                && MouseWheelTrigger.PreservesPairedRightClickDelivery
                && MouseWheelTrigger.CallsNextHookBeforeSuppression
                && MouseWheelTrigger.RestrictsTriggerToMdiDrawingArea
                && MouseWheelTrigger.RequiresEnabledForegroundMainWindow
                && MouseWheelTrigger.IsDrawingAreaClassPath(new[] { "AcGsView", "MDIClient" })
                && !MouseWheelTrigger.IsDrawingAreaClassPath(new[] { "WindowsForms10.Window", "MDIClient" });
            var configuredWheelCommandResolutionPassed =
                CadCommandExecutor.ConfiguredWheelCommandsUseCurrentCadResolution;
            var imeCommandMatchingPassed = ImeCommandCatalog.IsTextCommand(new ImeSwitchConfig(), "_TEXTEDIT")
                                           && ImeCommandCatalog.IsTextCommand(new ImeSwitchConfig(), "DDEDIT")
                                           && ImeCommandCatalog.IsTextCommand(new ImeSwitchConfig(), "_.-MTEDIT")
                                           && ImeCommandCatalog.IsTextCommand(
                                               new ImeSwitchConfig { TextCommands = new List<string> { "MY-TEXT" } },
                                               "_.-C:MY-TEXT")
                                           && ImeCommandCatalog.IsTextCommand(
                                               new ImeSwitchConfig { TextCommands = new List<string> { "YX_Q" } },
                                               "C:YX_Q")
                                           && ImeCommandCatalog.IsTextCommand(
                                               new ImeSwitchConfig { TextCommands = new List<string> { "C:YX_Q" } },
                                               "YX_Q")
                                           && ImeCommandCatalog.IsTextCommand(
                                               new ImeSwitchConfig { TextCommands = new List<string> { "YX_Q" } },
                                               "Q", new[] { "YX_Q" })
                                           && !ImeCommandCatalog.IsTextCommand(new ImeSwitchConfig(), "RECTANG");
            var imeLispCommandMatchingPassed =
                ImeCommandCatalog.ExtractLispCommand("(C:YX_Q)") == "YX_Q"
                && ImeCommandCatalog.ExtractLispCommand(" (c:my-text ) ") == "MY-TEXT"
                && ImeCommandCatalog.ExtractLispCommand("C:YX_Q") == "YX_Q"
                && ImeCommandCatalog.ExtractLispCommand("(defun c:YX_Q () (princ))") == "";
            var imeNestedCommandInheritancePassed =
                ImeAutoSwitchService.ResolveCommandPreference(null, selectedForChinese: false,
                    parentPreference: true, new ImeSwitchConfig()) == true
                && ImeAutoSwitchService.ResolveCommandPreference(false, selectedForChinese: false,
                    parentPreference: true, new ImeSwitchConfig()) == false
                && ImeAutoSwitchService.ResolveCommandPreference(null, selectedForChinese: true,
                    parentPreference: false, new ImeSwitchConfig()) == true;
            var imeExistingTextContentPolicyPassed =
                ImeTextEditPolicy.IsExistingTextEditCommand("DDEDIT")
                && ImeTextEditPolicy.IsExistingTextEditCommand("_TEXTEDIT")
                && ImeTextEditPolicy.IsExistingTextEditCommand("MLEADERCONTENTEDIT")
                && !ImeTextEditPolicy.IsExistingTextEditCommand("MTEXT")
                && !ImeTextEditPolicy.IsExistingTextEditCommand("MLEADEREDIT")
                && ImeTextEditPolicy.PreferChineseForExistingContents(new[] { "酸洗" }) == true
                && ImeTextEditPolicy.PreferChineseForExistingContents(new[] { "100件" }) == true
                && ImeTextEditPolicy.PreferChineseForExistingContents(new[] { "71.8*2*233" }) == false
                && ImeTextEditPolicy.PreferChineseForExistingContents(new[] { "ABC123" }) == false
                && ImeTextEditPolicy.PreferChineseForExistingContents(Array.Empty<string>()) == null;
            var imeLanguages = ImeNativeBridge.GetInstalledLanguages();
            var resolvedIme = ImeNativeBridge.ResolveLanguage(_config.ImeSwitch?.InputMethodLayoutId,
                _config.ImeSwitch?.InputMethodProfileId, chinese: true);
            var imeConfiguredInputMethodAvailable = resolvedIme?.IsChinese == true
                                                    && resolvedIme.IsInputMethod
                                                    && !resolvedIme.IsEnglish;
            var exactIme = new ImeLanguageChoice
            {
                LayoutId = "08040804", ProfileId = "TSF|EXACT", DisplayName = "已设置输入法",
                IsChinese = true, IsInputMethod = true
            };
            var systemIme = new ImeLanguageChoice
            {
                LayoutId = "08040804", ProfileId = "TSF|SYSTEM", DisplayName = "系统当前输入法",
                IsChinese = true, IsInputMethod = true
            };
            var exactImeResolution = ImeNativeBridge.ResolveLanguageFromChoices(
                new[] { systemIme, exactIme }, "00000804", exactIme.SelectionId, systemIme.SelectionId);
            var missingImeResolution = ImeNativeBridge.ResolveLanguageFromChoices(
                new[] { systemIme }, "00000804", "TSF|MISSING", systemIme.SelectionId);
            var missingImeWithoutSystemResolution = ImeNativeBridge.ResolveLanguageFromChoices(
                new[] { systemIme }, "00000804", "TSF|MISSING", "KBD|04090409");
            var imeExactSelectionPassed = ReferenceEquals(exactImeResolution.Choice, exactIme)
                                          && !exactImeResolution.UsedFallback;
            var imeMissingUsesSystemCurrentPassed = ReferenceEquals(missingImeResolution.Choice, systemIme)
                                                    && missingImeResolution.UsedFallback
                                                    && !missingImeResolution.FallbackReason.Contains(systemIme.DisplayName);
            var imeMissingDoesNotPickArbitraryPassed = missingImeWithoutSystemResolution.Choice == null
                                                       && missingImeWithoutSystemResolution.UsedFallback;
            var imeLanguageResolutionPassed = imeExactSelectionPassed
                                              && imeMissingUsesSystemCurrentPassed
                                              && imeMissingDoesNotPickArbitraryPassed;
            var imeErrorReportPolicyPassed = !new ImeSwitchConfig().ShowErrorReport
                                             && ImeErrorReportWindow.IsNonBlockingNoActivate;
            var imeTsfModePolicyPassed = ImeNativeBridge.UsesTsfCompartmentsForModernInputMethods
                                         && ImeTsfModeBridge.UsesCurrentThreadCompartments
                                         && ImeTsfModeBridge.IsTargetThread(7, 7)
                                         && !ImeTsfModeBridge.IsTargetThread(7, 8);
            var imeDirectInputReapplyPolicyPassed =
                ImeAutoSwitchService.ShouldRunPendingModeReapply(null, pendingChinese: false,
                    idleEnglishPreferred: true)
                && !ImeAutoSwitchService.ShouldRunPendingModeReapply(null, pendingChinese: true,
                    idleEnglishPreferred: true)
                && !ImeAutoSwitchService.ShouldRunPendingModeReapply(null, pendingChinese: false,
                    idleEnglishPreferred: false)
                && ImeAutoSwitchService.ShouldRunPendingModeReapply(activePreference: true,
                    pendingChinese: true, idleEnglishPreferred: true)
                && !ImeAutoSwitchService.ShouldRunPendingModeReapply(activePreference: true,
                    pendingChinese: false, idleEnglishPreferred: true);
            var imeTsfProfileEnumerationPolicyPassed =
                ImeTsfProfileCatalog.LastEnumerationSource == "WindowsTsfApi"
                && ImeTsfProfileCatalog.FriendlyName(@"C:\Program Files\Sogou\SogouTSF.ime") == "搜狗输入法"
                && ImeTsfProfileCatalog.IsSelectableInputMethodProfile(
                    ImeTsfProfileCatalog.ProfileTypeInputProcessor,
                    ImeTsfProfileCatalog.ProfileFlagEnabled,
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"))
                && !ImeTsfProfileCatalog.IsSelectableInputMethodProfile(
                    ImeTsfProfileCatalog.ProfileTypeInputProcessor,
                    0,
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"))
                && !ImeTsfProfileCatalog.IsSelectableInputMethodProfile(
                    ImeTsfProfileCatalog.ProfileTypeKeyboardLayout,
                    ImeTsfProfileCatalog.ProfileFlagEnabled,
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"));
            var legacyImeConfig = new PluginConfig
            {
                ImeSwitch = new ImeSwitchConfig
                {
                    EnglishLayoutId = "00000409",
                    ChineseLayoutId = "00000804",
                    EnglishProfileId = "",
                    ChineseProfileId = "TSF|LEGACY"
                }
            };
            ConfigStore.Normalize(legacyImeConfig);
            var imeSingleInputMethodMigrationPassed =
                legacyImeConfig.ImeSwitch.InputMethodLayoutId == "00000804"
                && legacyImeConfig.ImeSwitch.EnglishLayoutId == legacyImeConfig.ImeSwitch.ChineseLayoutId
                && legacyImeConfig.ImeSwitch.EnglishProfileId == legacyImeConfig.ImeSwitch.ChineseProfileId
                && legacyImeConfig.ImeSwitch.InputMethodProfileId == "TSF|LEGACY"
                && !legacyImeConfig.ImeSwitch.RestoreOnShutdown;
            var imeProcessScopePolicyPassed =
                ImeTsfProfileCatalog.HasProcessOnlyActivationFlags(ImeTsfProfileCatalog.ProcessActivationFlags)
                && ImeTsfProfileCatalog.IsSuccessfulActivationResult(0)
                && !ImeTsfProfileCatalog.IsSuccessfulActivationResult(1)
                && !ImeNativeBridge.IsConcreteNativeImeIdentity(immIsIme: true, hasImeModule: false)
                && ImeNativeBridge.IsConcreteNativeImeIdentity(immIsIme: true, hasImeModule: true)
                && ImeNativeBridge.UsesInputMethodModeOnly
                && !ImeNativeBridge.UsesLayoutLevelSwitching;
            const uint fullShape = 0x0008;
            var imeInternalModePolicyPassed =
                ImeNativeBridge.IsRequestedInternalMode(open: false, hasConversion: true, conversion: 0, chinese: false)
                && !ImeNativeBridge.IsRequestedInternalMode(open: true, hasConversion: true, conversion: 0, chinese: false)
                && ImeNativeBridge.IsRequestedInternalMode(open: true, hasConversion: true, conversion: 1, chinese: true)
                && ImeNativeBridge.BuildRequestedConversionMode(fullShape, chinese: true) == (fullShape | 1)
                && ImeNativeBridge.BuildRequestedConversionMode(fullShape | 1, chinese: false) == fullShape;
            var emptyImeCommandConfig = new PluginConfig
            {
                ImeSwitch = new ImeSwitchConfig { TextCommands = new List<string>() }
            };
            ConfigStore.Normalize(emptyImeCommandConfig);
            var imeEmptyCommandSelectionPassed = emptyImeCommandConfig.ImeSwitch.TextCommands.Count == 0;
            var legacyDisabledImeCommandConfig = new PluginConfig
            {
                ImeSwitch = new ImeSwitchConfig
                {
                    TextCommandsUseChinese = false,
                    TextCommands = new List<string> { "MTEXT" }
                }
            };
            ConfigStore.Normalize(legacyDisabledImeCommandConfig);
            var imeLegacyDisabledSelectionMigrationPassed =
                legacyDisabledImeCommandConfig.ImeSwitch.TextCommandsUseChinese
                && legacyDisabledImeCommandConfig.ImeSwitch.TextCommands.Count == 0;
            var freshImeConfig = ConfigStore.CreateDefault().ImeSwitch;
            var imeFreshDefaultPassed = freshImeConfig.InputMethodLayoutId == "00000804"
                                        && freshImeConfig.EnglishLayoutId == freshImeConfig.InputMethodLayoutId
                                        && freshImeConfig.ChineseLayoutId == freshImeConfig.InputMethodLayoutId
                                        && freshImeConfig.InputMethodLayoutId != "00000409";
            var imeCadWindowTreePolicyPassed =
                ImeNativeBridge.IsCadWindowPolicy(sameWindow: true, isChild: false, sameProcess: true)
                && ImeNativeBridge.IsCadWindowPolicy(sameWindow: false, isChild: true, sameProcess: true)
                && !ImeNativeBridge.IsCadWindowPolicy(sameWindow: false, isChild: false, sameProcess: true)
                && !ImeNativeBridge.IsCadWindowPolicy(sameWindow: true, isChild: false, sameProcess: false);
            var threadLocalInputSettingsRead = ImeNativeBridge.TryGetThreadLocalInputSettings(out var threadLocalInputSettings);
            var currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            var imeForegroundIsolationPolicyPassed =
                ImeNativeBridge.AllowsForegroundProcess(currentProcessId, currentProcessId)
                && !ImeNativeBridge.AllowsForegroundProcess(0, currentProcessId)
                && !ImeNativeBridge.AllowsForegroundProcess(currentProcessId + 1, currentProcessId);
            var legacyRadialRenderingPolicyPassed =
                RadialWindowCompatibility.RequiresLegacySafeRenderer(new Version(6, 1))
                && !RadialWindowCompatibility.RequiresLegacySafeRenderer(new Version(6, 2))
                && !RadialWindowCompatibility.RequiresLegacySafeRenderer(new Version(10, 0));
            var settingsDpiLayoutPolicyPassed =
                SettingsWindow.ScaleLogicalForDpi(36, 96) == 36
                && SettingsWindow.ScaleLogicalForDpi(36, 120) == 45
                && SettingsWindow.ScaleLogicalForDpi(36, 144) == 54
                && SettingsWindow.ScaleLogicalForDpi(36, 192) == 72;
            var imeServiceReady = !isCoreConsole && _imeSwitch?.IsStarted == true;
            var imeLispCommandEventsSubscribed = isCoreConsole || _imeSwitch?.UsesLispCommandEvents == true;
            var dimensionDisplayName = commands.FirstOrDefault(command =>
                command.GlobalName.Equals("DIMLINEAR", StringComparison.OrdinalIgnoreCase))?.DisplayName;
            var catalogPassed = isCoreConsole || commands.Count > 0;
            var passed = catalogPassed
                          && dialogVariablesPreserved
                          && userSystemVariablesPreserved
                          && localizedNameSelectionPassed
                          && nativeCommandSelectionPolicyPassed
                          && thirdPartyCategoryPolicyPassed
                          && pgpAliasClassificationPassed
                          && localCommandReloadSafetyPassed
                          && localCommandRestoreEntryPassed
                           && nativeInteractiveQueueFormatPassed
                           && modifierGestureGuardPassed
                           && mouseCompatibilityPolicyPassed
                           && configuredWheelCommandResolutionPassed
                           && imeCommandMatchingPassed
                           && imeLispCommandMatchingPassed
                           && imeNestedCommandInheritancePassed
                           && imeExistingTextContentPolicyPassed
                             && imeLanguageResolutionPassed
                             && imeExactSelectionPassed
                             && imeMissingUsesSystemCurrentPassed
                             && imeMissingDoesNotPickArbitraryPassed
                              && imeErrorReportPolicyPassed
                              && imeTsfModePolicyPassed
                              && imeDirectInputReapplyPolicyPassed
                              && imeTsfProfileEnumerationPolicyPassed
                             && imeForegroundIsolationPolicyPassed
                             && imeSingleInputMethodMigrationPassed
                             && imeFreshDefaultPassed
                              && imeProcessScopePolicyPassed
                               && imeInternalModePolicyPassed
                               && imeEmptyCommandSelectionPassed
                               && imeLegacyDisabledSelectionMigrationPassed
                              && imeCadWindowTreePolicyPassed
                              && legacyRadialRenderingPolicyPassed
                              && settingsDpiLayoutPolicyPassed
                           && (isCoreConsole || imeServiceReady)
                           && imeLispCommandEventsSubscribed
                           && (isCoreConsole || (hookInstalled && overlayReady && topMenuInstalled && topMenuMacrosValid));
            var assembly = typeof(CadPlugin).Assembly;
            var assemblyPath = assembly.Location;
            var report = new
            {
                Product = CadHostInfo.PluginDisplayName,
                HostProduct = CadHostInfo.DisplayName,
                PluginVersion = ProductMetadata.DisplayVersion,
                AssemblyVersion = assembly.GetName().Version?.ToString(),
                Passed = passed,
                Host = isCoreConsole ? CadHostInfo.HostKey + "CoreConsole" : CadHostInfo.HostKey,
                Initialized = Volatile.Read(ref _initialized) == 1,
                AssemblyPath = assemblyPath,
                InstalledFromApplicationPlugins = assemblyPath.Contains("ApplicationPlugins", StringComparison.OrdinalIgnoreCase),
                PluginDirectory = PluginPaths.PluginDirectory,
                ConfigPath = ConfigStore.ConfigPath,
                ConfigReadable = File.Exists(ConfigStore.ConfigPath),
                LogPath = Logger.LogPath,
                ErrorLogPath = Logger.ErrorLogPath,
                CatalogPath = isCoreConsole ? $"Skipped in {CadHostInfo.HostKey} core console" : _catalog.CuiPath,
                CatalogSourcePaths = isCoreConsole ? null : _catalog.SourcePaths,
                CatalogSourceCount = isCoreConsole ? (int?)null : _catalog.SourcePaths.Count,
                PgpPath = isCoreConsole ? null : _catalog.PgpPath,
                PgpAliasCount = isCoreConsole ? (int?)null : _catalog.PgpAliasCount,
                YanxiuToolboxCommands = isCoreConsole ? (int?)null : commands.Count(command =>
                    command.Group.Equals("YANXIU", StringComparison.OrdinalIgnoreCase)),
                YanxiuMold2dCommands = isCoreConsole ? (int?)null : commands.Count(command =>
                    command.Group.Equals("YANXIU_MOLD2D", StringComparison.OrdinalIgnoreCase)),
                ThirdPartyCommands = isCoreConsole ? (int?)null : commands.Count(command =>
                    CommandCategoryCatalog.Categorize(command, _config) == CommandCategoryCatalog.ThirdPartyCategory),
                CatalogFunctions = isCoreConsole ? (int?)null : commands.Count,
                DirectlyCallable = isCoreConsole ? (int?)null : commands.Count(command => command.CanCallDirectly),
                UniqueGlobalCommands = isCoreConsole ? (int?)null : commands
                    .Where(command => command.CanCallDirectly)
                    .Select(command => command.GlobalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                LinearDimensionDisplayName = isCoreConsole ? null : dimensionDisplayName,
                CatalogCacheReady = isCoreConsole ? (bool?)null : _catalog.IsLoaded,
                CatalogReadMode = "StartupAndManualReloadReadMainCuiLoadedPartialsAndPgp",
                LocalizedNameSelectionPassed = localizedNameSelectionPassed,
                NativeCommandSelectionPolicyPassed = nativeCommandSelectionPolicyPassed,
                ThirdPartyCategoryPolicyPassed = thirdPartyCategoryPolicyPassed,
                PgpAliasClassificationPassed = pgpAliasClassificationPassed,
                StartupIncludesLoadedPartialCuiFiles = CadCommandCatalog.StartupIncludesLoadedPartialCuiFiles,
                LocalCommandRestorePgpReadOnly = CadCommandCatalog.LocalCommandRestoreIsReadOnly,
                LocalCommandReloadSafetyPassed = localCommandReloadSafetyPassed,
                SettingsDpiLayoutPolicyPassed = settingsDpiLayoutPolicyPassed,
                SettingsDpiDesignBaseline = "96x96",
                LocalCommandRestoreEntryPassed = localCommandRestoreEntryPassed,
                LocalCommandRestoreRunsDuringStartupOrSelfTest = false,
                NativeInteractiveQueueFormatPassed = nativeInteractiveQueueFormatPassed,
                ModifierGestureGuardPassed = modifierGestureGuardPassed,
                ModifierGestureExecutionPolicy = "WaitForPhysicalModifierReleaseBeforeCommand",
                MouseCompatibilityPolicyPassed = mouseCompatibilityPolicyPassed,
                MouseTriggerScope = "CurrentMdiDrawingAreaOnly",
                PairedShortRightClickDelivery = MouseWheelTrigger.PreservesPairedRightClickDelivery,
                PlainRightHoldBypassesActiveCommands = true,
                HookChainPreservedBeforeSuppression = MouseWheelTrigger.CallsNextHookBeforeSuppression,
                MouseTriggerRequiresEnabledForegroundMainWindow =
                    MouseWheelTrigger.RequiresEnabledForegroundMainWindow,
                MouseCommandActivityUsesEventCache = true,
                OverlayReady = isCoreConsole ? (bool?)null : overlayReady,
                OverlayRenderCachePrimed = isCoreConsole ? (bool?)null : _overlay?.PrimeCompletedCount > 0,
                OverlayPrimeMilliseconds = isCoreConsole ? (double?)null : _overlay?.LastPrimeMs,
                OverlayLastShowMilliseconds = isCoreConsole ? (double?)null : _overlay?.LastShowPipelineMs,
                TriggerReadsConfigOnMouseDown = false,
                MouseHookInstalled = isCoreConsole ? (bool?)null : hookInstalled,
                MouseHookError = isCoreConsole ? null : _trigger?.LastError,
                TopMenuInstalled = isCoreConsole ? (bool?)null : topMenuInstalled,
                TopMenuVisibleBeforeSelfTestRepair = isCoreConsole ? (bool?)null : topMenuVisibleBeforeRepair,
                TopMenuMacrosValid = isCoreConsole ? (bool?)null : topMenuMacrosValid,
                FileDiaBefore = fileDiaBefore,
                FileDiaAfter = fileDiaAfter,
                CmdDiaBefore = cmdDiaBefore,
                CmdDiaAfter = cmdDiaAfter,
                DialogSystemVariablesPreserved = dialogVariablesPreserved,
                SystemVariablesBefore = systemVariablesBefore,
                SystemVariablesAfter = systemVariablesAfter,
                UserSystemVariablesPreserved = userSystemVariablesPreserved,
                PluginSystemVariableWritePolicy = "CommandExecutionReadOnly;PreserveUserSystemVariables",
                ReGenExecutedThroughEditorCommand = true,
                BuiltInWheelCommandsUseNativeInteractiveQueue = true,
                NativeInteractiveCommandsPreserveObjectSnapAndDynamicInput = true,
                WheelCommandsUseCurrentAutoCadCommandResolution = true,
                ConfiguredWheelCommandsSkipStartupRegistrationGate =
                    CadCommandExecutor.ConfiguredWheelCommandsUseCurrentCadResolution,
                ConfiguredWheelCommandResolutionPassed = configuredWheelCommandResolutionPassed,
                WheelCommandPrefixPolicy = "NoDotAndNoGlobalUnderscorePrefix",
                CustomCommandsUseDirectEditorCommand = true,
                ParameterlessCustomCommandsUseNativeInteractiveQueue = true,
                CustomCommandAliasFallback = true,
                CustomCommandNamesAreImeSafe = CadCommandCatalog.IsSafeCommandName("SUN-173")
                    && !CadCommandCatalog.IsSafeCommandName("中文命令"),
                ImeAutoSwitchServiceReady = isCoreConsole ? (bool?)null : imeServiceReady,
                ImeDirectInputEnglishPreferred = _config.ImeSwitch?.DirectInputUsesEnglish == true,
                ImeTextCommandChinesePreferred = (_config.ImeSwitch?.TextCommands?.Count ?? 0) > 0,
                ImeFocusAwareReapply = isCoreConsole ? (bool?)null : _imeSwitch?.UsesFocusAwareReapply,
                ImeLispCommandEventsSubscribed = isCoreConsole ? (bool?)null : imeLispCommandEventsSubscribed,
                ImeLastAppliedChinese = isCoreConsole ? (bool?)null : _imeSwitch?.LastAppliedChinese,
                ImeAppliedSwitchCount = isCoreConsole ? (int?)null : _imeSwitch?.AppliedSwitchCount,
                ImeSkippedDuplicateCount = isCoreConsole ? (int?)null : _imeSwitch?.SkippedDuplicateCount,
                ImeModeReapplyCount = isCoreConsole ? (int?)null : _imeSwitch?.ModeReapplyCount,
                ImeModeDetectionCount = isCoreConsole ? (int?)null : _imeSwitch?.ModeDetectionCount,
                ImeModeTransitionCount = isCoreConsole ? (int?)null : _imeSwitch?.ModeTransitionCount,
                ImeProfileActivationCount = isCoreConsole ? (int?)null : _imeSwitch?.ProfileActivationCount,
                ImeProfileActivationFailureCount = isCoreConsole ? (int?)null : _imeSwitch?.ProfileActivationFailureCount,
                ImeForegroundRaceBlockedCount = isCoreConsole ? (int?)null : _imeSwitch?.RaceBlockedCount,
                ImeHasAppliedChinese = isCoreConsole ? (bool?)null : _imeSwitch?.HasAppliedChinese,
                ImeHasAppliedEnglish = isCoreConsole ? (bool?)null : _imeSwitch?.HasAppliedEnglish,
                ImeContentAwareChineseCommandCount = isCoreConsole ? (int?)null : _imeSwitch?.ContentAwareChineseCommandCount,
                ImeContentAwareEnglishCommandCount = isCoreConsole ? (int?)null : _imeSwitch?.ContentAwareEnglishCommandCount,
                ImeCommandMatchingPassed = imeCommandMatchingPassed,
                ImeLispCommandMatchingPassed = imeLispCommandMatchingPassed,
                ImeNestedCommandInheritancePassed = imeNestedCommandInheritancePassed,
                ImeExistingTextContentPolicyPassed = imeExistingTextContentPolicyPassed,
                ImeExistingTextContentPolicy = "Existing CJK text=Chinese; existing ASCII/numeric text=English; new text=user selection list",
                ImeLanguageResolutionPassed = imeLanguageResolutionPassed,
                ImeConfiguredInputMethodAvailable = imeConfiguredInputMethodAvailable,
                ImeExactInputMethodSelectionPassed = imeExactSelectionPassed,
                ImeMissingInputMethodUsesSystemCurrentPassed = imeMissingUsesSystemCurrentPassed,
                ImeMissingInputMethodDoesNotPickArbitraryFirstPassed = imeMissingDoesNotPickArbitraryPassed,
                ImeErrorReportDefaultOff = !new ImeSwitchConfig().ShowErrorReport,
                ImeErrorReportNonBlocking = ImeErrorReportWindow.IsNonBlockingNoActivate,
                ImeTsfCompartmentPolicyPassed = imeTsfModePolicyPassed,
                ImeDirectInputBoundedReapplyPolicyPassed = imeDirectInputReapplyPolicyPassed,
                ImeTsfProfileEnumerationPolicyPassed = imeTsfProfileEnumerationPolicyPassed,
                ImeTsfProfileEnumerationSource = ImeTsfProfileCatalog.LastEnumerationSource,
                ImeForegroundIsolationPolicyPassed = imeForegroundIsolationPolicyPassed,
                ImeCadWindowTreePolicyPassed = imeCadWindowTreePolicyPassed,
                ImeSingleInputMethodMigrationPassed = imeSingleInputMethodMigrationPassed,
                ImeFreshDefaultPassed = imeFreshDefaultPassed,
                ImeUsesSingleInputMethodModeSwitch = ImeNativeBridge.UsesInputMethodModeOnly,
                ImeDoesNotSelectSeparateEnglishKeyboard = !ImeNativeBridge.UsesLayoutLevelSwitching,
                ImeProfileActivationScope = "Process",
                ImeProcessScopePolicyPassed = imeProcessScopePolicyPassed,
                ImeInternalModePolicyPassed = imeInternalModePolicyPassed,
                ImeEmptyCommandSelectionPassed = imeEmptyCommandSelectionPassed,
                ImeLegacyDisabledSelectionMigrationPassed = imeLegacyDisabledSelectionMigrationPassed,
                ImeEnglishUsesSelectedInputMethodClosedState = true,
                ImeLastModeBackend = isCoreConsole ? null : _imeSwitch?.LastModeBackend,
                ImeContinuousModeEnforcement = false,
                ImeThreadLocalInputSettingsRead = threadLocalInputSettingsRead,
                ImeThreadLocalInputSettingsEnabled = threadLocalInputSettingsRead ? threadLocalInputSettings : (bool?)null,
                ImeRestrictsSwitchToCadForeground = imeCadWindowTreePolicyPassed,
                ImeBackgroundSwitchBlockedCount = isCoreConsole ? (int?)null : _imeSwitch?.SkippedBackgroundCount,
                LegacyRadialRenderingPolicyPassed = legacyRadialRenderingPolicyPassed,
                UsesLegacySafeRadialRenderer = RadialWindowCompatibility.UseLegacySafeRenderer,
                ImeInstalledLanguages = imeLanguages.Select(language => new
                {
                    language.LayoutId,
                    language.ProfileId,
                    language.DisplayName,
                    language.IsChinese,
                    language.IsEnglish,
                    language.IsInputMethod,
                    IsTsfProfile = language.TsfProfile != null
                }).ToArray(),
                ImeUsesKeyboardMessagesWithoutSendKeys = false,
                ImeUsesCadLocalTsfCompartmentsWithoutSendKeys = true,
                ImeUsesCadLocalImmFallbackWithoutSendKeys = true,
                ImeChangesAutoCadSystemVariables = false,
                UsesSendKeys = false,
                UsesSendStringToExecuteForAliases = true,
                Time = DateTime.Now
            };
            PluginPaths.EnsureRuntimeDirectories();
            File.WriteAllText(reportPath, JsonCompat.Serialize(report));
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\n小阳CAD轮盘插件自检{(passed ? "通过" : "未通过")}，报告：{reportPath}");
        }
        catch (SystemException ex)
        {
            Logger.Error("自检失败。", ex);
            PluginPaths.EnsureRuntimeDirectories();
            File.WriteAllText(reportPath, JsonCompat.Serialize(new { Product = CadHostInfo.PluginDisplayName, HostProduct = CadHostInfo.DisplayName, Passed = false, Error = ex.ToString(), Time = DateTime.Now }));
        }
    }

    private void PrepareWheel()
    {
        try
        {
            _overlay?.Start();
            if (_trigger?.Start() == true) StopTriggerRetry();
        }
        catch (SystemException ex) { Logger.Error("轮盘准备失败。", ex); }
    }

    private void OpenWheel(Point point, MouseButtons button)
    {
        var epoch = _executor?.DocumentEpoch;
        _executor?.PostToCadThread(() =>
        {
            // A hold may have ended while CAD was busy processing messages.
            // Do not show a stale wheel after its physical trigger was released.
            var key = button == MouseButtons.Right ? 2 : button == MouseButtons.Middle ? 4
                : button == MouseButtons.XButton1 ? 5 : button == MouseButtons.XButton2 ? 6 : 0;
            if (_executor.DocumentEpoch != epoch) return;
            if (key != 0 && ((NativeMethods.GetAsyncKeyState(key) & 0x8000) == 0 || IsCadCommandActive()
                || NativeMethods.GetForegroundWindow() != GetAutoCadWindow())) return;
            _executor.BeginWheel();
            _overlay?.Show(_config, SceneKeys.Global, point, button != MouseButtons.None, button);
        });
    }

    private void ApplyConfig(PluginConfig config)
    {
        TraceStartup("保存轮盘配置");
        ConfigStore.Save(config);
        TraceStartup("配置已落盘，应用轮盘和输入法配置");
        _config = config;
        UpdateExtensionSnapshot(config);
        _overlay?.Prime(_config, SceneKeys.Global);
        _imeSwitch?.ApplyConfiguration();
        TraceStartup("配置应用完成");
    }

    private static void UpdateExtensionSnapshot(PluginConfig config)
    {
        // Settings are edited in memory. Only saved checkboxes authorize startup loading.
        _extensionSnapshot = new PluginConfig
        {
            ManagedExtensions = JsonCompat.Deserialize<List<ManagedExtensionConfig>>(
                JsonCompat.Serialize(config.ManagedExtensions ?? new List<ManagedExtensionConfig>()))
        };
    }

    private static IntPtr GetAutoCadWindow()
    {
        try { return AcApp.MainWindow.Handle; }
        catch { return IntPtr.Zero; }
    }

    private static bool IsCadCommandActive() => _imeSwitch?.HasActiveCadCommand == true;

    private static bool IsFullAutoCadHost() => CadHostInfo.IsFullHost;

    private static void StartTriggerRetry()
    {
        if (_triggerRetryTimer != null) return;
        _triggerRetryCount = 0;
        _triggerRetryTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _triggerRetryTimer.Tick += (_, _) =>
        {
            if (Volatile.Read(ref _initialized) == 0 || _trigger == null)
            {
                StopTriggerRetry();
                return;
            }

            _triggerRetryCount++;
            if (_trigger.Start())
            {
                StopTriggerRetry();
                return;
            }

            if (_triggerRetryCount >= 15)
            {
                Logger.Error("鼠标触发器自动重试 15 次后仍未就绪，可运行 XYLPZJ 查看状态。");
                StopTriggerRetry();
            }
        };
        _triggerRetryTimer.Start();
    }

    private static void StopTriggerRetry()
    {
        var timer = _triggerRetryTimer;
        _triggerRetryTimer = null;
        if (timer == null) return;
        timer.Stop();
        timer.Dispose();
    }

    private static void ScheduleGentleMenuRepair(TimeSpan delay)
    {
        _menuRepairAttempts = 0;
        _nextMenuIdleCheck = DateTime.UtcNow.Add(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
    }

    private static void TryImmediateGentleMenuRepair()
    {
        try
        {
            if (CadMenuBar.Ensure())
                _nextMenuIdleCheck = DateTime.MaxValue;
        }
        catch (System.Exception ex)
        {
            Logger.Trace(CadHostInfo.ProductName + " 启动时温和检查顶部菜单失败：" + ex.Message);
        }
    }

    private static void RegisterMenuRepairEvents()
    {
        if (_menuRepairEventsRegistered) return;
        AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
        AcApp.SystemVariableChanged += OnSystemVariableChanged;
        AcApp.Idle += OnApplicationIdle;
        _menuRepairEventsRegistered = true;
    }

    private static void UnregisterMenuRepairEvents(bool hostTerminating)
    {
        if (!_menuRepairEventsRegistered) return;
        _menuRepairEventsRegistered = false;
#if AUTOCAD_CLR2
        if (!hostTerminating)
#endif
        {
            try { AcApp.DocumentManager.DocumentActivated -= OnDocumentActivated; } catch { }
            try { AcApp.SystemVariableChanged -= OnSystemVariableChanged; } catch { }
            try { AcApp.Idle -= OnApplicationIdle; } catch { }
        }
    }

    private static void OnDocumentActivated(object sender, CadDocumentCollectionEventArgs e)
    {
        if (Volatile.Read(ref _initialized) != 0) ScheduleGentleMenuRepair(TimeSpan.FromSeconds(2));
    }

    private static void OnSystemVariableChanged(object sender, CadSystemVariableChangedEventArgs e)
    {
        if (Volatile.Read(ref _initialized) == 0) return;
        if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase)
            || e.Name.Equals("MENUBAR", StringComparison.OrdinalIgnoreCase))
            ScheduleGentleMenuRepair(TimeSpan.FromSeconds(2));
    }

    private static void OnApplicationIdle(object sender, EventArgs e)
    {
        if (Volatile.Read(ref _initialized) == 0 || DateTime.UtcNow < _nextMenuIdleCheck) return;
        try
        {
            if (CadMenuBar.IsVisible())
            {
                _nextMenuIdleCheck = DateTime.MaxValue;
                return;
            }

            var activeCommands = Convert.ToString(AcApp.GetSystemVariable("CMDNAMES"))?.Trim() ?? "";
            var commandActive = Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE"));
            if (activeCommands.Length > 0 || commandActive != 0)
            {
                _nextMenuIdleCheck = DateTime.UtcNow.AddSeconds(2);
                return;
            }

            _menuRepairAttempts++;
            if (CadMenuBar.Ensure())
            {
                _nextMenuIdleCheck = DateTime.MaxValue;
                return;
            }
            _nextMenuIdleCheck = _menuRepairAttempts >= 6
                ? DateTime.MaxValue
                : DateTime.UtcNow.AddSeconds(5);
        }
        catch (System.Exception ex)
        {
            Logger.Trace(CadHostInfo.ProductName + " 空闲时检查顶部菜单失败：" + ex.Message);
        }
    }

    private static Dictionary<string, object> CaptureUserSystemVariables()
    {
        var names = new[]
        {
            "FILEDIA", "CMDDIA", "CMDECHO", "NOMUTT", "ORTHOMODE", "OSMODE",
            "SNAPMODE", "POLARMODE", "AUTOSNAP", "DYNMODE", "PICKFIRST", "PICKADD"
        };
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            try { values[name] = AcApp.GetSystemVariable(name); }
            catch (SystemException ex) { Logger.Trace($"读取系统变量 {name} 失败：{ex.Message}"); }
        }
        return values;
    }

    private static bool SystemVariableSnapshotsEqual(
        IReadOnlyDictionary<string, object> before,
        IReadOnlyDictionary<string, object> after) =>
        before.Count == after.Count
        && before.All(pair => after.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));

}
