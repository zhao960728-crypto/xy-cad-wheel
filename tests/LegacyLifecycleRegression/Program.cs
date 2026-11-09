using XiaoYang.AutoCADPlugin;
using WheelCommandRegression;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main()
    {
        Probe.Owner = Environment.CurrentManagedThreadId;
        try
        {
            var ready = false; var starts = 0;
            var startup = new LegacyDeferredStartup(() => ready, () => starts++);
            Check(!startup.Tick() && starts == 0, "Startup ran before host readiness");
            ready = true;
            Check(startup.Tick() && starts == 1, "Ready host did not initialize");
            Check(startup.Tick() && starts == 1, "Repeated Idle duplicated initialization");
            startup = new LegacyDeferredStartup(() => true, () => starts++);
            startup.Cancel(); Check(startup.Tick() && starts == 1, "Quit before ready initialized services");
            LegacyDeferredStartup nested = null; var nestedReadyCalls = 0;
            nested = new LegacyDeferredStartup(() => { nestedReadyCalls++; nested.Tick(); return true; }, () => starts++);
            Check(nested.Tick() && starts == 2 && nestedReadyCalls == 1, "Readiness message pump reentered startup");
            nested = new LegacyDeferredStartup(() => { nested.Cancel(); return true; }, () => starts++);
            Check(nested.Tick() && starts == 2, "Cancellation during readiness was ignored");
            nested = new LegacyDeferredStartup(() => true, () => { starts++; nested.Tick(); });
            Check(nested.Tick() && starts == 3, "Initialization message pump reentered startup");
            var failedStarts = 0;
            nested = new LegacyDeferredStartup(() => true, () => { failedStarts++; throw new InvalidOperationException("fixture"); });
            try { nested.Tick(); } catch (InvalidOperationException) { }
            Check(nested.Tick() && failedStarts == 1, "Failed partial startup was retried");

            RunDispose(false);
            RunDispose(true);
            Console.WriteLine("PASS " + checks + " lifecycle checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Probe.DenyHostCalls = false; }
    }

    private static void RunDispose(bool terminating)
    {
        AcApp.Manager = new();
        AcApp.Manager.Activate(new());
        var configReads = 0;
        var extensions = new ExtensionLoadService();
        extensions.Start(() => { configReads++; return new(); });
        var executor = new CadCommandExecutor(new CadCommandCatalog(), () => new());
        var manager = AcApp.Manager;
        Check(manager.ActivationListeners == 2, "Event registration failed");
#if AUTOCAD_CLR2
        Probe.DenyHostCalls = terminating;
#endif
        Probe.DeniedHostCalls = 0;
        extensions.Dispose(terminating); executor.Dispose(terminating);
        var accesses = Probe.Accesses;
        extensions.Dispose(); executor.Dispose();
        Check(Probe.DeniedHostCalls == 0, "Cleanup touched destroyed host even if an exception was caught");
        Check(Probe.Accesses == accesses, "Repeated cleanup touched host");
#if AUTOCAD_CLR2
        Check(manager.ActivationListeners == (terminating ? 2 : 0), "Legacy ordinary/shutdown event policy failed");
#else
        Check(manager.ActivationListeners == 0, "Modern host stopped unhooking events");
#endif
        var epoch = executor.DocumentEpoch;
        manager.RaiseLateEvents(); AcApp.RaiseIdle();
        Check(executor.DocumentEpoch == epoch && configReads == 0, "Stopped callbacks remained active");
        Check(Probe.DeniedHostCalls == 0, "Late callback touched destroyed host");
        Probe.DenyHostCalls = false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
}
