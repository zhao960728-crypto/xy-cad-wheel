using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

namespace XiaoYang.AutoCADPlugin;

internal sealed class ImeTsfProfileInfo
{
    public Guid TipClsid { get; init; }
    public Guid ProfileGuid { get; init; }
    public ushort LanguageId { get; init; }
    public string DisplayName { get; init; } = "";
    public string SelectionId => $"TSF|{TipClsid:D}|{LanguageId:X4}|{ProfileGuid:D}";
}

internal static class ImeTsfProfileCatalog
{
    private const string UserTipPath = @"Software\Microsoft\CTF\TIP";
    private const string MachineTipPath = @"SOFTWARE\Microsoft\CTF\TIP";
    internal const uint ProfileTypeInputProcessor = 0x0001;
    internal const uint ProfileTypeKeyboardLayout = 0x0002;
    internal const uint ActivateForProcess = 0x10000000;
    internal const uint ActivateForSession = 0x20000000;
    internal const uint ActivateForSystemAll = 0x40000000;
    internal const uint DontCareCurrentInputLanguage = 0x00000004;
    internal const uint ProcessActivationFlags = ActivateForProcess | DontCareCurrentInputLanguage;
    internal const uint ProfileFlagEnabled = 0x00000002;
    private static readonly Guid TipKeyboardCategory = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
    public static string LastEnumerationSource { get; private set; } = "";

