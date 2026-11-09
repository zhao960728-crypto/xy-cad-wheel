using System;

namespace XiaoYang.AutoCADPlugin;

internal sealed class ManagedExtensionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    // Adding a file only registers it. Running its code requires an explicit check.
    public bool Enabled { get; set; }
    // Empty means a load-only entry. A real command may be discovered or supplied by the user.
    public string StartupCommand { get; set; } = "";
}

