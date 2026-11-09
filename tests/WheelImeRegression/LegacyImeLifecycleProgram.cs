using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using XiaoYang.AutoCADPlugin;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

internal static class LegacyImeLifecycleProgram
{
    private static int checks;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static int Main()
    {
        try
        {
            NativeMethods.WindowEnabled = true;
            var config = new PluginConfig { ImeSwitch = new() { Enabled = true, TextCommands = new() { "MTEXT" } } };
            var manager = AcApp.DocumentManager;
            var doc = new Document(); manager.Activate(doc);
            using var service = new ImeAutoSwitchService(() => config, () => (IntPtr)1);
            service.Start();
            Check(ImeNativeBridge.Calls.Count == 0, "Startup switched input method inside registration");
            Tick(service); Check(ImeNativeBridge.Calls.Count == 1 && !ImeNativeBridge.Calls.Last(), "Ready Idle did not apply initial preference");
            var count = ImeNativeBridge.Calls.Count;
            service.ApplyConfiguration();
            Check(ImeNativeBridge.Calls.Count == count, "Settings save switched input method synchronously");
            NativeMethods.WindowEnabled = false; Tick(service);
            Check(ImeNativeBridge.Calls.Count == count, "Settings save modal dialog was modified");
            NativeMethods.WindowEnabled = true; Tick(service);
            Check(ImeNativeBridge.Calls.Count == count + 1, "Settings preference not applied after modal close");

            count = ImeNativeBridge.Calls.Count; doc.Begin("QSAVE");
            Check(ImeNativeBridge.Calls.Count == count && service.HasActiveCadCommand, "Drawing save callback switched input method");
            NativeMethods.WindowEnabled = false; Tick(service); doc.End("QSAVE"); Tick(service);
            Check(ImeNativeBridge.Calls.Count == count, "Drawing save dialog switched input method");
            Check(!service.HasActiveCadCommand, "Drawing save left wheel busy");
            NativeMethods.WindowEnabled = true; Tick(service);
            Check(ImeNativeBridge.Calls.Count == count + 1 && !ImeNativeBridge.Calls.Last(), "Drawing save did not restore normal input");

            count = ImeNativeBridge.Calls.Count; doc.Begin("MTEXT");
            Check(ImeNativeBridge.Calls.Count == count, "Text command switched inside native callback");
            Tick(service); Check(ImeNativeBridge.Calls.Last(), "Text input did not switch to Chinese on Idle");
            doc.End("MTEXT"); Tick(service);
            Check(!ImeNativeBridge.Calls.Last(), "Text command end did not restore English");

            HostAccessProbe.Deny = true; HostAccessProbe.Denied = 0;
            service.Dispose(hostTerminating: true); service.Dispose();
            Check(HostAccessProbe.Denied == 0 && !service.IsStarted && !service.HasActiveCadCommand,
                "Shutdown touched destroyed host or retained busy state");
            count = ImeNativeBridge.Calls.Count;
            doc.Begin("MTEXT"); doc.End("MTEXT"); doc.BeginLisp("(C:TEST)"); doc.EndLisp();
            manager.Activate(new()); manager.Destroy(doc); AcApp.Tick();
            Check(HostAccessProbe.Denied == 0 && ImeNativeBridge.Calls.Count == count,
                "Retained shutdown callbacks accessed native host");
            HostAccessProbe.Deny = false;

            using var ordinary = new ImeAutoSwitchService(() => config, () => (IntPtr)1);
            ordinary.Start(); ordinary.Dispose(); count = ImeNativeBridge.Calls.Count;
            doc.Begin("MTEXT"); AcApp.Tick();
            Check(ImeNativeBridge.Calls.Count == count && !ordinary.IsStarted, "Ordinary disposal retained activity");
            Console.WriteLine("PASS " + checks + " legacy save/input/shutdown checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { HostAccessProbe.Deny = false; NativeMethods.WindowEnabled = true; }
    }

    private static void Tick(ImeAutoSwitchService service)
    {
        foreach (var field in new[] { "_nextFocusCheckUtc", "_nextModeReapplyUtc" })
            typeof(ImeAutoSwitchService).GetField(field, Private).SetValue(service, DateTime.MinValue);
        AcApp.Tick();
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
}
