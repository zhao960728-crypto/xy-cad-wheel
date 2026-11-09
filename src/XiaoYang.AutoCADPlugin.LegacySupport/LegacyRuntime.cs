using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace XiaoYang.AutoCADPlugin;

// Used only by the CLR 2 kernels. Modern kernels retain their framework APIs.
internal static class LegacyRuntime
{
    public static T[] Empty<T>() => EmptyCache<T>.Value;
    private static class EmptyCache<T> { internal static readonly T[] Value = new T[0]; }
    public static bool IsNullOrWhiteSpace(string text) => text == null || text.Trim().Length == 0;
    public static string Combine(params string[] paths)
    {
        if (paths == null || paths.Length == 0) throw new ArgumentException("Missing path.");
        var result = paths[0];
        for (var index = 1; index < paths.Length; index++) result = Path.Combine(result, paths[index]);
        return result;
    }
    public static string Join<T>(string separator, IEnumerable<T> values) => string.Join(separator, values.Select(value => Convert.ToString(value)).ToArray());
    public static string Join(string separator, params object[] values) => Join<object>(separator, values);
    public static string Concat(IEnumerable<string> values) => string.Join("", values.ToArray());
    public static string Concat(IEnumerable<char> values) => new string(values.ToArray());
    public static Guid ParseGuid(string text) => new Guid(text);
    public static bool TryParseGuid(string text, out Guid value)
    {
        try { value = new Guid(text); return true; }
        catch (ArgumentException) { value = Guid.Empty; return false; }
        catch (FormatException) { value = Guid.Empty; return false; }
        catch (OverflowException) { value = Guid.Empty; return false; }
    }
    public static bool TryParseGuidExact(string text, string format, out Guid value) =>
        TryParseGuid(text, out value) && value.ToString(format).Equals(text, StringComparison.OrdinalIgnoreCase);
    public static XDocument LoadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { ProhibitDtd = true, XmlResolver = null });
        return XDocument.Load(reader);
    }
    internal static int ReadDpi(Control control)
    {
        using var graphics = control.CreateGraphics();
        return Math.Max(96, (int)Math.Round(graphics.DpiX));
    }
}

internal class LegacyForm : Form
{
    private int _dpi = 96;
    public int DeviceDpi => IsHandleCreated ? _dpi : LegacyRuntime.ReadDpi(this);
    public event EventHandler DpiChanged;
    protected override void OnHandleCreated(EventArgs args) { base.OnHandleCreated(args); _dpi = LegacyRuntime.ReadDpi(this); }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x02E0) return;
        _dpi = Math.Max(96, (int)((long)message.WParam & 0xffff));
        DpiChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal class LegacyControl : Control
{
    private int _dpi = 96;
    public int DeviceDpi => IsHandleCreated ? _dpi : LegacyRuntime.ReadDpi(this);
    protected override void OnHandleCreated(EventArgs args) { base.OnHandleCreated(args); _dpi = LegacyRuntime.ReadDpi(this); }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x02E0) _dpi = Math.Max(96, (int)((long)message.WParam & 0xffff));
    }
}

