using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class RadialForegroundWindow : Form
{
	private static readonly IntPtr HwndTopMost = new IntPtr(-1);

	private static readonly Color TransparentKeyColor = Color.FromArgb(1, 2, 3);

	private readonly RadialMenuControl _control = new RadialMenuControl(TransparentKeyColor)
	{
		Dock = DockStyle.Fill,
		DrawBackdrop = false,
		DrawGuides = false,
		DrawHover = false
	};

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams obj = base.CreateParams;
			obj.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
			obj.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
			obj.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
			return obj;
		}
	}

	public RadialForegroundWindow()
	{
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		TopMost = true;
		StartPosition = FormStartPosition.Manual;
		AutoScaleMode = AutoScaleMode.None;
		BackColor = TransparentKeyColor;
		TransparencyKey = TransparentKeyColor;
		Controls.Add(_control);
	}

	public void ShowMenu(PluginConfig config, SceneMenuConfig scene, Rectangle bounds, bool dragMode)
	{
		Bounds = bounds;
		Opacity = scene.StyleId == "01" || scene.StyleId == "orbit" || scene.StyleId == "free" ? 1.0 : RadialWindowCompatibility.EffectiveWindowOpacity(config.OpacityPercent);
		_control.SetMenu(config, scene, dragMode, reuseCache: true);
		if (!Visible)
		{
			Show();
		}
		SetWindowPos(Handle, HwndTopMost, Left, Top, Width, Height, 0x0010 | 0x0040);
		UpdateCursor(Cursor.Position);
	}

	public void PrimeMenu(PluginConfig config, SceneMenuConfig scene, Size size)
	{
		Size = size;
		_control.SetMenu(config, scene);
	}

	public void SetMenu(PluginConfig config, SceneMenuConfig scene)
	{
		_control.SetMenu(config, scene);
	}

	public void UpdateCursor(Point cursor)
	{
		if (Visible)
		{
			_control.UpdateCursor(PointToClient(cursor));
		}
	}

	public void EnsureOnTop()
	{
		if (Visible)
		{
			SetWindowPos(Handle, HwndTopMost, Left, Top, Width, Height, 0x0010 | 0x0040);
		}
	}

	internal void CopyNavigationFrom(RadialMenuControl source) => _control.CopyNavigationFrom(source);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
