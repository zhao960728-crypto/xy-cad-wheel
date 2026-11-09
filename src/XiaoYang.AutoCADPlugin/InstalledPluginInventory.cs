using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

namespace XiaoYang.AutoCADPlugin;

internal sealed class InstalledPluginInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    internal string IdentityPath { get; set; }
    public string Source { get; set; }
    public string Status { get; set; }
    public string ProductCode { get; set; }
    public string UninstallCommand { get; set; }
}

/// <summary>Read-only discovery; removal is delegated only to an exactly matched registered installer.</summary>
internal static class InstalledPluginInventory
{
    public static List<InstalledPluginInfo> Read(IEnumerable<string> managedPaths, out string diagnostic)
    {
        var entries = new List<InstalledPluginInfo>();
        var warnings = new List<string>();
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Identity(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (!identities.TryGetValue(path, out var resolved)) identities[path] = resolved = ResolveIdentityPath(path);
            return resolved;
        }
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cadAssemblies = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            try
            {
                if (string.IsNullOrEmpty(assembly.Location)) continue;
                var location = Identity(assembly.Location);
                loaded.Add(location);
                if (assembly.GetReferencedAssemblies().Any(a => string.Equals(a.Name, "AcMgd", StringComparison.OrdinalIgnoreCase) || string.Equals(a.Name, "AcCoreMgd", StringComparison.OrdinalIgnoreCase) || string.Equals(a.Name, "ZwManaged", StringComparison.OrdinalIgnoreCase)))
                    cadAssemblies.Add(location);
            }
            catch (NotSupportedException) { }
        }
        try
        {
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules) loaded.Add(Identity(module.FileName));
        }
        catch (Exception ex) { warnings.Add("部分本机模块无法读取：" + ex.Message); }
        var roots = new[]
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "ApplicationPlugins"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "ApplicationPlugins")
        };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var bundle in Directory.GetDirectories(root, "*.bundle", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var bundleIdentity = Identity(bundle);
                        if (entries.Any(e => string.Equals(e.IdentityPath, bundleIdentity, StringComparison.OrdinalIgnoreCase))) continue;
                        var manifest = System.IO.Path.Combine(bundle, "PackageContents.xml");
                        if (!File.Exists(manifest)) continue;
                        var xml = XDocument.Load(manifest);
                        var package = xml.Root;
                        if (package == null) continue;
                        var modules = package.Descendants().Where(e => e.Name.LocalName == "ComponentEntry")
                            .Select(e => (string)e.Attribute("ModuleName")).Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => Identity(System.IO.Path.GetFullPath(System.IO.Path.Combine(bundle, s.Replace('/', System.IO.Path.DirectorySeparatorChar))))).ToList();
                        var productCode = (string)package.Attribute("ProductCode");
                        var own = bundle.IndexOf("XY-CAD-H", StringComparison.OrdinalIgnoreCase) >= 0 || modules.Any(p => p.IndexOf("MrZ.XiaoYangRadialMenu", StringComparison.OrdinalIgnoreCase) >= 0);
                        entries.Add(new InstalledPluginInfo
                        {
                            Name = (string)package.Attribute("Name") ?? System.IO.Path.GetFileName(bundle), Path = bundle, IdentityPath = bundleIdentity,
                            Source = own ? "轮盘本体" : "ApplicationPlugins 包", ProductCode = productCode,
                            Status = modules.Any(loaded.Contains) ? "当前 CAD 已加载模块" : "未检测到已加载模块（脚本状态未知）",
                            UninstallCommand = own ? null : FindUninstaller(productCode)
                        });
                    }
                    catch (Exception ex) { warnings.Add(System.IO.Path.GetFileName(bundle) + "：" + ex.Message); }
                }
            }
            catch (Exception ex) { warnings.Add(root + "：" + ex.Message); }
        }
        ReadDemandLoad(entries, loaded, warnings, Identity);
        foreach (var path in (managedPaths ?? Enumerable.Empty<string>()).Concat(cadAssemblies).Concat(loaded.Where(p => string.Equals(System.IO.Path.GetExtension(p), ".arx", StringComparison.OrdinalIgnoreCase) || string.Equals(System.IO.Path.GetExtension(p), ".zrx", StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var identity = Identity(path);
            if (entries.Any(e => string.Equals(identity, e.IdentityPath, StringComparison.OrdinalIgnoreCase) || IsWithin(identity, e.IdentityPath))) continue;
            entries.Add(new InstalledPluginInfo { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path, IdentityPath = identity, Source = "扩展文件 / 当前 CAD 模块", Status = loaded.Contains(identity) ? "当前 CAD 已加载" : "未检测到已加载模块（脚本状态未知）" });
        }
        diagnostic = warnings.Count == 0 ? "读取完成。覆盖标准插件包、注册加载项、轮盘登记的扩展及当前 CAD 的 .NET / ARX / ZRX 模块；其他加载方式可能无法识别。" : "部分项目读取失败：" + string.Join("；", warnings);
        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadDemandLoad(List<InstalledPluginInfo> entries, HashSet<string> loaded, List<string> warnings, Func<string, string> identity)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var autocad = root.OpenSubKey(@"Software\Autodesk\AutoCAD");
                if (autocad == null) continue;
                foreach (var release in autocad.GetSubKeyNames())
                {
                    using var releaseKey = autocad.OpenSubKey(release);
                    if (releaseKey == null) continue;
                    foreach (var product in releaseKey.GetSubKeyNames())
                    {
                        using var applications = releaseKey.OpenSubKey(product + @"\Applications");
                        if (applications == null) continue;
                        foreach (var name in applications.GetSubKeyNames())
                        {
                            using var app = applications.OpenSubKey(name);
                            var loader = app?.GetValue("LOADER") as string;
                            if (string.IsNullOrWhiteSpace(loader)) continue;
                            loader = Environment.ExpandEnvironmentVariables(loader.Trim().Trim('"'));
                            var loaderIdentity = identity(loader);
                            if (entries.Any(e => string.Equals(e.IdentityPath, loaderIdentity, StringComparison.OrdinalIgnoreCase) || IsWithin(loaderIdentity, e.IdentityPath))) continue;
                            entries.Add(new InstalledPluginInfo
                            {
                                Name = app.GetValue("DESCRIPTION") as string ?? name,
                                Path = loader, IdentityPath = loaderIdentity,
                                Source = "注册加载项 · " + release + " / " + product,
                                Status = loaded.Contains(loaderIdentity) ? "当前 CAD 已加载" : "已登记，当前加载未确认（可能属于其他 CAD 版本）"
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { warnings.Add("部分注册加载项无法读取：" + ex.Message); }
        }
    }
    // Resolve junctions and short paths with a metadata-only handle. Never load or modify the file.
    internal static string ResolveIdentityPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        string fullPath;
        try { fullPath = System.IO.Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException || ex is PathTooLongException) { return path; }
        using var handle = InventoryCreateFile(fullPath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) return fullPath;
        var buffer = new StringBuilder(512);
        var length = InventoryGetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) return fullPath;
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = InventoryGetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity) return fullPath;
        }
        var result = buffer.ToString();
        if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + result.Substring(8);
        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result.Substring(4) : result;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle InventoryCreateFile(string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint InventoryGetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    private static bool IsWithin(string file, string directory) => file.StartsWith(directory.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string FindUninstaller(string productCode)
    {
        if (!Guid.TryParse(productCode, out var code)) return null;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + code.ToString("B"));
                var command = key?.GetValue("UninstallString") as string;
                if (TryParseUninstaller(command, out _, out _)) return command;
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    internal static bool TryParseUninstaller(string command, out string executable, out string arguments)
    {
        executable = null; arguments = null;
        if (string.IsNullOrWhiteSpace(command)) return false;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) return false;
            executable = command.Substring(1, end - 1); arguments = command.Substring(end + 1).Trim();
        }
        else
        {
            var end = command.IndexOf(' ');
            executable = end < 0 ? command : command.Substring(0, end);
            arguments = end < 0 ? "" : command.Substring(end + 1).Trim();
        }
        if (string.Equals(executable, "msiexec.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(executable, "msiexec", StringComparison.OrdinalIgnoreCase)) executable = System.IO.Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        if (!System.IO.Path.IsPathRooted(executable) || !File.Exists(executable) || !string.Equals(System.IO.Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var name = System.IO.Path.GetFileNameWithoutExtension(executable);
        return !new[] { "cmd", "powershell", "pwsh", "wscript", "cscript", "rundll32", "regsvr32" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public static void StartUninstall(InstalledPluginInfo entry)
    {
        var current = FindUninstaller(entry.ProductCode);
        if (string.IsNullOrWhiteSpace(current) || !string.Equals(current, entry.UninstallCommand, StringComparison.Ordinal) || !TryParseUninstaller(current, out var executable, out var arguments))
            throw new InvalidOperationException("卸载登记已改变或不可用，请刷新后重试。");
        if (string.Equals(System.IO.Path.GetFileName(executable), "msiexec.exe", StringComparison.OrdinalIgnoreCase))
            arguments = "/x " + Guid.Parse(entry.ProductCode).ToString("B");
        Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true });
    }
}