// IDispatch reflection works on CLR 2 without loading the CLR 4 dynamic binder.
internal sealed class LegacyComObject
{
    private readonly object _value;
    internal LegacyComObject(object value) { _value = value ?? throw new ArgumentNullException(nameof(value)); }
    private object Get(string name) => Invoke(name, BindingFlags.GetProperty, new object[0]);
    private object Call(string name, params object[] args) => Invoke(name, BindingFlags.InvokeMethod, args);
    private object Invoke(string name, BindingFlags flags, object[] args)
    {
        try { return _value.GetType().InvokeMember(name, flags | BindingFlags.Public | BindingFlags.Instance, null, _value, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is COMException) { throw ex.InnerException; }
    }
    public LegacyComObject MenuGroups => new(Get("MenuGroups"));
    public LegacyComObject Menus => new(Get("Menus"));
    public LegacyComObject MenuBar => new(Get("MenuBar"));
    public int Count => Convert.ToInt32(Get("Count"));
    public string Name => Convert.ToString(Get("Name"));
    public string NameNoMnemonic => Convert.ToString(Get("NameNoMnemonic"));
    public string Label => Convert.ToString(Get("Label"));
    public string Macro => Convert.ToString(Get("Macro"));
    public bool OnMenuBar => Convert.ToBoolean(Get("OnMenuBar"));
    public LegacyComObject Item(int index) => new(Call("Item", index));
    public LegacyComObject Load(string path) => new(Call("Load", path));
    public void InsertInMenuBar(int index) => Call("InsertInMenuBar", index);
    public void Unload() => Call("Unload");
}

internal enum LegacyRegistryView { Registry64 = 0x100, Registry32 = 0x200 }

// Read-only registry adapter preserves both WOW64 views on .NET 3.5.
internal static class LegacyRegistry
{
    internal static LegacyRegistryKey OpenBaseKey(RegistryHive hive, LegacyRegistryView view) =>
        new(new IntPtr((int)hive), (int)view, false);
}

internal sealed class LegacyRegistryKey : IDisposable
{
    private IntPtr _handle;
    private readonly int _view;
    private readonly bool _ownsHandle;
    internal LegacyRegistryKey(IntPtr handle, int view, bool ownsHandle) { _handle = handle; _view = view; _ownsHandle = ownsHandle; }
    public LegacyRegistryKey OpenSubKey(string path)
    {
        var error = RegOpenKeyEx(_handle, path, 0, 0x20019 | _view, out var child);
        if (error == 2 || error == 3) return null;
        if (error != 0) throw new Win32Exception(error);
        return new LegacyRegistryKey(child, _view, true);
    }
    public string[] GetSubKeyNames()
    {
        var names = new List<string>();
        for (uint index = 0; ; index++)
        {
            var name = new StringBuilder(256); uint length = 256;
            var error = RegEnumKeyEx(_handle, index, name, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (error == 259) break;
            if (error != 0) throw new Win32Exception(error);
            names.Add(name.ToString());
        }
        return names.ToArray();
    }
    public object GetValue(string name)
    {
        uint length = 0, type;
        var error = RegQueryValueEx(_handle, name, IntPtr.Zero, out type, null, ref length);
        if (error == 2) return null;
        if (error != 0) throw new Win32Exception(error);
        if (length > 1048576) throw new InvalidDataException("Registry value exceeds the read limit.");
        var bytes = new byte[length];
        error = RegQueryValueEx(_handle, name, IntPtr.Zero, out type, bytes, ref length);
        if (error != 0) throw new Win32Exception(error);
        if (type == 1 || type == 2)
        {
            var value = Encoding.Unicode.GetString(bytes, 0, (int)length).TrimEnd('\0');
            return type == 2 ? Environment.ExpandEnvironmentVariables(value) : value;
        }
        if (type == 4 && length == 4) return BitConverter.ToInt32(bytes, 0);
        if (type == 11 && length == 8) return BitConverter.ToInt64(bytes, 0);
        if (type == 7) return Encoding.Unicode.GetString(bytes, 0, (int)length).TrimEnd('\0').Split('\0');
        return bytes;
    }
    public void Dispose() { if (_ownsHandle && _handle != IntPtr.Zero) { RegCloseKey(_handle); _handle = IntPtr.Zero; } }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyEx(IntPtr key, string path, uint options, int access, out IntPtr result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegEnumKeyEx(IntPtr key, uint index, StringBuilder name, ref uint length, IntPtr reserved, IntPtr className, IntPtr classLength, IntPtr lastWrite);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegQueryValueEx(IntPtr key, string name, IntPtr reserved, out uint type, byte[] data, ref uint length);
    [DllImport("advapi32.dll")] private static extern int RegCloseKey(IntPtr key);
}
