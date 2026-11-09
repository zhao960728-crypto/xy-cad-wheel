using System.Drawing;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private Panel _batchAppearancePanel;
    private FlowLayoutPanel _batchSelectionTools;
    private Action _applyBatchAppearance;
    private readonly Label _batchAppearanceStatus = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly NumericUpDown _cellIconRatio = new() { Minimum = 10, Maximum = 90, Value = 55, Increment = 5 };
    private bool _loadingDesignFields;
    private Button _practiceButton;

    private void EndPreviewEditing()
    {
        var wasEditing = _preview.BatchSelectionMode || _preview.ResizeMode || _practiceMode;
        if (_preview.BatchSelectionMode) EndBatchAppearance();
        _preview.EndGeometryEditing();
        _practiceMode = false;
        _preview.EnableCellSelection = true;
        if (_resizeCellButton != null) _resizeCellButton.Text = "调整单格大小";
        if (_practiceButton != null) _practiceButton.Text = "练习模式";
        if (wasEditing) _previewSelection.Text = $"已选择：第 {_catalogTarget.Ring.Value} 圈 / 第 {_catalogTarget.Slot.Value} 格";
        _preview.Invalidate();
    }

    private void ShowBatchCellAppearance()
    {
        if (_practiceMode) return;
        _preview.BatchSelectionMode = true;
        _preview.ResizeMode = false;
        if (_resizeCellButton != null) _resizeCellButton.Text = "调整单格大小";
        _preview.BatchSelection.Clear();
        _batchAppearancePanel.Visible = true;
        _currentFields.Visible = false;
        _batchSelectionTools.Visible = true;
        _batchAppearancePanel.BringToFront();
        UpdateBatchSelectionStatus();
        _preview.Invalidate();
    }

    private void EndBatchAppearance()
    {
        _preview.BatchSelectionMode = false;
        _preview.BatchSelection.Clear();
        if (_batchAppearancePanel != null) _batchAppearancePanel.Visible = false;
        if (_currentFields != null) _currentFields.Visible = true;
        if (_batchSelectionTools != null) _batchSelectionTools.Visible = false;
        _previewSelection.Text = $"已选择：第 {_catalogTarget.Ring.Value} 圈 / 第 {_catalogTarget.Slot.Value} 格";
        _preview.Invalidate();
    }

    private void UpdateBatchSelectionStatus()
    {
        _batchAppearanceStatus.Text = $"已选择 {_preview.BatchSelection.Count} 格。勾选要修改的属性，再点预览下方 ✓ 应用；✕ 取消不改动。";
        _previewSelection.Text = $"批量选择：{_preview.BatchSelection.Count} 格（点击格子切换高亮）";
    }

    private Control BuildBatchSelectionTools()
    {
        _batchSelectionTools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Visible = false };
        foreach (var title in new[] { "全选", "当前圈", "反选", "清空", "✓ 应用", "✕ 取消" })
        {
            var button = new Button { Text = title, AutoSize = true, MinimumSize = new Size(62, 30) };
            button.Click += (_, _) =>
            {
                if (title == "✓ 应用") { _applyBatchAppearance?.Invoke(); return; }
                if (title == "✕ 取消") { EndBatchAppearance(); return; }
                var selected = _preview.BatchSelection;
                var all = _config.Menus[SceneKeys.Global].Rings.Where(r => r.Enabled)
                    .SelectMany(r => Enumerable.Range(0, r.Slots).Select(s => (r.Index, s))).ToList();
                var previous = selected.ToArray();
                selected.Clear();
                foreach (var cell in all)
                    if (title == "全选" || title == "当前圈" && cell.Index == (int)_catalogTarget.Ring.Value - 1
                        || title == "反选" && !previous.Contains(cell)) selected.Add(cell);
                UpdateBatchSelectionStatus();
                _preview.Invalidate();
            };
            _batchSelectionTools.Controls.Add(button);
        }
        return _batchSelectionTools;
    }

    private Control BuildBatchAppearancePanel()
    {
        _batchAppearancePanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Visible = false, Padding = new Padding(8) };
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var font = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        font.Items.AddRange(_fontFamily.Items.Cast<object>().ToArray());
        if (font.Items.Count > 0) font.SelectedIndex = 0;
        var size = new NumericUpDown { Minimum = 6, Maximum = 72, Value = 14, Width = 64 };
        var auto = new CheckBox { Text = "自动字号", Checked = true, AutoSize = true };
        auto.CheckedChanged += (_, _) => size.Enabled = !auto.Checked;
        size.Enabled = false;
        var sizePanel = new FlowLayoutPanel { AutoSize = true }; sizePanel.Controls.Add(size); sizePanel.Controls.Add(auto);
        var bold = new CheckBox { Text = "加粗", AutoSize = true };
        var effect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label", Width = 150 };
        effect.Items.AddRange(_textEffect.Items.Cast<object>().ToArray());
        if (effect.Items.Count > 0) effect.SelectedIndex = 0;
        var stylePanel = new FlowLayoutPanel { AutoSize = true }; stylePanel.Controls.Add(bold); stylePanel.Controls.Add(effect);
        var color = Color.Black;
        var colorButton = new Button { Text = "选择颜色", AutoSize = true };
        colorButton.Click += (_, _) => { using var picker = new ColorDialog { Color = color, FullOpen = true }; if (picker.ShowDialog(this) == DialogResult.OK) { color = picker.Color; colorButton.Text = ColorTranslator.ToHtml(color); } };
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        mode.Items.AddRange(new object[] { "跟随全局", "图标与文字", "仅文字", "仅图标", "纯图片（铺满格子）", "图片与文字（铺满格子）" }); mode.SelectedIndex = 0;
        var ratio = new NumericUpDown { Minimum = 10, Maximum = 90, Value = 55, Increment = 5 };
        var cellSize = new NumericUpDown { Minimum = 25, Maximum = 250, Value = 100, Increment = 5 };
        var shape = CreateCellShapeCombo();
        var labels = new[] { "字体", "字号", "文字样式", "文字颜色", "显示方式", "图标占比 %", "格子大小 %", "格子形状" };
        var editors = new Control[] { font, sizePanel, stylePanel, colorButton, mode, ratio, cellSize, shape };
        var changes = new List<CheckBox>();
        for (int i = 0; i < labels.Length; i++)
        {
            var check = new CheckBox { Text = labels[i], AutoSize = true }; changes.Add(check);
            fields.Controls.Add(check, 0, i); fields.Controls.Add(editors[i], 1, i);
        }
        fields.Controls.Add(_batchAppearanceStatus, 0, labels.Length); fields.SetColumnSpan(_batchAppearanceStatus, 2);
        _batchAppearancePanel.Controls.Add(fields);
        _applyBatchAppearance = () =>
        {
            if (_preview.BatchSelection.Count == 0 || !changes.Any(c => c.Checked)) { _batchAppearanceStatus.Text = "请在预览选择格子，并勾选要修改的属性。"; return; }
            if (!_preview.BatchSelectionMode) return;
            if (_preview.BatchSelection.Any(cell => !_config.Menus[SceneKeys.Global].Rings.Any(r =>
                    r.Enabled && r.Index == cell.Item1 && cell.Item2 >= 0 && cell.Item2 < r.Slots)))
            {
                _preview.BatchSelection.Clear();
                _batchAppearanceStatus.Text = "轮盘布局已改变，请重新选择格子。";
                _preview.Invalidate();
                return;
            }
            var sizeBackups = _config.Menus[SceneKeys.Global].Rings
                .Select(r => (Ring: r, Items: r.Items.ToArray(), Geometry: r.Items.Select(i => (Item: i, Size: i.CellSizePercent, X: i.FreeX, Y: i.FreeY)).ToArray())).ToList();
            if (changes[6].Checked)
            {
                var style = _config.Menus[SceneKeys.Global].StyleId;
                if (style == "classic" || style == "01")
                { _batchAppearanceStatus.Text = "完整格子大小适用于独立格、节点、分组面板与自设计。请先选择对应样式。"; return; }
                foreach (var cell in _preview.BatchSelection)
                {
                    var ring = _config.Menus[SceneKeys.Global].Rings.First(r => r.Index == cell.Item1);
                    if (!_preview.TrySetCellSize(cell.Item1, cell.Item2, (int)cellSize.Value))
                    {
                        foreach (var backup in sizeBackups)
                        {
                            backup.Ring.Items.Clear(); backup.Ring.Items.AddRange(backup.Items);
                            foreach (var geometry in backup.Geometry)
                            { geometry.Item.CellSizePercent = geometry.Size; geometry.Item.FreeX = geometry.X; geometry.Item.FreeY = geometry.Y; }
                        }
                        _preview.Invalidate();
                        _batchAppearanceStatus.Text = "所选大小会导致格子重叠或超出边界，未应用批量修改。请减小数值或先调整位置。";
                        return;
                    }
                }
            }
            foreach (var cell in _preview.BatchSelection)
            {
                var ring = _config.Menus[SceneKeys.Global].Rings.FirstOrDefault(r => r.Index == cell.Item1 && r.Enabled);
                if (ring == null || cell.Item2 >= ring.Slots) continue;
                var item = ring.Items.FirstOrDefault(i => i.Slot == cell.Item2);
                if (item == null) { item = new MenuItemConfig { Slot = cell.Item2 }; ring.Items.Add(item); }
                if (changes[0].Checked) item.FontFamily = font.SelectedItem?.ToString() ?? _config.FontFamily;
                if (changes[1].Checked) item.FontSizePx = auto.Checked ? 0 : (int)size.Value;
                if (changes[2].Checked) { item.FontBold = bold.Checked; item.TextEffect = (effect.SelectedItem as Choice)?.Value ?? TextEffects.None; }
                if (changes[3].Checked) item.TextColor = WheelAppearance.ToHex(color);
                if (changes[4].Checked) item.ContentMode = CellContentValue(mode.SelectedIndex);
                if (changes[5].Checked) item.IconRatioPercent = (int)ratio.Value;
                if (changes[7].Checked) item.CellShape = CellShapeValue(shape.SelectedIndex);
            }
            EndBatchAppearance();
            RefreshCellAppearanceChanges();
            _saveStatus.Text = "批量样式已应用到草稿，点击应用并保存生效";
        };
        return _batchAppearancePanel;
    }

    private static string CellContentValue(int index) => index == 1 ? "IconAndText" : index == 2 ? "TextOnly" : index == 3 ? "IconOnly" : index == 4 ? "ImageOnly" : index == 5 ? "ImageAndText" : "Inherit";
    private static int CellContentIndex(string value) => value == "IconAndText" ? 1 : value == "TextOnly" ? 2 : value == "IconOnly" ? 3 : value == "ImageOnly" ? 4 : value == "ImageAndText" ? 5 : 0;
    private static string CellShapeValue(int index) => new[] { "Circle", "Square", "Rounded", "Heart", "Star", "Hexagon", "Diamond" }[Math.Max(0, Math.Min(6, index))];
    private static ComboBox CreateCellShapeCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
        combo.Items.AddRange(new object[] { "圆形", "方形", "圆角方形", "爱心", "五角星", "六边形", "菱形" }); combo.SelectedIndex = 0;
        return combo;
    }
}
