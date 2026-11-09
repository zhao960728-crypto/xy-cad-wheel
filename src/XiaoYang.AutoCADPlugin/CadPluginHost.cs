#if ZWCAD
using ZwSoft.ZwCAD.Runtime;
#else
using Autodesk.AutoCAD.Runtime;
#endif

namespace XiaoYang.AutoCADPlugin;

/// <summary>
/// AutoCAD registration adapter. It intentionally contains no feature logic;
/// all lifecycle and command operations are centralized in CadPluginApplication.
/// </summary>
public sealed class CadPlugin : IExtensionApplication
{
    private static readonly ICadPluginApplication Application = new CadPluginApplication();

    public void Initialize() => Application.Initialize();

    public void Terminate() => Application.Terminate();

    [CommandMethod("XYLP", CommandFlags.Session | CommandFlags.NoHistory | CommandFlags.Redraw)]
    [CommandMethod("XYMENU", CommandFlags.Session | CommandFlags.NoHistory | CommandFlags.Redraw)]
    public void ShowWheelCommand() => Application.ShowWheel();

    [CommandMethod("XYLPSZ", CommandFlags.Session | CommandFlags.NoHistory)]
    [CommandMethod("XYSETTINGS", CommandFlags.Session | CommandFlags.NoHistory)]
    public void ShowSettingsCommand() => Application.ShowSettings();

    [CommandMethod("XYLPIME", CommandFlags.Session | CommandFlags.NoHistory)]
    public void ShowImeSettingsCommand() => Application.ShowImeSettings();

    [CommandMethod("XYLPIMETOGGLE", CommandFlags.Session | CommandFlags.NoHistory)]
    public void ToggleImeSwitchCommand() => Application.ToggleImeSwitch();

    [CommandMethod("XYREFRESHCMDS", CommandFlags.Session | CommandFlags.NoHistory)]
    public void RefreshCommandsCommand() => Application.RefreshCommands();

    [CommandMethod("XYRESTORELOCALCMDS", CommandFlags.Session | CommandFlags.NoHistory)]
    public void RestoreLocalCommandsCommand() => Application.RestoreLocalCommands();

    [CommandMethod("XYLPMENU", CommandFlags.Session | CommandFlags.NoHistory)]
    public void RepairMenuCommand() => Application.RepairMenu();

    [CommandMethod("XYLPSTART", CommandFlags.Session | CommandFlags.NoHistory)]
    public void StartupCommand() => Application.ScheduleStartupMenuRepair();

    [CommandMethod("XYLPZJ", CommandFlags.NoHistory)]
    [CommandMethod("XYSELFTEST", CommandFlags.NoHistory)]
    public void SelfTestCommand() => Application.RunSelfTest();

}
