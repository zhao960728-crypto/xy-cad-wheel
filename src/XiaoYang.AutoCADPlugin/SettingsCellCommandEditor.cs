using System.Drawing;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private readonly TextBox _cellDisplayName = new();
    private readonly TextBox _cellCommandSearch = new();
    private readonly Label _cellCommandIdentity = new() { AutoSize = true, MaximumSize = new Size(430, 0) };
    private readonly ListBox _cellCommandMatches = new() { IntegralHeight = false, Height = 112, HorizontalScrollbar = true };
    private readonly Button _saveCellDisplayName = new() { Text = "应用显示名", AutoSize = true };

    private sealed class CellCommandMatch
    {
        internal string Kind;
        internal string Value;
        internal string Name;
        internal string Caption;
        public override string ToString() => Caption;
    }

    private static string GetCommandDisplayName(CustomCommandConfig command) =>
        string.IsNullOrWhiteSpace(command.DisplayNameOverride) ? command.Name : command.DisplayNameOverride.Trim();

    private static string CatalogDisplayLabel(string original, string display) =>
        string.Equals(original, display, StringComparison.CurrentCulture) ? original : $"{original}（显示名：{display}）";

    private Control BuildSelectedCommandEditor()
    {
        var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
        _cellCommandSearch.Dock = DockStyle.Top;
        _cellCommandSearch.MinimumSize = new Size(0, _cellCommandSearch.PreferredHeight);
        _cellDisplayName.Dock = DockStyle.Top;
        _cellDisplayName.MinimumSize = new Size(0, _cellDisplayName.PreferredHeight);
        _cellCommandMatches.Dock = DockStyle.Top;
        var bind = new Button { Text = "将搜索结果绑定到当前格", AutoSize = true };
        bind.Click += (_, _) => BindSelectedCellCommand();
        _cellCommandSearch.TextChanged += (_, _) => SearchCellCommands();
        _saveCellDisplayName.Click += (_, _) => ApplyCellDisplayName();
        var reset = new Button { Text = "恢复原名称", AutoSize = true };
        reset.Click += (_, _) => ApplyCellDisplayName(true);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.Add(_saveCellDisplayName); actions.Controls.Add(reset);
        var controls = new Control[] {
            new Label { Text = "搜索名称 / 执行命令 / 快捷键，选择结果后绑定功能", AutoSize = true },
            _cellCommandSearch, _cellCommandMatches, bind, _cellCommandIdentity,
            new Label { Text = "显示名称（原名称和执行命令保留，命令库同步标注）", AutoSize = true },
            _cellDisplayName, actions };
        for (var i = 0; i < controls.Length; i++) panel.Controls.Add(controls[i], 0, i);
        return panel;
    }

    private void SearchCellCommands()
    {
        var query = _cellCommandSearch.Text.Trim();
        _cellCommandMatches.BeginUpdate();
        try
        {
            _cellCommandMatches.Items.Clear();
            if (query.Length == 0) return;
            bool Matches(string value) => (value ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
            foreach (var command in _allCommands.Where(command => command.CanCallDirectly &&
                (Matches(command.GlobalName) || Matches(command.DisplayName) || Matches(GetCommandDisplayName(command)) ||
                 Matches(command.Help) || Matches(CommandCategoryCatalog.Categorize(command, _config)) || command.Aliases.Any(Matches)))
                .GroupBy(command => command.GlobalName, StringComparer.OrdinalIgnoreCase).Select(CadCommandCatalog.SelectBestLocalizedCandidate))
                _cellCommandMatches.Items.Add(new CellCommandMatch { Kind = ActionKinds.CadCommand, Value = command.GlobalName,
                    Name = GetCommandDisplayName(command), Caption = $"{CatalogDisplayLabel(command.DisplayName, GetCommandDisplayName(command))} [{command.GlobalName}]" });
            foreach (var custom in _config.CustomCommands.Where(command => Matches(command.Name) || Matches(GetCommandDisplayName(command)) || Matches(command.CommandName) || Matches(command.Category)))
                _cellCommandMatches.Items.Add(new CellCommandMatch { Kind = ActionKinds.CustomCommand, Value = custom.Id,
                    Name = GetCommandDisplayName(custom), Caption = $"★ {CatalogDisplayLabel(custom.Name, GetCommandDisplayName(custom))} [{custom.CommandName}]" });
        }
        finally { _cellCommandMatches.EndUpdate(); }
    }

    private void LoadSelectedCommandEditor()
    {
        var exists = TryGetSelectedWheelItem(out var item);
        _cellDisplayName.Text = exists ? item.Label : "";
        _cellDisplayName.Enabled = exists;
        _saveCellDisplayName.Enabled = exists;
        if (!exists) { _cellCommandIdentity.Text = "当前是空格：先搜索并绑定功能。"; return; }
        var custom = item.Kind == ActionKinds.CustomCommand ? _config.CustomCommands.FirstOrDefault(command => command.Id == item.Value) : null;
        var native = item.Kind == ActionKinds.CadCommand ? _allCommands.FirstOrDefault(command =>
            CadCommandCatalog.NormalizeCommandName(command.GlobalName).Equals(CadCommandCatalog.NormalizeCommandName(item.Value), StringComparison.OrdinalIgnoreCase)) : null;
        _cellCommandIdentity.Text = $"原名称：{custom?.Name ?? native?.DisplayName ?? item.Value}\n执行命令：{custom?.CommandName ?? item.Value}";
    }

    private void BindSelectedCellCommand()
    {
        if (_cellCommandMatches.SelectedItem is not CellCommandMatch match)
        { _cellCommandIdentity.Text = "请先搜索并选中一个命令结果。"; return; }
        var ring = ResolveTargetRing(_catalogTarget);
        var slot = (int)_catalogTarget.Slot.Value - 1;
        var previous = ring.Items.FirstOrDefault(item => item.Slot == slot);
        if (previous != null && previous.Kind == match.Kind && previous.Value == match.Value) return;
        if (previous != null) { ring.Items.Remove(previous); _config.Menus[SceneKeys.Global].UnassignedItems.Add(previous); }
        ring.Items.Add(new MenuItemConfig { Slot = slot, Kind = match.Kind, Value = match.Value, Label = match.Name,
            ContentMode = previous?.ContentMode ?? "Inherit",
            CellSizePercent = previous?.CellSizePercent ?? 100, CellShape = previous?.CellShape ?? "Circle",
            FreeX = previous?.FreeX, FreeY = previous?.FreeY, IconRatioPercent = previous?.IconRatioPercent ?? 55,
            FontSizePx = previous?.FontSizePx ?? 0, FontFamily = previous?.FontFamily ?? "", TextColor = previous?.TextColor ?? "",
            FontBold = previous?.FontBold, TextEffect = previous?.TextEffect ?? "" });
        RefreshCellAppearanceChanges();
        LoadSelectedCommandEditor();
    }

    private void ApplyCellDisplayName(bool reset = false)
    {
        if (!TryGetSelectedWheelItem(out var item)) return;
        var display = _cellDisplayName.Text.Trim();
        if (!reset && display.Length == 0) { _cellCommandIdentity.Text = "显示名称不能为空。"; return; }
        if (item.Kind == ActionKinds.CustomCommand)
        {
            var custom = _config.CustomCommands.FirstOrDefault(command => command.Id == item.Value);
            if (custom == null) { _cellCommandIdentity.Text = "原自定义命令已不存在，请先重新绑定。"; return; }
            custom.DisplayNameOverride = reset ? "" : display;
            UpdateWheelLabels(item.Kind, item.Value, GetCommandDisplayName(custom));
        }
        else if (item.Kind == ActionKinds.CadCommand)
        {
            var key = CadCommandCatalog.NormalizeCommandName(item.Value);
            if (!CadCommandCatalog.IsSafeCommandName(key)) return;
            var native = _allCommands.FirstOrDefault(command => CadCommandCatalog.NormalizeCommandName(command.GlobalName).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (reset) _config.CommandDisplayNameOverrides.Remove(key);
            else _config.CommandDisplayNameOverrides[key] = display;
            UpdateWheelLabels(item.Kind, key, reset ? native?.DisplayName ?? item.Value : display);
        }
        else { _cellCommandIdentity.Text = "该类型暂不支持显示名同步，请先绑定命令库中的功能。"; return; }
        FilterCatalog();
        SearchCellCommands();
        RefreshCellAppearanceChanges();
        LoadSelectedCommandEditor();
    }
}
