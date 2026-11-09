using System.Reflection;
using System.IO;
using XiaoYang.AutoCADPlugin;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static string Validate(string package, UpdateProductProfile profile, string version)
    {
        using var service = new PluginUpdateService(profile);
        try { return (string)typeof(PluginUpdateService).GetMethod("ValidatePackage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(service, new object[] { package, new UpdateManifest { Version = version } }); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
    private static int Main(string[] args)
    {
        try
        {
            var repo = Path.GetFullPath(args[0]);
            var run = Path.Combine(repo, "work", "installer-choice-20261020-84", "update-validation");
            Directory.CreateDirectory(run);
            var package = Path.Combine(repo, "dist", "小阳CAD轮盘插件-2.2.0.84");
            Check(Validate(package, UpdateProductProfile.AutoCAD, "2.2.0.84") == package, "Complete AutoCAD 84 package was rejected.");
            Check(Validate(Path.Combine(repo, "dist", "小阳ZWCAD轮盘插件-2.2.0.84"), UpdateProductProfile.ZwCAD, "2.2.0.84") != null, "ZWCAD package regressed.");
            Check(Validate(Path.Combine(repo, "dist", "小阳CAD轮盘插件-2.2.0.83"), UpdateProductProfile.AutoCAD, "2.2.0.83") != null, "Prior package compatibility regressed.");
            foreach (var name in new[] { "Theraot.Core.dll", "XYCAD.LegacyUpdater.exe", "XYCAD.LegacyUpdater.exe.config", "MrZ.XiaoYangRadialMenu.AutoCAD2010.dll", "MrZ.XiaoYangRadialMenu.AutoCAD2011.dll", "MrZ.XiaoYangRadialMenu.AutoCAD2012.dll" })
            {
                var copy = Path.Combine(run, Guid.NewGuid().ToString("N")); CopyTree(package, copy);
                File.Delete(Path.Combine(copy, "小阳CAD轮盘插件.bundle", "Contents", "NetFramework35", name));
                var rejected = false;
                try { Validate(copy, UpdateProductProfile.AutoCAD, "2.2.0.84"); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Missing legacy component was accepted: " + name);
            }
            var wrong = Path.Combine(run, Guid.NewGuid().ToString("N")); CopyTree(package, wrong);
            var manifest = Path.Combine(wrong, "小阳CAD轮盘插件.bundle", "PackageContents.xml");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("NetFramework35/MrZ.XiaoYangRadialMenu.AutoCAD2010.dll", "NetFramework48/MrZ.XiaoYangRadialMenu.AutoCAD2010.dll"));
            var blocked = false;
            try { Validate(wrong, UpdateProductProfile.AutoCAD, "2.2.0.84"); } catch (InvalidDataException) { blocked = true; }
            Check(blocked, "Manifest pointing to the wrong CLR was accepted.");
            Console.WriteLine("PASS " + checks + " package/update checks; " + run); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
