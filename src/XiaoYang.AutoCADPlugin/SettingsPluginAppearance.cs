using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private readonly Dictionary<Control, PluginControlColors> _pluginOriginalColors = new();
    private bool _applyingPluginAppearance;
    private bool _pluginAppearanceWasEnabled;

    private sealed class PluginControlColors
    {
        public Color Back, Fore, Border, Over, Down;
        public bool VisualStyle;
        public DataGridViewCellStyle? Cells, Headers, Rows;
        public Color GridBackground, GridLine;
        public Color Link, ActiveLink, VisitedLink;
        public Color ThemedBack, ThemedFore;
        public TabDrawMode TabDrawMode;
    }

    private Control BuildPluginAppearanceControl()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        var choose = new Button { Text = "插件界面配色…", AutoSize = true };
        var reset = new Button { Text = "恢复插件默认配色", AutoSize = true };
        var hint = new Label { Text = "单色 / 渐变 · 适用于整个插件设置界面", AutoSize = true, Margin = new Padding(8, 10, 0, 0) };
        choose.Click += (_, _) =>
        {
            using var dialog = new WheelAppearanceDialog(_config.PluginAppearance) { Text = "插件界面配色" };
            ReplacePluginAppearanceHint(dialog);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _config.PluginAppearance = dialog.Result.Clone();
            _config.PluginAppearanceEnabled = true;
            ApplyPluginAppearance();
            UpdateSaveStatus();
        };
        reset.Click += (_, _) =>
        {
            _config.PluginAppearanceEnabled = false;
            ApplyPluginAppearance();
            UpdateSaveStatus();
        };
        panel.Controls.Add(choose); panel.Controls.Add(reset); panel.Controls.Add(hint);
        return panel;
    }

    private static void ReplacePluginAppearanceHint(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label label && label.Text.Contains("文字颜色仍在格子文字设置中调整"))
                label.Text = "仅调整小阳CAD轮盘插件设置界面，不改变CAD界面或轮盘配色；文字自动保持对比度。";
            ReplacePluginAppearanceHint(child);
        }
    }

    private void ApplyPluginAppearance()
    {
        if (_applyingPluginAppearance || IsDisposed) return;
        if (!_config.PluginAppearanceEnabled && !_pluginAppearanceWasEnabled) return;
        var restoreNavigation = !_config.PluginAppearanceEnabled && _pluginAppearanceWasEnabled;
        _applyingPluginAppearance = true;
        try
        {
            var appearance = _config.PluginAppearance ?? new WheelAppearance();
            var start = WheelAppearance.ParseColor(appearance.StartColor, Color.White);
            var end = appearance.FillMode == "Gradient" ? WheelAppearance.ParseColor(appearance.EndColor, start) : start;
            var average = Color.FromArgb((start.R + end.R) / 2, (start.G + end.G) / 2, (start.B + end.B) / 2);
            // Controls carrying text receive a solid card surface so even a
            // black-to-white gradient cannot put unreadable text at one end.
            var card = BlendPluginColor(average, average.GetBrightness() < .45f ? Color.Black : Color.White, .18f);
            ApplyPluginControl(this, appearance, average, card);
            _pluginAppearanceWasEnabled = _config.PluginAppearanceEnabled;
            if (!_config.PluginAppearanceEnabled)
            {
                foreach (var control in _pluginOriginalColors.Keys.ToArray())
                {
                    control.Paint -= PaintPluginSurface;
                    control.ControlAdded -= PluginAppearanceControlAdded;
                    control.Disposed -= PluginAppearanceControlDisposed;
                    control.BackColorChanged -= PluginAppearanceColorChanged;
                    control.ForeColorChanged -= PluginAppearanceColorChanged;
                    if (control is TabControl tabs) tabs.DrawItem -= DrawPluginTab;
                }
                _pluginOriginalColors.Clear();
            }
            Invalidate(true);
        }
        finally { _applyingPluginAppearance = false; }
        if (restoreNavigation) SelectPage(_pages.SelectedIndex);
    }

    private void ApplyPluginControl(Control control, WheelAppearance appearance, Color background, Color card)
    {
        // Only the actual wheel and the text-color sample represent user content.
        // Their surrounding preview pane is part of the settings interface.
        if (ReferenceEquals(control, _preview) || ReferenceEquals(control, _textColorSwatch)) return;
        if (!_pluginOriginalColors.TryGetValue(control, out var original))
        {
            original = new PluginControlColors { Back = control.BackColor, Fore = control.ForeColor };
            // Preserve ambient colors as ambient, particularly for controls
            // dynamically added while their parent already carries the palette.
            var properties = System.ComponentModel.TypeDescriptor.GetProperties(control);
            if (properties["BackColor"]?.ShouldSerializeValue(control) == false) original.Back = Color.Empty;
            if (properties["ForeColor"]?.ShouldSerializeValue(control) == false) original.Fore = Color.Empty;
            if (control is LinkLabel link)
            { original.Link = link.LinkColor; original.ActiveLink = link.ActiveLinkColor; original.VisitedLink = link.VisitedLinkColor; }
            if (control is Button button)
            {
                original.Border = button.FlatAppearance.BorderColor;
                original.Over = button.FlatAppearance.MouseOverBackColor;
                original.Down = button.FlatAppearance.MouseDownBackColor;
                original.VisualStyle = button.UseVisualStyleBackColor;
            }
            if (control is DataGridView grid)
            {
                original.Cells = grid.DefaultCellStyle.Clone(); original.Headers = grid.ColumnHeadersDefaultCellStyle.Clone();
                original.Rows = grid.RowHeadersDefaultCellStyle.Clone(); original.GridBackground = grid.BackgroundColor;
                original.GridLine = grid.GridColor; original.VisualStyle = grid.EnableHeadersVisualStyles;
            }
            if (control is TabControl tabs)
            {
                original.TabDrawMode = tabs.DrawMode;
                tabs.DrawItem += DrawPluginTab;
            }
            _pluginOriginalColors[control] = original;
            if (control is Panel || control is TabPage || ReferenceEquals(control, this)) control.Paint += PaintPluginSurface;
            control.ControlAdded += PluginAppearanceControlAdded;
            control.Disposed += PluginAppearanceControlDisposed;
            control.BackColorChanged += PluginAppearanceColorChanged;
            control.ForeColorChanged += PluginAppearanceColorChanged;
        }
        var enabled = _config.PluginAppearanceEnabled;
        var container = control is Panel || control is TabControl || control is TabPage || ReferenceEquals(control, this);
        control.BackColor = enabled ? (container ? background : card) : original.Back;
        control.ForeColor = enabled ? PluginContrast(control.BackColor) : original.Fore;
        if (control is TabControl tabControl)
            tabControl.DrawMode = enabled ? TabDrawMode.OwnerDrawFixed : original.TabDrawMode;
        if (control is LinkLabel linkControl)
        {
            linkControl.LinkColor = enabled ? PluginReadableAccent(appearance.GetSelectedColor(), card) : original.Link;
            linkControl.ActiveLinkColor = enabled ? PluginReadableAccent(appearance.GetHoverColor(), card) : original.ActiveLink;
            linkControl.VisitedLinkColor = enabled ? PluginReadableAccent(appearance.GetSelectedColor(), card) : original.VisitedLink;
        }
        if (control is Button buttonControl)
        {
            if (enabled)
            {
                var isSelectedNav = Equals(buttonControl.Tag, "nav") && buttonControl.AccessibleDescription == "当前页面";
                var isSelectedSection = ReferenceEquals(buttonControl, _sectionPrimary) && _pages.SelectedIndex is 1 or 5
                    || ReferenceEquals(buttonControl, _sectionSecondary) && _pages.SelectedIndex is 2 or 4;
                var important = Equals(buttonControl.Tag, "primary") || isSelectedNav || isSelectedSection;
                if (Equals(buttonControl.Tag, "danger"))
                { buttonControl.BackColor = original.Back; buttonControl.ForeColor = original.Fore; }
                else if (important)
                { buttonControl.BackColor = appearance.GetSelectedColor(); buttonControl.ForeColor = PluginContrast(buttonControl.BackColor); }
                buttonControl.UseVisualStyleBackColor = false;
                buttonControl.FlatAppearance.BorderColor = appearance.GetBorderColor();
                // Tint hover toward the card to retain contrast with the label.
                buttonControl.FlatAppearance.MouseOverBackColor = BlendPluginHover(buttonControl.BackColor, appearance.GetHoverColor());
                buttonControl.FlatAppearance.MouseDownBackColor = BlendPluginHover(buttonControl.BackColor, appearance.GetSelectedColor());
            }
            else
            {
                buttonControl.FlatAppearance.BorderColor = original.Border;
                buttonControl.FlatAppearance.MouseOverBackColor = original.Over;
                buttonControl.FlatAppearance.MouseDownBackColor = original.Down;
                buttonControl.UseVisualStyleBackColor = original.VisualStyle;
                if (Equals(buttonControl.Tag, "nav"))
                { buttonControl.BackColor = buttonControl.AccessibleDescription == "当前页面" ? AccentDark : Header; buttonControl.ForeColor = Color.White; }
            }
        }
        if (control is DataGridView gridControl)
        {
            if (enabled)
            {
                gridControl.EnableHeadersVisualStyles = false;
                gridControl.BackgroundColor = card; gridControl.GridColor = appearance.GetBorderColor();
                foreach (var style in new[] { gridControl.DefaultCellStyle, gridControl.ColumnHeadersDefaultCellStyle, gridControl.RowHeadersDefaultCellStyle })
                {
                    style.BackColor = card; style.ForeColor = PluginContrast(card);
                    style.SelectionBackColor = appearance.GetSelectedColor(); style.SelectionForeColor = PluginContrast(appearance.GetSelectedColor());
                }
            }
            else
            {
                gridControl.DefaultCellStyle = original.Cells!.Clone(); gridControl.ColumnHeadersDefaultCellStyle = original.Headers!.Clone();
                gridControl.RowHeadersDefaultCellStyle = original.Rows!.Clone(); gridControl.BackgroundColor = original.GridBackground;
                gridControl.GridColor = original.GridLine; gridControl.EnableHeadersVisualStyles = original.VisualStyle;
            }
        }
        original.ThemedBack = control.BackColor;
        original.ThemedFore = control.ForeColor;
        foreach (Control child in control.Controls) ApplyPluginControl(child, appearance, background, card);
    }

    private void DrawPluginTab(object? sender, DrawItemEventArgs e)
    {
        if (!_config.PluginAppearanceEnabled || sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount) return;
        var appearance = _config.PluginAppearance ?? new WheelAppearance();
        var selected = e.Index == tabs.SelectedIndex;
        var color = selected ? appearance.GetSelectedColor() : tabs.BackColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
            PluginContrast(color), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -3, -3), PluginContrast(color), color);
    }

    private void PluginAppearanceColorChanged(object? sender, EventArgs e)
    {
        // Status and selection refreshes still assign the default palette. Keep
        // those updates from leaving unreadable dark labels on a dark theme.
        if (_applyingPluginAppearance || !_config.PluginAppearanceEnabled || sender is not Control control
            || !_pluginOriginalColors.TryGetValue(control, out var original)) return;
        _applyingPluginAppearance = true;
        try
        {
            control.BackColor = original.ThemedBack;
            control.ForeColor = original.ThemedFore;
        }
        finally { _applyingPluginAppearance = false; }
    }

    private void PaintPluginSurface(object? sender, PaintEventArgs e)
    {
        if (!_config.PluginAppearanceEnabled || sender is not Control control || control.Width < 1 || control.Height < 1) return;
        var appearance = _config.PluginAppearance ?? new WheelAppearance();
        // Use the same window-wide coordinates for every nested panel; opaque
        // child panel backgrounds therefore do not restart or hide the gradient.
        var origin = control.PointToClient(PointToScreen(Point.Empty));
        using var brush = appearance.CreateBrush(new RectangleF(origin.X, origin.Y, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)));
        e.Graphics.FillRectangle(brush, control.ClientRectangle);
        using var pen = new Pen(appearance.GetBorderColor());
        if (control is TabPage) e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, control.Width - 1), Math.Max(0, control.Height - 1));
    }

    private void PluginAppearanceControlAdded(object? sender, ControlEventArgs e)
    {
        if (_config.PluginAppearanceEnabled && !_applyingPluginAppearance && IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(ApplyPluginAppearance));
    }

    private void PluginAppearanceControlDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control) _pluginOriginalColors.Remove(control);
    }

    private static Color BlendPluginColor(Color first, Color second, float amount) => Color.FromArgb(
        (int)(first.R * (1 - amount) + second.R * amount), (int)(first.G * (1 - amount) + second.G * amount), (int)(first.B * (1 - amount) + second.B * amount));

    private static Color BlendPluginHover(Color original, Color target)
    {
        var text = PluginContrast(original);
        for (var amount = .18f; amount >= .01f; amount /= 2)
        { var candidate = BlendPluginColor(original, target, amount); if (PluginContrast(candidate) == text) return candidate; }
        return original;
    }

    private static Color PluginContrast(Color background)
    {
        return PluginLuminance(background) > .179 ? Color.Black : Color.White;
    }

    private static Color PluginReadableAccent(Color accent, Color background)
    {
        var first = PluginLuminance(accent); var second = PluginLuminance(background);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05) >= 4.5 ? accent : PluginContrast(background);
    }

    private static double PluginLuminance(Color background)
    {
        double Channel(byte value) { var c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Channel(background.R) + .7152 * Channel(background.G) + .0722 * Channel(background.B);
    }
}
