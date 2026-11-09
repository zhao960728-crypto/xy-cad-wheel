using System.IO;
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#else
#if AUTOCAD_PRE2013
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif
#endif

namespace XiaoYang.AutoCADPlugin;

internal static class CadHostInfo
{
    public static string ProductYear
    {
        get
        {
#if ZWCAD
            try
            {
                var major = AcApp.Version.Major;
                if (major is >= 10 and <= 99) return (2000 + major).ToString();
            }
            catch { }
            try
            {
                var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(RuntimeCompat.ProcessPath);
                if (version.ProductMajorPart is >= 10 and <= 99)
                    return (2000 + version.ProductMajorPart).ToString();
            }
            catch { }
            return "2020+";
#else
            try
            {
                var version = Convert.ToString(AcApp.GetSystemVariable("ACADVER")) ?? "";
                if (version.StartsWith("18.0", StringComparison.OrdinalIgnoreCase)) return "2010";
                if (version.StartsWith("18.1", StringComparison.OrdinalIgnoreCase)) return "2011";
                if (version.StartsWith("18.2", StringComparison.OrdinalIgnoreCase)) return "2012";
                if (version.StartsWith("19.0", StringComparison.OrdinalIgnoreCase)) return "2013";
                if (version.StartsWith("19.1", StringComparison.OrdinalIgnoreCase)) return "2014";
                if (version.StartsWith("20.0", StringComparison.OrdinalIgnoreCase)) return "2015";
                if (version.StartsWith("20.1", StringComparison.OrdinalIgnoreCase)) return "2016";
                if (version.StartsWith("21.0", StringComparison.OrdinalIgnoreCase)) return "2017";
                if (version.StartsWith("22.0", StringComparison.OrdinalIgnoreCase)) return "2018";
                if (version.StartsWith("23.0", StringComparison.OrdinalIgnoreCase)) return "2019";
                if (version.StartsWith("23.1", StringComparison.OrdinalIgnoreCase)) return "2020";
                if (version.StartsWith("24.0", StringComparison.OrdinalIgnoreCase)) return "2021";
                if (version.StartsWith("24.1", StringComparison.OrdinalIgnoreCase)) return "2022";
                if (version.StartsWith("24.2", StringComparison.OrdinalIgnoreCase)) return "2023";
                if (version.StartsWith("24.3", StringComparison.OrdinalIgnoreCase)) return "2024";
                if (version.StartsWith("25.0", StringComparison.OrdinalIgnoreCase)) return "2025";
                if (version.StartsWith("25.1", StringComparison.OrdinalIgnoreCase)) return "2026";
                if (version.StartsWith("26.0", StringComparison.OrdinalIgnoreCase)) return "2027";
            }
            catch { }
#if AUTOCAD_PRE2013
            return typeof(CadHostInfo).Assembly.GetName().Name.Substring("MrZ.XiaoYangRadialMenu.AutoCAD".Length);
#elif NETFRAMEWORK
            return "2014-2024";
#elif NET10_0_OR_GREATER
            return "2027";
#else
            return "2026";
#endif
#endif
        }
    }

    public static string DisplayName => ProductName + " " + ProductYear;

#if ZWCAD
    public const string ProductName = "中望CAD";
    public const string PluginDisplayName = "小阳CAD轮盘插件（中望版）";
    public const string HostKey = "ZWCAD";
    public const string HostExecutableName = "zwcad";
    public const string ConfigFilePrefix = "zwcad";
    public const string PgpFileName = "ZWCAD.pgp";
    public static object AutomationApplication => AcApp.ZcadApplication;
#else
    public const string ProductName = "AutoCAD";
    public const string PluginDisplayName = "小阳CAD轮盘插件";
    public const string HostKey = "AutoCAD";
    public const string HostExecutableName = "acad";
    public const string ConfigFilePrefix = "autocad";
    public const string PgpFileName = "acad.pgp";
    public static object AutomationApplication => Autodesk.AutoCAD.ApplicationServices.Application.AcadApplication;
#endif

    public static bool IsFullHost =>
        Path.GetFileNameWithoutExtension(RuntimeCompat.ProcessPath)
            .Equals(HostExecutableName, StringComparison.OrdinalIgnoreCase);

    public static object GetSystemVariable(string name) => AcApp.GetSystemVariable(name);

    public static void SetSystemVariable(string name, object value) => AcApp.SetSystemVariable(name, value);

    public static IReadOnlyList<string> GetSupportSearchPaths()
    {
        var result = new List<string>();
#if ZWCAD
        var variables = new[] { "ZWCADPREFIX", "ZWCADSEARCHPATH", "ACADPREFIX" };
#else
        var variables = new[] { "ACADPREFIX" };
#endif
        foreach (var variable in variables)
        {
            try
            {
                var value = Convert.ToString(GetSystemVariable(variable)) ?? "";
                result.AddRange(value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => path.Length > 0));
            }
            catch { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
