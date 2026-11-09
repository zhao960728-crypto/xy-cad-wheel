using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    private Button _ringPresetButton;
    private Control BuildRingCountWithPresets()
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true };
        _ringCount.Dock = DockStyle.None;
        var choose = new Button { Text = "推荐设置 ▾", AutoSize = true };
        _ringPresetButton = choose;
        var menu = new ContextMenuStrip();
        choose.Disposed += (_, _) => menu.Dispose();
        choose.Click += (_, _) =>
        {
            if (!_config.Menus.TryGetValue(SceneKeys.Global, out var scene)) return;
            foreach (ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
            menu.Items.Clear();
            foreach (var preset in WheelRingPresets.ForStyle(scene.StyleId))
            {
                var option = new ToolStripMenuItem(preset.Length + " 圈 · " + string.Join(" + ", preset));
                option.Click += (_, _) =>
                {
                    using var preview = new WheelRingPresetDialog(_config, preset);
                    if (preview.ShowDialog(this) == DialogResult.OK) ApplyRingPreset(preset);
                };
                menu.Items.Add(option);
            }
            menu.Show(choose, 0, choose.Height);
        };
        row.Controls.Add(_ringCount);
        row.Controls.Add(choose);
        return row;
    }

    private void ApplyRingPreset(int[] preset)
    {
        if (_loadingUi || !_config.Menus.TryGetValue(SceneKeys.Global, out var scene)) return;
        WheelRingPresets.Apply(scene, preset);
        _updatingRingLayout = true;
        try { _ringCount.Value = preset.Length; }
        finally { _updatingRingLayout = false; }
        ApplyRingCount();
    }
}
