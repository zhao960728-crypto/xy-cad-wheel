using System.Drawing;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class ImeSettingsDialog : Form
{
    private static readonly Color Canvas = Color.FromArgb(244, 246, 248);
    private static readonly Color Surface = Color.White;
    private static readonly Color Ink = Color.FromArgb(30, 41, 53);
    private static readonly Color Muted = Color.FromArgb(99, 115, 129);
    private static readonly Color Border = Color.FromArgb(215, 223, 230);
    private static readonly Color Accent = Color.FromArgb(0, 112, 132);
    private static readonly Color Header = Color.FromArgb(28, 38, 49);

    private readonly Action<ImeSwitchConfig> _saved;
    private readonly CheckBox _enabled = new();
    private readonly Panel _enabledCard = new();
    private readonly CheckBox _directInputUsesEnglish = new();
    private readonly CheckBox _otherCommandsUseEnglish = new();
    private readonly CheckBox _commandEndUsesEnglish = new();
    private readonly CheckBox _documentActivatedUsesEnglish = new();
    private readonly CheckBox _showErrorReport = new();
    private readonly ComboBox _inputMethod = new();
    private readonly Label _inputMethodWarning = new();
    private readonly CheckedListBox _recommendedCommands = new();
    private readonly ListBox _customCommands = new();
    private readonly TextBox _customCommand = new();
    private readonly Label _status = new();
    private readonly Label _enabledHint = new();
    private bool _loadingLanguage;
    private bool _configuredInputMethodUnavailable;

    public ImeSettingsDialog(ImeSwitchConfig source, Action<ImeSwitchConfig> saved)
    {
        _saved = saved ?? (_ => { });
        Text = "CAD 输入法自动切换设置";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 560);
        Size = new Size(900, 720);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Canvas;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        BuildUi();
        PerformAutoScale();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var margin = SettingsWindow.ScaleLogicalForDpi(12, DeviceDpi);
        var available = new Size(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        LoadConfiguration(Clone(source ?? new ImeSwitchConfig()));
        UpdateEnabledState();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Header, Padding = new Padding(24, 15, 24, 10) };
        var title = new Label
        {
            Text = "CAD 输入法自动切换",
            Dock = DockStyle.Top,
            Height = 29,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            ForeColor = Color.White
        };
        var subtitle = new Label
        {
            Text = "为命令和文字编辑设置输入偏好，切换后仍可手动调整。",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(201, 213, 222)
        };
        header.Controls.Add(subtitle);
        header.Controls.Add(title);

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 68, Padding = new Padding(20, 12, 20, 12), BackColor = Surface, ColumnCount = 4, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        var save = CreateButton("保存并应用", 112, primary: true);
        save.Dock = DockStyle.Fill;
        save.Click += (_, _) => SaveAndClose();
        var cancel = CreateButton("取消", 82);
        cancel.Dock = DockStyle.Fill;
        cancel.Margin = new Padding(8, 0, 0, 0);
        cancel.Click += (_, _) => Close();
        var restore = CreateButton("恢复推荐", 96);
        restore.Dock = DockStyle.Fill;
        restore.Click += (_, _) => RestoreRecommended();
        footer.Controls.Add(restore, 0, 0);
        footer.Controls.Add(cancel, 2, 0);
        footer.Controls.Add(save, 3, 0);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 12), AutoScroll = true, BackColor = Canvas };
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(16, 8),
            Name = "ImePages"
        };
        var general = new TabPage("切换规则与输入法") { BackColor = Canvas, Padding = new Padding(8, 12, 8, 8), AutoScroll = true };
        var commands = new TabPage("中文命令名单") { BackColor = Canvas, Padding = new Padding(8, 12, 8, 8), AutoScroll = true };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(BuildBehaviorCard(), 0, 0);
        content.Controls.Add(BuildLanguageCard(), 0, 1);
        general.Controls.Add(content);
        var commandCard = BuildCommandCard();
        commandCard.Dock = DockStyle.Fill;
        commandCard.MinimumSize = new Size(0, 260);
        commands.Controls.Add(commandCard);
        tabs.TabPages.Add(general);
        tabs.TabPages.Add(commands);
        _status.Dock = DockStyle.Bottom;
        _status.Height = 30;
        _status.AutoEllipsis = true;
        _status.Padding = new Padding(4, 8, 4, 4);
        _status.ForeColor = Muted;
        body.Controls.Add(tabs);
        body.Controls.Add(_status);
        var enabled = BuildEnabledCard();
        body.Controls.Add(enabled);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private Control BuildEnabledCard()
    {
        _enabledCard.Dock = DockStyle.Top;
        _enabledCard.Height = 64;
        _enabledCard.Margin = new Padding(0, 0, 0, 10);
        _enabledCard.Padding = new Padding(16, 8, 16, 8);
        _enabledCard.BackColor = Surface;
        _enabledCard.BorderStyle = BorderStyle.FixedSingle;

        _enabled.Text = "启用输入法自动切换";
        _enabled.Dock = DockStyle.Top;
        _enabled.Height = 25;
        _enabled.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        _enabled.ForeColor = Ink;
        _enabled.CheckedChanged += (_, _) => UpdateEnabledState();
        _enabledHint.Text = "仅在 CAD 前台生效。已有文字按内容判断，新建文字按中文命令名单。";
        _enabledHint.Dock = DockStyle.Fill;
        _enabledHint.ForeColor = Muted;
        _enabledCard.Controls.Add(_enabledHint);
        _enabledCard.Controls.Add(_enabled);
        return _enabledCard;
    }

    private Control BuildBehaviorCard()
    {
        var card = CreateCard(164);
        var title = CreateSectionTitle("输入偏好与切换规则");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(0, 4, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
        ConfigureCheck(_directInputUsesEnglish, "命令行直接输入时首选英文");
        ConfigureCheck(_otherCommandsUseEnglish, "进入普通命令时切换一次英文");
        ConfigureCheck(_commandEndUsesEnglish, "命令结束、取消或失败后切一次英文");
        ConfigureCheck(_documentActivatedUsesEnglish, "切换到空闲图纸时首选英文");
        ConfigureCheck(_showErrorReport, "输入法异常时显示提示（不打断操作）");
        _showErrorReport.CheckedChanged += (_, _) => UpdateStatus();
        grid.Controls.Add(_directInputUsesEnglish, 0, 0);
        grid.Controls.Add(_otherCommandsUseEnglish, 1, 0);
        grid.Controls.Add(_commandEndUsesEnglish, 0, 1);
        grid.Controls.Add(_documentActivatedUsesEnglish, 1, 1);
        grid.Controls.Add(_showErrorReport, 0, 2);
        grid.SetColumnSpan(_showErrorReport, 2);
        void FitBehaviorRows()
        {
            var compact = grid.ClientSize.Width < SettingsWindow.ScaleLogicalForDpi(690, DeviceDpi);
            var checks = new[] { _directInputUsesEnglish, _otherCommandsUseEnglish, _commandEndUsesEnglish, _documentActivatedUsesEnglish, _showErrorReport };
            grid.SuspendLayout();
            grid.RowCount = compact ? 5 : 3;
            grid.RowStyles.Clear();
            for (var row = 0; row < grid.RowCount; row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / grid.RowCount));
            for (var index = 0; index < checks.Length; index++)
            {
                grid.SetCellPosition(checks[index], new TableLayoutPanelCellPosition(compact ? 0 : index % 2, compact ? index : index / 2));
                grid.SetColumnSpan(checks[index], compact || index == 4 ? 2 : 1);
            }
            var height = SettingsWindow.ScaleLogicalForDpi(compact ? 224 : 164, DeviceDpi);
            if (card.Height != height) card.Height = height;
            grid.ResumeLayout();
        }
        grid.SizeChanged += (_, _) => FitBehaviorRows();
        card.Controls.Add(grid);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildLanguageCard()
    {
        var card = CreateCard(178);
        var title = CreateSectionTitle("CAD 使用的输入法");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(0, 2, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        ConfigureLanguageCombo(_inputMethod);
        _inputMethod.Dock = DockStyle.None;
        _inputMethod.Anchor = AnchorStyles.Left;
        _inputMethod.Width = 340;
        grid.SizeChanged += (_, _) => _inputMethod.Width = Math.Min(SettingsWindow.ScaleLogicalForDpi(340, DeviceDpi),
            Math.Max(40, grid.ClientSize.Width - (int)grid.ColumnStyles[0].Width - _inputMethod.Margin.Horizontal));
        _inputMethod.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingLanguage) return;
            _configuredInputMethodUnavailable = false;
            UpdateLanguageWarning();
            UpdateStatus();
        };
        grid.Controls.Add(CreateFieldLabel("同一个输入法"), 0, 0);
        grid.Controls.Add(_inputMethod, 1, 0);
        var languageHint = new Label
        {
            Text = "英文和中文共用上面这个输入法；只在进入命令、编辑框或结束命令时切换一次，之后可手动切换，不会锁定。",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        grid.Controls.Add(languageHint, 0, 1);
        grid.SetColumnSpan(languageHint, 2);
        _inputMethodWarning.Dock = DockStyle.Fill;
        _inputMethodWarning.AutoSize = false;
        _inputMethodWarning.ForeColor = Color.FromArgb(190, 82, 35);
        _inputMethodWarning.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(_inputMethodWarning, 0, 2);
        grid.SetColumnSpan(_inputMethodWarning, 2);
        card.Controls.Add(grid);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildCommandCard()
    {
        var card = CreateCard(260);
        var title = CreateSectionTitle("需要中文输入的命令");
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 3, 0, 0) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        var recommended = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 10, 0),
            ColumnCount = 1,
            RowCount = 2
        };
        recommended.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        recommended.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        recommended.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var recommendedToolbar = new Panel { Dock = DockStyle.Fill };
        recommendedToolbar.Controls.Add(new Label
        {
            Text = "常用文字命令",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Muted
        });
        var invert = CreateButton("反选", 58);
        invert.Dock = DockStyle.Right;
        invert.Click += (_, _) => InvertRecommendedCommands();
        var selectAll = CreateButton("全选", 58);
        selectAll.Dock = DockStyle.Right;
        selectAll.Click += (_, _) => SetAllRecommendedCommands(true);
        recommendedToolbar.Controls.Add(invert);
        recommendedToolbar.Controls.Add(selectAll);
        _recommendedCommands.Dock = DockStyle.Fill;
        _recommendedCommands.CheckOnClick = true;
        _recommendedCommands.MultiColumn = true;
        void FitCommandColumns()
        {
            var textWidth = TextRenderer.MeasureText("MLEADERCONTENTEDIT", _recommendedCommands.Font).Width;
            var columnWidth = textWidth + SettingsWindow.ScaleLogicalForDpi(36, DeviceDpi);
            _recommendedCommands.ColumnWidth = columnWidth;
            _recommendedCommands.MultiColumn = _recommendedCommands.ClientSize.Width >= columnWidth * 2;
        }
        _recommendedCommands.SizeChanged += (_, _) => FitCommandColumns();
        _recommendedCommands.FontChanged += (_, _) => FitCommandColumns();
        _recommendedCommands.IntegralHeight = false;
        _recommendedCommands.BorderStyle = BorderStyle.FixedSingle;
		_recommendedCommands.ItemCheck += (_, _) =>
		{
			if (IsHandleCreated) BeginInvoke((Action)UpdateStatus);
		};
        recommended.Controls.Add(recommendedToolbar, 0, 0);
        recommended.Controls.Add(_recommendedCommands, 0, 1);

        var custom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 0, 0) };
        custom.Controls.Add(new Label
        {
            Text = "第三方 / LISP 命令",
            Dock = DockStyle.Top,
            Height = 25,
            ForeColor = Muted
        });
        var input = new Panel { Dock = DockStyle.Top, Height = 34 };
        _customCommand.Dock = DockStyle.Fill;
        _customCommand.CharacterCasing = CharacterCasing.Upper;
        RuntimeCompat.SetTextBoxCueBanner(_customCommand, "例如 YX_Q");
        _customCommand.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            AddCustomCommand();
            e.SuppressKeyPress = true;
        };
        var add = CreateButton("添加", 66, primary: true);
        add.Dock = DockStyle.Right;
        add.Click += (_, _) => AddCustomCommand();
        input.Controls.Add(_customCommand);
        input.Controls.Add(add);
        _customCommands.Dock = DockStyle.Fill;
        _customCommands.BorderStyle = BorderStyle.FixedSingle;
        _customCommands.HorizontalScrollbar = true;
        var delete = CreateButton("删除所选", 86);
        delete.Dock = DockStyle.Right;
        delete.Height = 32;
        delete.Click += (_, _) => DeleteCustomCommand();
        var customActions = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 4, 0, 0) };
        customActions.Controls.Add(delete);
        custom.Controls.Add(_customCommands);
        custom.Controls.Add(customActions);
        custom.Controls.Add(input);
        _customCommands.BringToFront();

        columns.Controls.Add(recommended, 0, 0);
        columns.Controls.Add(custom, 1, 0);
        card.Controls.Add(columns);
        card.Controls.Add(title);
        return card;
    }

    private void LoadConfiguration(ImeSwitchConfig config)
    {
        var languages = ImeNativeBridge.GetInstalledLanguages(refresh: true);
        foreach (var choice in languages.Where(choice => choice.IsChinese && choice.IsInputMethod)
                     .OrderByDescending(choice => choice.TsfProfile != null)
                     .ThenBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            _inputMethod.Items.Add(choice);
        _loadingLanguage = true;
        _configuredInputMethodUnavailable = SelectLanguage(_inputMethod, config.InputMethodLayoutId,
            config.InputMethodProfileId);
        _loadingLanguage = false;
        UpdateLanguageWarning();

        _enabled.Checked = config.Enabled;
        _directInputUsesEnglish.Checked = config.DirectInputUsesEnglish;
        _otherCommandsUseEnglish.Checked = config.OtherCommandsUseEnglish;
        _commandEndUsesEnglish.Checked = config.CommandEndUsesEnglish;
        _documentActivatedUsesEnglish.Checked = config.DocumentActivatedUsesEnglish;
        _showErrorReport.Checked = config.ShowErrorReport;

        var configured = new HashSet<string>(config.TextCommands ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var command in ImeCommandCatalog.Recommended)
            _recommendedCommands.Items.Add(command, configured.Contains(command));
        foreach (var command in ImeCommandCatalog.CustomCommands(config))
            _customCommands.Items.Add(command);
        UpdateStatus();
    }

    private void SaveAndClose()
    {
        if (_enabled.Checked && _inputMethod.SelectedItem is not ImeLanguageChoice)
        {
            MessageBox.Show(this, "未检测到可用的中文输入法，请先在 Windows 中安装微软拼音、搜狗或微信输入法。",
                "未检测到输入法", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var inputMethod = _inputMethod.SelectedItem as ImeLanguageChoice;
        var inputMethodLayout = inputMethod?.LayoutId ?? "00000804";
        var inputMethodProfile = inputMethod?.ProfileId ?? "";

        var result = new ImeSwitchConfig
        {
            Enabled = _enabled.Checked,
            DirectInputUsesEnglish = _directInputUsesEnglish.Checked,
            TextCommandsUseChinese = true,
            OtherCommandsUseEnglish = _otherCommandsUseEnglish.Checked,
            CommandEndUsesEnglish = _commandEndUsesEnglish.Checked,
            DocumentActivatedUsesEnglish = _documentActivatedUsesEnglish.Checked,
            ShowErrorReport = _showErrorReport.Checked,
            RestoreOnShutdown = false,
            InputMethodLayoutId = inputMethodLayout,
            InputMethodProfileId = inputMethodProfile,
            EnglishLayoutId = inputMethodLayout,
            ChineseLayoutId = inputMethodLayout,
            EnglishProfileId = inputMethodProfile,
            ChineseProfileId = inputMethodProfile,
            TextCommands = _recommendedCommands.CheckedItems.Cast<object>()
                .Concat(_customCommands.Items.Cast<object>())
                .Select(item => ImeCommandCatalog.Normalize(item?.ToString()))
                .Where(command => command.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        _saved(result);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RestoreRecommended()
    {
        _directInputUsesEnglish.Checked = true;
        _otherCommandsUseEnglish.Checked = true;
        _commandEndUsesEnglish.Checked = true;
        _documentActivatedUsesEnglish.Checked = true;
        _showErrorReport.Checked = false;
        var defaults = new HashSet<string>(ImeCommandCatalog.Recommended, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _recommendedCommands.Items.Count; index++)
            _recommendedCommands.SetItemChecked(index, defaults.Contains(_recommendedCommands.Items[index]?.ToString() ?? ""));
        _customCommands.Items.Clear();
        _loadingLanguage = true;
        _configuredInputMethodUnavailable = SelectLanguage(_inputMethod, "00000804", "");
        _loadingLanguage = false;
        UpdateLanguageWarning();
        UpdateStatus();
    }

    private void AddCustomCommand()
    {
        var command = ImeCommandCatalog.Normalize(_customCommand.Text);
        if (!CadCommandCatalog.IsSafeCommandName(command))
        {
            MessageBox.Show(this, "只填写英文完整命令名，例如 HZQ-155、SUN-TEXT 或 MYTEXT。",
                "命令格式无效", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var recommendedIndex = ImeCommandCatalog.Recommended
            .Select((item, index) => new { Item = item, Index = index })
            .FirstOrDefault(pair => pair.Item.Equals(command, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;
        if (recommendedIndex >= 0)
            _recommendedCommands.SetItemChecked(recommendedIndex, true);
        else if (!_customCommands.Items.Cast<object>().Any(item =>
                     item.ToString().Equals(command, StringComparison.OrdinalIgnoreCase)))
            _customCommands.Items.Add(command);
        _customCommand.Clear();
        UpdateStatus();
    }

    private void DeleteCustomCommand()
    {
        var index = _customCommands.SelectedIndex;
        if (index < 0) return;
        _customCommands.Items.RemoveAt(index);
        if (_customCommands.Items.Count > 0)
            _customCommands.SelectedIndex = Math.Min(index, _customCommands.Items.Count - 1);
        UpdateStatus();
    }

    private void SetAllRecommendedCommands(bool value)
    {
        for (var index = 0; index < _recommendedCommands.Items.Count; index++)
            _recommendedCommands.SetItemChecked(index, value);
        UpdateStatus();
    }

    private void InvertRecommendedCommands()
    {
        for (var index = 0; index < _recommendedCommands.Items.Count; index++)
            _recommendedCommands.SetItemChecked(index, !_recommendedCommands.GetItemChecked(index));
        UpdateStatus();
    }

    private void UpdateEnabledState()
    {
        _enabledCard.BackColor = _enabled.Checked ? Color.FromArgb(231, 247, 249) : Surface;
        _enabledHint.Text = _enabled.Checked
            ? "已有文字按内容判断，新建文字按中文命令名单；切换后仍可手动调整。"
            : "当前已停用。保存后不会自动切换输入法。";
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var languages = ImeNativeBridge.GetInstalledLanguages();
        var hasChinese = languages.Any(language => language.IsChinese && language.IsInputMethod);
        var state = _enabled.Checked ? "已启用" : "已停用";
        var commandCount = _recommendedCommands.CheckedItems.Count + _customCommands.Items.Count;
        var languageState = hasChinese
            ? "使用同一输入法"
            : "缺少可用的中文输入法";
        var preference = _directInputUsesEnglish.Checked
            ? "直接输入英文"
            : "直接输入保持当前模式";
        var commandState = commandCount > 0
            ? $"{commandCount} 个中文命令"
            : "未配置额外中文命令";
        var reportState = _showErrorReport.Checked ? "异常文本报告已开启" : "异常只记日志";
        _status.Text = $"{state} · {preference} · {languageState} · {commandState} · {reportState}";
        _status.ForeColor = hasChinese && !_configuredInputMethodUnavailable
            ? Muted
            : Color.FromArgb(190, 70, 45);
    }

    private void UpdateLanguageWarning()
    {
        _inputMethodWarning.Text = _configuredInputMethodUnavailable
            ? "当前设置不可用；运行时将直接沿用 Windows 当前输入法，只有保存后才会更新选择。"
            : "";
    }

    private static bool SelectLanguage(ComboBox combo, string layoutId, string profileId)
    {
        var requestedProfile = (profileId ?? "").Trim();
        if (requestedProfile.Length > 0)
        {
            for (var index = 0; index < combo.Items.Count; index++)
            {
                if (combo.Items[index] is not ImeLanguageChoice choice
                    || !choice.SelectionId.Equals(requestedProfile, StringComparison.OrdinalIgnoreCase)) continue;
                combo.SelectedIndex = index;
                return false;
            }
        }
        if (ImeTsfProfileCatalog.TryGetActiveSelectionId(out var activeSelection))
        {
            for (var index = 0; index < combo.Items.Count; index++)
            {
                if (combo.Items[index] is not ImeLanguageChoice choice
                    || !choice.SelectionId.Equals(activeSelection, StringComparison.OrdinalIgnoreCase)) continue;
                combo.SelectedIndex = index;
                return requestedProfile.Length > 0;
            }
        }
        if (requestedProfile.Length > 0)
        {
            combo.SelectedIndex = -1;
            return true;
        }
        var normalized = ImeNativeBridge.NormalizeLayoutId(layoutId, "00000804");
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is not ImeLanguageChoice choice
                || !choice.IsInputMethod || !ImeNativeBridge.SameLayout(choice.LayoutId, normalized)) continue;
            combo.SelectedIndex = index;
            return false;
        }
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is not ImeLanguageChoice choice
                || !choice.IsChinese) continue;
            combo.SelectedIndex = index;
            return false;
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return false;
    }

    private static ImeSwitchConfig Clone(ImeSwitchConfig source) => new()
    {
        Enabled = source.Enabled,
        DirectInputUsesEnglish = source.DirectInputUsesEnglish,
        TextCommandsUseChinese = true,
        OtherCommandsUseEnglish = source.OtherCommandsUseEnglish,
        CommandEndUsesEnglish = source.CommandEndUsesEnglish,
        DocumentActivatedUsesEnglish = source.DocumentActivatedUsesEnglish,
        ShowErrorReport = source.ShowErrorReport,
        RestoreOnShutdown = false,
        InputMethodLayoutId = source.InputMethodLayoutId,
        InputMethodProfileId = source.InputMethodProfileId,
        EnglishLayoutId = source.EnglishLayoutId,
        ChineseLayoutId = source.ChineseLayoutId,
        EnglishProfileId = source.EnglishProfileId,
        ChineseProfileId = source.ChineseProfileId,
        TextCommands = (source.TextCommands ?? new List<string>()).ToList()
    };

    private static Panel CreateCard(int height) => new()
    {
        Dock = DockStyle.Top,
        Height = height,
        Margin = new Padding(0, 0, 0, 10),
        Padding = new Padding(16, 10, 16, 10),
        BackColor = Surface,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Label CreateSectionTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 28,
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        ForeColor = Ink
    };

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Ink
    };

    private static void ConfigureCheck(CheckBox check, string text)
    {
        check.Text = text;
        check.Dock = DockStyle.Fill;
        check.AutoSize = true;
        check.ForeColor = Ink;
    }

    private static void ConfigureLanguageCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.IntegralHeight = false;
        combo.DropDownHeight = 180;
    }

    private static Button CreateButton(string text, int width, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : Ink,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.BorderSize = 1;
        button.Margin = new Padding(4, 0, 0, 0);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 92, 110) : Color.FromArgb(230, 240, 242);
        return button;
    }
}