    public static IReadOnlyList<ImeTsfProfileInfo> GetEnabledProfiles()
    {
        var result = new List<ImeTsfProfileInfo>();
        if (TryEnumerateEnabledProfiles(result))
            LastEnumerationSource = "WindowsTsfApi";
        else
        {
            LastEnumerationSource = "RegistryFallback";
            ReadEnabledProfilesFromRegistry(result);
        }
        return result
            .GroupBy(item => item.SelectionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool TryEnumerateEnabledProfiles(ICollection<ImeTsfProfileInfo> result)
    {
        ITfInputProcessorProfiles? profiles = null;
        IEnumTfInputProcessorProfiles? enumerator = null;
        try
        {
            var hr = TF_CreateInputProcessorProfiles(out profiles);
            if (hr < 0 || profiles == null || profiles is not ITfInputProcessorProfileMgr manager)
                return false;
            if (manager.EnumProfiles(0, out enumerator) < 0 || enumerator == null)
                return false;

            while (enumerator.Next(1, out var profile, out var fetched) == 0 && fetched == 1)
            {
                if (!IsSelectableInputMethodProfile(profile.ProfileType, profile.Flags,
                        profile.TipClsid, profile.ProfileGuid)) continue;
                var tipName = profile.TipClsid.ToString("B");
                var languageName = "0x" + profile.LanguageId.ToString("X8");
                var profileName = profile.ProfileGuid.ToString("B");
                result.Add(new ImeTsfProfileInfo
                {
                    TipClsid = profile.TipClsid,
                    ProfileGuid = profile.ProfileGuid,
                    LanguageId = profile.LanguageId,
                    DisplayName = ReadDisplayName(tipName, languageName, profileName)
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("通过 Windows TSF 接口读取已启用输入法失败，已回退到兼容注册表读取。", ex);
            return false;
        }
        finally
        {
            if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
            if (profiles != null && Marshal.IsComObject(profiles)) Marshal.ReleaseComObject(profiles);
        }
    }

    private static void ReadEnabledProfilesFromRegistry(ICollection<ImeTsfProfileInfo> result)
    {
        try
        {
            using var userTipRoot = Registry.CurrentUser.OpenSubKey(UserTipPath);
            if (userTipRoot == null) return;
            foreach (var tipName in userTipRoot.GetSubKeyNames())
            {
                if (!Guid.TryParse(tipName, out var tipClsid)) continue;
                using var tipKey = userTipRoot.OpenSubKey(tipName + @"\LanguageProfile");
                if (tipKey == null) continue;
                foreach (var languageName in tipKey.GetSubKeyNames())
                {
                    if (!TryParseLanguageId(languageName, out var languageId)) continue;
                    using var languageKey = tipKey.OpenSubKey(languageName);
                    if (languageKey == null) continue;
                    foreach (var profileName in languageKey.GetSubKeyNames())
                    {
                        if (!Guid.TryParse(profileName, out var profileGuid)) continue;
                        using var profileKey = languageKey.OpenSubKey(profileName);
                        if (!IsEnabled(profileKey)) continue;
                        result.Add(new ImeTsfProfileInfo
                        {
                            TipClsid = tipClsid,
                            ProfileGuid = profileGuid,
                            LanguageId = languageId,
                            DisplayName = ReadDisplayName(tipName, languageName, profileName)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取 Windows TSF 输入法兼容注册表配置失败。", ex);
        }
    }

    public static bool TryActivateForProcess(ImeTsfProfileInfo? profile)
    {
        if (profile == null) return false;
        ITfInputProcessorProfiles? profiles = null;
        try
        {
            var hr = TF_CreateInputProcessorProfiles(out profiles);
            if (hr < 0 || profiles == null) return false;
            if (profiles is not ITfInputProcessorProfileMgr manager) return false;
            var tip = profile.TipClsid;
            var profileGuid = profile.ProfileGuid;
            return IsSuccessfulActivationResult(manager.ActivateProfile(ProfileTypeInputProcessor, profile.LanguageId,
                ref tip, ref profileGuid, IntPtr.Zero, ProcessActivationFlags));
        }
        catch (Exception ex)
        {
            Logger.Error("按 CAD 进程范围激活 Windows TSF 输入法失败：" + profile.DisplayName, ex);
            return false;
        }
        finally
        {
            if (profiles != null && Marshal.IsComObject(profiles)) Marshal.ReleaseComObject(profiles);
        }
    }

    public static bool TryActivateKeyboardLayoutForProcess(ushort languageId, IntPtr keyboardLayout)
    {
        if (keyboardLayout == IntPtr.Zero) return false;
        ITfInputProcessorProfiles? profiles = null;
        try
        {
            var hr = TF_CreateInputProcessorProfiles(out profiles);
            if (hr < 0 || profiles == null) return false;
            if (profiles is not ITfInputProcessorProfileMgr manager) return false;
            var emptyClsid = Guid.Empty;
            var emptyProfile = Guid.Empty;
            return IsSuccessfulActivationResult(manager.ActivateProfile(ProfileTypeKeyboardLayout, languageId,
                ref emptyClsid, ref emptyProfile, keyboardLayout, ProcessActivationFlags));
        }
        catch (Exception ex)
        {
            Logger.Error("按 CAD 进程范围激活 Windows 键盘布局失败。", ex);
            return false;
        }
        finally
        {
            if (profiles != null && Marshal.IsComObject(profiles)) Marshal.ReleaseComObject(profiles);
        }
    }

    internal static bool HasProcessOnlyActivationFlags(uint flags) =>
        (flags & ActivateForProcess) != 0
        && (flags & ActivateForSession) == 0
        && (flags & ActivateForSystemAll) == 0;

    // ActivateProfile returns S_FALSE when the profile is not enabled. S_FALSE
    // is a non-negative HRESULT, but it is not a successful activation.
    internal static bool IsSuccessfulActivationResult(int hresult) => hresult == 0;

    internal static bool IsSelectableInputMethodProfile(uint profileType, uint flags, Guid tipClsid,
        Guid profileGuid) =>
        profileType == ProfileTypeInputProcessor
        && (flags & ProfileFlagEnabled) != 0
        && tipClsid != Guid.Empty
        && profileGuid != Guid.Empty;

    public static bool TryGetActiveSelectionId(out string selectionId)
    {
        selectionId = "";
        ITfInputProcessorProfiles? profiles = null;
        try
        {
            var hr = TF_CreateInputProcessorProfiles(out profiles);
            if (hr < 0 || profiles == null) return false;
            if (profiles is not ITfInputProcessorProfileMgr manager) return false;
            var category = TipKeyboardCategory;
            if (manager.GetActiveProfile(ref category, out var active) != 0) return false;
            if (active.ProfileType == ProfileTypeInputProcessor
                && active.TipClsid != Guid.Empty
                && active.ProfileGuid != Guid.Empty)
            {
                selectionId = $"TSF|{active.TipClsid:D}|{active.LanguageId:X4}|{active.ProfileGuid:D}";
                return true;
            }
            if (active.ProfileType == ProfileTypeKeyboardLayout && active.KeyboardLayout != IntPtr.Zero)
            {
                selectionId = "KBD|" + unchecked((uint)active.KeyboardLayout.ToInt64()).ToString("X8");
                return true;
            }
            return false;
        }
        catch { return false; }
        finally
        {
            if (profiles != null && Marshal.IsComObject(profiles)) Marshal.ReleaseComObject(profiles);
        }
    }

    private static bool IsEnabled(RegistryKey? key)
    {
        if (key == null) return false;
        var value = key.GetValue("Enable");
        if (value == null) return true;
        try { return Convert.ToInt32(value) != 0; }
        catch { return false; }
    }

    private static bool TryParseLanguageId(string value, out ushort languageId)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
        return ushort.TryParse(text, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out languageId);
    }

    private static string ReadDisplayName(string tipName, string languageName, string profileName)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var profile = machine.OpenSubKey($@"{MachineTipPath}\{tipName}\LanguageProfile\{languageName}\{profileName}");
                using var tip = machine.OpenSubKey($@"{MachineTipPath}\{tipName}");
                using var classRegistration = machine.OpenSubKey(
                    $@"SOFTWARE\Classes\CLSID\{tipName}\InprocServer32");
                var description = FirstText(profile?.GetValue("Description"), profile?.GetValue("Display Description"),
                    tip?.GetValue("Description"), tip?.GetValue("Display Description"));
                if (description.Length > 0) return FriendlyName(ResolveIndirectString(description));
                var module = FirstText(classRegistration?.GetValue(null), classRegistration?.GetValue("Assembly"));
                var friendlyModule = FriendlyName(module);
                if (module.Length > 0 && !friendlyModule.Equals(module, StringComparison.Ordinal))
                    return friendlyModule;
            }
            catch { }
        }
        return "输入法 " + tipName;
    }

    private static string FirstText(params object?[] values) => values
        .Select(value => (value?.ToString() ?? "").Trim())
        .FirstOrDefault(value => value.Length > 0) ?? "";

    private static string ResolveIndirectString(string value)
    {
        if (!value.StartsWith("@", StringComparison.Ordinal)) return value;
        try
        {
            var buffer = new StringBuilder(512);
            return SHLoadIndirectString(value, buffer, buffer.Capacity, IntPtr.Zero) == 0
                ? buffer.ToString().Trim()
                : value;
        }
        catch { return value; }
    }

    internal static string FriendlyName(string value)
    {
        if (value.IndexOf("WeType", StringComparison.OrdinalIgnoreCase) >= 0) return "微信输入法";
        if (value.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0 || value.Contains("搜狗")) return "搜狗输入法";
        if (value.IndexOf("Microsoft Pinyin", StringComparison.OrdinalIgnoreCase) >= 0 || value.Contains("微软拼音")) return "微软拼音";
        return value;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int outputCount, IntPtr reserved);

    [DllImport("msctf.dll")]
    private static extern int TF_CreateInputProcessorProfiles(out ITfInputProcessorProfiles profiles);

    [ComImport]
    [Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfiles
    {
    }

    [ComImport]
    [Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig]
        int ActivateProfile(uint profileType, ushort languageId, ref Guid clsid,
            ref Guid profile, IntPtr keyboardLayout, uint flags);

        [PreserveSig]
        int DeactivateProfile(uint profileType, ushort languageId, ref Guid clsid,
            ref Guid profile, IntPtr keyboardLayout, uint flags);

        [PreserveSig]
        int GetProfile(uint profileType, ushort languageId, ref Guid clsid,
            ref Guid profile, IntPtr keyboardLayout, out TfInputProcessorProfile result);

        [PreserveSig]
        int EnumProfiles(ushort languageId, out IEnumTfInputProcessorProfiles enumerator);

        [PreserveSig]
        int ReleaseInputProcessor(ref Guid clsid, uint flags);

        [PreserveSig]
        int RegisterProfile(ref Guid clsid, ushort languageId, ref Guid profile,
            IntPtr description, uint descriptionLength, IntPtr iconFile, uint iconFileLength,
            uint iconIndex, IntPtr substituteKeyboardLayout, uint preferredLayout,
            [MarshalAs(UnmanagedType.Bool)] bool enabledByDefault, uint flags);

        [PreserveSig]
        int UnregisterProfile(ref Guid clsid, ushort languageId, ref Guid profile, uint flags);

        [PreserveSig]
        int GetActiveProfile(ref Guid category, out TfInputProcessorProfile profile);
    }

    [ComImport]
    [Guid("71C6E74D-0F28-11D8-A82A-00065B84435C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumTfInputProcessorProfiles
    {
        [PreserveSig]
        int Clone(out IEnumTfInputProcessorProfiles enumerator);

        [PreserveSig]
        int Next(uint count, out TfInputProcessorProfile profile, out uint fetched);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Skip(uint count);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TfInputProcessorProfile
    {
        public uint ProfileType;
        public ushort LanguageId;
        public Guid TipClsid;
        public Guid ProfileGuid;
        public Guid CategoryId;
        public IntPtr SubstituteKeyboardLayout;
        public uint Capabilities;
        public IntPtr KeyboardLayout;
        public uint Flags;
    }
}
