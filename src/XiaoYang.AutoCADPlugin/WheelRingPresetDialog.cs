using System.Drawing;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class WheelRingPresetDialog : Form
{
    internal WheelRingPresetDialog(PluginConfig source, int[] preset)
    {
        // The preview owns a deep copy; cancellation never touches the settings draft.
        var copy = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(source));
        var scene = copy.Menus[SceneKeys.Global];
        WheelRingPresets.Apply(scene, preset);
        Text = "推荐设置预览 · " + string.Join(" + ", preset);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(660, 660);
        MinimumSize = new Size(400, 400);
        ShowInTaskbar = false;
        MinimizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = preset.Length + " 圈：" + string.Join(" + ", preset) + "\n确认后填入当前设置；超出格数的命令会保留在未分配命令中。", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8) }, 0, 0);
        var wheel = new RadialMenuControl(scene.StyleId == "orbit" || scene.StyleId == "01" ? Color.FromArgb(43, 48, 56) : Color.White)
            { Dock = DockStyle.Fill, ScaleMenuForPreview = true };
        wheel.SetMenu(copy, scene);
        root.Controls.Add(wheel, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var confirm = new Button { Text = "确定使用", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);
        AcceptButton = confirm; CancelButton = cancel;
        PerformAutoScale();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
    }
}
