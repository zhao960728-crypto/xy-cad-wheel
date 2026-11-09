using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class RadialMenuWindow : Form
{
	private static readonly IntPtr HwndTopMost = new IntPtr(-1);

	private static readonly Color TransparentKeyColor = Color.FromArgb(1, 2, 3);

	private readonly Action<MenuItemConfig> _selected;

	private readonly RadialMenuControl _control = new RadialMenuControl(TransparentKeyColor)
	{
		Dock = DockStyle.Fill
	};

	private readonly RadialForegroundWindow _foreground = new RadialForegroundWindow();

	private readonly System.Windows.Forms.Timer _dragTimer = new System.Windows.Forms.Timer
	{
		Interval = 16
	};

	private PluginConfig _config = new PluginConfig();

	private SceneMenuConfig _scene = new SceneMenuConfig();

	private MouseButtons _triggerButton = MouseButtons.Right;

	private bool _dragMode;

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams obj = base.CreateParams;
			obj.ExStyle |= 134217728;
			return obj;
		}
	}

	public RadialMenuWindow(Action<MenuItemConfig> selected)
	{
		_selected = selected;
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = FormStartPosition.Manual;
		// The wheel renderer and hit testing use the same screen-pixel bounds.
		// Do not let host font/DPI auto-scaling resize either overlay independently.
		AutoScaleMode = AutoScaleMode.None;
		BackColor = TransparentKeyColor;
		base.TransparencyKey = TransparentKeyColor;
		base.KeyPreview = true;
		// On Windows 7 the color-key + opacity combination can become a black
		// rectangle. The compatibility renderer draws the complete wheel in this
		// single ellipse-shaped window and does not create a transparent label layer.
		_control.DrawLabels = RadialWindowCompatibility.UseLegacySafeRenderer;
		base.Controls.Add(_control);
		_control.ViewChanged += (_, _) =>
		{
			_foreground.CopyNavigationFrom(_control);
			if (RadialWindowCompatibility.UseLegacySafeRenderer && (_scene.StyleId == "11" || _scene.StyleId == "06")) ApplyWindowCompatibility();
		};
		_control.ItemSelected += delegate(object _, MenuItemConfig item)
		{
			Hide();
			_selected(item);
		};
		_control.CancelRequested += delegate
		{
			Hide();
		};
		_dragTimer.Tick += delegate
		{
			TickDragCommit();
		};
	}

	public void ShowMenu(PluginConfig config, string sceneKey, Point center, bool dragMode, MouseButtons triggerButton)
	{
		_config = config;
		_scene = config.Menus.TryGetValue(SceneKeys.Global, out SceneMenuConfig value)
			? value
			: config.Menus.Values.First();
		_dragMode = dragMode;
		_triggerButton = triggerButton;
		base.Bounds = FitMenuBounds(EffectiveMenuSize(config, _scene), center, Screen.FromPoint(center).WorkingArea);
		_control.SetMenu(config, _scene, dragMode, reuseCache: true);
		ApplyWindowCompatibility();
		if (!base.Visible)
		{
			Show();
		}
		SetWindowPos(base.Handle, HwndTopMost, base.Left, base.Top, base.Width, base.Height, 80u);
		// The overlay must not take keyboard focus away from the drawing:
		// after Hide, CAD otherwise leaves native commands waiting for focus.
		if (RadialWindowCompatibility.UseLegacySafeRenderer)
		{
			_foreground.Hide();
		}
		else
		{
			_foreground.ShowMenu(config, _scene, base.Bounds, dragMode);
		}
		UpdateCursor(Cursor.Position);
		if (dragMode)
		{
			_dragTimer.Stop();
			_dragTimer.Start();
		}
		Logger.Trace($"Overlay shown. Wheel=global, Drag={dragMode}, Center={center}, Bounds={base.Bounds}");
	}

	public void UpdateCursor(Point cursor)
	{
		if (base.Visible)
		{
			_control.UpdateCursor(PointToClient(cursor));
			if (!RadialWindowCompatibility.UseLegacySafeRenderer)
			{
				_foreground.CopyNavigationFrom(_control);
				_foreground.EnsureOnTop();
			}
		}
	}

	public void PrimeMenu(PluginConfig config, string sceneKey)
	{
		_config = config;
		_scene = config.Menus.TryGetValue(SceneKeys.Global, out SceneMenuConfig value)
			? value
			: config.Menus.Values.First();
		int menuSize = EffectiveMenuSize(config, _scene);
		Size = new Size(menuSize, menuSize);
		ApplyWindowCompatibility();
		_control.SetMenu(config, _scene);
		ApplyWindowCompatibility();
		if (!RadialWindowCompatibility.UseLegacySafeRenderer)
		{
			_foreground.PrimeMenu(config, _scene, Size);
		}
	}

	public void CommitCursor(Point cursor)
	{
		Point point;
		if (!base.Visible)
		{
			point = cursor;
			Logger.Trace("Commit ignored because overlay is hidden. Cursor=" + point.ToString());
			return;
		}
		point = cursor;
		Logger.Trace("Commit cursor=" + point.ToString());
		_dragTimer.Stop();
		_dragMode = false;
		_control.EndDragMode();
		_control.CommitAt(PointToClient(cursor));
	}

	public void ReloadConfig()
	{
		_control.SetMenu(_config, _scene);
		if (!RadialWindowCompatibility.UseLegacySafeRenderer)
		{
			_foreground.SetMenu(_config, _scene);
		}
	}

	protected override void SetVisibleCore(bool value)
	{
		if (!value)
		{
			_dragTimer.Stop();
			_dragMode = false;
			_foreground.Hide();
		}
		base.SetVisibleCore(value);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_dragTimer.Dispose();
			_foreground.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if ((keyData & Keys.KeyCode) == Keys.Escape)
		{
			Hide();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	internal static Rectangle FitMenuBounds(int requestedSize, Point center, Rectangle workingArea)
	{
		// Position clamping alone cannot keep a 900px wheel inside a 720px
		// display. Fit the complete square first, then clamp on the actual
		// trigger monitor (including negative coordinates and taskbar offsets).
		var available = Math.Max(1, Math.Min(workingArea.Width, workingArea.Height));
		var margin = Math.Min(8, Math.Max(0, (available - 120) / 2));
		var size = Math.Max(1, Math.Min(Math.Max(260, Math.Min(900, requestedSize)), available - margin * 2));
		var left = Math.Max(workingArea.Left + margin,
			Math.Min(center.X - size / 2, workingArea.Right - margin - size));
		var top = Math.Max(workingArea.Top + margin,
			Math.Min(center.Y - size / 2, workingArea.Bottom - margin - size));
		return new Rectangle(left, top, size, size);
	}

	private static int EffectiveMenuSize(PluginConfig config, SceneMenuConfig scene)
	{
		int size = scene.MenuSizePx > 0 ? scene.MenuSizePx : config.MenuSizePx;
		return Math.Max(260, Math.Min(900, size));
	}

	private void ApplyWindowCompatibility()
	{
		base.Opacity = (_scene.StyleId == "01" || _scene.StyleId == "orbit" || _scene.StyleId == "free")
			? (RadialWindowCompatibility.UseLegacySafeRenderer ? 1.0 : Math.Max(.05, Math.Min(1.0, _scene.SurfaceOpacityPercent / 100.0)))
			: RadialWindowCompatibility.EffectiveWindowOpacity(_config.OpacityPercent);
		if (!RadialWindowCompatibility.UseLegacySafeRenderer)
		{
			Region = null;
			return;
		}

		// Do not depend on per-pixel/color-key composition for the outside area on
		// Windows 7. Restrict the native window itself to the wheel circle.
		var previous = Region;
		Region = _control.CreateVisibleStyleRegion();
		previous?.Dispose();
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	private void TickDragCommit()
	{
		if (!_dragMode || !base.Visible)
		{
			_dragTimer.Stop();
			return;
		}
		Point position = Cursor.Position;
		UpdateCursor(position);
		if ((Control.MouseButtons & _triggerButton) != _triggerButton)
		{
			Point point = position;
			Logger.Trace("Drag trigger released by radial timer at " + point.ToString());
			CommitCursor(position);
		}
	}
}
