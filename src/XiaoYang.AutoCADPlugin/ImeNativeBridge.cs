using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class ImeLanguageChoice
{
    public string LayoutId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string CultureName { get; init; } = "";
    public IntPtr Handle { get; init; }
    public InputLanguage? NativeLanguage { get; init; }
    public bool IsChinese { get; init; }
    public bool IsEnglish { get; init; }
    public bool IsInputMethod { get; init; }
    public string ProfileId { get; init; } = "";
    public ImeTsfProfileInfo? TsfProfile { get; init; }
    public string SelectionId => ProfileId.Length > 0 ? ProfileId : "KBD|" + LayoutId;
    public override string ToString() => TsfProfile != null
        ? $"{DisplayName} · Windows 输入法"
        : $"{DisplayName} · {LayoutId}";
}

internal sealed class ImeSwitchOutcome
{
    public bool Success { get; init; }
    public bool RaceBlocked { get; init; }
    public bool ModeDetected { get; init; }
    public bool ModeChanged { get; init; }
    public bool AlreadyCorrect { get; init; }
    public bool ProfileActivationAttempted { get; init; }
    public bool ProfileActivatedForProcess { get; init; }
    public bool? ActualChinese { get; init; }
    public IntPtr Target { get; init; }
    public string SelectionId { get; init; } = "";
    public string RequestedSelectionId { get; init; } = "";
    public bool UsedFallback { get; init; }
    public string FallbackReason { get; init; } = "";
    public string ModeBackend { get; init; } = "";
    public string ModeFailureStage { get; init; } = "";
}

internal sealed class ImeLanguageResolution
{
    public ImeLanguageChoice? Choice { get; init; }
    public string RequestedSelectionId { get; init; } = "";
    public bool UsedFallback { get; init; }
    public string FallbackReason { get; init; } = "";
}

internal static class ImeNativeBridge
{
    private const uint ImeCmodeNative = 0x0001;
    private const uint WmImeControl = 0x0283;
    private const uint ImcGetConversionMode = 0x0001;
    private const uint ImcSetConversionMode = 0x0002;
    private const uint ImcGetSentenceMode = 0x0003;
    private const uint ImcSetSentenceMode = 0x0004;
    private const uint ImcGetOpenStatus = 0x0005;
    private const uint ImcSetOpenStatus = 0x0006;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint SpiGetThreadLocalInputSettings = 0x104E;
    private static readonly object LanguageCacheSync = new();
    private static readonly object ProcessProfileSync = new();
    private static IReadOnlyList<ImeLanguageChoice> _cachedLanguages = Array.Empty<ImeLanguageChoice>();
    private static DateTime _languageCacheExpiresUtc = DateTime.MinValue;

