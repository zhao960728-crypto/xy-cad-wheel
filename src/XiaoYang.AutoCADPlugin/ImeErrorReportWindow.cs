using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class ImeErrorReportWindow : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private readonly TextBox _report = new();
    private readonly Action _disableReports;

    public ImeErrorReportWindow(Action disableReports)
    {
        _disableReports = disableReports ?? (() => { });
        Text = "小阳CAD输入法文本报告";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(520, 286);
        MinimumSize = new Size(460, 250);
        Font = new Font("宋体", 9F);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(14, 11, 14, 0),
            Text = "输入法自动切换报告（不影响 CAD 正常操作）",
            Font = new Font("宋体", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 48, 60),
            BackColor = Color.FromArgb(239, 247, 249)
        };
        _report.Dock = DockStyle.Fill;
        _report.Multiline = true;
        _report.ReadOnly = true;
        _report.ScrollBars = ScrollBars.Vertical;
        _report.BorderStyle = BorderStyle.FixedSingle;
        _report.BackColor = Color.White;
        _report.ForeColor = Color.FromArgb(45, 55, 65);
        _report.Font = new Font("宋体", 9F);
        _report.Margin = new Padding(12);

        var reportHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 8) };
        reportHost.Controls.Add(_report);
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Color.FromArgb(246, 248, 250)
        };
        var close = new Button { Text = "关闭", Dock = DockStyle.Right, Width = 82 };
        close.Click += (_, _) => Close();
        var disable = new Button { Text = "不再显示此报告", Dock = DockStyle.Right, Width = 136 };
        disable.Click += (_, _) =>
        {
            try { _disableReports(); }
            finally { Close(); }
        };
        footer.Controls.Add(close);
        footer.Controls.Add(disable);

        Controls.Add(reportHost);
        Controls.Add(footer);
        Controls.Add(header);
    }

    internal static bool IsNonBlockingNoActivate => true;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void ShowReport(string text, IntPtr ownerWindow)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ShowReport(text, ownerWindow)));
            return;
        }

        _report.Text = text ?? "";
        PositionNearWorkingArea();
        if (!Visible)
        {
            if (ownerWindow != IntPtr.Zero) Show(new WindowHandleOwner(ownerWindow));
            else Show();
        }
        SetWindowPos(Handle, HwndTop, Left, Top, Width, Height, SwpNoActivate | SwpShowWindow);
    }

    private sealed class WindowHandleOwner : IWin32Window
    {
        public WindowHandleOwner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    private void PositionNearWorkingArea()
    {
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Left = Math.Max(workingArea.Left, workingArea.Right - Width - 18);
        Top = Math.Max(workingArea.Top, workingArea.Bottom - Height - 18);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);
}
