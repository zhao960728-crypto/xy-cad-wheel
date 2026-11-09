using System.Drawing;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private readonly ComboBox _wheelStyle = new();
    private TableLayoutPanel _layoutFields;
    private TableLayoutPanel _appearanceFields;
    private TableLayoutPanel _currentFields;
    private readonly ComboBox _contentMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, MinimumSize = new Size(0, 26) };
    private bool _updatingContentMode;
    private readonly ComboBox _cellContentMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, MinimumSize = new Size(0, 26) };
    private bool _updatingCellContentMode;
    private readonly Label _styleHelp = new() { Dock = DockStyle.Top, Height = 54 };
    private readonly TextBox _groupName = new();
    private readonly NumericUpDown _nodeInnerSize = new() { Minimum = 50, Maximum = 150, Value = 100, Increment = 5 };
    private readonly NumericUpDown _nodeOuterSize = new() { Minimum = 50, Maximum = 150, Value = 100, Increment = 5 };
    private bool _updatingNodeSizes;
    private readonly TrackBar _surfaceOpacity = new() { Minimum = 0, Maximum = 95, TickStyle = TickStyle.None };
    private readonly Label _surfaceOpacityValue = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private bool _practiceMode;
    private readonly Label _saveStatus = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private string _savedSnapshot;
    private readonly ComboBox _commandSource = new();
    private Point _dragStart;
    private RadialCellSelectedEventArgs _dragCell;

    private void HookWheelDragDrop()
    {
        _preview.AllowDrop = true;
        _catalogTree.ItemDrag += (_, e) =>
        {
            if (!_batchMode && e.Item is TreeNode node && (node.Tag is CadCommandInfo || node.Tag is CustomCommandConfig))
                _catalogTree.DoDragDrop(node.Tag, DragDropEffects.Copy);
        };
        _preview.MouseDown += (_, e) =>
        {
            _dragStart = e.Location;
            _dragCell = !_practiceMode && !_preview.BatchSelectionMode && !_preview.ResizeMode && _config.Menus[SceneKeys.Global].StyleId != "free" && e.Button == MouseButtons.Left ? _preview.HitCell(e.Location) : null;
        };
        _preview.MouseMove += (_, e) =>
        {
            if (_dragCell == null || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - _dragStart.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - _dragStart.Y) < SystemInformation.DragSize.Height) return;
            var source = _dragCell;
            _dragCell = null;
            try { _preview.DoDragDrop(source, DragDropEffects.Move); }
            finally { _dragCell = null; }
        };
        _preview.MouseUp += (_, e) =>
        {
            var clickedCell = _dragCell;
            _dragCell = null;
            if (clickedCell != null && e.Button == MouseButtons.Left && _compactPreviewOpen)
            {
                _compactPreviewOpen = false;
                _applyResponsiveChrome?.Invoke();
            }
        };
        _preview.DragEnter += (_, e) => e.Effect = _practiceMode || _preview.BatchSelectionMode ? DragDropEffects.None : e.Data.GetDataPresent(typeof(FreeCellDrag)) ? (_config.Menus[SceneKeys.Global].StyleId == "free" ? DragDropEffects.Copy : DragDropEffects.None) : e.Data.GetDataPresent(typeof(CellCountDrag)) ? DragDropEffects.Copy : e.Data.GetDataPresent(typeof(RadialCellSelectedEventArgs))
            ? DragDropEffects.Move : e.Data.GetDataPresent(typeof(CadCommandInfo)) || e.Data.GetDataPresent(typeof(CustomCommandConfig)) ? DragDropEffects.Copy : DragDropEffects.None;
        _preview.DragOver += (_, e) =>
        {
            var point = _preview.PointToClient(new Point(e.X, e.Y));
            if (_practiceMode || _preview.BatchSelectionMode) { e.Effect = DragDropEffects.None; return; }
            if (e.Data.GetDataPresent(typeof(FreeCellDrag)))
            { e.Effect = _config.Menus[SceneKeys.Global].StyleId == "free" ? DragDropEffects.Copy : DragDropEffects.None; return; }
            if (e.Data.GetData(typeof(CellCountDrag)) is CellCountDrag countDrag)
            {
                var drop = ResolveCountDrop(point, countDrag.Add);
                e.Effect = drop == null ? DragDropEffects.None : DragDropEffects.Copy;
                _preview.UpdateCursor(point);
                _previewSelection.Text = drop == null ? "拖＋到两格之间；拖－到格子上（每圈 1–32 格）"
                    : $"第 {drop.RingIndex + 1} 圈：松开后{(countDrag.Add ? "插入第" : "删除第")} {drop.Slot + 1} 格";
                return;
            }
            _preview.UpdateCursor(point);
            e.Effect = _practiceMode || _preview.HitCell(point) == null ? DragDropEffects.None
                : e.Data.GetDataPresent(typeof(RadialCellSelectedEventArgs)) ? DragDropEffects.Move
                : e.Data.GetDataPresent(typeof(CadCommandInfo)) || e.Data.GetDataPresent(typeof(CustomCommandConfig)) ? DragDropEffects.Copy : DragDropEffects.None;
        };
        _preview.DragDrop += (_, e) =>
        {
            if (_practiceMode || _preview.BatchSelectionMode) return;
            if (e.Data.GetData(typeof(FreeCellDrag)) is FreeCellDrag freeDrag)
            { AddFreeCell(_preview.PointToClient(new Point(e.X, e.Y)), freeDrag.Shape); return; }
            if (e.Data.GetData(typeof(CellCountDrag)) is CellCountDrag countDrag)
            {
                ApplyCountDrop(_preview.PointToClient(new Point(e.X, e.Y)), countDrag.Add);
                return;
            }
            var target = _preview.HitCell(_preview.PointToClient(new Point(e.X, e.Y)));
            if (target == null || _practiceMode) return;
            var wheel = _config.Menus[SceneKeys.Global];
            var to = wheel.Rings.First(r => r.Index == target.RingIndex);
            var old = to.Items.FirstOrDefault(i => i.Slot == target.Slot);
            MenuItemConfig item;
            if (e.Data.GetData(typeof(RadialCellSelectedEventArgs)) is RadialCellSelectedEventArgs fromCell)
            {
                if (fromCell.RingIndex == target.RingIndex && fromCell.Slot == target.Slot) return;
                var from = wheel.Rings.First(r => r.Index == fromCell.RingIndex);
                WheelCellEditing.Swap(from, fromCell.Slot, to, target.Slot);
                RefreshPreview();
                _preview.SelectCell(target.RingIndex, target.Slot);
                RefreshWheelGrid();
                return;
            }
            else
            {
                if (e.Data.GetData(typeof(CadCommandInfo)) is CadCommandInfo command)
                    item = new MenuItemConfig { Label = GetCommandDisplayName(command), Value = command.GlobalName };
                else if (e.Data.GetData(typeof(CustomCommandConfig)) is CustomCommandConfig custom)
                    item = new MenuItemConfig { Label = GetCommandDisplayName(custom), Kind = ActionKinds.CustomCommand, Value = custom.Id };
                else return;
                item.ContentMode = old?.ContentMode ?? "Inherit";
                item.CellSizePercent = old?.CellSizePercent ?? 100;
                item.CellShape = old?.CellShape ?? "Circle";
                item.FreeX = old?.FreeX; item.FreeY = old?.FreeY;
                item.IconRatioPercent = old?.IconRatioPercent ?? 55;
                item.FontSizePx = old?.FontSizePx ?? 0;
                item.FontFamily = old?.FontFamily ?? "";
                item.TextColor = old?.TextColor ?? "";
                item.FontBold = old?.FontBold;
                item.TextEffect = old?.TextEffect ?? "";
                if (old != null) { to.Items.Remove(old); wheel.UnassignedItems.Add(old); }
            }
            item.Slot = target.Slot;
            to.Items.Add(item);
            RefreshPreview();
            _preview.SelectCell(target.RingIndex, target.Slot);
            RefreshWheelGrid();
        };
    }

    private static TableLayoutPanel EditorFields(TabPage page)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 14, 12, 12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(table);
        return table;
    }

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("轮盘编辑") { Padding = new Padding(8) };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var layout = new TabPage("尺寸与布局") { AutoScroll = true, Padding = new Padding(4) };
        var appearance = new TabPage("背景与配色") { AutoScroll = true, Padding = new Padding(4) };
        var current = new TabPage("图标与文字") { Padding = new Padding(4) };
        var cellTabs = new TabControl { Dock = DockStyle.Fill };
        var displayPage = new TabPage("显示与功能") { AutoScroll = true, Padding = new Padding(8) };
        var typographyPage = new TabPage("文字与图片") { AutoScroll = true, Padding = new Padding(4) };
        cellTabs.TabPages.AddRange(new[] { displayPage, typographyPage });
        current.Controls.Add(cellTabs);
        tabs.TabPages.AddRange(new[] { layout, appearance, current });
        tabs.SelectedIndexChanged += (_, _) => EndPreviewEditing();
        cellTabs.SelectedIndexChanged += (_, _) => EndPreviewEditing();
        _layoutFields = EditorFields(layout);
        _layoutFields.SuspendLayout();
        ConfigureNumeric(_menuSize, 260, 900, 10);
        ConfigureNumeric(_centerRadius, 20, 250, 2);
        ConfigureNumeric(_ringCount, 1, RingConfig.MaximumRingCount, 1);
        ConfigureNumeric(_cellFontSize, 6, 72, 1);
        AddEditorRow(_layoutFields, 0, "整体尺寸 px", _menuSize);
        AddEditorRow(_layoutFields, 1, "中心圆半径 px", _centerRadius);
        AddEditorRow(_layoutFields, 2, "圈数 / 分组数", BuildRingCountWithPresets());
        _ringEditors.AutoSize = true;
        _ringEditors.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _ringEditors.ColumnCount = 7;
        foreach (var width in new[] { 60, 44, 58, 44, 82, 60, 80 })
            _ringEditors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        AddEditorRow(_layoutFields, 3, "各圈 / 组容量", _ringEditors);
        var ringCaption = (Label)_layoutFields.GetControlFromPosition(0, 3);
        ringCaption.Dock = DockStyle.Top;
        ringCaption.Height = 38;
        ringCaption.Margin = _ringEditors.Margin;
        var group = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, MinimumSize = new Size(0, 38) };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        _groupName.Dock = DockStyle.Fill;
        group.MaximumSize = new Size(360, 0);
        var rename = new Button { Text = "设置标题", Dock = DockStyle.Fill };
        rename.Click += (_, _) =>
        {
            var ring = ResolveTargetRing(_catalogTarget);
            ring.Name = _groupName.Text.Trim();
            RefreshPreview();
        };
        group.Controls.Add(_groupName, 0, 0);
        group.Controls.Add(rename, 1, 0);
        AddEditorRow(_layoutFields, 4, "分组标题", group);
        var groupNote = new Label { AutoSize = true, Text = "用于当前分组的入口或页签；先在预览中选中该组，再修改标题。", MaximumSize = new Size(480, 0), Padding = new Padding(0, 6, 0, 10) };
        _layoutFields.Controls.Add(groupNote, 0, 5);
        _layoutFields.SetColumnSpan(groupNote, 2);
        AddEditorRow(_layoutFields, 6, "内圈格子大小 %", _nodeInnerSize);
        AddEditorRow(_layoutFields, 7, "外圈格子大小 %", _nodeOuterSize);
        var nodeSizeNote = new Label { AutoSize = true, MaximumSize = new Size(480, 0), Padding = new Padding(0, 6, 0, 10), Text = "调整当前分组的格子大小。放大受可用间距限制：同圈格子之间、内外圈之间均至少保留 8 px。" };
        _layoutFields.Controls.Add(nodeSizeNote, 0, 8);
        _layoutFields.SetColumnSpan(nodeSizeNote, 2);
        _nodeInnerSize.ValueChanged += (_, _) => ApplyNodeSizes();
        _nodeOuterSize.ValueChanged += (_, _) => ApplyNodeSizes();

        _appearanceFields = EditorFields(appearance);
        _appearanceFields.SuspendLayout();
        var palette = new Button { Text = "打开调色盘 · 单色 / 渐变", AutoSize = true };
        palette.Click += (_, _) =>
        {
            var wheel = _config.Menus[SceneKeys.Global];
            using var dialog = new WheelAppearanceDialog(wheel.Appearance);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            wheel.Appearance = dialog.Result;
            RefreshPreview();
        };
        AddEditorRow(_appearanceFields, 0, "界面配色", palette);
        AddEditorRow(_appearanceFields, 1, "整体透明度", BuildMenuOpacityEditor());
        AddEditorRow(_appearanceFields, 2, "背景图片", BuildBackgroundPicker());
        AddEditorRow(_appearanceFields, 3, "图片透明度", BuildBackgroundOpacityEditor());
        var opacityRow = new TableLayoutPanel { ColumnCount = 2, Height = 38, MaximumSize = new Size(300, 38), Dock = DockStyle.Fill };
        opacityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        opacityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        _surfaceOpacity.Dock = DockStyle.Fill;
        _surfaceOpacityValue.Dock = DockStyle.Fill;
        opacityRow.Controls.Add(_surfaceOpacity, 0, 0);
        opacityRow.Controls.Add(_surfaceOpacityValue, 1, 0);
        _surfaceOpacity.ValueChanged += (_, _) =>
        {
            if (_loadingUi) return;
            _config.Menus[SceneKeys.Global].SurfaceOpacityPercent = 100 - _surfaceOpacity.Value;
            _surfaceOpacityValue.Text = _surfaceOpacity.Value + "%";
            RefreshPreview();
        };
        AddEditorRow(_appearanceFields, 4, "背景透明度", opacityRow);
        var appearanceNote = new Label { AutoSize = true, MaximumSize = new Size(480, 0), Text = "图标轮盘与独立格轮盘可独立调整底板透明度，图标与文字保持清晰。经典轮盘继续使用原来的透明度和背景图片设置。", Padding = new Padding(0, 12, 0, 12) };
        _appearanceFields.Controls.Add(appearanceNote, 0, 5);
        _appearanceFields.SetColumnSpan(appearanceNote, 2);

        _currentFields = EditorFields(typographyPage);
        _currentFields.SuspendLayout();
        var commandEditor = BuildSelectedCommandEditor();
        commandEditor.MaximumSize = new Size(600, 0);
        displayPage.Controls.Add(commandEditor);
        _contentMode.Items.AddRange(new object[] { "自动图文", "仅图标", "仅文字", "图标与文字", "纯图片（铺满格子）", "图片与文字（铺满格子）" });
        _contentMode.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingUi || _updatingContentMode || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
            wheel.ContentMode = _contentMode.SelectedIndex == 1 ? "IconOnly" : _contentMode.SelectedIndex == 2 ? "TextOnly" : _contentMode.SelectedIndex == 3 ? "IconAndText" : _contentMode.SelectedIndex == 4 ? "ImageOnly" : _contentMode.SelectedIndex == 5 ? "ImageAndText" : "Auto";
            RefreshPreview();
        };
        AddEditorRow(_currentFields, 0, "全局显示方式", _contentMode);
        AddEditorRow(_currentFields, 1, "当前格字号 px", BuildCellFontSizeEditor());
        AddEditorRow(_currentFields, 2, "字体", BuildFontFamilyEditor());
        AddEditorRow(_currentFields, 3, "文字样式", BuildTextStyleEditor());
        AddEditorRow(_currentFields, 4, "文字颜色", BuildTextColorPicker());
        var icon = new Button { Text = "图标库 / 自选图片 / 裁剪", AutoSize = true };
        icon.Click += (_, _) =>
        {
            if (!TryGetSelectedWheelItem(out var item))
            {
                MessageBox.Show(this, "请先在预览中选中已绑定命令的位置。", "自定义图标");
                return;
            }
            using var dialog = new CommandIconDialog(item.IconData, item.ImageMode);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            item.IconData = dialog.ResultIconData;
            item.ImageMode = dialog.ResultImageMode;
            RefreshPreview();
        };
        AddEditorRow(_currentFields, 5, "命令图标", icon);
        var tools = BuildWheelClearTools();
        _currentFields.Controls.Add(tools, 0, 6);
        _currentFields.SetColumnSpan(tools, 2);
        _cellContentMode.Items.AddRange(new object[] { "跟随全局", "图标与文字", "仅文字", "仅图标", "纯图片（铺满格子）", "图片与文字（铺满格子）" });
        _cellContentMode.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingUi || _updatingCellContentMode || !TryGetSelectedWheelItem(out var item)) return;
            item.ContentMode = CellContentValue(_cellContentMode.SelectedIndex);
            RefreshPreview();
        };
        AddEditorRow(_currentFields, 7, "当前格显示", _cellContentMode);
        AddEditorRow(_currentFields, 8, "图标占比 %", _cellIconRatio);
        _cellIconRatio.ValueChanged += (_, _) =>
        {
            if (_loadingUi || _loadingDesignFields || !TryGetSelectedWheelItem(out var item)) return;
            item.IconRatioPercent = (int)_cellIconRatio.Value;
            RefreshPreview();
        };
        typographyPage.Controls.Add(BuildBatchAppearancePanel());
        var batchAppearance = new Button { Text = "批量样式 · 在预览中选择", AutoSize = true, Dock = DockStyle.Top, Tag = "primary" };
        batchAppearance.Click += (_, _) => ShowBatchCellAppearance();
        typographyPage.Controls.Add(batchAppearance);

        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, ColumnCount = 3 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        _wheelStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        _wheelStyle.Items.AddRange(new object[] { "经典轮盘", "图标轮盘", "分组面板", "节点轮盘", "独立格轮盘", "自设计轮盘" });
        _wheelStyle.Dock = DockStyle.Fill;
        _wheelStyle.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingUi || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
            EndPreviewEditing();
            wheel.StyleId = _wheelStyle.SelectedIndex == 1 ? "01" : _wheelStyle.SelectedIndex == 2 ? "11" : _wheelStyle.SelectedIndex == 3 ? "06" : _wheelStyle.SelectedIndex == 4 ? "orbit" : _wheelStyle.SelectedIndex == 5 ? "free" : "classic";
            UpdateStyleEditors();
            RefreshPreview();
        };
        var unused = new Button { Text = "未分配命令", Dock = DockStyle.Fill };
        unused.Click += (_, _) => ShowUnassignedItems();
        header.Controls.Add(_wheelStyle, 0, 0);
        header.Controls.Add(unused, 1, 0);
        var practice = _practiceButton = new Button { Text = "练习模式", Dock = DockStyle.Fill };
        practice.Click += (_, _) =>
        {
            var enterPractice = !_practiceMode;
            EndPreviewEditing();
            _practiceMode = enterPractice;
            _preview.EnableCellSelection = !_practiceMode;
            practice.Text = _practiceMode ? "退出练习" : "练习模式";
            _previewSelection.Text = _practiceMode ? "练习中：选择命令仅显示提示，不执行 CAD 命令" : "点击位置，设置当前命令";
        };
        _preview.ItemSelected += (_, item) =>
        {
            if (_practiceMode) _previewSelection.Text = "练习选中：" + item.Label;
        };
        _preview.CancelRequested += (_, _) =>
        {
            if (_practiceMode) _previewSelection.Text = "练习取消；可继续选择";
        };
        header.Controls.Add(practice, 2, 0);
        page.Controls.Add(tabs);
        page.Controls.Add(_styleHelp);
        page.Controls.Add(header);
        _layoutFields.ResumeLayout(true);
        _appearanceFields.ResumeLayout(true);
        _currentFields.ResumeLayout(true);
        return page;
    }

    private void UpdateStyleEditors()
    {
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        var original = wheel.StyleId == "classic";
        var circles = original || wheel.StyleId == "01" || wheel.StyleId == "orbit";
        var nodeStyle = wheel.StyleId == "06";
        if (_ringPresetButton != null) _ringPresetButton.Visible = wheel.StyleId == "classic" || wheel.StyleId == "01";
        var countCaption = _layoutFields.GetControlFromPosition(0, 2);
        if (countCaption != null) countCaption.Text = wheel.StyleId == "11" ? "分组数" : "圈数";
        var capacityCaption = _layoutFields.GetControlFromPosition(0, 3);
        if (capacityCaption != null) capacityCaption.Text = wheel.StyleId == "11" ? "分组容量" : "各圈设置";
        var titleCaption = _layoutFields.GetControlFromPosition(0, 4);
        if (titleCaption != null) titleCaption.Text = nodeStyle ? "圈入口标题" : "分组标题";
        var titleNote = _layoutFields.GetControlFromPosition(0, 5);
        if (titleNote != null) titleNote.Text = nodeStyle ? "用于当前圈的入口；先在预览中选中该圈，再修改标题。" : "用于当前分组的入口或页签；先在预览中选中该组，再修改标题。";
        _surfaceOpacity.Value = 100 - Math.Max(5, Math.Min(100, wheel.SurfaceOpacityPercent));
        _surfaceOpacityValue.Text = _surfaceOpacity.Value + "%";
        foreach (Control control in _layoutFields.Controls)
        {
            var row = _layoutFields.GetRow(control);
            if (row == 1) control.Visible = circles;
            if (row == 4 || row == 5) control.Visible = !circles;
        }
        _updatingContentMode = true;
        try { _contentMode.SelectedIndex = wheel.ContentMode == "IconOnly" ? 1 : wheel.ContentMode == "TextOnly" ? 2 : wheel.ContentMode == "IconAndText" ? 3 : wheel.ContentMode == "ImageOnly" ? 4 : wheel.ContentMode == "ImageAndText" ? 5 : 0; }
        finally { _updatingContentMode = false; }
        foreach (Control control in _currentFields.Controls)
            if (_currentFields.GetRow(control) == 0) control.Visible = true;
        foreach (Control control in _appearanceFields.Controls)
        {
            var row = _appearanceFields.GetRow(control);
            if (row == 0) control.Visible = !original;
            if (row >= 1 && row <= 3) control.Visible = original || (row == 1 && wheel.StyleId != "01" && wheel.StyleId != "orbit");
            if (row == 4) control.Visible = wheel.StyleId == "01" || wheel.StyleId == "orbit" || wheel.StyleId == "free";
        }
        var hasCellSize = wheel.StyleId == "06" || wheel.StyleId == "orbit";
        foreach (Control control in _ringEditors.Controls)
        {
            var column = _ringEditors.GetColumn(control);
            if (column == 3 || column == 4) control.Visible = circles;
            if (column == 5 || column == 6) control.Visible = hasCellSize;
        }
        if (_ringEditors.ColumnStyles.Count >= 7)
        {
            _ringEditors.ColumnStyles[3].Width = circles ? ScaleLogical(44) : 0;
            _ringEditors.ColumnStyles[4].Width = circles ? ScaleLogical(82) : 0;
            _ringEditors.ColumnStyles[5].Width = hasCellSize ? ScaleLogical(112) : 0;
            _ringEditors.ColumnStyles[6].Width = hasCellSize ? ScaleLogical(80) : 0;
        }
        RefreshNodeSizeEditors();
        _styleHelp.Text = original ? "经典轮盘保留原来的显示与操作。切换样式不会改变命令绑定。"
            : wheel.StyleId == "01" ? "图标按方向排列；指向后突出显示命令。背景透明，图标和文字清晰。"
            : wheel.StyleId == "free" ? "拖动独立格自由摆放；用预览下方的格子形状和调整大小设计，格子之间保留间距。"
            : wheel.StyleId == "orbit" ? "各圈独立圆格同时展开。圈径用于安排位置；可分别调整容量和格子大小，空间不足时自动调整位置与大小。不分页、不连线。"
            : wheel.StyleId == "06" ? "每个入口对应第 N 圈，优先单圈；命令较多时向内错位扩展第二、第三圈。各圈格子一样大，不分页。"
            : "每一圈对应一个分组，组内命令一次展示，不分页。分组标题用于入口或页签；隐藏的圆环尺寸与背景配置仍然保留。";
    }

    private void RefreshNodeSizeEditors()
    {
        if (_layoutFields == null || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        var ring = wheel.Rings.FirstOrDefault(item => item.Index == (int)_catalogTarget.Ring.Value - 1);
        _updatingNodeSizes = true;
        try
        {
            _nodeInnerSize.Value = Math.Max(50, Math.Min(150, ring?.NodeInnerSizePercent ?? 100));
            _nodeOuterSize.Value = Math.Max(50, Math.Min(150, ring?.NodeOuterSizePercent ?? 100));
            if (ring != null && _ringEditors.GetControlFromPosition(6, ring.Index) is NumericUpDown rowSize)
                rowSize.Value = _nodeInnerSize.Value;
            var caption = _layoutFields.GetControlFromPosition(0, 6);
            if (caption != null) caption.Text = "当前圈格子大小 %";
            var note = _layoutFields.GetControlFromPosition(0, 8);
            if (note != null) note.Text = "调整当前圈的格子大小。放大受可用间距限制：同圈格子之间、内外圈之间均至少保留 8 px。";
            foreach (Control control in _layoutFields.Controls)
            {
                var row = _layoutFields.GetRow(control);
                if (row == 6 || row == 7 || row == 8) control.Visible = false;
            }
        }
        finally { _updatingNodeSizes = false; }
    }

    private void ApplyNodeSizes()
    {
        if (_loadingUi || _updatingNodeSizes || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel) || (wheel.StyleId != "06" && wheel.StyleId != "orbit")) return;
        var ring = wheel.Rings.FirstOrDefault(item => item.Index == (int)_catalogTarget.Ring.Value - 1);
        if (ring == null) return;
        ring.NodeInnerSizePercent = (int)_nodeInnerSize.Value;
        if (wheel.StyleId == "06") ring.NodeOuterSizePercent = (int)_nodeOuterSize.Value;
        RefreshPreview();
        // A size edit should reveal the selected group, while opening settings
        // alone continues to show the node style's root navigation.
        var selectionEnabled = _preview.EnableCellSelection;
        _preview.EnableCellSelection = true;
        try { _preview.SelectCell(ring.Index, Math.Max(0, Math.Min(ring.Slots - 1, (int)_catalogTarget.Slot.Value - 1))); }
        finally { _preview.EnableCellSelection = selectionEnabled; }
    }

    private void UpdateSaveStatus()
    {
        if (_loadingUi || _savedSnapshot == null) return;
        _saveStatus.Text = JsonCompat.Serialize(_config, false) == _savedSnapshot
            ? "设置已保存" : "有未保存的调整 · 点击应用并保存";
    }

    private TabPage BuildMaintenanceTab()
    {
        var page = new TabPage("备份与维护") { AutoScroll = true, Padding = new Padding(16) };
        var fields = EditorFields(page);
        var audit = new Button { Text = "检查快捷命令重复 / 占用", AutoSize = true };
        audit.Click += (_, _) => ShowShortcutUsageReport();
        AddEditorRow(fields, 0, "快捷命令检查", audit);
        var backup = BuildSettingsTools();
        fields.Controls.Add(backup, 0, 1);
        fields.SetColumnSpan(backup, 2);
        return page;
    }

    private void ShowUnassignedItems()
    {
        var wheel = _config.Menus[SceneKeys.Global];
        using var dialog = new Form { Text = "未分配命令 · 添加到预览所选位置", StartPosition = FormStartPosition.CenterParent, Size = new Size(520, 420), MinimizeBox = false, MaximizeBox = false };
        var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = nameof(MenuItemConfig.Label) };
        list.Items.AddRange(wheel.UnassignedItems.Cast<object>().ToArray());
        var place = new Button { Text = "放入当前选中的格子", Dock = DockStyle.Bottom, Height = 42 };
        place.Click += (_, _) =>
        {
            if (list.SelectedItem is not MenuItemConfig item) return;
            var ring = ResolveTargetRing(_catalogTarget);
            var slot = (int)_catalogTarget.Slot.Value - 1;
            var old = ring.Items.FirstOrDefault(entry => entry.Slot == slot);
            wheel.UnassignedItems.Remove(item);
            if (old != null) { ring.Items.Remove(old); wheel.UnassignedItems.Add(old); }
            item.Slot = slot;
            ring.Items.Add(item);
            RefreshPreview();
            RefreshWheelGrid();
            LoadSelectedCellAppearance();
            dialog.Close();
        };
        dialog.Controls.Add(list);
        dialog.Controls.Add(place);
        dialog.ShowDialog(this);
    }
}
