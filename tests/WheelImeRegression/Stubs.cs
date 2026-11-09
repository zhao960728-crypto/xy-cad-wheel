// Test doubles for host/IME boundaries. Production services and event handlers
// are linked above; these doubles never switch the user's real input method.
using System.Drawing;
using System.Windows.Forms;
internal static class HostAccessProbe
{
    internal static bool Deny;
    internal static int Denied;
    internal static void Touch()
    {
        if (!Deny) return;
        Denied++;
        throw new InvalidOperationException("Native host has already been destroyed");
    }
}
namespace Autodesk.AutoCAD.ApplicationServices
{
    public class CommandEventArgs : EventArgs { public string GlobalCommandName { get; set; } }
    public class LispWillStartEventArgs : EventArgs { public string FirstLine { get; set; } }
    public class DocumentCollectionEventArgs : EventArgs { public Document Document { get; set; } }
    public class Document
    {
        public string CommandInProgress { get; set; } = "";
        private readonly DatabaseServices.Database database = new();
        public DatabaseServices.Database Database { get { HostAccessProbe.Touch(); return database; } }
        public EditorInput.Editor Editor { get; } = new();
        public event EventHandler<CommandEventArgs> CommandWillStart, CommandEnded, CommandCancelled, CommandFailed;
        public event EventHandler<LispWillStartEventArgs> LispWillStart;
        public event EventHandler LispEnded, LispCancelled;
        public void Begin(string name) => CommandWillStart?.Invoke(this, new() { GlobalCommandName = name });
        public void End(string name) => CommandEnded?.Invoke(this, new() { GlobalCommandName = name });
        public void Cancel(string name) => CommandCancelled?.Invoke(this, new() { GlobalCommandName = name });
        public void Fail(string name) => CommandFailed?.Invoke(this, new() { GlobalCommandName = name });
        public void BeginLisp(string name) => LispWillStart?.Invoke(this, new() { FirstLine = name });
        public void EndLisp() => LispEnded?.Invoke(this, EventArgs.Empty);
    }
    public class DocumentCollection
    {
        public Document MdiActiveDocument { get; private set; }
        public event EventHandler<DocumentCollectionEventArgs> DocumentActivated, DocumentToBeDestroyed;
        public void Activate(Document document) { MdiActiveDocument = document; DocumentActivated?.Invoke(this, new() { Document = document }); }
        public void ActivateBeforeCurrentChanges(Document document) { DocumentActivated?.Invoke(this, new() { Document = document }); MdiActiveDocument = document; }
        public void Destroy(Document document) => DocumentToBeDestroyed?.Invoke(this, new() { Document = document });
    }
}
namespace Autodesk.AutoCAD.ApplicationServices.Core
{
    public static class Application
    {
        private static readonly ApplicationServices.DocumentCollection manager = new();
        public static ApplicationServices.DocumentCollection DocumentManager { get { HostAccessProbe.Touch(); return manager; } }
        private static EventHandler idle;
        public static event EventHandler Idle { add { idle += value; } remove { HostAccessProbe.Touch(); idle -= value; } }
        public static object GetSystemVariable(string name) => NativeCommandActive;
        public static int NativeCommandActive;
        public static void Tick() => idle?.Invoke(null, EventArgs.Empty);
    }
}
namespace Autodesk.AutoCAD.DatabaseServices
{
    public enum OpenMode { ForRead }
    public struct ObjectId { public int Value; public bool IsNull => Value == 0; public bool IsErased => false; }
    public class DBObject { public ObjectId ObjectId { get; set; } }
    public class DBText : DBObject { public string TextString { get; set; } }
    public class MText : DBObject { public string Contents { get; set; } public string Text => Contents; }
    public class BlockReference : DBObject { public List<ObjectId> AttributeCollection { get; } = new(); }
    public class ObjectEventArgs : EventArgs { public DBObject DBObject { get; set; } }
    public class Database
    {
        public TransactionManager TransactionManager { get; } = new();
        public event EventHandler<ObjectEventArgs> ObjectOpenedForModify;
        public void Modify(DBObject value) => ObjectOpenedForModify?.Invoke(this, new() { DBObject = value });
    }
    public class TransactionManager { public Transaction StartTransaction() => new(); }
    public class Transaction : IDisposable { public DBObject GetObject(ObjectId id, OpenMode mode, bool erased) => null; public void Dispose() { } }
}
namespace Autodesk.AutoCAD.EditorInput
{
    public enum PromptStatus { OK, Error }
    public class Editor { public bool IsQuiescent { get; set; } = false; public SelectionResult SelectImplied() => new(); }
    public class SelectionResult { public PromptStatus Status => PromptStatus.Error; public SelectionSet Value => null; }
    public class SelectionSet { public DatabaseServices.ObjectId[] GetObjectIds() => Array.Empty<DatabaseServices.ObjectId>(); }
}
namespace XiaoYang.AutoCADPlugin
{
    internal class PluginConfig { public ImeSwitchConfig ImeSwitch { get; set; } = new(); public Dictionary<string, SceneMenuConfig> Menus { get; } = new() { ["global"] = new() }; public int MenuSizePx => 400; }
    internal class SceneMenuConfig { public int MenuSizePx => 400; }
    internal class MenuItemConfig { }
    internal static class SceneKeys { public const string Global = "global"; }
    internal static class CadCommandCatalog
    {
        public static string NormalizeCommandName(string value) => (value ?? "").Trim().ToUpperInvariant();
        public static bool IsSafeCommandName(string value) => !string.IsNullOrWhiteSpace(value) && value.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
    }
    internal static class ConfigStore { public static void Save(PluginConfig config) => throw new Exception("Tests must not save configuration."); }
    internal static class CadHostInfo { public const string DisplayName = "Test CAD", ProductName = "Test CAD"; }
    internal static class Logger
    {
        public static readonly List<string> Errors = new();
        public static string ErrorLogPath => "test-only";
        public static void Info(string text) { } public static void Trace(string text) { }
        public static void Error(string text, Exception error = null) { lock (Errors) Errors.Add(text + error); }
    }
    internal class ImeErrorReportWindow : Form { public ImeErrorReportWindow(Action close) { } public void ShowReport(string text, IntPtr owner) { } }
    internal class ImeSwitchOutcome
    {
        public bool Success, AlreadyCorrect, ModeChanged, ModeDetected, RaceBlocked, UsedFallback, ProfileActivationAttempted, ProfileActivatedForProcess;
        public string SelectionId = "test", RequestedSelectionId = "test", FallbackReason = "", ModeBackend = "test", ModeFailureStage = "";
        public IntPtr Target;
    }
    internal static class ImeNativeBridge
    {
        public static bool Foreground = true, Succeeds = true;
        public static IntPtr Target = (IntPtr)1;
        public static readonly List<bool> Calls = new();
        public static bool IsCurrentProcessForeground(IntPtr main) => Foreground;
        public static IntPtr ResolveInputTarget(IntPtr main) => Foreground ? Target : IntPtr.Zero;
        public static ImeSwitchOutcome TrySwitch(string layout, string profile, bool chinese, IntPtr main)
        { Calls.Add(chinese); return new() { Success = Succeeds, ModeDetected = Succeeds, ModeChanged = Succeeds, Target = Target }; }
    }
    internal class RadialMenuWindow : Form
    {
        public RadialMenuWindow(Action<MenuItemConfig> selected) { }
        public void ShowMenu(PluginConfig config, string scene, Point center, bool drag, MouseButtons button) { }
        public void UpdateCursor(Point cursor) { } public void CommitCursor(Point cursor) { }
        public void ReloadConfig() { } public void PrimeMenu(PluginConfig config, string scene) { }
    }
    internal static class RadialMenuControl { public static void WarmSharedRenderCache(PluginConfig config, SceneMenuConfig scene, Size size) { } }
    internal static class NativeMethods
    {
        public struct POINT { public int X, Y; }
        public struct RECT { public int Left, Top, Right, Bottom; }
        public static int CursorMoves, Clicks;
        public static bool GetWindowRect(IntPtr window, out RECT rect) { rect = default; return false; }
        public static IntPtr GetForegroundWindow() => IntPtr.Zero;
        internal static bool WindowEnabled;
        public static bool IsWindowEnabled(IntPtr window) => WindowEnabled;
        public static IntPtr WindowFromPoint(POINT point) => IntPtr.Zero;
        public static IntPtr GetParent(IntPtr window) => IntPtr.Zero;
        public static uint GetWindowThreadProcessId(IntPtr window, out uint pid) { pid = 0; return 0; }
        public static int GetClassName(IntPtr window, char[] buffer, int size) => 0;
        public static short GetAsyncKeyState(int key) => 0;
        public static void SetCursorPos(int x, int y) => CursorMoves++;
        public static void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr info) => Clicks++;
    }
}
