using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using XiaoYang.AutoCADPlugin;

internal static class Program
{
    private static int failures, passed;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    [STAThread]
    private static int Main(string[] args)
    {
        Test("side button identity, native layout, short-click replay and busy guard", _ => SideButtonRegression.Run());
        Test("opening B cannot leave idle A permanently blocking the wheel", service => {
            var a = Active; a.Begin("OPEN"); a.BeginLisp("(C:OPENHELPER)");
            var b = new Document(); AcApp.DocumentManager.Activate(b);
            // Simulate an end notification lost/routed to the new document.
            b.End("OPEN"); b.EndLisp();
            a.Editor.IsQuiescent = true;
            AcApp.DocumentManager.Activate(a); Tick(service);
            Check(!service.HasActiveCadCommand, "idle A retained stale command depth");
            Check(((System.Collections.ICollection)Get(service, "_commandFrames")).Count == 0, "idle A retained mode frames");
            Check(((System.Collections.ICollection)Get(service, "_lispExecutions")).Count == 0, "idle A retained Lisp frames");
        });
        Test("idle synchronizes busy guard after activation precedes current document", service => {
            var a = Active; a.Begin("LINE");
            var b = new Document { }; b.Editor.IsQuiescent = true;
            AcApp.DocumentManager.ActivateBeforeCurrentChanges(b); Tick(service);
            Check(!service.HasActiveCadCommand, "B inherited previous active snapshot");
            AcApp.DocumentManager.ActivateBeforeCurrentChanges(a); Tick(service);
            Check(service.HasActiveCadCommand, "A lost suspended LINE busy guard");
        });
        Test("disabled IME also repairs stale wheel guard", service => {
            config.ImeSwitch.Enabled = false; service.ApplyConfiguration();
            Active.Begin("OPEN"); Active.Editor.IsQuiescent = true; Tick(service);
            Check(!service.HasActiveCadCommand, "disabled IME left wheel blocked");
        });
        Test("quiescent reconciliation preserves native and Lisp activity", service => {
            Active.Begin("MTEXT"); Active.Editor.IsQuiescent = true;
            Active.CommandInProgress = "MTEXT"; Tick(service);
            Check(service.HasActiveCadCommand, "native command was cleared");
            Active.CommandInProgress = ""; AcApp.NativeCommandActive = 32; Tick(service);
            Check(service.HasActiveCadCommand, "active Lisp was cleared");
            AcApp.NativeCommandActive = 0; Active.Editor.IsQuiescent = false; Tick(service);
            Check(service.HasActiveCadCommand, "nonquiescent command was cleared");
        });
        Test("success stops reapply and preserves subsequent manual mode", service => {
            Active.Begin("MTEXT"); var count = ImeNativeBridge.Calls.Count;
            Tick(service); Tick(service); Check(ImeNativeBridge.Calls.Count == count, "successful Chinese mode was reapplied");
            Active.End("MTEXT"); count = ImeNativeBridge.Calls.Count; Tick(service);
            Check(ImeNativeBridge.Calls.Count == count, "successful English mode was reapplied");
        });
        Test("failed ordinary command switch gets bounded retries", service => {
            ImeNativeBridge.Succeeds = false; Active.Begin("LINE");
            Check(Pending(service) == 6, "ordinary command did not schedule retry");
            var count = ImeNativeBridge.Calls.Count;
            for (var i = 0; i < 10; i++) Tick(service);
            Check(ImeNativeBridge.Calls.Count == count + 6 && Pending(service) == 0, "retry did not stop at six");
        });
        Test("new successful focus cancels stale failed retry", service => {
            ImeNativeBridge.Succeeds = false; Active.Begin("MTEXT");
            ImeNativeBridge.Succeeds = true; ImeNativeBridge.Target = (IntPtr)2; Tick(service);
            var count = ImeNativeBridge.Calls.Count; Tick(service);
            Check(Pending(service) == 0 && ImeNativeBridge.Calls.Count == count, "stale retry survived focus success");
        });
        Test("drawing switch preserves suspended command and wheel busy guard", service => {
            var a = Active; a.Begin("MTEXT"); var b = new Document(); AcApp.DocumentManager.Activate(b);
            Check(!service.HasActiveCadCommand && !ImeNativeBridge.Calls.Last(), "new drawing inherited old mode");
            AcApp.DocumentManager.Activate(a);
            Check(service.HasActiveCadCommand && ImeNativeBridge.Calls.Last(), "suspended command lost");
            a.Cancel("MTEXT"); Check(!service.HasActiveCadCommand && !ImeNativeBridge.Calls.Last(), "cancel did not restore English");
        });
        Test("background document events cannot alter active drawing", service => {
            var a = Active; a.Begin("MTEXT"); var b = new Document(); AcApp.DocumentManager.Activate(b); b.Begin("MTEXT");
            var count = ImeNativeBridge.Calls.Count; a.Begin("LINE"); a.End("LINE"); a.End("MTEXT");
            Check(count == ImeNativeBridge.Calls.Count && service.HasActiveCadCommand, "background event changed active state");
            b.Fail("MTEXT"); Check(!service.HasActiveCadCommand && !ImeNativeBridge.Calls.Last(), "failure did not restore English");
        });
        Test("nested Lisp commands retain Chinese and return to English", service => {
            Active.BeginLisp("(C:YX_Q)"); Active.Begin("LINE");
            Check(ImeNativeBridge.Calls.Last(), "child lost parent Chinese"); Active.End("LINE");
            Check(service.HasActiveCadCommand && ImeNativeBridge.Calls.Last(), "parent frame lost"); Active.EndLisp();
            Check(!service.HasActiveCadCommand && !ImeNativeBridge.Calls.Last(), "Lisp end did not clear busy state");
        });
        Test("existing text editing still detects Chinese and English", service => {
            Active.Begin("DDEDIT"); Active.Database.Modify(new DBText { ObjectId = new() { Value = 1 }, TextString = "酸洗100件" });
            Check(ImeNativeBridge.Calls.Last(), "Chinese text not recognized");
            Active.Database.Modify(new DBText { ObjectId = new() { Value = 2 }, TextString = "ABC123" });
            Check(!ImeNativeBridge.Calls.Last(), "English text not recognized");
        });
        Test("CAD background cancels retries without calling IME", service => {
            ImeNativeBridge.Succeeds = false; Active.Begin("MTEXT"); var count = ImeNativeBridge.Calls.Count;
            ImeNativeBridge.Foreground = false; Tick(service);
            Check(Pending(service) == 0 && ImeNativeBridge.Calls.Count == count, "background IME modified");
        });
        Test("disabled IME still tracks wheel busy state across drawings", service => {
            config.ImeSwitch.Enabled = false; service.ApplyConfiguration(); var a = Active; a.Begin("LINE");
            AcApp.DocumentManager.Activate(new()); Check(!service.HasActiveCadCommand, "inactive drawing remains busy");
            AcApp.DocumentManager.Activate(a); Check(service.HasActiveCadCommand, "disabled IME lost command depth");
            a.End("LINE"); Check(!service.HasActiveCadCommand, "depth did not balance");
        });
        Test("destroying a suspended drawing releases its state", service => {
            var a = Active; a.Begin("MTEXT"); AcApp.DocumentManager.Activate(new()); AcApp.DocumentManager.Destroy(a);
            Check(!service.HasActiveCadCommand, "destroy left busy state");
            Check(((System.Collections.ICollection)Get(service, "_commandFrames")).Count == 0, "destroy left command frame");
        });
        Run("short-click replay is blocked after leaving CAD", () => {
            var type = typeof(MouseWheelTrigger); var ctor = type.GetConstructors().Single();
            var values = new List<object> { (Func<string>)(() => TriggerModes.RightHold), (Func<int>)(() => 200), (Func<IntPtr>)(() => IntPtr.Zero), (Func<bool>)(() => false), (Action<Point, MouseButtons>)((_, _) => {}), (Action<Point>)(_ => {}) };
            if (ctor.GetParameters().Length == 7) values.Add((Action)(() => {}));
            using var trigger = (MouseWheelTrigger)ctor.Invoke(values.ToArray());
            var button = Enum.ToObject(type.GetNestedType("TriggerButton", BindingFlags.NonPublic), 0);
            type.GetMethod("ReplayClick", Private).Invoke(trigger, new[] { button, (object)new Point(123, 456) });
            Check(NativeMethods.CursorMoves == 0 && NativeMethods.Clicks == 0, "replay injected cursor/click outside CAD");
        });
        // The old implementation crashes its STA callback after timeout. Run only
        // the safe cancellation path as a red/green baseline; test in-flight finish
        // against the fixed implementation after that path passes.
        if (!args.Contains("--ime-only")) Run("overlay cancels callbacks still queued after timeout", OverlayTimeout);
        Console.WriteLine($"RESULT: {passed} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }
    private static PluginConfig config;
    private static Document Active => AcApp.DocumentManager.MdiActiveDocument;
    private static void Test(string name, Action<ImeAutoSwitchService> body) => Run(name, () => {
        AcApp.NativeCommandActive = 0;
        ImeNativeBridge.Foreground = ImeNativeBridge.Succeeds = true; ImeNativeBridge.Target = (IntPtr)1; ImeNativeBridge.Calls.Clear();
        config = new() { ImeSwitch = new() { Enabled = true, TextCommands = new() { "MTEXT", "YX_Q" } } };
        AcApp.DocumentManager.Activate(new()); using var service = new ImeAutoSwitchService(() => config, () => (IntPtr)1);
        service.Start(); body(service);
    });
    private static void Tick(ImeAutoSwitchService service) {
        Set(service, "_nextFocusCheckUtc", DateTime.MinValue); Set(service, "_nextModeReapplyUtc", DateTime.MinValue); AcApp.Tick();
    }
    private static object Get(object obj, string name) => obj.GetType().GetField(name, Private).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    private static int Pending(ImeAutoSwitchService service) => (int)Get(service, "_pendingModeReapplyCount");
    private static void Check(bool value, string error) { if (!value) throw new Exception(error); }
    private static void Run(string name, Action action) {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
    }
    private static void OverlayTimeout() {
        using var host = new RadialOverlayHost(_ => {}); host.Start();
        Check(SpinWait.SpinUntil(() => host.IsReady, 5000), "overlay not ready");
        var window = (Form)Get(host, "_window");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        window.BeginInvoke((Action)(() => { entered.Set(); release.Wait(3000); }));
        Check(entered.Wait(2000), "STA blocker not entered"); var calls = 0;
        var send = typeof(RadialOverlayHost).GetMethod("Send", Private);
        send.Invoke(host, new object[] { (Action<RadialMenuWindow>)(_ => calls++), 30 });
        release.Set(); window.Invoke((Action)(() => {}));
        Check(calls == 0, "expired overlay action ran late");
        using var finished = new ManualResetEventSlim();
        send.Invoke(host, new object[] { (Action<RadialMenuWindow>)(_ => { Thread.Sleep(100); finished.Set(); }), 30 });
        Check(finished.Wait(2000), "in-flight callback failed to finish"); window.Invoke((Action)(() => {}));
        Check(host.IsReady, "overlay stopped after late completion");
    }
}
