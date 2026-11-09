using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private readonly DataGridView _managedExtensionGrid = new();
    private readonly Label _managedExtensionStatus = new();
    private bool _updatingManagedExtensions;
    private readonly ComboBox _managedStartupCommand = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _managedStartupHint = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(650, 0) };
    private bool _updatingManagedStartup;

    private TabPage BuildExtensionManagerTab()
    {
        var page = new TabPage("扩展管理") { BackColor = Canvas, Padding = new Padding(0, 8, 0, 0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var explanation = new Label
        {
            Text = "仅勾选的扩展会随轮盘加载，新添加的扩展默认不启用。勾选后点击底部“应用并保存”。\n文件保留在原目录，以便查找同目录依赖。取消勾选或卸载已加载的扩展后，需要重启 CAD 才会停止加载。",
            AutoSize = true, Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(8, 8, 8, 12)
        };
        layout.Controls.Add(explanation, 0, 0);
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(5, 0, 5, 8) };
        var add = new Button { Text = "添加扩展文件…", AutoSize = true, Height = 32, Padding = new Padding(5) };
        var remove = new Button { Text = "卸载所选扩展", AutoSize = true, Height = 32, Padding = new Padding(5) };
        var refresh = new Button { Text = "刷新加载状态", AutoSize = true, Height = 32, Padding = new Padding(5) };
        add.Click += (_, __) =>
        {
            using var picker = new OpenFileDialog
            {
                Title = "添加要由轮盘管理的扩展",
#if ZWCAD
                Filter = "CAD 扩展文件|*.lsp;*.fas;*.vlx;*.dll;*.zrx|所有文件|*.*",
#else
                Filter = "CAD 扩展文件|*.lsp;*.fas;*.vlx;*.dll;*.arx|所有文件|*.*",
#endif
                Multiselect = true, CheckFileExists = true, RestoreDirectory = true
            };
            if (picker.ShowDialog(this) == DialogResult.OK) AddManagedExtensions(picker.FileNames);
        };
        remove.Click += (_, __) => RemoveManagedExtensions();
        refresh.Click += (_, __) => RefreshManagedExtensionRows();
        var inventory = new Button { Text = "现有插件 / 加载状态", AutoSize = true, Height = 32, Padding = new Padding(5) };
        inventory.Click += (_, __) => _pages.SelectedIndex = 8;
        toolbar.Controls.Add(add); toolbar.Controls.Add(remove); toolbar.Controls.Add(refresh); toolbar.Controls.Add(inventory);
        toolbar.Controls.Add(new Label { Text = "启动命令（留空仅加载）", AutoSize = true, Margin = new Padding(8, 10, 3, 0) });
        toolbar.Controls.Add(_managedStartupCommand);
        var discover = new Button { Text = "读取命令", AutoSize = true, Padding = new Padding(5) };
        var createEntry = new Button { Text = "生成命令入口", AutoSize = true, Padding = new Padding(5) };
        var bindEntry = new Button { Text = "绑定当前轮盘格", AutoSize = true, Padding = new Padding(5) };
        var runEntry = new Button { Text = "运行入口（保存后）", AutoSize = true, Padding = new Padding(5) };
        discover.Click += (_, __) => ShowManagedStartupCommands();
        createEntry.Click += (_, __) => CreateManagedStartupEntry(false);
        bindEntry.Click += (_, __) => CreateManagedStartupEntry(true);
        runEntry.Click += (_, __) => RunManagedStartupEntry();
        toolbar.Controls.Add(discover); toolbar.Controls.Add(createEntry); toolbar.Controls.Add(bindEntry); toolbar.Controls.Add(runEntry);
        toolbar.Controls.Add(_managedStartupHint);
        _managedStartupCommand.TextChanged += (_, __) =>
        {
            if (_updatingManagedStartup || SelectedManagedExtension() is not ManagedExtensionConfig entry) return;
            var command = _managedStartupCommand.Text.Trim();
            if (command.Length > 0 && !ExtensionCommandDiscovery.IsCommandName(command)) { _managedStartupHint.Text = "启动命令只允许字母、数字、下划线和连字符；不接受宏或多条命令。"; return; }
            entry.StartupCommand = command;
            foreach (DataGridViewRow row in _managedExtensionGrid.Rows) if (ReferenceEquals(row.Tag, entry)) row.Cells[4].Value = command.Length == 0 ? "仅加载" : command;
            _managedStartupHint.Text = "启动命令已修改，点击“应用并保存”后生效。";
            UpdateSaveStatus();
        };
        layout.Controls.Add(toolbar, 0, 1);

        _managedExtensionGrid.Dock = DockStyle.Fill;
        _managedExtensionGrid.BackgroundColor = Surface;
        _managedExtensionGrid.BorderStyle = BorderStyle.FixedSingle;
        _managedExtensionGrid.AllowUserToAddRows = false;
        _managedExtensionGrid.AllowUserToDeleteRows = false;
        _managedExtensionGrid.AllowUserToResizeRows = false;
        _managedExtensionGrid.RowHeadersVisible = false;
        _managedExtensionGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _managedExtensionGrid.MultiSelect = true;
        _managedExtensionGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _managedExtensionGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _managedExtensionGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _managedExtensionGrid.DefaultCellStyle.Padding = new Padding(3, 5, 3, 5);
        _managedExtensionGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", Width = 58, MinimumWidth = 54, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        _managedExtensionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExtensionName", HeaderText = "扩展名称", ReadOnly = true, FillWeight = 25, MinimumWidth = 100 });
        _managedExtensionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourcePath", HeaderText = "来源完整路径", ReadOnly = true, FillWeight = 50, MinimumWidth = 160, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
        _managedExtensionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LoadStatus", HeaderText = "加载状态", ReadOnly = true, FillWeight = 25, MinimumWidth = 120, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
        _managedExtensionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StartupCommand", HeaderText = "启动命令", ReadOnly = true, FillWeight = 22, MinimumWidth = 90 });
        _managedExtensionGrid.SelectionChanged += (_, __) => { if (!_updatingManagedExtensions) ShowManagedStartupCommands(); };
        _managedExtensionGrid.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (_managedExtensionGrid.IsCurrentCellDirty) _managedExtensionGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _managedExtensionGrid.CellValueChanged += (_, e) =>
        {
            if (_updatingManagedExtensions || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var row = _managedExtensionGrid.Rows[e.RowIndex];
            if (row.Tag is not ManagedExtensionConfig extension) return;
            extension.Enabled = row.Cells[0].Value is true;
            UpdateSaveStatus();
            row.Cells[3].Value = ManagedExtensionStatus(extension);
            _managedExtensionStatus.Text = "启用设置已更改，点击底部“应用并保存”生效。已加载的扩展取消启用后，请重启 CAD。";
        };
        _managedExtensionGrid.AllowDrop = true;
        _managedExtensionGrid.DragEnter += (_, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        _managedExtensionGrid.DragDrop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddManagedExtensions(files);
        };
        layout.Controls.Add(_managedExtensionGrid, 0, 2);
        _managedExtensionStatus.AutoSize = true;
        _managedExtensionStatus.Dock = DockStyle.Fill;
        _managedExtensionStatus.Padding = new Padding(8, 10, 8, 8);
        _managedExtensionStatus.ForeColor = Muted;
        _managedExtensionStatus.Text = "可将多个扩展文件拖入列表。“卸载”仅移除这里的登记，不删除原文件，也不影响轮盘本体。";
        layout.Controls.Add(_managedExtensionStatus, 0, 3);
        page.Controls.Add(layout);
        page.Enter += (_, __) => RefreshManagedExtensionRows();
        RefreshManagedExtensionRows();
        return page;
    }

    private ManagedExtensionConfig SelectedManagedExtension() => _managedExtensionGrid.CurrentRow?.Tag as ManagedExtensionConfig;

    private void ShowManagedStartupCommands()
    {
        var entry = SelectedManagedExtension();
        _updatingManagedStartup = true;
        try
        {
            _managedStartupCommand.Items.Clear();
            _managedStartupCommand.Items.Add("");
            _managedStartupCommand.Enabled = entry != null;
            if (entry == null) { _managedStartupCommand.Text = ""; _managedStartupHint.Text = "选择扩展后可读取、选择或手动填写启动命令；未启用不会运行。"; return; }
            var commands = ExtensionCommandDiscovery.Read(entry.FilePath, out var detail);
            foreach (var command in commands) _managedStartupCommand.Items.Add(command);
            _managedStartupCommand.Text = entry.StartupCommand ?? "";
            _managedStartupHint.Text = detail;
        }
        finally { _updatingManagedStartup = false; }
    }

    private bool ValidateManagedStartup(out ManagedExtensionConfig entry)
    {
        entry = SelectedManagedExtension();
        if (entry == null) { _managedExtensionStatus.Text = "请先选择扩展。"; return false; }
        var command = _managedStartupCommand.Text.Trim();
        if (command.Length > 0 && !ExtensionCommandDiscovery.IsCommandName(command)) { _managedExtensionStatus.Text = "启动命令格式无效，请填写单个真实命令名，或留空仅加载。"; return false; }
        entry.StartupCommand = command;
        return true;
    }

    private void CreateManagedStartupEntry(bool bind)
    {
        if (!ValidateManagedStartup(out var entry)) return;
        var generated = ExtensionLoadService.CreateStartupCommand(entry);
        var custom = _config.CustomCommands.FirstOrDefault(c => c.Id == generated.Id);
        if (custom == null) { custom = generated; _config.CustomCommands.Add(custom); }
        else
        {
            custom.Name = generated.Name; custom.CommandName = generated.CommandName;
            custom.Arguments = generated.Arguments; custom.DisableDialogs = false; custom.Silent = false;
        }
        if (bind)
        {
            if (!TryGetSelectedWheelItem(out var cell)) { _managedExtensionStatus.Text = "已生成命令入口；请先在轮盘预览选择格子，再点击绑定。"; }
            else { cell.Kind = ActionKinds.CustomCommand; cell.Value = custom.Id; cell.Label = custom.Name; _managedExtensionStatus.Text = "已绑定当前轮盘格；启用扩展并点击“应用并保存”后可使用。"; }
        }
        else _managedExtensionStatus.Text = "已生成自定义命令入口；可在命令管理选择它或绑定轮盘。启用并保存后可运行。";
        RefreshCustomList(); FilterCatalog(); RefreshWheelGrid(); RefreshPreview(); UpdateSaveStatus();
    }

    private void RunManagedStartupEntry()
    {
        if (!ValidateManagedStartup(out var entry)) return;
        try
        {
            ExtensionLoadService.QueueStartupEntry(entry.Id, entry.FilePath, entry.StartupCommand);
            _managedExtensionStatus.Text = "启动入口已排队。关闭设置窗口返回 CAD 后处理；原生信任检查未通过时不会继续启动命令。";
        }
        catch (Exception ex) { _managedExtensionStatus.Text = ex.Message; }
    }
    private void RefreshManagedExtensionRows()
    {
        if (_managedExtensionGrid.Columns.Count == 0) return;
        _config.ManagedExtensions ??= new List<ManagedExtensionConfig>();
        var selected = new HashSet<string>(_managedExtensionGrid.SelectedRows.Cast<DataGridViewRow>().Select(row => (row.Tag as ManagedExtensionConfig)?.Id));
        _updatingManagedExtensions = true;
        try
        {
            _managedExtensionGrid.Rows.Clear();
            foreach (var extension in _config.ManagedExtensions)
            {
                var index = _managedExtensionGrid.Rows.Add(extension.Enabled, extension.Name, extension.FilePath, ManagedExtensionStatus(extension), string.IsNullOrWhiteSpace(extension.StartupCommand) ? "仅加载" : extension.StartupCommand);
                var row = _managedExtensionGrid.Rows[index];
                row.Tag = extension;
                row.Cells[2].ToolTipText = extension.FilePath;
                row.Selected = selected.Contains(extension.Id);
            }
        }
        finally { _updatingManagedExtensions = false; }
        if (_managedExtensionGrid.Visible) ShowManagedStartupCommands();
    }

    private static string ManagedExtensionStatus(ManagedExtensionConfig extension)
    {
        if (!File.Exists(extension.FilePath)) return "找不到原文件";
        if (!ExtensionLoadService.IsSupported(extension.FilePath)) return "当前宿主不支持此文件类型";
        var current = ExtensionLoadService.GetStatus(extension.Id);
        if (!extension.Enabled) return string.IsNullOrWhiteSpace(current) ? "未启用" : "下次启动停用 · " + current;
        return string.IsNullOrWhiteSpace(current) ? "待应用并加载" : current;
    }

    private void AddManagedExtensions(IEnumerable<string> files)
    {
        _config.ManagedExtensions ??= new List<ManagedExtensionConfig>();
        var added = 0;
        var skipped = new List<string>();
        foreach (var file in files)
        {
            string path;
            try { path = Path.GetFullPath(file); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { skipped.Add(Path.GetFileName(file) + "（路径无效）"); continue; }
            if (!File.Exists(path)) { skipped.Add(Path.GetFileName(path) + "（文件不存在）"); continue; }
            if (!ExtensionLoadService.IsSupported(path)) { skipped.Add(Path.GetFileName(path) + "（当前宿主不支持）"); continue; }
            if (_config.ManagedExtensions.Any(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            { skipped.Add(Path.GetFileName(path) + "（已在列表中）"); continue; }
            var commands = ExtensionCommandDiscovery.Read(path, out _);
            _config.ManagedExtensions.Add(new ManagedExtensionConfig { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileNameWithoutExtension(path), FilePath = path, Enabled = false, StartupCommand = commands.Count == 1 ? commands[0] : "" });
            added++;
        }
        RefreshManagedExtensionRows();
        _managedExtensionStatus.Text = $"已添加 {added} 个扩展，默认未启用。选择启用项后，点击底部“应用并保存”。";
        UpdateSaveStatus();
        if (skipped.Count > 0)
            MessageBox.Show(this, "以下文件未添加：\n" + string.Join("\n", skipped), "添加扩展", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RemoveManagedExtensions()
    {
        var selected = _managedExtensionGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag as ManagedExtensionConfig).Where(item => item != null).ToList();
        if (selected.Count == 0) { _managedExtensionStatus.Text = "请先选择要卸载的扩展。"; return; }
        foreach (var item in selected) _config.ManagedExtensions.Remove(item);
        UpdateSaveStatus();
        RefreshManagedExtensionRows();
        _managedExtensionStatus.Text = $"已移除 {selected.Count} 个扩展登记，原文件未删除。点击“应用并保存”；已加载的扩展需要重启 CAD 才会停止加载。";
    }
    private TabPage BuildInstalledPluginInventoryTab()
    {
        var page = new TabPage("现有插件") { BackColor = Canvas, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(4, 8, 4, 12), Text = "读取电脑已有的 CAD 插件与当前 CAD 加载状态。此处独立于轮盘的“扩展管理”。\n卸载调用插件自身的卸载程序；不删除归属不明的共享文件。已加载的 .NET 插件需重启 CAD 才能释放。" }, 0, 0);
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var refresh = new Button { Text = "重新读取", AutoSize = true, Padding = new Padding(5) };
        toolbar.Controls.Add(refresh);
        var back = new Button { Text = "返回扩展管理", AutoSize = true, Padding = new Padding(5) };
        back.Click += (_, __) => _pages.SelectedIndex = 7;
        toolbar.Controls.Add(back);
        layout.Controls.Add(toolbar, 0, 1);
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Surface, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
            ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
        };
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.DefaultCellStyle.Padding = new Padding(3, 5, 3, 5);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "插件 / 来源", FillWeight = 24 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "位置", FillWeight = 34 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "加载状态", FillWeight = 24 });
        grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "卸载", FillWeight = 18 });
        layout.Controls.Add(grid, 0, 2);
        var status = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(4, 10, 4, 4) };
        layout.Controls.Add(status, 0, 3);
        void ReadInventory()
        {
            try
            {
                var entries = InstalledPluginInventory.Read(_config.ManagedExtensions?.Select(e => e.FilePath), out var detail);
                grid.Rows.Clear();
                foreach (var entry in entries)
                {
                    var index = grid.Rows.Add(entry.Name + "\n" + entry.Source, entry.Path, entry.Status, entry.UninstallCommand == null ? "无安全卸载入口" : "卸载…");
                    grid.Rows[index].Tag = entry;
                    grid.Rows[index].Cells[3].ToolTipText = entry.UninstallCommand == null ? "未找到与插件产品码精确匹配的卸载程序，请使用插件原安装器。" : "将进行两次确认，然后启动原厂卸载程序。";
                }
                status.Text = $"共 {entries.Count} 项。" + detail;
            }
            catch (Exception ex) { status.Text = "读取失败：" + ex.Message; }
        }
        refresh.Click += (_, __) => ReadInventory();
        grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 3 || grid.Rows[e.RowIndex].Tag is not InstalledPluginInfo entry) return;
            if (entry.UninstallCommand == null)
            {
                status.Text = "此插件未找到精确匹配的卸载程序。请使用其原安装器；不会强删未知文件、配置或共享依赖。";
                return;
            }
            if (MessageBox.Show(this, "确认卸载插件“" + entry.Name + "”？\n位置：" + entry.Path + "\n将调用该插件登记的卸载程序。请先保存相关 CAD 文档。", "卸载确认（1/2）", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (MessageBox.Show(this, "最后确认：启动“" + entry.Name + "”的卸载程序？\n" + entry.UninstallCommand + "\n清理范围由该卸载程序决定，请保留其中的用户配置。已加载模块需关闭并重启 CAD 后才能释放。", "卸载确认（2/2）", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                InstalledPluginInventory.StartUninstall(entry);
                status.Text = "已启动插件卸载程序，尚未确认卸载完成。完成后请重启 CAD 并重新读取；本窗口不会自动关闭 CAD。";
            }
            catch (Exception ex) { status.Text = "无法启动卸载：" + ex.Message; }
        };
        page.Controls.Add(layout);
        page.Enter += (_, __) => ReadInventory();
        return page;
    }
}






