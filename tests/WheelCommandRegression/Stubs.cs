using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace WheelCommandRegression
{
    internal static class Probe
    {
        internal static int Owner;
        internal static int Accesses;
        internal static bool DenyHostCalls;
        internal static int DeniedHostCalls;
        internal static void Touch()
        {
            if (DenyHostCalls)
            {
                DeniedHostCalls++;
                throw new InvalidOperationException("Native host has already been destroyed");
            }
            if (Environment.CurrentManagedThreadId != Owner)
                throw new InvalidOperationException("CAD API accessed outside constructor thread");
            Accesses++;
        }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    public readonly record struct TypedValue(object Value);
    public sealed class ResultBuffer(params TypedValue[] values) { public TypedValue[] AsArray() => values; }
    public readonly record struct ObjectId(int Value)
    {
        public bool IsValid { get { WheelCommandRegression.Probe.Touch(); return Value > 0; } }
        public bool IsErased { get { WheelCommandRegression.Probe.Touch(); return false; } }
    }
}

namespace Autodesk.AutoCAD.EditorInput
{
    public enum PromptStatus { OK, Error }
    public sealed class SelectionSet(ObjectId[] ids)
    {
        public ObjectId[] GetObjectIds() { WheelCommandRegression.Probe.Touch(); return ids.ToArray(); }
    }
    public sealed class PromptSelectionResult(ObjectId[] ids)
    {
        public PromptStatus Status => ids.Length > 0 ? PromptStatus.OK : PromptStatus.Error;
        public SelectionSet Value => new(ids);
    }
    public sealed class Editor
    {
        internal string Input = "";
        internal readonly List<string> Messages = [];
        public bool IsQuiescent => true;
        public PromptStringResult GetString(string prompt) => new(Input);
        public void WriteMessage(string message) => Messages.Add(message);
        internal ObjectId[] Selected = [];
        internal int Restores;
        internal readonly List<(object[] Tokens, ObjectId[] Selection)> Commands = [];
        public PromptSelectionResult SelectImplied() { WheelCommandRegression.Probe.Touch(); return new(Selected.ToArray()); }
        public void SetImpliedSelection(ObjectId[] ids) { WheelCommandRegression.Probe.Touch(); Restores++; Selected = ids.ToArray(); }
        public void Command(params object[] tokens) { WheelCommandRegression.Probe.Touch(); Commands.Add((tokens, Selected.ToArray())); }
    }
    public sealed class PromptStringResult(string text)
    {
        public PromptStatus Status => PromptStatus.OK;
        public string StringResult => text;
    }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    public sealed class Document
    {
        public string CommandInProgress => "";
        private readonly Autodesk.AutoCAD.EditorInput.Editor editor = new();
        public Autodesk.AutoCAD.EditorInput.Editor Editor { get { WheelCommandRegression.Probe.Touch(); return editor; } }
        internal readonly List<(string Line, ObjectId[] Selection)> Sent = [];
        public void SendStringToExecute(string line, bool activate, bool wrapUpInactiveDoc, bool echoCommand)
        {
            WheelCommandRegression.Probe.Touch();
            Sent.Add((line, editor.Selected.ToArray()));
        }
        public IDisposable LockDocument() { WheelCommandRegression.Probe.Touch(); return new Lease(); }
        private sealed class Lease : IDisposable { public void Dispose() => WheelCommandRegression.Probe.Touch(); }
    }
    public sealed class DocumentCollectionEventArgs : EventArgs { public Document Document { get; init; } }
    public sealed class DocumentCollection
    {
        private Document active;
        public Document MdiActiveDocument { get { WheelCommandRegression.Probe.Touch(); return active; } }
        private EventHandler<DocumentCollectionEventArgs> activated, created, closing;
        public event EventHandler<DocumentCollectionEventArgs> DocumentActivated { add { activated += value; } remove { WheelCommandRegression.Probe.Touch(); activated -= value; } }
        public event EventHandler<DocumentCollectionEventArgs> DocumentCreated { add { created += value; } remove { WheelCommandRegression.Probe.Touch(); created -= value; } }
        public event EventHandler<DocumentCollectionEventArgs> DocumentToBeDestroyed { add { closing += value; } remove { WheelCommandRegression.Probe.Touch(); closing -= value; } }
        internal int ActivationListeners => activated?.GetInvocationList().Length ?? 0;
        internal void RaiseLateEvents()
        {
            created?.Invoke(this, new()); activated?.Invoke(this, new()); closing?.Invoke(this, new());
        }
        internal int Contexts;
        internal Action ContextTransition;
        internal void Activate(Document document)
        {
            WheelCommandRegression.Probe.Touch(); active = document;
            activated?.Invoke(this, new());
        }
        public Task ExecuteInCommandContextAsync(Func<object, Task> callback, object data)
        {
            WheelCommandRegression.Probe.Touch(); Contexts++;
            active.Editor.Selected = [];
            ContextTransition?.Invoke();
            return callback(data);
        }
    }
}

namespace Autodesk.AutoCAD.ApplicationServices.Core
{
    public static class Application
    {
        private static EventHandler idle;
        public static event EventHandler Idle { add { idle += value; } remove { WheelCommandRegression.Probe.Touch(); idle -= value; } }
        internal static void RaiseIdle() => idle?.Invoke(null, EventArgs.Empty);
        internal static DocumentCollection Manager = new();
        internal static string Names = "";
        internal static int Active;
        public static DocumentCollection DocumentManager { get { WheelCommandRegression.Probe.Touch(); return Manager; } }
        public static object GetSystemVariable(string name)
        {
            WheelCommandRegression.Probe.Touch();
            return name switch { "CMDNAMES" => Names, "CMDACTIVE" => Active, _ => throw new InvalidOperationException(name) };
        }
    }
}

namespace Autodesk.AutoCAD.Runtime
{
    [Flags] public enum CommandFlags { Redraw = 4 }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class CommandClassAttribute(Type type) : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CommandMethodAttribute(string name, CommandFlags flags = 0) : Attribute
    {
        public CommandFlags Flags => flags;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LispFunctionAttribute(string name) : Attribute { }
    public class Exception : System.Exception { public string ErrorStatus => "test"; }
}

namespace XiaoYang.AutoCADPlugin
{
    internal sealed class CadCommandCatalog
    {
        internal static bool Defined = true;
        internal bool IsCommandDefined(string name) => Defined;
        internal static bool IsSafeCommandName(string name) => !string.IsNullOrWhiteSpace(name);
        internal static string NormalizeCommandName(string name) => name?.Trim() ?? "";
    }
    internal sealed class PluginConfig
    {
        public List<CustomCommandConfig> CustomCommands { get; } = [];
        public List<ManagedExtensionConfig> ManagedExtensions { get; } = [];
    }
    internal sealed class ManagedExtensionConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string StartupCommand { get; set; }
        public bool Enabled { get; set; }
    }
    internal static class ExtensionCommandDiscovery
    {
        public static bool IsCommandName(string name) => !string.IsNullOrWhiteSpace(name) && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
    }
    internal static class CommandCategoryCatalog { public const string DefaultCustomCategory = "Custom"; }
    internal sealed class MenuItemConfig { public string Kind { get; set; } = "Command"; public string Value { get; set; } = "ERASE"; }
    internal sealed class CustomCommandConfig
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Id { get; set; } = "custom";
        public string CommandName { get; set; } = "MOVE";
        public List<string> Arguments { get; set; } = ["0,0", "1,1"];
        public bool Silent { get; set; }
        public bool DisableDialogs { get; set; }
    }
    internal static class ActionKinds { public const string CustomCommand = "CustomCommand"; }
#if !EXTENSION_SELECTION_TEST
    internal static class ExtensionLoadService
    {
        public const string StartupEntryCommand = "XYSTART";
        public static string BuildStartupEntryLine(string value) => "XYSTART " + value + " ";
    }
#endif
    internal static class CadHostInfo { public const string DisplayName = "AutoCAD", ProductName = "AutoCAD"; }
    internal static class Logger
    {
        internal static readonly List<Exception> Errors = [];
        public static void Info(string message) { }
        public static void Error(string message, Exception exception) => Errors.Add(exception);
    }
    internal static class NativeMethods
    {
        public static IntPtr GetForegroundWindow() => new(1);
        public static uint GetWindowThreadProcessId(IntPtr window, out int processId) { processId = Environment.ProcessId; return 1; }
        internal static volatile bool ModifierDown;
        public static short GetAsyncKeyState(int key) => ModifierDown && key == 16 ? unchecked((short)0x8000) : (short)0;
    }
}
