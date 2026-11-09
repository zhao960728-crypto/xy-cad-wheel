using System.Drawing;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private Button _resizeCellButton;

    private sealed class CellCountDrag
    {
        internal bool Add;
    }

    private sealed class FreeCellDrag { internal string Shape; }

    private void AddFreeCell(Point point, string shape)
    {
        if (_practiceMode || _preview.BatchSelectionMode || _config.Menus[SceneKeys.Global].StyleId != "free") return;
        var ring = ResolveTargetRing(_catalogTarget);
        if (ring.Slots >= 32) { _saveStatus.Text = "当前圈最多32格，请选择另一圈"; return; }
        var slot = ring.Slots;
        WheelCellEditing.Insert(ring, slot, 1);
        var item = new MenuItemConfig { Slot = slot, CellShape = shape };
        ring.Items.Add(item);
        if (!_preview.TryPlaceCell(ring.Index, slot, point))
        {
            ring.Items.Remove(item); ring.Slots--;
            _saveStatus.Text = "这里空间不足，请拖到留有间距的空白位置";
            _preview.Invalidate();
            return;
        }
        CompleteCellPositionEdit(ring, slot);
        _saveStatus.Text = "已添加自设计空格，可绑定命令或继续拖动摆放";
    }

    private Control BuildPreviewCellTools()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        foreach (var add in new[] { true, false })
        {
            var button = new Button { Text = add ? "＋ 加" : "－ 减", AutoSize = true,
                AccessibleName = add ? "拖到两格之间添加格子" : "拖到格子上删除格子" };
            var start = Point.Empty;
            button.MouseDown += (_, e) => start = e.Location;
            button.MouseMove += (_, e) =>
            {
                if (_practiceMode || _preview.BatchSelectionMode || e.Button != MouseButtons.Left ||
                    (Math.Abs(e.X - start.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - start.Y) < SystemInformation.DragSize.Height)) return;
                _preview.ShowInsertionPoints = add;
                _preview.Invalidate();
                try { button.DoDragDrop(new CellCountDrag { Add = add }, DragDropEffects.Copy); }
                finally { _preview.ShowInsertionPoints = false; _preview.Invalidate(); }
            };
            panel.Controls.Add(button);
        }
        panel.Controls.Add(new Label { Text = "拖＋到两格之间；拖－到格子上", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        var shape = CreateCellShapeCombo();
        panel.Controls.Add(new Label { Text = "格子形状", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        panel.Controls.Add(shape);
        var addShape = new Button { Text = "拖入新格", AutoSize = true };
        var shapeStart = Point.Empty;
        addShape.MouseDown += (_, e) => shapeStart = e.Location;
        addShape.MouseMove += (_, e) =>
        {
            if (_practiceMode || _preview.BatchSelectionMode || e.Button != MouseButtons.Left || _config.Menus[SceneKeys.Global].StyleId != "free") return;
            if (Math.Abs(e.X - shapeStart.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - shapeStart.Y) < SystemInformation.DragSize.Height) return;
            addShape.DoDragDrop(new FreeCellDrag { Shape = CellShapeValue(shape.SelectedIndex) }, DragDropEffects.Copy);
        };
        panel.Controls.Add(addShape);
        shape.SelectionChangeCommitted += (_, _) =>
        {
            if (_practiceMode || _preview.BatchSelectionMode) return;
            var ring = ResolveTargetRing(_catalogTarget);
            var slot = (int)_catalogTarget.Slot.Value - 1;
            var item = ring.Items.FirstOrDefault(i => i.Slot == slot);
            if (item == null) { item = new MenuItemConfig { Slot = slot }; ring.Items.Add(item); }
            item.CellShape = CellShapeValue(shape.SelectedIndex);
            RefreshPreview();
            _saveStatus.Text = "已设置当前格形状，独立格和自设计模式中显示";
        };
        _preview.CellSelected += (_, e) =>
        {
            var item = _config.Menus[SceneKeys.Global].Rings.FirstOrDefault(r => r.Index == e.RingIndex)?.Items.FirstOrDefault(i => i.Slot == e.Slot);
            shape.SelectedIndex = Enumerable.Range(0, 7).FirstOrDefault(i => CellShapeValue(i) == (item?.CellShape ?? "Circle"));
        };
        var resize = _resizeCellButton = new Button { Text = "调整单格大小", AutoSize = true };
        resize.Click += (_, _) =>
        {
            if (_practiceMode) return;
            var style = _config.Menus[SceneKeys.Global].StyleId;
            if (style == "classic" || style == "01")
            { _saveStatus.Text = "完整格子大小适用于独立格、节点、分组面板与自设计；请先选择对应样式"; return; }
            if (_preview.BatchSelectionMode) EndBatchAppearance();
            _preview.ResizeMode = !_preview.ResizeMode;
            resize.Text = _preview.ResizeMode ? "✓ 结束调大小" : "调整单格大小";
            _preview.Invalidate();
            _saveStatus.Text = _preview.ResizeMode ? "点击格子后滚轮上滚放大、下滚缩小（每档5%），也可拖动手柄；空间不足时保持原大小" : "单格尺寸已更新到草稿";
        };
        _preview.CellGeometryChanged += (_, _) => { _saveStatus.Text = "格子位置或大小已更新，点击应用并保存生效"; };
        panel.Controls.Add(resize);
        var align = new Button { Text = "微调整齐", AutoSize = true };
        align.Click += (_, _) =>
        {
            if (_practiceMode || _config.Menus[SceneKeys.Global].StyleId != "free")
            { _saveStatus.Text = "在自设计模式摆好格子后，点击微调整齐"; return; }
            if (_preview.BatchSelectionMode) return;
            _saveStatus.Text = _preview.NudgeFreeLayout()
                ? "已在原位置附近轻微对齐，保留整体图案；点击应用并保存生效"
                : "当前格子无需微调，或附近空间不足；保留原摆放";
            _preview.Invalidate();
        };
        panel.Controls.Add(align);
        panel.Controls.Add(BuildBatchSelectionTools());
        return panel;
    }

    private RadialCellSelectedEventArgs ResolveCountDrop(Point point, bool add)
    {
        if (_practiceMode || _preview.BatchSelectionMode) return null;
        var target = add ? _preview.HitInsertion(point) : _preview.HitCell(point);
        var ring = target == null ? null : _config.Menus[SceneKeys.Global].Rings.FirstOrDefault(r => r.Index == target.RingIndex && r.Enabled);
        return ring == null || (add ? ring.Slots >= 32 : ring.Slots <= 1) ? null : target;
    }

    private void ApplyCountDrop(Point point, bool add)
    {
        var target = ResolveCountDrop(point, add);
        if (target == null) return;
        var wheel = _config.Menus[SceneKeys.Global];
        var ring = wheel.Rings.First(r => r.Index == target.RingIndex);
        if (add) WheelCellEditing.Insert(ring, target.Slot, 1);
        else WheelCellEditing.Remove(wheel, ring, new[] { target.Slot });
        CompleteCellPositionEdit(ring, Math.Min(target.Slot, ring.Slots - 1));
        _saveStatus.Text = add ? "已插入空格，点击应用并保存生效" : "已删除格子，原命令保留在未分配命令中";
    }

    private Control BuildCellPositionTools()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
        foreach (var text in new[] { "在前面插入", "在后面插入", "删除所选位置" })
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) =>
            {
                var ring = ResolveTargetRing(_catalogTarget);
                var slot = (int)_catalogTarget.Slot.Value - 1;
                if (text == "删除所选位置")
                {
                    if (ring.Slots <= 1) { _saveStatus.Text = "每圈至少保留一格"; return; }
                    if (!ChooseCellCountChange(ring, ring.Slots - 1, slot)) return;
                }
                else
                {
                    if (ring.Slots >= 32) { _saveStatus.Text = "每圈最多 32 格"; return; }
                    slot += text == "在后面插入" ? 1 : 0;
                    WheelCellEditing.Insert(ring, slot, 1);
                }
                CompleteCellPositionEdit(ring, Math.Min(slot, ring.Slots - 1));
            };
            panel.Controls.Add(button);
        }
        return panel;
    }

    private void CompleteCellPositionEdit(RingConfig ring, int slot)
    {
        UpdateAssignmentSlotLimit(_catalogTarget);
        UpdateAssignmentSlotLimit(_customTarget);
        RebuildRingEditors();
        RefreshWheelGrid();
        RefreshPreview();
        _preview.SelectCell(ring.Index, slot);
    }

    // Numeric capacity changes and the explicit position tools use the same editor.
    private bool ChooseCellCountChange(RingConfig ring, int newCount, int selectedSlot)
    {
        var adding = newCount > ring.Slots;
        var delta = Math.Abs(newCount - ring.Slots);
        if (delta == 0) return true;
        using var dialog = CreateCellDialog(adding ? "选择新格子的插入位置" : "选择要删除的格子", 540, 480);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hint = new Label { AutoSize = true, Dock = DockStyle.Top, Text = adding
            ? $"第 {ring.Index + 1} 圈增加 {delta} 格：选择插入点，后续格子顺移。"
            : $"第 {ring.Index + 1} 圈减少 {delta} 格：勾选 {delta} 个位置。\n命令及图标移入“未分配命令”，其余格子保持顺序。" };
        body.Controls.Add(hint, 0, 0);
        var insert = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var remove = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        remove.ItemCheck += (_, e) => hint.Text = $"第 {ring.Index + 1} 圈减少 {delta} 格，已勾选 {remove.CheckedIndices.Count + (e.NewValue == CheckState.Checked ? 1 : -1)} 格。\n命令及图标移入“未分配命令”，其余格子保持顺序。";
        for (var slot = 0; slot < ring.Slots; slot++)
        {
            var name = ring.Items.FirstOrDefault(item => item.Slot == slot)?.Label ?? "空格";
            insert.Items.Add($"第 {slot + 1} 格之前 · {name}");
            remove.Items.Add($"第 {slot + 1} 格 · {name}", delta == 1 && slot == selectedSlot);
        }
        insert.Items.Add("最后一格之后");
        insert.SelectedIndex = Math.Max(0, Math.Min(ring.Slots, selectedSlot));
        body.Controls.Add(adding ? (Control)insert : remove, 0, 1);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var apply = new Button { Text = adding ? "插入" : "删除这些位置", AutoSize = true };
        apply.Click += (_, _) =>
        {
            if (!adding && remove.CheckedIndices.Count != delta)
            { hint.Text = $"需要勾选 {delta} 格，当前勾选 {remove.CheckedIndices.Count} 格。命令将保留到未分配列表。"; return; }
            dialog.DialogResult = DialogResult.OK;
        };
        footer.Controls.Add(cancel); footer.Controls.Add(apply);
        body.Controls.Add(footer, 0, 2);
        dialog.Controls.Add(body);
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        if (adding) WheelCellEditing.Insert(ring, insert.SelectedIndex, delta);
        else WheelCellEditing.Remove(_config.Menus[SceneKeys.Global], ring, remove.CheckedIndices.Cast<int>());
        return true;
    }

    private Form CreateCellDialog(string title, int width, int height)
    {
        var area = Screen.FromControl(this).WorkingArea;
        return new Form { Text = title, Font = Font, AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96), StartPosition = FormStartPosition.CenterParent,
            Size = new Size(Math.Min(ScaleLogical(width), area.Width - 32), Math.Min(ScaleLogical(height), area.Height - 32)),
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false };
    }

}
