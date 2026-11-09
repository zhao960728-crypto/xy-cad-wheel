using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using XiaoYang.AutoCADPlugin;
using WheelCommandRegression;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

internal static class ExtensionSelectionProgram
{
    private static int Main()
    {
        Probe.Owner = Environment.CurrentManagedThreadId;
        var folder = Path.Combine(Path.GetTempPath(), "XY-extension-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "fixture.lsp"); File.WriteAllText(path, "; not executed by this offline test");
        try
        {
            Check(typeof(ExtensionLoadService).GetMethod("StartExtensionCommand").GetCustomAttribute<CommandMethodAttribute>().Flags.HasFlag(CommandFlags.Redraw), "entry must preserve PickFirst via Redraw");
            Run(path, "loaded extension retains selection", (service, doc, entry) =>
            {
                MarkLoaded(service, doc, path); Select(doc, 101); Start(doc, entry);
                Check(doc.Sent.Count == 1 && doc.Sent[0].Line == "PLUGINRUN " && doc.Sent[0].Selection.Single().Value == 101, "loaded startup did not retain selection");
            });
            Run(path, "first load does not restore consumed selection", (service, doc, entry) =>
            {
                Select(doc, 101); Start(doc, entry); Select(doc); Complete(service);
                Check(doc.Sent.Count == 1 && doc.Editor.Selected.Length == 0 && doc.Editor.Restores == 0, "first load executed or restored stale selection");
                Check(ExtensionLoadService.GetStatus(entry.Id).Contains("重新选择"), "first load missing actionable status");
                Select(doc, 202); Start(doc, entry);
                Check(doc.Sent.Count == 2 && doc.Sent[1].Line == "PLUGINRUN " && doc.Sent[1].Selection.Single().Value == 202, "second invocation did not use current selection");
            });
            Run(path, "first load preserves user's new selection", (service, doc, entry) =>
            {
                Select(doc, 101); Start(doc, entry); Select(doc, 303); Complete(service);
                Check(doc.Sent.Count == 1 && doc.Editor.Selected.Single().Value == 303 && doc.Editor.Restores == 0, "new user selection was modified or used unexpectedly");
            });
            Run(path, "switch away and back cancels startup", (service, doc, entry) =>
            {
                Start(doc, entry); AcApp.Manager.Activate(new()); AcApp.Manager.Activate(doc); Complete(service);
                Check(doc.Sent.Count == 1 && ExtensionLoadService.GetStatus(entry.Id).Contains("取消"), "document round trip did not cancel");
            });
            Run(path, "first load without selection starts normally", (service, doc, entry) =>
            {
                Start(doc, entry); Complete(service);
                Check(doc.Sent.Count == 2 && doc.Sent[1].Line == "PLUGINRUN ", "normal first-load startup regressed");
            });
            Run(path, "dispose cancels pending load and releases document event", (service, doc, entry) =>
            {
                Start(doc, entry); var token = Token(service); service.Dispose();
                Check(AcApp.Manager.ActivationListeners == 0, "document event leaked");
                ExtensionLoadService.Complete(new(new(token), new("")));
                Check(doc.Sent.Count == 1, "disposed service launched startup");
            });
            Console.WriteLine("6 extension startup selection scenarios passed.");
            return 0;
        }
        catch (System.Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
    private static void Run(string path, string name, Action<ExtensionLoadService, Document, ManagedExtensionConfig> body)
    {
        AcApp.Manager = new(); var doc = new Document(); AcApp.Manager.Activate(doc);
        var entry = new ManagedExtensionConfig { Id = Guid.NewGuid().ToString("N"), Name = "Fixture", FilePath = path, StartupCommand = "PLUGINRUN", Enabled = true };
        var config = new PluginConfig(); config.ManagedExtensions.Add(entry);
        using var service = new ExtensionLoadService(); service.Start(() => config);
        body(service, doc, entry); Console.WriteLine("PASS " + name);
    }
    private static void Select(Document doc, params int[] ids) => doc.Editor.Selected = ids.Select(id => new ObjectId(id)).ToArray();
    private static void Start(Document doc, ManagedExtensionConfig entry) { doc.Editor.Input = entry.Id; ExtensionLoadService.StartExtensionCommand(); }
    private static string Token(ExtensionLoadService service)
    {
        var request = typeof(ExtensionLoadService).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
        return (string)request.GetType().GetField("Token").GetValue(request);
    }
    private static void Complete(ExtensionLoadService service) => ExtensionLoadService.Complete(new(new(Token(service)), new("")));
    private static void MarkLoaded(ExtensionLoadService service, Document doc, string path)
    {
        var loaded = (Dictionary<Document, HashSet<string>>)typeof(ExtensionLoadService).GetField("_documentLoaded", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
        loaded.Add(doc, new(StringComparer.OrdinalIgnoreCase) { path });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
