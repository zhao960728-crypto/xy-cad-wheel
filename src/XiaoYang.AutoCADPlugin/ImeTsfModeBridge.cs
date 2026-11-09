using System.Runtime.InteropServices;

namespace XiaoYang.AutoCADPlugin;

internal sealed class ImeTsfModeState
{
    public bool IsChinese { get; init; }
    public bool IsOpen { get; init; }
    public bool HasConversion { get; init; }
    public uint Conversion { get; init; }
}

internal sealed class ImeTsfModeApplyResult
{
    public bool Available { get; init; }
    public bool Success { get; init; }
    public bool AlreadyCorrect { get; init; }
    public bool Changed { get; init; }
    public bool? ActualChinese { get; init; }
    public string FailureStage { get; init; } = "";
}

/// <summary>
/// Reads and writes the TSF keyboard compartments owned by the focused CAD UI
/// thread. Modern TIPs such as WeChat, Sogou and Microsoft Pinyin can ignore a
/// proxy IMM context, while these compartments remain their authoritative mode.
/// No keyboard message, global hotkey, CAD variable, or cross-process state is
/// changed here.
/// </summary>
internal static class ImeTsfModeBridge
{
    private const uint ImeCmodeNative = 0x0001;
    private static readonly Guid KeyboardOpenClose = new("58273AAD-01BB-4164-95C6-755BA0B5162D");
    private static readonly Guid KeyboardConversion = new("CCF05DD7-4A87-11D7-A6E2-00065B84435C");
    private static readonly Guid PluginClient = new("6B52E597-268B-43F0-A378-6BF43A644628");

    internal static bool UsesCurrentThreadCompartments => true;
    internal static bool IsTargetThread(uint currentThreadId, uint targetThreadId) =>
        currentThreadId != 0 && currentThreadId == targetThreadId;

    internal static ImeTsfModeApplyResult Apply(bool chinese, uint targetThreadId)
    {
        var currentThreadId = GetCurrentThreadId();
        if (!IsTargetThread(currentThreadId, targetThreadId))
            return new ImeTsfModeApplyResult
            {
                Available = true,
                FailureStage = "CAD 输入线程不一致"
            };

        ITfThreadMgr? threadManager = null;
        try
        {
            if (!TryGetManager(out threadManager, out var compartments))
                return new ImeTsfModeApplyResult { FailureStage = "当前线程没有可用的 TSF 管理器" };
            if (!TryGetClientId(threadManager, out var clientId))
                return new ImeTsfModeApplyResult
                {
                    Available = true,
                    FailureStage = "无法取得当前 TSF 线程的客户端标识"
                };

            var beforeReadable = TryRead(compartments, out var before);
            if (beforeReadable && before.IsChinese == chinese)
                return new ImeTsfModeApplyResult
                {
                    Available = true,
                    Success = true,
                    AlreadyCorrect = true,
                    ActualChinese = before.IsChinese
                };

            bool wrote;
            if (chinese)
            {
                wrote = TryWrite(compartments, clientId, KeyboardOpenClose, 1);
                var conversion = beforeReadable && before.HasConversion ? before.Conversion : 0;
                wrote &= TryWrite(compartments, clientId, KeyboardConversion,
                    unchecked((int)(conversion | ImeCmodeNative)));
            }
            else
            {
                // Clear native conversion before closing the same selected TIP.
                // Some third-party TIPs preserve conversion state while closed.
                var conversionWritten = !beforeReadable || !before.HasConversion
                                        || TryWrite(compartments, clientId, KeyboardConversion,
                                            unchecked((int)(before.Conversion & ~ImeCmodeNative)));
                wrote = conversionWritten && TryWrite(compartments, clientId, KeyboardOpenClose, 0);
            }

            if (!wrote || !TryRead(compartments, out var after))
                return new ImeTsfModeApplyResult
                {
                    Available = true,
                    ActualChinese = beforeReadable ? before.IsChinese : null,
                    FailureStage = !wrote ? "写入 TSF 输入模式失败" : "回读 TSF 输入模式失败"
                };

            return new ImeTsfModeApplyResult
            {
                Available = true,
                Success = after.IsChinese == chinese,
                Changed = !beforeReadable || before.IsChinese != after.IsChinese,
                ActualChinese = after.IsChinese,
                FailureStage = after.IsChinese == chinese ? "" : "TSF 输入模式回读不一致"
            };
        }
        catch (Exception ex)
        {
            Logger.Trace("TSF 输入模式切换失败：" + ex.Message);
            // Once this thread has a TSF manager, an interface failure must not
            // be hidden by accepting a stale IMM result from a modern TIP.
            return new ImeTsfModeApplyResult
            {
                Available = threadManager != null,
                FailureStage = "TSF 输入模式接口异常"
            };
        }
        finally
        {
            if (threadManager != null && Marshal.IsComObject(threadManager))
                Marshal.ReleaseComObject(threadManager);
        }
    }