    public static IReadOnlyList<ImeLanguageChoice> GetInstalledLanguages(bool refresh = false)
    {
        lock (LanguageCacheSync)
        {
            if (!refresh && _cachedLanguages.Count > 0 && DateTime.UtcNow < _languageCacheExpiresUtc)
                return _cachedLanguages;
        }
        var result = new List<ImeLanguageChoice>();
        InputLanguage[] nativeLanguages = Array.Empty<InputLanguage>();
        try
        {
            nativeLanguages = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().ToArray();
            foreach (InputLanguage language in nativeLanguages)
            {
                var culture = language.Culture ?? CultureInfo.InvariantCulture;
                var twoLetter = culture.TwoLetterISOLanguageName;
                var id = FormatLayoutId(language.Handle);
                result.Add(new ImeLanguageChoice
                {
                    LayoutId = id,
                    DisplayName = string.IsNullOrWhiteSpace(language.LayoutName) ? culture.DisplayName : language.LayoutName,
                    CultureName = culture.Name,
                    Handle = language.Handle,
                    NativeLanguage = language,
                    IsChinese = twoLetter.Equals("zh", StringComparison.OrdinalIgnoreCase),
                    IsEnglish = twoLetter.Equals("en", StringComparison.OrdinalIgnoreCase),
                    IsInputMethod = IsConcreteNativeImeHandle(language.Handle)
                });
            }
            foreach (var profile in ImeTsfProfileCatalog.GetEnabledProfiles())
            {
                var language = nativeLanguages.FirstOrDefault(item => LanguageId(item.Handle) == profile.LanguageId);
                if (language == null) continue;
                var culture = language.Culture ?? CultureInfo.InvariantCulture;
                var twoLetter = culture.TwoLetterISOLanguageName;
                result.Add(new ImeLanguageChoice
                {
                    LayoutId = FormatLayoutId(language.Handle),
                    DisplayName = profile.DisplayName,
                    CultureName = culture.Name,
                    Handle = language.Handle,
                    NativeLanguage = language,
                    IsChinese = twoLetter.Equals("zh", StringComparison.OrdinalIgnoreCase),
                    IsEnglish = twoLetter.Equals("en", StringComparison.OrdinalIgnoreCase),
                    IsInputMethod = true,
                    ProfileId = profile.SelectionId,
                    TsfProfile = profile
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取 Windows 输入语言失败。", ex);
        }
        var snapshot = result
            .GroupBy(item => item.SelectionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.IsEnglish)
            .ThenByDescending(item => item.IsChinese)
            .ThenByDescending(item => item.TsfProfile != null)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        lock (LanguageCacheSync)
        {
            _cachedLanguages = snapshot;
            _languageCacheExpiresUtc = DateTime.UtcNow.AddSeconds(10);
        }
        return snapshot;
    }

    public static string GetCurrentLayoutId()
    {
        try
        {
            var current = InputLanguage.CurrentInputLanguage;
            if (current != null && current.Handle != IntPtr.Zero) return FormatLayoutId(current.Handle);
            var handle = GetKeyboardLayout(GetCurrentThreadId());
            return handle == IntPtr.Zero ? "" : FormatLayoutId(handle);
        }
        catch { return ""; }
    }

    public static ImeLanguageChoice? ResolveLanguage(string layoutId, bool chinese)
        => ResolveLanguage(layoutId, "", chinese);

    public static ImeLanguageChoice? ResolveLanguage(string layoutId, string profileId, bool chinese)
        => ResolveLanguageWithFallback(layoutId, profileId).Choice;

    internal static ImeLanguageResolution ResolveLanguageWithFallback(string layoutId, string profileId)
    {
        // Both Chinese and English are conversion modes of one installed Chinese
        // input method. A plain Chinese/US keyboard layout is not an IME and must
        // never be selected as a fallback.
        var normalized = NormalizeLayoutId(layoutId, "00000804");
        var choices = GetInstalledLanguages();
        var inputMethods = choices.Where(item => item.IsChinese && item.IsInputMethod).ToArray();
        var requestedProfile = (profileId ?? "").Trim();
        var activeSelection = ImeTsfProfileCatalog.TryGetActiveSelectionId(out var active)
            ? active
            : "";
        return ResolveLanguageFromChoices(inputMethods, normalized, requestedProfile, activeSelection);
    }

    internal static ImeLanguageResolution ResolveLanguageFromChoices(IEnumerable<ImeLanguageChoice> choices,
        string layoutId, string profileId, string activeSelectionId)
    {
        var inputMethods = (choices ?? Array.Empty<ImeLanguageChoice>())
            .Where(item => item != null && item.IsChinese && item.IsInputMethod)
            .ToArray();
        var normalized = NormalizeLayoutId(layoutId, "00000804");
        var requestedProfile = (profileId ?? "").Trim();
        var exact = requestedProfile.Length == 0
            ? null
            : inputMethods.FirstOrDefault(item =>
                item.SelectionId.Equals(requestedProfile, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return new ImeLanguageResolution
            {
                Choice = exact,
                RequestedSelectionId = requestedProfile
            };

        var activeSelection = (activeSelectionId ?? "").Trim();
        var systemCurrent = activeSelection.Length > 0
            ? inputMethods.FirstOrDefault(item => item.SelectionId.Equals(activeSelection,
                StringComparison.OrdinalIgnoreCase))
            : null;

        // A missing explicit profile must never cause an arbitrary IME change.
        // Continue with Windows' current input method when it is known; otherwise
        // leave Windows untouched. The saved configuration is never rewritten.
        if (requestedProfile.Length > 0)
            return new ImeLanguageResolution
            {
                Choice = systemCurrent,
                RequestedSelectionId = requestedProfile,
                UsedFallback = true,
                FallbackReason = "已设置的输入法当前不可用，已沿用 Windows 当前输入法"
            };

        // Legacy settings did not store a concrete TSF profile. Prefer the
        // system-current method, then accept only an unambiguous same-layout IME.
        var sameLayout = inputMethods.Where(item => SameLayout(item.LayoutId, normalized)).ToArray();
        var fallback = systemCurrent ?? (sameLayout.Length == 1 ? sameLayout[0] : null);
        return new ImeLanguageResolution
        {
            Choice = fallback,
            RequestedSelectionId = normalized
        };
    }

    public static bool TrySwitch(string layoutId, bool chinese, IntPtr mainWindow, out string appliedLayoutId)
        => TrySwitch(layoutId, "", chinese, mainWindow, out appliedLayoutId);

    public static bool TrySwitch(string layoutId, string profileId, bool chinese, IntPtr mainWindow, out string appliedLayoutId)
    {
        var outcome = TrySwitch(layoutId, profileId, chinese, mainWindow);
        appliedLayoutId = outcome.SelectionId;
        return outcome.Success;
    }

    internal static ImeSwitchOutcome TrySwitch(string layoutId, string profileId, bool chinese, IntPtr mainWindow)
    {
        var resolution = ResolveLanguageWithFallback(layoutId, profileId);
        var choice = resolution.Choice;
        if (choice == null)
            return new ImeSwitchOutcome
            {
                Success = resolution.UsedFallback,
                AlreadyCorrect = resolution.UsedFallback,
                RequestedSelectionId = resolution.RequestedSelectionId,
                UsedFallback = resolution.UsedFallback,
                FallbackReason = resolution.FallbackReason
            };
        try
        {
            var snapshot = CaptureInputTarget(mainWindow);
            if (snapshot == null) return new ImeSwitchOutcome { RaceBlocked = true };

            var activationAttempted = false;
            var activatedForProcess = false;
            var activeConfirmed = ImeTsfProfileCatalog.TryGetActiveSelectionId(out var activeSelection)
                                  && activeSelection.Equals(choice.SelectionId, StringComparison.OrdinalIgnoreCase);
            lock (ProcessProfileSync)
            {
                if (!activeConfirmed)
                {
                    activationAttempted = true;
                    if (!IsSnapshotCurrent(snapshot, mainWindow))
                        return new ImeSwitchOutcome { RaceBlocked = true, Target = snapshot.Target };
                    activatedForProcess = choice.TsfProfile != null
                        ? ImeTsfProfileCatalog.TryActivateForProcess(choice.TsfProfile)
                        : ImeTsfProfileCatalog.TryActivateKeyboardLayoutForProcess(
                            LanguageId(choice.Handle), choice.Handle);
                    // S_OK is necessary but not sufficient when DONTCARE is used:
                    // some TIPs can defer activation until their language becomes
                    // current. Never apply IMM mode to an unverified old profile.
                    activeConfirmed = ImeTsfProfileCatalog.TryGetActiveSelectionId(out activeSelection)
                                      && activeSelection.Equals(choice.SelectionId, StringComparison.OrdinalIgnoreCase);
                    if (!activatedForProcess || !activeConfirmed)
                    {
                        return new ImeSwitchOutcome
                        {
                            Target = snapshot.Target,
                            SelectionId = choice.SelectionId,
                            RequestedSelectionId = resolution.RequestedSelectionId,
                            UsedFallback = resolution.UsedFallback,
                            FallbackReason = resolution.FallbackReason,
                            ProfileActivationAttempted = true,
                            ProfileActivatedForProcess = false
                        };
                    }
                }
            }

            // Profile activation is explicitly process scoped. Mode changes are
            // even narrower: they apply only to the verified CAD input context.
            // No CurrentInputLanguage/ActivateKeyboardLayout, session activation,
            // SendKeys, asynchronous window message, or AutoCAD variable is used.
            if (!IsSnapshotCurrent(snapshot, mainWindow))
                return new ImeSwitchOutcome
                {
                    RaceBlocked = true,
                    Target = snapshot.Target,
                    SelectionId = choice.SelectionId,
                    ProfileActivationAttempted = activationAttempted,
                    ProfileActivatedForProcess = activatedForProcess
                };
            var mode = ApplyConversionMode(snapshot, mainWindow, chinese, choice.TsfProfile != null);
            return new ImeSwitchOutcome
            {
                Success = mode.Success,
                RaceBlocked = mode.RaceBlocked,
                ModeDetected = mode.Detected,
                ModeChanged = mode.Changed,
                AlreadyCorrect = mode.AlreadyCorrect,
                ProfileActivationAttempted = activationAttempted,
                ProfileActivatedForProcess = activatedForProcess,
                ActualChinese = mode.ActualChinese,
                Target = snapshot.Target,
                SelectionId = choice.SelectionId,
                RequestedSelectionId = resolution.RequestedSelectionId,
                UsedFallback = resolution.UsedFallback,
                FallbackReason = resolution.FallbackReason,
                ModeBackend = mode.Backend,
                ModeFailureStage = mode.FailureStage
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"切换 CAD 输入法内部{(chinese ? "中文" : "英文")}模式失败：{choice.DisplayName}", ex);
            return new ImeSwitchOutcome
            {
                SelectionId = choice.SelectionId,
                RequestedSelectionId = resolution.RequestedSelectionId,
                UsedFallback = resolution.UsedFallback,
                FallbackReason = resolution.FallbackReason
            };
        }
    }

    internal static bool TryReapplyConversionMode(bool chinese, IntPtr mainWindow)
    {
        var snapshot = CaptureInputTarget(mainWindow);
        return snapshot != null && ApplyConversionMode(snapshot, mainWindow, chinese, preferTsf: true).Success;
    }

    internal static bool TryDetectConversionMode(IntPtr mainWindow, out bool chinese, out IntPtr target)
    {
        chinese = false;
        target = IntPtr.Zero;
        var snapshot = CaptureInputTarget(mainWindow);
        if (snapshot == null || !IsSnapshotCurrent(snapshot, mainWindow)) return false;
        if (ImeTsfModeBridge.TryRead(snapshot.TargetThread, out var tsfState))
        {
            chinese = tsfState.IsChinese;
            target = snapshot.Target;
            return IsSnapshotCurrent(snapshot, mainWindow);
        }
        if (!TryReadConversionMode(snapshot.Target, out chinese, out _, out _, out _, out _)) return false;
        if (!IsSnapshotCurrent(snapshot, mainWindow)) return false;
        target = snapshot.Target;
        return true;
    }

    public static string NormalizeLayoutId(string value, string fallback)
    {
        var text = new string((value ?? "").Trim().Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (text.Length == 0) return fallback;
        if (text.Length > 8) text = text.Substring(text.Length - 8);
        return text.PadLeft(8, '0');
    }

    internal static bool SameLayout(string left, string right)
    {
        var a = NormalizeLayoutId(left, "");
        var b = NormalizeLayoutId(right, "");
        if (a.Length == 0 || b.Length == 0) return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        return a.Substring(Math.Max(0, a.Length - 4))
            .Equals(b.Substring(Math.Max(0, b.Length - 4)), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatLayoutId(IntPtr handle) => unchecked((uint)handle.ToInt64()).ToString("X8");

    private static ushort LanguageId(IntPtr handle) => unchecked((ushort)((uint)handle.ToInt64() & 0xFFFF));

    internal static bool UsesInputMethodModeOnly => true;
    internal static bool UsesLayoutLevelSwitching => false;
    internal static bool ReportsConfiguredInputMethodFallback => true;
    internal static bool UsesTargetImeWindowCompatibilityControl => true;
    internal static bool UsesTsfCompartmentsForModernInputMethods => true;

    internal static bool IsRequestedInternalMode(bool open, bool hasConversion, uint conversion, bool chinese)
    {
        if (!chinese) return !open;
        if (!open) return false;
        return !hasConversion || (conversion & ImeCmodeNative) != 0;
    }

    internal static uint BuildRequestedConversionMode(uint conversion, bool chinese) =>
        chinese ? conversion | ImeCmodeNative : conversion & ~ImeCmodeNative;

    // ImmIsIME alone reports true for some plain US/base keyboard layouts.
    // A classic IMM input method must also have a real IME module file.
    private static bool IsConcreteNativeImeHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !ImmIsIME(handle)) return false;
        var fileName = new StringBuilder(260);
        return IsConcreteNativeImeIdentity(true,
            ImmGetIMEFileName(handle, fileName, (uint)fileName.Capacity) > 0 && fileName.Length > 0);
    }

    internal static bool IsConcreteNativeImeIdentity(bool immIsIme, bool hasImeModule) =>
        immIsIme && hasImeModule;

    internal static IntPtr ResolveInputTarget(IntPtr mainWindow) => CaptureInputTarget(mainWindow)?.Target ?? IntPtr.Zero;

    internal static bool IsCurrentProcessForeground(IntPtr mainWindow) => CaptureInputTarget(mainWindow) != null;

    internal static bool AllowsForegroundProcess(uint foregroundProcessId, uint currentProcessId) =>
        foregroundProcessId != 0 && foregroundProcessId == currentProcessId;

    internal static bool TryGetThreadLocalInputSettings(out bool enabled)
    {
        enabled = false;
        try { return SystemParametersInfo(SpiGetThreadLocalInputSettings, 0, ref enabled, 0); }
        catch { return false; }
    }

    private static InputTargetSnapshot? CaptureInputTarget(IntPtr mainWindow)
    {
        if (mainWindow == IntPtr.Zero || !BelongsToCurrentProcess(mainWindow)) return null;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsCadWindow(mainWindow, foreground)) return null;
        var foregroundThread = GetWindowThreadProcessId(foreground, out var foregroundProcess);
        if (!AllowsForegroundProcess(foregroundProcess, (uint)Process.GetCurrentProcess().Id)) return null;
        var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
        var target = GetGUIThreadInfo(foregroundThread, ref info) ? info.Focus : IntPtr.Zero;
        if (target == IntPtr.Zero) target = foreground;
        if (!IsCadWindow(mainWindow, target) || !BelongsToCurrentProcess(target)) return null;
        var targetThread = GetWindowThreadProcessId(target, out var targetProcess);
        if (!AllowsForegroundProcess(targetProcess, (uint)Process.GetCurrentProcess().Id)) return null;
        return new InputTargetSnapshot(mainWindow, foreground, foregroundThread, target, targetThread);
    }

    private static bool IsSnapshotCurrent(InputTargetSnapshot snapshot, IntPtr mainWindow)
    {
        if (snapshot == null || mainWindow != snapshot.MainWindow) return false;
        if (GetForegroundWindow() != snapshot.Foreground || !IsCadWindow(mainWindow, snapshot.Foreground)) return false;
        var foregroundThread = GetWindowThreadProcessId(snapshot.Foreground, out var foregroundProcess);
        if (foregroundThread != snapshot.ForegroundThread
            || !AllowsForegroundProcess(foregroundProcess, (uint)Process.GetCurrentProcess().Id)) return false;
        var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
        var target = GetGUIThreadInfo(foregroundThread, ref info) && info.Focus != IntPtr.Zero
            ? info.Focus
            : snapshot.Foreground;
        if (target != snapshot.Target || !IsCadWindow(mainWindow, target)) return false;
        var targetThread = GetWindowThreadProcessId(target, out var targetProcess);
        return targetThread == snapshot.TargetThread
               && AllowsForegroundProcess(targetProcess, (uint)Process.GetCurrentProcess().Id);
    }

    private static bool IsCadWindow(IntPtr mainWindow, IntPtr window) =>
        window != IntPtr.Zero && (window == mainWindow || IsChild(mainWindow, window));

    internal static bool IsCadWindowPolicy(bool sameWindow, bool isChild, bool sameProcess) =>
        sameProcess && (sameWindow || isChild);

    private sealed class InputTargetSnapshot
    {
        public InputTargetSnapshot(IntPtr mainWindow, IntPtr foreground, uint foregroundThread,
            IntPtr target, uint targetThread)
        {
            MainWindow = mainWindow;
            Foreground = foreground;
            ForegroundThread = foregroundThread;
            Target = target;
            TargetThread = targetThread;
        }

        public IntPtr MainWindow { get; }
        public IntPtr Foreground { get; }
        public uint ForegroundThread { get; }
        public IntPtr Target { get; }
        public uint TargetThread { get; }
    }

    private static bool BelongsToCurrentProcess(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var processId);
        return processId == (uint)Process.GetCurrentProcess().Id;
    }

    private sealed class ImeModeApplyResult
    {
        public bool Success { get; init; }
        public bool RaceBlocked { get; init; }
        public bool Detected { get; init; }
        public bool Changed { get; init; }
        public bool AlreadyCorrect { get; init; }
        public bool? ActualChinese { get; init; }
        public string Backend { get; init; } = "";
        public string FailureStage { get; init; } = "";
    }

    private static ImeModeApplyResult ApplyConversionMode(InputTargetSnapshot snapshot, IntPtr mainWindow,
        bool chinese, bool preferTsf)
    {
        if (!IsSnapshotCurrent(snapshot, mainWindow)) return new ImeModeApplyResult { RaceBlocked = true };
        if (preferTsf)
        {
            var tsf = ImeTsfModeBridge.Apply(chinese, snapshot.TargetThread);
            if (tsf.Available)
                return new ImeModeApplyResult
                {
                    Success = tsf.Success,
                    Detected = tsf.ActualChinese.HasValue,
                    Changed = tsf.Changed,
                    AlreadyCorrect = tsf.AlreadyCorrect,
                    ActualChinese = tsf.ActualChinese,
                    Backend = "TSF",
                    FailureStage = tsf.FailureStage
                };
        }
        if (!TryReadConversionMode(snapshot.Target, out var beforeChinese, out var open, out var conversion,
                out var sentence, out var hasConversion)) return new ImeModeApplyResult();
        if (IsRequestedInternalMode(open, hasConversion, conversion, chinese))
            return new ImeModeApplyResult
            {
                Success = true,
                Detected = true,
                AlreadyCorrect = true,
                ActualChinese = beforeChinese,
                Backend = "IMM"
            };

        // First use the target window's direct IMM context. This is the fastest
        // path for classic IMEs and Microsoft Pinyin.
        var context = ImmGetContext(snapshot.Target);
        if (context != IntPtr.Zero)
        {
            try
            {
                if (!IsSnapshotCurrent(snapshot, mainWindow)) return new ImeModeApplyResult { RaceBlocked = true };
                // Keep one selected Chinese input-method profile for both states.
                // Chinese is its open/native state; English is the closed state
                // of that same IME context, never a US-keyboard substitution.
                if (chinese && !open)
                {
                    ImmSetOpenStatus(context, true);
                    open = ImmGetOpenStatus(context);
                    hasConversion = ImmGetConversionStatus(context, out conversion, out sentence);
                }
                if (chinese && open)
                {
                    var requestedConversion = BuildRequestedConversionMode(conversion, chinese: true);
                    if (hasConversion && requestedConversion != conversion)
                        ImmSetConversionStatus(context, requestedConversion, sentence);
                }
                else if (!chinese && open)
                {
                    ImmSetOpenStatus(context, false);
                }
            }
            finally { ImmReleaseContext(snapshot.Target, context); }
        }

        if (!IsSnapshotCurrent(snapshot, mainWindow)) return new ImeModeApplyResult { RaceBlocked = true };
        if (TryReadConversionMode(snapshot.Target, out var directChinese, out _, out _, out _, out _)
            && directChinese == chinese)
            return new ImeModeApplyResult
            {
                Success = true,
                Detected = true,
                Changed = beforeChinese != directChinese,
                ActualChinese = directChinese,
                Backend = "IMM"
            };

        // WeChat, Sogou and some other TSF input processors expose a compatibility
        // IME window while their focused HWND has a stale or proxy IMM context.
        // WM_IME_CONTROL targets that exact CAD input context and avoids SendKeys,
        // global hotkeys, system-variable writes and cross-application switching.
        TryApplyImeWindowMode(snapshot.Target, chinese);

        if (!IsSnapshotCurrent(snapshot, mainWindow)) return new ImeModeApplyResult { RaceBlocked = true };
        if (!TryReadConversionMode(snapshot.Target, out var afterChinese, out _, out _, out _, out _))
            return new ImeModeApplyResult { Detected = true, ActualChinese = beforeChinese };
        return new ImeModeApplyResult
        {
            Success = afterChinese == chinese,
            Detected = true,
            Changed = beforeChinese != afterChinese,
            ActualChinese = afterChinese,
            Backend = "IMM"
        };
    }

    private static bool TryReadConversionMode(IntPtr window, out bool chinese, out bool open,
        out uint conversion, out uint sentence, out bool hasConversion)
    {
        chinese = false;
        open = false;
        conversion = 0;
        sentence = 0;
        hasConversion = false;
        if (window == IntPtr.Zero) return false;
        if (TryReadImeWindowMode(window, out chinese, out open, out conversion, out sentence,
                out hasConversion)) return true;
        var context = ImmGetContext(window);
        if (context == IntPtr.Zero) return false;
        try
        {
            open = ImmGetOpenStatus(context);
            hasConversion = ImmGetConversionStatus(context, out conversion, out sentence);
            chinese = open && (!hasConversion || (conversion & ImeCmodeNative) != 0);
            return true;
        }
        finally { ImmReleaseContext(window, context); }
    }

    private static bool TryReadImeWindowMode(IntPtr target, out bool chinese, out bool open,
        out uint conversion, out uint sentence, out bool hasConversion)
    {
        chinese = false;
        open = false;
        conversion = 0;
        sentence = 0;
        hasConversion = false;
        var imeWindow = ImmGetDefaultIMEWnd(target);
        if (imeWindow == IntPtr.Zero
            || !TrySendImeControl(imeWindow, ImcGetOpenStatus, IntPtr.Zero, out var openResult)) return false;
        open = openResult != IntPtr.Zero;
        if (TrySendImeControl(imeWindow, ImcGetConversionMode, IntPtr.Zero, out var conversionResult))
        {
            conversion = unchecked((uint)conversionResult.ToInt64());
            hasConversion = true;
        }
        if (TrySendImeControl(imeWindow, ImcGetSentenceMode, IntPtr.Zero, out var sentenceResult))
            sentence = unchecked((uint)sentenceResult.ToInt64());
        chinese = open && (!hasConversion || (conversion & ImeCmodeNative) != 0);
        return true;
    }

    private static bool TryApplyImeWindowMode(IntPtr target, bool chinese)
    {
        var imeWindow = ImmGetDefaultIMEWnd(target);
        if (imeWindow == IntPtr.Zero) return false;
        if (!TrySendImeControl(imeWindow, ImcSetOpenStatus, chinese ? new IntPtr(1) : IntPtr.Zero, out _))
            return false;
        if (!chinese) return true;
        if (TrySendImeControl(imeWindow, ImcGetConversionMode, IntPtr.Zero, out var conversionResult))
        {
            var requested = BuildRequestedConversionMode(unchecked((uint)conversionResult.ToInt64()), true);
            TrySendImeControl(imeWindow, ImcSetConversionMode, new IntPtr(unchecked((long)requested)), out _);
        }
        if (TrySendImeControl(imeWindow, ImcGetSentenceMode, IntPtr.Zero, out var sentenceResult))
            TrySendImeControl(imeWindow, ImcSetSentenceMode, sentenceResult, out _);
        return true;
    }

    private static bool TrySendImeControl(IntPtr imeWindow, uint command, IntPtr value, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (imeWindow == IntPtr.Zero) return false;
        try
        {
            return SendMessageTimeout(imeWindow, WmImeControl, new IntPtr(command), value,
                       SmtoAbortIfHung, 200, out result) != IntPtr.Zero;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public System.Drawing.Rectangle CaretRect;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] ref bool value, uint update);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmIsIME(IntPtr keyboardLayout);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ImmGetIMEFileName(IntPtr keyboardLayout, StringBuilder fileName, uint bufferLength);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr context);

    [DllImport("imm32.dll")]
    private static extern bool ImmGetConversionStatus(IntPtr context, out uint conversion, out uint sentence);

    [DllImport("imm32.dll")]
    private static extern bool ImmGetOpenStatus(IntPtr context);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetOpenStatus(IntPtr context, bool open);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetConversionStatus(IntPtr context, uint conversion, uint sentence);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeoutMilliseconds, out IntPtr result);
}
