using System.Runtime.InteropServices;
using System.IO;
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#endif

namespace XiaoYang.AutoCADPlugin;

internal static class CadMenuBar
{
    private const string MenuGroupName = "XYCADMENU";
    private const string MenuName = "小阳CAD轮盘";
    private static object _menu;
    private static bool _reportedVisible;

    public static bool Ensure(bool force = false)
    {
        try
        {
            if (!force && !IsHostCommandIdle()) return IsVisible();
            dynamic application = CadHostInfo.AutomationApplication;
            dynamic groups = application.MenuGroups;
            if (groups.Count == 0) return false;

            var menuSourceChanged = PluginPaths.EnsureVersionMenuFiles();
            dynamic group = FindGroup(groups);
            if (force && group != null)
            {
                TryUnloadGroup(group);
                group = null;
                _menu = null;
                _reportedVisible = false;
                menuSourceChanged = true;
            }

            if (group != null && FindMenu(group.Menus) == null)
            {
                Logger.Error("发现同名 XYCADMENU 菜单组，但其中不包含小阳菜单；为保护其他 CUI，已停止自动处理。 ");
                return false;
            }

            if (group == null)
            {
                var menuFile = !menuSourceChanged && File.Exists(PluginPaths.VersionMenuCompiledPath)
                    ? PluginPaths.VersionMenuCompiledPath
                    : PluginPaths.VersionMenuSourcePath;
                group = groups.Load(menuFile);
                Logger.Info($"已加载独立顶部菜单组：{menuFile}");
            }

            dynamic menu = FindMenu(group.Menus);
            if (menu == null) return false;
            _menu = menu;
            if (!menu.OnMenuBar)
                menu.InsertInMenuBar(Math.Max(0, application.MenuBar.Count - 1));

            var visible = menu.OnMenuBar && EnsureHostMenuBarVisible();
            if (visible && !_reportedVisible)
            {
                Logger.Info($"{CadHostInfo.ProductName} 顶部“小阳CAD轮盘”独立菜单已显示。");
                _reportedVisible = true;
            }
            return visible;
        }
        catch (COMException ex) when (ex.ErrorCode == unchecked((int)0x80210045)
                                          || ex.ErrorCode == unchecked((int)0x80210015))
        {
            Logger.Trace($"{CadHostInfo.ProductName} 顶部菜单尚未就绪，等待下次空闲重试。");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"注册 {CadHostInfo.ProductName} 顶部“小阳CAD轮盘”独立菜单失败。", ex);
            return false;
        }
    }

    public static bool IsVisible()
    {
        try
        {
            dynamic application = CadHostInfo.AutomationApplication;
            dynamic group = FindGroup(application.MenuGroups);
            if (group == null) return false;
            dynamic menu = FindMenu(group.Menus);
#if ZWCAD
            return menu != null && menu.OnMenuBar;
#else
            // 只确认本插件菜单已注册，不改写也不要求用户的 MENUBAR 值。
            return menu != null && menu.OnMenuBar;
#endif
        }
        catch
        {
            return false;
        }
    }

    public static void Remove()
    {
        // The partial menu is intentionally left registered for the next launch.
        // Releasing COM references here avoids shutdown crashes in CoreCLR hosts.
        _menu = null;
        _reportedVisible = false;
    }

    private static dynamic FindGroup(dynamic groups)
    {
        for (var index = 0; index < groups.Count; index++)
        {
            dynamic group = groups.Item(index);
            if (string.Equals((string)group.Name, MenuGroupName, StringComparison.OrdinalIgnoreCase))
                return group;
        }
        return null;
    }

    private static void TryUnloadGroup(dynamic group)
    {
        try
        {
            if (group == null) return;
            var name = Convert.ToString(group.Name) ?? "";
            if (!string.Equals(name, MenuGroupName, StringComparison.OrdinalIgnoreCase)) return;
            group.Unload();
            Logger.Info("已卸载本插件 XYCADMENU 菜单组，准备重新加载。");
        }
        catch (Exception ex)
        {
            Logger.Trace("卸载本插件 XYCADMENU 菜单组失败，将直接尝试重新加载：" + ex.Message);
        }
    }

    private static dynamic FindMenu(dynamic menus)
    {
        for (var index = 0; index < menus.Count; index++)
        {
            dynamic menu = menus.Item(index);
            if (string.Equals(NormalizeMenuLabel((string)menu.NameNoMnemonic), MenuName,
                    StringComparison.OrdinalIgnoreCase))
                return menu;
        }
        return null;
    }

    private static bool EnsureHostMenuBarVisible()
    {
        // 菜单栏是否显示由用户的 MENUBAR/工作空间设置决定；插件只注册自己的菜单，
        // 不为显示菜单而改写 CAD 系统变量。
        return true;
    }

    internal static string BuildCommandMacro(string command) => command + " ";

    private static bool IsHostCommandIdle()
    {
        try
        {
            var names = Convert.ToString(CadHostInfo.GetSystemVariable("CMDNAMES"))?.Trim() ?? "";
            return names.Length == 0;
        }
        catch { return false; }
    }

    internal static bool ValidateCommandMacros()
    {
        if (_menu == null) return false;
        dynamic menu = _menu;
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["打开轮盘"] = "XYLP",
            ["轮盘设置..."] = "XYLPSZ",
            ["输入法设置..."] = "XYLPIME",
            ["刷新命令库"] = "XYREFRESHCMDS",
            ["本地命令恢复"] = "XYRESTORELOCALCMDS",
            ["修复顶部菜单"] = "XYLPMENU",
            ["运行自检"] = "XYLPZJ"
        };
#if ZWCAD
        // ZWCAD 2020 COM 的 PopupMenu.Count 返回最后一个索引，而不是项目数。
        var lastIndex = Convert.ToInt32(menu.Count);
#else
        var lastIndex = Convert.ToInt32(menu.Count) - 1;
#endif
        for (var index = 0; index <= lastIndex; index++)
        {
            try
            {
                dynamic item = menu.Item(index);
                var label = NormalizeMenuLabel((string)item.Label);
                if (!expected.TryGetValue(label, out var command)) continue;
                var macro = (string)item.Macro;
                if (MacroInvokesCommand(macro, command))
                    expected.Remove(label);
            }
            catch { }
        }
        return expected.Count == 0;
    }

    internal static string NormalizeMenuLabel(string label)
    {
        var normalized = (label ?? string.Empty).Replace("&", string.Empty).Trim();
        if (normalized.Length >= 3 && normalized.EndsWith(")", StringComparison.Ordinal))
        {
            var open = normalized.LastIndexOf('(');
            if (open >= 0 && normalized.Length - open == 3
                          && char.IsLetterOrDigit(normalized[open + 1]))
                normalized = normalized.Substring(0, open).TrimEnd();
        }
        return normalized;
    }

    private static bool MacroInvokesCommand(string macro, string command)
    {
        // AutoCAD COM typically exposes ^C as control character 0x03, while
        // ZWCAD 2020 exposes the same menu macro as literal text "^C^C".
        // Compare the effective command instead of the host-specific encoding.
        var normalized = (macro ?? string.Empty).Replace("_", string.Empty).Trim();
        while (normalized.StartsWith("^C", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(2).TrimStart();
        normalized = normalized.TrimStart('\x03').TrimStart();
        return string.Equals(normalized, command, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase);
    }
}