    internal static bool TryRead(uint targetThreadId, out ImeTsfModeState state)
    {
        state = new ImeTsfModeState();
        if (!IsTargetThread(GetCurrentThreadId(), targetThreadId)) return false;
        ITfThreadMgr? threadManager = null;
        try
        {
            if (!TryGetManager(out threadManager, out var compartments)) return false;
            return TryRead(compartments, out state);
        }
        catch { return false; }
        finally
        {
            if (threadManager != null && Marshal.IsComObject(threadManager))
                Marshal.ReleaseComObject(threadManager);
        }
    }

    private static bool TryGetManager(out ITfThreadMgr? threadManager,
        out ITfCompartmentMgr compartments)
    {
        threadManager = null;
        compartments = null!;
        if (TF_GetThreadMgr(out threadManager) < 0 || threadManager == null) return false;
        if (threadManager is not ITfCompartmentMgr compartmentManager) return false;
        compartments = compartmentManager;
        return true;
    }

    private static bool TryGetClientId(ITfThreadMgr threadManager, out uint clientId)
    {
        clientId = 0;
        if (threadManager is not ITfClientId clientIdManager) return false;
        var clientGuid = PluginClient;
        if (clientIdManager.GetClientId(ref clientGuid, out clientId) < 0 || clientId == 0) return false;
        return true;
    }

    private static bool TryRead(ITfCompartmentMgr manager, out ImeTsfModeState state)
    {
        state = new ImeTsfModeState();
        if (!TryRead(manager, KeyboardOpenClose, out var openValue)) return false;
        var hasConversion = TryRead(manager, KeyboardConversion, out var conversionValue);
        var open = openValue != 0;
        var conversion = hasConversion ? unchecked((uint)conversionValue) : 0;
        state = new ImeTsfModeState
        {
            IsOpen = open,
            HasConversion = hasConversion,
            Conversion = conversion,
            IsChinese = open && (!hasConversion || (conversion & ImeCmodeNative) != 0)
        };
        return true;
    }

    private static bool TryRead(ITfCompartmentMgr manager, Guid compartmentId, out int value)
    {
        value = 0;
        ITfCompartment? compartment = null;
        try
        {
            if (manager.GetCompartment(ref compartmentId, out compartment) < 0 || compartment == null) return false;
            if (compartment.GetValue(out var raw) < 0 || raw == null) return false;
            value = Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch { return false; }
        finally
        {
            if (compartment != null && Marshal.IsComObject(compartment)) Marshal.ReleaseComObject(compartment);
        }
    }

    private static bool TryWrite(ITfCompartmentMgr manager, uint clientId, Guid compartmentId, int value)
    {
        ITfCompartment? compartment = null;
        try
        {
            if (manager.GetCompartment(ref compartmentId, out compartment) < 0 || compartment == null) return false;
            object boxed = value;
            return compartment.SetValue(clientId, ref boxed) >= 0;
        }
        catch { return false; }
        finally
        {
            if (compartment != null && Marshal.IsComObject(compartment)) Marshal.ReleaseComObject(compartment);
        }
    }

    [DllImport("msctf.dll")]
    private static extern int TF_GetThreadMgr(out ITfThreadMgr threadManager);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [ComImport]
    [Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfThreadMgr
    {
    }

    [ComImport]
    [Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfCompartmentMgr
    {
        [PreserveSig]
        int GetCompartment(ref Guid compartmentId, out ITfCompartment compartment);

        [PreserveSig]
        int ClearCompartment(uint clientId, ref Guid compartmentId);

        [PreserveSig]
        int EnumCompartments(out IntPtr enumerator);
    }

    [ComImport]
    [Guid("BB08F7A9-607A-4384-8623-056892B64371")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfCompartment
    {
        [PreserveSig]
        int SetValue(uint clientId, [In, MarshalAs(UnmanagedType.Struct)] ref object value);

        [PreserveSig]
        int GetValue([MarshalAs(UnmanagedType.Struct)] out object value);
    }

    [ComImport]
    [Guid("D60A7B49-1B9F-4BE2-B702-47E9DC05DEC3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfClientId
    {
        [PreserveSig]
        int GetClientId(ref Guid classId, out uint clientId);
    }
}
