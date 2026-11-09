using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using XiaoYang.AutoCADPlugin;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using WheelCommandRegression;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Probe.Owner = Environment.CurrentManagedThreadId;
        var cases = new (string Name, Action Body)[]
        {
            ("background native command retains PickFirst on CAD thread", Native),
            ("known parameterized custom retains arguments and selection without context", () => Custom(true)),
            ("unknown parameterized custom retains arguments and selection without context", () => Custom(false)),
            ("scripted arguments quote spaces and preserve ENTER", ScriptedArguments),
            ("PAUSE rejects complete submission", RejectPause),
            ("active command is left untouched", Active),
            ("document changed while wheel open cancels", () => Switch(false)),
            ("document switched away and back cancels", () => Switch(true)),
            ("document changed during modifier release cancels", Modifier),
            ("dispose prevents queued execution", Disposed),
            ("missing custom never executes a native command", Missing),
        };
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); return 1; }
        }
        Console.WriteLine($"{cases.Length} regression scenarios passed; {Probe.Accesses} CAD API accesses checked.");
        return 0;
    }

    private static (CadCommandExecutor Executor, Document Doc, PluginConfig Config) Setup()
    {
        AcApp.Manager = new(); AcApp.Names = ""; AcApp.Active = 0;
        NativeMethods.ModifierDown = false; Logger.Errors.Clear();
        CadCommandCatalog.Defined = true;
        var doc = new Document(); AcApp.Manager.Activate(doc);
        doc.Editor.Selected = [new ObjectId(101), new ObjectId(202)];
        var config = new PluginConfig();
        return (new CadCommandExecutor(new CadCommandCatalog(), () => config), doc, config);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds = 150)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(2); }
    }
    private static void NoErrors() => Check(Logger.Errors.Count == 0, string.Join("; ", Logger.Errors.Select(e => e.ToString())));
    private static void BackgroundQueue(CadCommandExecutor executor, MenuItemConfig item) => Task.Run(() => executor.Queue(item)).GetAwaiter().GetResult();
    private static bool Original(ObjectId[] ids) => ids.SequenceEqual(new[] { new ObjectId(101), new ObjectId(202) });
    private static void Native()
    {
        var (executor, doc, _) = Setup(); using (executor)
        {
            executor.BeginWheel(); BackgroundQueue(executor, new()); Pump();
            Check(doc.Sent.Count == 1 && doc.Sent[0].Line == "ERASE ", "native command queue mismatch");
            Check(Original(doc.Sent[0].Selection), "native command lost preselection");
            Check(AcApp.Manager.Contexts == 0 && doc.Editor.Restores == 0, "native command changed context or selection"); NoErrors();
        }
    }
    private static void Custom(bool defined)
    {
        var (executor, doc, config) = Setup(); using (executor)
        {
            CadCommandCatalog.Defined = defined;
            config.CustomCommands.Add(new()); BackgroundQueue(executor, new() { Kind = ActionKinds.CustomCommand, Value = "custom" }); Pump();
            Check(AcApp.Manager.Contexts == 0 && doc.Editor.Restores == 0 && doc.Editor.Commands.Count == 0, "custom entered command context or changed selection");
            Check(doc.Sent.Count == 1 && Original(doc.Sent[0].Selection), "custom lost selected objects");
            Check(doc.Sent[0].Line == "MOVE 0,0 1,1 ", "custom arguments changed or disappeared"); NoErrors();
        }
    }
    private static void Active()
    {
        var (executor, doc, _) = Setup(); using (executor)
        {
            AcApp.Names = "LINE"; AcApp.Active = 1; BackgroundQueue(executor, new()); Pump();
            Check(doc.Sent.Count == 0 && doc.Editor.Commands.Count == 0 && Original(doc.Editor.Selected), "active command was interrupted"); NoErrors();
        }
    }
    private static void Switch(bool returnToOriginal)
    {
        var (executor, doc, _) = Setup(); using (executor)
        {
            executor.BeginWheel(); var other = new Document(); AcApp.Manager.Activate(other);
            if (returnToOriginal) AcApp.Manager.Activate(doc);
            BackgroundQueue(executor, new()); Pump();
            Check(doc.Sent.Count == 0 && other.Sent.Count == 0, "command crossed wheel document boundary"); NoErrors();
        }
    }
    private static void Modifier()
    {
        var (executor, doc, _) = Setup(); using (executor)
        {
            NativeMethods.ModifierDown = true; executor.BeginWheel(); BackgroundQueue(executor, new()); Pump(80);
            Check(doc.Sent.Count == 0, "submitted with modifier held");
            var other = new Document(); AcApp.Manager.Activate(other); NativeMethods.ModifierDown = false; Pump(250);
            Check(doc.Sent.Count == 0 && other.Sent.Count == 0, "modifier wait crossed document boundary"); NoErrors();
        }
    }
    private static void Disposed()
    {
        var (executor, doc, _) = Setup(); BackgroundQueue(executor, new()); executor.Dispose(); Pump();
        BackgroundQueue(executor, new()); Pump();
        Check(doc.Sent.Count == 0, "disposed executor submitted command"); NoErrors();
    }
    private static void Missing()
    {
        var (executor, doc, _) = Setup(); using (executor)
        {
            BackgroundQueue(executor, new() { Kind = ActionKinds.CustomCommand, Value = "deleted" }); Pump();
            Check(doc.Sent.Count == 0 && doc.Editor.Commands.Count == 0, "missing custom executed");
            Check(Logger.Errors.Count == 1 && Logger.Errors[0].Message.Contains("不存在"), "missing custom did not report expected error");
        }
    }
    private static void ScriptedArguments()
    {
        var (executor, doc, config) = Setup(); using (executor)
        {
            config.CustomCommands.Add(new() { CommandName = "PLUGINARGS", Arguments = ["two words", "<ENTER>", "tail"] });
            BackgroundQueue(executor, new() { Kind = ActionKinds.CustomCommand, Value = "custom" }); Pump();
            Check(doc.Sent.Count == 1 && doc.Sent[0].Line == "PLUGINARGS \"two words\"  tail ", "space quoting or ENTER token incorrect");
            Check(AcApp.Manager.Contexts == 0 && doc.Editor.Commands.Count == 0 && Original(doc.Sent[0].Selection), "scripted command lost native selection path"); NoErrors();
        }
    }
    private static void RejectPause()
    {
        var (executor, doc, config) = Setup(); using (executor)
        {
            config.CustomCommands.Add(new() { Arguments = ["0,0", "<PAUSE>"] });
            BackgroundQueue(executor, new() { Kind = ActionKinds.CustomCommand, Value = "custom" }); Pump();
            Check(doc.Sent.Count == 0 && doc.Editor.Commands.Count == 0 && AcApp.Manager.Contexts == 0, "PAUSE submitted a partial command");
            Check(Logger.Errors.Count == 1 && Logger.Errors[0].Message.Contains("<PAUSE>"), "PAUSE rejection not reported");
            Check(Original(doc.Editor.Selected), "PAUSE rejection changed selection");
        }
    }
}
