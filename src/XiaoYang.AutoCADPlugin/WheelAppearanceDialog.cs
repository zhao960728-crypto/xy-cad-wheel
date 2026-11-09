using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class WheelAppearanceDialog : Form
{
    private readonly WheelAppearance _draft;
    private readonly ComboBox _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _angle = new NumericUpDown { Minimum = 0, Maximum = 359, Increment = 15, Dock = DockStyle.Fill };
    private readonly Button _end = new Button();
    private readonly Panel _preview = new PreviewPanel { Dock = DockStyle.Fill, MinimumSize = new Size(200, 160) };
    public WheelAppearance Result { get; private set; }

    public WheelAppearanceDialog(WheelAppearance source)
    {
        _draft = (source ?? new WheelAppearance()).Clone();
        _draft.Normalize();
        Text = "轮盘调色盘";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(630, 510);
        MinimumSize = new Size(490, 440);
        BackColor = Color.FromArgb(244, 246, 248);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8) };
        var ok = new Button { Text = "使用此配色", AutoSize = true, Height = 30 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, Height = 30 };
        var reset = new Button { Text = "恢复默认", AutoSize = true, Height = 30 };
        ok.Click += (_, __) => { _draft.Normalize(); Result = _draft.Clone(); DialogResult = DialogResult.OK; Close(); };
        reset.Click += (_, __) => ResetDraft();
        footer.Controls.Add(ok); footer.Controls.Add(cancel); footer.Controls.Add(reset);
        AcceptButton = ok; CancelButton = cancel;

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(18), AutoScroll = true, AutoScrollMinSize = new Size(0, 390) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(0, 0, 16, 0) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 7; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _mode.Items.AddRange(new object[] { "单色", "渐变" });
        AddRow(form, 0, "填充方式", _mode);
        AddColor(form, 1, "主色", () => _draft.StartColor, value => _draft.StartColor = value);
        AddColor(form, 2, "渐变末色", () => _draft.EndColor, value => _draft.EndColor = value, _end);
        AddRow(form, 3, "方向 °", _angle);
        AddColor(form, 4, "边线", () => _draft.BorderColor, value => _draft.BorderColor = value);
        AddColor(form, 5, "悬停", () => _draft.HoverColor, value => _draft.HoverColor = value);
        AddColor(form, 6, "选中", () => _draft.SelectedColor, value => _draft.SelectedColor = value);
        var hint = new Label { Text = "点击色块打开调色盘。\n0° 从左到右，90° 从上到下。\n文字颜色仍在格子文字设置中调整。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(99, 115, 129), AutoSize = true };
        form.Controls.Add(hint, 0, 7); form.SetColumnSpan(hint, 2);
        body.Controls.Add(form, 0, 0); body.Controls.Add(_preview, 1, 0);
        Controls.Add(body); Controls.Add(footer);
        _mode.SelectedIndexChanged += (_, __) => { _draft.FillMode = _mode.SelectedIndex == 1 ? "Gradient" : "Solid"; UpdatePreview(); };
        _angle.ValueChanged += (_, __) => { _draft.GradientAngle = (int)_angle.Value; UpdatePreview(); };
        _preview.Paint += DrawPreview;
        _mode.SelectedIndex = _draft.FillMode == "Gradient" ? 1 : 0;
        _angle.Value = _draft.GradientAngle;
        PerformAutoScale();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var margin = SettingsWindow.ScaleLogicalForDpi(12, DeviceDpi);
        var available = new Size(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Margin = new Padding(3, 5, 3, 5);
        table.Controls.Add(control, 1, row);
    }

    private void AddColor(TableLayoutPanel table, int row, string label, Func<string> read, Action<string> write, Button button = null)
    {
        button = button ?? new Button();
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.Tag = read;
        button.Click += (_, __) =>
        {
            using (var picker = new ColorDialog { FullOpen = true, Color = WheelAppearance.ParseColor(read(), Color.White) })
                if (picker.ShowDialog(this) == DialogResult.OK) { write(WheelAppearance.ToHex(picker.Color)); UpdatePreview(); }
        };
        AddRow(table, row, label, button);
    }

    private void UpdatePreview()
    {
        _end.Enabled = _angle.Enabled = _draft.FillMode == "Gradient";
        RefreshSwatches(this);
        _preview.Invalidate();
    }

    private static void RefreshSwatches(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (control is Button button && button.Tag is Func<string> read)
            {
                button.Text = read(); button.BackColor = WheelAppearance.ParseColor(read(), Color.White);
                button.ForeColor = button.BackColor.GetBrightness() < 0.5f ? Color.White : Color.FromArgb(30, 41, 53);
                button.UseVisualStyleBackColor = false;
            }
            if (control.HasChildren) RefreshSwatches(control);
        }
    }

    private void ResetDraft()
    {
        var defaults = new WheelAppearance();
        _draft.FillMode = defaults.FillMode; _draft.StartColor = defaults.StartColor; _draft.EndColor = defaults.EndColor;
        _draft.GradientAngle = defaults.GradientAngle; _draft.BorderColor = defaults.BorderColor;
        _draft.HoverColor = defaults.HoverColor; _draft.SelectedColor = defaults.SelectedColor;
        _mode.SelectedIndex = 0; _angle.Value = _draft.GradientAngle; UpdatePreview();
    }

    private void DrawPreview(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Max(20, Math.Min(_preview.ClientSize.Width - 20, _preview.ClientSize.Height - 95));
        var bounds = new RectangleF((_preview.Width - size) / 2f, 25, size, size);
        using (var fill = _draft.CreateBrush(bounds)) g.FillEllipse(fill, bounds);
        using (var hover = new SolidBrush(Color.FromArgb(70, _draft.GetHoverColor()))) g.FillPie(hover, bounds.X, bounds.Y, bounds.Width, bounds.Height, -90, 45);
        using (var selected = new SolidBrush(Color.FromArgb(110, _draft.GetSelectedColor()))) g.FillPie(selected, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0, 45);
        using (var border = new Pen(_draft.GetBorderColor(), 1.5f))
        {
            g.DrawEllipse(border, bounds);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4;
                var cx = bounds.X + size / 2f; var cy = bounds.Y + size / 2f;
                g.DrawLine(border, cx, cy, cx + (float)Math.Cos(angle) * size / 2, cy + (float)Math.Sin(angle) * size / 2);
            }
            var center = new RectangleF(bounds.X + size * .37f, bounds.Y + size * .37f, size * .26f, size * .26f);
            using (var fill = _draft.CreateBrush(bounds)) g.FillEllipse(fill, center);
            g.DrawEllipse(border, center);
        }
        TextRenderer.DrawText(g, "配色预览", Font, new Rectangle(0, (int)bounds.Bottom + 15, _preview.Width, 24), ForeColor, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "仅确认后应用；取消保留原配色", Font, new Rectangle(0, (int)bounds.Bottom + 42, _preview.Width, 40), Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
    }

    private sealed class PreviewPanel : Panel
    {
        public PreviewPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}
