using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class CellCommandEditorRegression
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool, string> check)
    {
        var config = new PluginConfig();
        ConfigStore.Normalize(config);
        config.CustomCommands.Add(new CustomCommandConfig { Id = "rename-fixture", Name = "原始展开功能", DisplayNameOverride = "我的展开",
            CommandName = "XYCUT-UNFOLD", Arguments = new() { "keep-argument" }, Category = "测试功能" });
        var wheel = config.Menus[SceneKeys.Global];
        wheel.Rings[0].Slots = 4;
        wheel.Rings[0].Items = new() {
            new MenuItemConfig { Slot = 0, Kind = ActionKinds.CadCommand, Value = "LINE", Label = "原始直线", FontSizePx = 19 },
            new MenuItemConfig { Slot = 1, Kind = ActionKinds.CustomCommand, Value = "rename-fixture", Label = "我的展开" },
            new MenuItemConfig { Slot = 2, Kind = ActionKinds.CadCommand, Value = "_LINE", Label = "原始直线" } };
        wheel.UnassignedItems.Add(new MenuItemConfig { Kind = ActionKinds.CustomCommand, Value = "rename-fixture", Label = "我的展开" });
        var original = JsonCompat.Serialize(config);
        var saves = 0;
        using var settings = new SettingsWindow(config, new CadCommandCatalog(), _ => saves++, loadCatalog: false);
        var native = new CadCommandInfo { GlobalName = "LINE", DisplayName = "原始直线", Aliases = new[] { "TEST-L-ALIAS" }, IsCoreCommand = true };
        typeof(SettingsWindow).GetField("_allCommands", Flags).SetValue(settings, new[] { native });
        var draft = Field<PluginConfig>(settings, "_config");
        var search = Field<TextBox>(settings, "_cellCommandSearch");
        var matches = Field<ListBox>(settings, "_cellCommandMatches");
        var display = Field<TextBox>(settings, "_cellDisplayName");
        var target = typeof(SettingsWindow).GetField("_catalogTarget", Flags).GetValue(settings);
        ((NumericUpDown)target.GetType().GetProperty("Ring").GetValue(target)).Value = 1;
        ((NumericUpDown)target.GetType().GetProperty("Slot").GetValue(target)).Value = 1;
        foreach (var query in new[] { "原始展开功能", "我的展开", "xycut-unfold" })
        {
            search.Text = query; Invoke(settings, "SearchCellCommands");
            check(matches.Items.Count == 1 && matches.Items[0].ToString().Contains("原始展开功能") && matches.Items[0].ToString().Contains("我的展开"),
                "cell command search retains original and display name for " + query);
        }
        search.Text = "test-l-alias"; Invoke(settings, "SearchCellCommands");
        check(matches.Items.Count == 1 && matches.Items[0].ToString().Contains("[LINE]"), "cell search resolves native alias to execution command");
        search.Text = "原始直线"; Invoke(settings, "SearchCellCommands");
        check(matches.Items.Count == 1, "cell search finds native original name");
        search.Text = "我的展开"; Invoke(settings, "SearchCellCommands"); matches.SelectedIndex = 0;
        Invoke(settings, "BindSelectedCellCommand");
        var edited = draft.Menus[SceneKeys.Global];
        var bound = edited.Rings[0].Items.Single(item => item.Slot == 0);
        check(bound.Kind == ActionKinds.CustomCommand && bound.Value == "rename-fixture" && bound.Label == "我的展开" && bound.FontSizePx == 19,
            "binding search result uses stable command id and retains selected cell typography");
        check(edited.UnassignedItems.Any(item => item.Kind == ActionKinds.CadCommand && item.Value == "LINE"), "binding preserves displaced command in unassigned list");
        var custom = draft.CustomCommands.Single(command => command.Id == "rename-fixture");
        display.Text = "新显示名称"; Invoke(settings, "ApplyCellDisplayName", false);
        check(custom.Name == "原始展开功能" && custom.CommandName == "XYCUT-UNFOLD" && custom.Arguments.SequenceEqual(new[] { "keep-argument" }) && custom.DisplayNameOverride == "新显示名称",
            "custom display rename preserves original name execution and arguments");
        check(AllItems(edited).Where(item => item.Kind == ActionKinds.CustomCommand && item.Value == "rename-fixture").Count() == 3 &&
            AllItems(edited).Where(item => item.Value == "rename-fixture").All(item => item.Label == "新显示名称"), "custom display rename synchronizes wheel duplicates and unassigned items");
        search.Text = "新显示名称"; Invoke(settings, "SearchCellCommands");
        check(matches.Items.Count == 1 && matches.Items[0].ToString().Contains("原始展开功能"), "renamed custom command remains searchable with original name visible");
        var serialized = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(draft));
        var restored = serialized.CustomCommands.Single(command => command.Id == "rename-fixture");
        check(restored.Name == custom.Name && restored.DisplayNameOverride == custom.DisplayNameOverride && restored.CommandName == custom.CommandName,
            "serialization preserves separate original and display names");
        display.Text = " "; Invoke(settings, "ApplyCellDisplayName", false);
        check(custom.DisplayNameOverride == "新显示名称", "blank display name cannot erase existing override");
        Invoke(settings, "ApplyCellDisplayName", true);
        check(custom.DisplayNameOverride == "" && AllItems(edited).Where(item => item.Value == "rename-fixture").All(item => item.Label == "原始展开功能"), "reset restores original custom label everywhere");

        search.Text = "test-l-alias"; Invoke(settings, "SearchCellCommands"); matches.SelectedIndex = 0;
        Invoke(settings, "BindSelectedCellCommand");
        display.Text = "我的直线"; Invoke(settings, "ApplyCellDisplayName", false);
        check(draft.CommandDisplayNameOverrides["LINE"] == "我的直线" && native.DisplayName == "原始直线" && native.GlobalName == "LINE",
            "native display override preserves catalog original name and execution token");
        check(AllItems(edited).Where(item => item.Kind == ActionKinds.CadCommand).All(item => item.Label == "我的直线") &&
            edited.Rings[0].Items.Any(item => item.Value == "_LINE"), "native rename synchronizes normalized aliases without rewriting their stored execution values");
        search.Text = "我的直线"; Invoke(settings, "SearchCellCommands");
        check(matches.Items.Count == 1 && matches.Items[0].ToString().Contains("原始直线"), "native display name search retains original label");
        Invoke(settings, "ApplyCellDisplayName", true);
        check(!draft.CommandDisplayNameOverrides.ContainsKey("LINE") && AllItems(edited).Where(item => item.Kind == ActionKinds.CadCommand).All(item => item.Label == "原始直线"), "native reset clears override and restores labels");
        check(saves == 0 && JsonCompat.Serialize(config) == original, "cell command edits remain draft-only without calling save or mutating input config");
    }

    private static IEnumerable<MenuItemConfig> AllItems(SceneMenuConfig wheel) => wheel.Rings.SelectMany(ring => ring.Items).Concat(wheel.UnassignedItems);
    private static T Field<T>(SettingsWindow settings, string name) => (T)typeof(SettingsWindow).GetField(name, Flags).GetValue(settings);
    private static void Invoke(SettingsWindow settings, string name, params object[] args) => typeof(SettingsWindow).GetMethod(name, Flags).Invoke(settings, args);
}
