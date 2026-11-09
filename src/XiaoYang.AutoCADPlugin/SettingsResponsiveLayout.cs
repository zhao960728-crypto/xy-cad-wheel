using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private bool _responsiveLayoutReady;
    private bool _responsiveLayoutRunning;
    private bool _responsiveLayoutQueued;
    private readonly HashSet<Control> _responsiveObserved = new();

    private readonly Dictionary<ColumnStyle, float> _responsiveColumns = new();
    private readonly Dictionary<RowStyle, float> _responsiveRows = new();
    private readonly Dictionary<ComboBox, (string Font, int Count, int Width)> _responsiveComboWidths = new();
    private readonly Dictionary<Control, int> _responsiveHeights = new();

    // DPI scaling and a user's larger UI font are separate inputs. No system or
    // wheel setting is changed: this only reserves enough space for control text.
    private int ResponsiveSettingsDpi => Math.Max(DeviceDpi,
        (int)Math.Ceiling(96 * Font.SizeInPoints / 9f));

    private void InitializeResponsiveSettingsLayout()
    {
        _responsiveLayoutReady = true;
        Layout += (_, _) => QueueResponsiveSettingsLayout();
        FontChanged += (_, _) => { _applyResponsiveChrome?.Invoke(); ApplyResponsiveSettingsLayout(); };
        DpiChanged += (_, _) => { _applyResponsiveChrome?.Invoke(); QueueResponsiveSettingsLayout(); };
        Shown += (_, _) => ApplyResponsiveSettingsLayout();
        ApplyResponsiveSettingsLayout();
    }

    private void QueueResponsiveSettingsLayout()
    {
        if (!_responsiveLayoutReady || _responsiveLayoutRunning || _responsiveLayoutQueued || !IsHandleCreated || IsDisposed) return;
        _responsiveLayoutQueued = true;
        BeginInvoke(new Action(() =>
        {
            _responsiveLayoutQueued = false;
            if (!IsDisposed) ApplyResponsiveSettingsLayout();
        }));
    }

    private static FlowLayoutPanel CreateResponsiveEditorFlow() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty
    };

    private static void PrepareResponsiveEditorFlow(FlowLayoutPanel panel)
    {
        foreach (Control child in panel.Controls)
        {
            child.Dock = DockStyle.None;
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            if (child is Button || child is LinkLabel || child is CheckBox) child.AutoSize = true;
            if (child is ComboBox) child.Width = 210;
            if (child is TextBox) child.Width = 170;
            if (child is Panel) child.Size = new Size(42, 28);
        }
    }

    private void ApplyResponsiveSettingsLayout()
    {
        if (!_responsiveLayoutReady || _responsiveLayoutRunning || IsDisposed) return;
        _responsiveLayoutRunning = true;
        var layoutContainers = ResponsiveDescendants(this).Where(c => c.HasChildren).Prepend((Control)this).ToArray();
        foreach (var container in layoutContainers) container.SuspendLayout();
        try
        {
            var controls = ResponsiveDescendants(this).ToArray();
            var live = new HashSet<Control>(controls);
            _responsiveObserved.RemoveWhere(c => c.IsDisposed || !live.Contains(c));
            foreach (var old in _responsiveComboWidths.Keys.Where(c => !live.Contains(c)).ToArray()) _responsiveComboWidths.Remove(old);
            foreach (var old in _responsiveHeights.Keys.Where(c => !live.Contains(c)).ToArray()) _responsiveHeights.Remove(old);
            var columns = new HashSet<ColumnStyle>(controls.OfType<TableLayoutPanel>().SelectMany(t => t.ColumnStyles.Cast<ColumnStyle>()));
            var rows = new HashSet<RowStyle>(controls.OfType<TableLayoutPanel>().SelectMany(t => t.RowStyles.Cast<RowStyle>()));
            foreach (var old in _responsiveColumns.Keys.Where(c => !columns.Contains(c)).ToArray()) _responsiveColumns.Remove(old);
            foreach (var old in _responsiveRows.Keys.Where(r => !rows.Contains(r)).ToArray()) _responsiveRows.Remove(old);
            int unit = ScaleLogicalForDpi(4, ResponsiveSettingsDpi);
            float baselineScale = Math.Max(96, DeviceDpi) / 96f;
            float layoutScale = ResponsiveSettingsDpi / 96f;
            foreach (var control in controls)
            {
                if (_responsiveObserved.Add(control))
                {
                    control.FontChanged += (_, _) => QueueResponsiveSettingsLayout();
                    if (control is TabPage || control is FlowLayoutPanel)
                        control.SizeChanged += (_, _) => QueueResponsiveSettingsLayout();
                    if (control is TabControl tabs) tabs.SelectedIndexChanged += (_, _) => QueueResponsiveSettingsLayout();
                    if (control is ComboBox observedCombo) observedCombo.DropDown += (_, _) =>
                    { _responsiveComboWidths.Remove(observedCombo); ApplyResponsiveSettingsLayout(); };
                }
                if (control is Button button)
                {
                    var text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine);
                    var height = Math.Max(ScaleLogicalForDpi(30, ResponsiveSettingsDpi), text.Height + button.Padding.Vertical + unit * 2);
                    if (Equals(button.Tag, "nav"))
                    {
                        if (button.Height < height) button.Height = height;
                    }
                    else
                    {
                        var minimum = new Size(text.Width + button.Padding.Horizontal + unit * 2, height);
                        if (button.MinimumSize != minimum) button.MinimumSize = minimum;
                    }
                }
                else if (control is ComboBox combo)
                {
                    string fontKey = combo.Font.Name + "|" + combo.Font.SizeInPoints + "|" + combo.Font.Style;
                    if (!_responsiveComboWidths.TryGetValue(combo, out var cached) || cached.Font != fontKey || cached.Count != combo.Items.Count)
                    {
                        cached = (fontKey, combo.Items.Count, combo.Items.Cast<object>().Select(item => TextRenderer.MeasureText(combo.GetItemText(item), combo.Font).Width).DefaultIfEmpty(0).Max());
                        _responsiveComboWidths[combo] = cached;
                    }
                    int longest = cached.Width;
                    combo.DropDownWidth = Math.Max(combo.Width, Math.Min(ScaleLogicalForDpi(720, ResponsiveSettingsDpi), longest + unit * 7));
                    if (combo.Parent is FlowLayoutPanel flow)
                        combo.Width = Math.Min(Math.Max(ScaleLogicalForDpi(180, ResponsiveSettingsDpi), TextRenderer.MeasureText(combo.Text, combo.Font).Width + unit * 7),
                            Math.Max(1, flow.ClientSize.Width - flow.Padding.Horizontal - combo.Margin.Horizontal));
                }
                else if (control is NumericUpDown number)
                {
                    int width = TextRenderer.MeasureText(number.Maximum.ToString(), number.Font).Width + unit * 8;
                    if (number.MaximumSize.Width > 0 && number.MaximumSize.Width < width) number.MaximumSize = new Size(width, 0);
                    number.MinimumSize = new Size(width, number.PreferredHeight);
                }
                else if (control is TextBox text && !text.Multiline)
                {
                    text.MinimumSize = new Size(text.MinimumSize.Width, text.PreferredHeight);
                    if (text.Parent is FlowLayoutPanel flow)
                        text.Width = Math.Min(ScaleLogicalForDpi(180, ResponsiveSettingsDpi), Math.Max(1, flow.ClientSize.Width - text.Margin.Horizontal));
                }
                else if (control is LinkLabel link)
                {
                    var measured = TextRenderer.MeasureText(link.Text, link.Font);
                    link.MinimumSize = new Size(measured.Width + unit, measured.Height + unit);
                }
                else if (control is CheckBox check && check.AutoSize)
                {
                    var measured = TextRenderer.MeasureText(check.Text, check.Font);
                    check.MinimumSize = new Size(measured.Width + unit * 5, measured.Height + unit);
                }
                else if (control is Label label && label.AutoSize && label.Dock == DockStyle.Top)
                {
                    int available = Math.Max(1, label.Parent.ClientSize.Width - label.Parent.Padding.Horizontal - label.Margin.Horizontal);
                    if (label.Parent is TableLayoutPanel table && table.GetColumnSpan(label) == 1)
                    {
                        int column = table.GetColumn(label);
                        var widths = table.GetColumnWidths();
                        if (column >= 0 && column < widths.Length) available = Math.Max(1, widths[column] - label.Margin.Horizontal);
                    }
                    label.MaximumSize = new Size(available, 0);
                }
            }
            // Size fixed grid columns by the controls they contain, then let the
            // scrollable page handle an exceptionally small working area.
            foreach (var table in controls.OfType<TableLayoutPanel>().Reverse())
            {
                for (int column = 0; column < table.ColumnStyles.Count; column++)
                {
                    var style = table.ColumnStyles[column];
                    if (style.SizeType != SizeType.Absolute || style.Width <= 0) continue;
                    if (!_responsiveColumns.ContainsKey(style)) _responsiveColumns[style] = style.Width / baselineScale;
                    int need = (int)Math.Ceiling(_responsiveColumns[style] * layoutScale);
                    foreach (Control child in table.Controls)
                    {
                        if (table.GetColumn(child) != column || table.GetColumnSpan(child) != 1) continue;
                        if (child is Button || child is NumericUpDown) need = Math.Max(need, child.MinimumSize.Width + child.Margin.Horizontal);
                        else if (child is Label || child is CheckBox)
                            need = Math.Max(need, TextRenderer.MeasureText(child.Text, child.Font).Width + child.Padding.Horizontal + child.Margin.Horizontal + (child is CheckBox ? unit * 5 : unit));
                    }
                    if ((int)style.Width != need) style.Width = need;
                }
                for (int row = 0; row < table.RowStyles.Count; row++)
                {
                    var style = table.RowStyles[row];
                    if (style.SizeType != SizeType.Absolute) continue;
                    if (!_responsiveRows.ContainsKey(style)) _responsiveRows[style] = style.Height / baselineScale;
                    int need = table.Controls.Cast<Control>().Where(c => table.GetRow(c) == row && table.GetRowSpan(c) == 1)
                        .Select(c => Math.Max(c.MinimumSize.Height, c.PreferredSize.Height) + c.Margin.Vertical).DefaultIfEmpty(0).Max();
                    need = Math.Max(need, (int)Math.Ceiling(_responsiveRows[style] * layoutScale));
                    if ((int)style.Height != need) style.Height = need;
                }
                if (table == _ringEditors)
                {
                    int width = (int)Math.Ceiling(table.ColumnStyles.Cast<ColumnStyle>().Sum(c => c.Width)) + table.Padding.Horizontal;
                    table.MinimumSize = new Size(width, 0);
                    table.MaximumSize = Size.Empty;
                }
                if (table.Dock == DockStyle.Bottom)
                {
                    int need = table.Controls.Cast<Control>().Select(c => c.MinimumSize.Height + c.Margin.Vertical).DefaultIfEmpty(0).Max() + table.Padding.Vertical;
                    if (!_responsiveHeights.ContainsKey(table)) _responsiveHeights[table] = (int)(table.Height / baselineScale);
                    table.Height = Math.Max((int)(_responsiveHeights[table] * layoutScale), need);
                }
            }
            if (_styleHelp.Width > 0)
            {
                int height = TextRenderer.MeasureText(_styleHelp.Text, _styleHelp.Font,
                    new Size(_styleHelp.Width, int.MaxValue), TextFormatFlags.WordBreak).Height + unit * 3;
                _styleHelp.Height = Math.Max(ScaleLogicalForDpi(42, ResponsiveSettingsDpi), height);
            }
            if (!_responsiveHeights.ContainsKey(_pageTitle)) _responsiveHeights[_pageTitle] = (int)(_pageTitle.Height / baselineScale);
            _pageTitle.Height = Math.Max((int)(_responsiveHeights[_pageTitle] * layoutScale), _pageTitle.PreferredHeight + unit);
        }
        finally
        {
            // Commit each subtree once, from leaves to root. Per-control metric
            // changes otherwise trigger hundreds of complete form layouts.
            for (int i = layoutContainers.Length - 1; i >= 0; i--) layoutContainers[i].ResumeLayout(true);
            _responsiveLayoutRunning = false;
        }
    }

    private static IEnumerable<Control> ResponsiveDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (var descendant in ResponsiveDescendants(child)) yield return descendant; }
    }
}

