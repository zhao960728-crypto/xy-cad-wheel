namespace XiaoYang.AutoCADPlugin;

/// <summary>
/// Single application-facing contract for the plugin's lifecycle and user
/// operations. Host-specific command declarations stay in <see cref="CadPlugin"/>.
/// </summary>
internal interface ICadPluginApplication
{
    void Initialize();
    void Terminate();
    void ShowWheel();
    void ShowSettings();
    void ShowImeSettings();
    void ToggleImeSwitch();
    void RefreshCommands();
    void RestoreLocalCommands();
    void RepairMenu();
    void ScheduleStartupMenuRepair();
    void RunSelfTest();
}
