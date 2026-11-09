using System.Drawing;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class CommandRenameDialog : Form
{
    private readonly TextBox _name = new();

    public string CommandDisplayName => _name.Text.Trim();

    public bool RestoreOriginalName { get; private set; }

    public CommandRenameDialog(string commandName, string currentName, string originalName)
    {
        Text = "修改显示名称";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 206);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(242, 245, 248);

        var title = new Label
        {
            Text = $"执行命令：{commandName}",
            AutoEllipsis = true,
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(18, 14, 18, 0),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 53)
        };
        var hint = new Label
        {
            Text = "这里只修改界面和轮盘中的显示名称，不会改变实际执行命令。",
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(18, 6, 18, 0),
            ForeColor = Color.FromArgb(99, 115, 129)
        };
        var editor = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(18, 8, 18, 8) };
        _name.Dock = DockStyle.Fill;
        _name.Text = currentName ?? "";
        _name.MaxLength = 80;
        editor.Controls.Add(_name);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            Padding = new Padding(18, 12, 12, 10),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var save = new Button { Text = "保存名称", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var restore = new Button { Text = $"恢复“{Shorten(originalName)}”", AutoSize = true };
        restore.Click += (_, _) =>
        {
            RestoreOriginalName = true;
            _name.Text = originalName ?? "";
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(restore);

        Controls.Add(buttons);
        Controls.Add(editor);
        Controls.Add(hint);
        Controls.Add(title);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && !RestoreOriginalName && CommandDisplayName.Length == 0)
        {
            MessageBox.Show(this, "显示名称不能为空。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private static string Shorten(string value)
    {
        var text = (value ?? "原名").Trim();
        return text.Length <= 10 ? text : text.Substring(0, 9) + "…";
    }
}
