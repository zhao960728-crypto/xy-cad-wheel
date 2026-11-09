using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class MouseWheelTrigger : IDisposable
{
	private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	private struct MouseInfo
	{
		public NativePoint Point;

		public IntPtr Window;

		public uint HitTestCode;

		public IntPtr ExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MouseInfoEx
	{
		public MouseInfo Base;
		public uint MouseData;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		public int X;

		public int Y;
	}

	internal enum TriggerButton
	{
		Right,
		Middle,
		XButton1,
		XButton2
	}

	private const int WhMouse = 7;

	private const int WmMouseMove = 512;

	private const int WmMButtonDown = 519;

	private const int WmMButtonUp = 520;

	private const int WmRButtonDown = 516;

	private const int WmRButtonUp = 517;
	private const int WmXButtonDown = 523;
	private const int WmXButtonUp = 524;

	private const int VkShift = 16;

	private const int VkControl = 17;

	private const int VkMenu = 18;

	private static readonly IntPtr ReplayMarker = new IntPtr(1481196109);

	private readonly Func<string> _triggerModeProvider;

	private readonly Func<int> _holdDelayProvider;

	private readonly Func<IntPtr> _autoCADHwndProvider;

	private readonly Func<bool> _cadCommandActiveProvider;

	private readonly Action<Point, MouseButtons> _show;

	private readonly Action<Point> _commit;

	private readonly Action _cancel;

	private readonly LowLevelMouseProc _proc;

	private readonly object _stateSync = new object();

	private IntPtr _hook;

	private uint _autoCADProcessId;

	private IntPtr _autoCADWindow;

	private volatile bool _tracking;

	private volatile bool _active;

	private volatile bool _disposed;

	private Point _start;

	private Point _current;

	private System.Threading.Timer? _timer;

	private TriggerButton _triggerButton;

	private int _triggerModifierVirtualKey;

	private int _gestureId;

	private string _lastError = "";

	public bool IsInstalled
	{
		get
		{
			lock (_stateSync)
			{
				return _hook != IntPtr.Zero;
			}
		}
	}

	public string LastError
	{
		get
		{
			lock (_stateSync)
			{
				return _lastError;
			}
		}
	}

	public MouseWheelTrigger(Func<string> triggerModeProvider, Func<int> holdDelayProvider,
		Func<IntPtr> autoCADHwndProvider, Func<bool> cadCommandActiveProvider,
		Action<Point, MouseButtons> show, Action<Point> commit, Action cancel)
	{
		_triggerModeProvider = triggerModeProvider;
		_holdDelayProvider = holdDelayProvider;
		_autoCADHwndProvider = autoCADHwndProvider;
		_cadCommandActiveProvider = cadCommandActiveProvider ?? (() => false);
		_show = show;
		_commit = commit;
		_cancel = cancel;
		_proc = HookCallback;
	}

	public bool Start()
	{
		lock (_stateSync)
		{
			if (_disposed)
			{
				_lastError = "鼠标触发器已经释放。";
				return false;
			}
			if (_hook != IntPtr.Zero) return true;

			IntPtr intPtr = _autoCADHwndProvider();
			_autoCADWindow = intPtr;
			uint processId;
			uint threadId = intPtr != IntPtr.Zero
				? NativeMethods.GetWindowThreadProcessId(intPtr, out processId)
				: 0u;
			if (threadId == 0)
			{
				SetStartError($"找不到 {CadHostInfo.ProductName} 主窗口线程。");
				return false;
			}

			_hook = SetWindowsHookEx(WhMouse, _proc, IntPtr.Zero, threadId);
			if (_hook == IntPtr.Zero)
			{
				SetStartError("安装鼠标钩子失败，Win32Error=" + Marshal.GetLastWin32Error());
				return false;
			}

			_lastError = "";
			Logger.Info(CadHostInfo.ProductName + " 鼠标触发器已就绪，ThreadId=" + threadId);
			return true;
		}
	}

	private void SetStartError(string message)
	{
		if (!string.Equals(_lastError, message, StringComparison.Ordinal))
			Logger.Error(message);
		_lastError = message;
	}

	public void Dispose()
	{
		IntPtr hook;
		lock (_stateSync)
		{
			if (_disposed) return;
			_disposed = true;
			_tracking = false;
			_active = false;
			_gestureId++;
			_timer?.Dispose();
			_timer = null;
			hook = _hook;
			_hook = IntPtr.Zero;
			_autoCADWindow = IntPtr.Zero;
		}
		if (hook != IntPtr.Zero)
		{
			UnhookWindowsHookEx(hook);
		}
	}

	private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
	{
		if (nCode < 0 || _disposed)
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		int num = wParam.ToInt32();
		// Most hook traffic is ordinary pointer movement. Decide whether this
		// event can affect a gesture before copying any native event structure.
		if (!_tracking && !IsAnyTriggerDown(num))
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		if (_tracking && num != 512 && !IsAnyTriggerUp(num) && !IsAnyTriggerDown(num))
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		MouseInfo mouseInfo = Marshal.PtrToStructure<MouseInfo>(lParam);
		if (mouseInfo.ExtraInfo == ReplayMarker)
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		// WH_MOUSE uses MOUSEHOOKSTRUCTEX, not MSLLHOOKSTRUCT. The native
		// base struct includes pointer alignment (20 bytes on x86, 32 on x64).
		uint mouseData = num == WmXButtonDown || num == WmXButtonUp ? ReadExtendedMouseData(lParam) : 0;
		Point point = new Point(mouseInfo.Point.X, mouseInfo.Point.Y);
		string triggerMode = TriggerModes.Normalize(_triggerModeProvider());
		if (TryGetTriggerButton(num, mouseData, triggerMode, out var button, out var pressedModifierVirtualKey))
		{
			if (!IsInAutoCadDrawingArea(point)
				|| ShouldBypassForActiveCommand(triggerMode, _cadCommandActiveProvider()))
			{
				return CallNextHookEx(_hook, nCode, wParam, lParam);
			}
			lock (_stateSync)
			{
				if (_tracking)
				{
					_timer?.Dispose();
					_timer = null;
					_tracking = false;
					_active = false;
				}
				int gestureId = ++_gestureId;
				_tracking = true;
				_active = false;
				_triggerButton = button;
				_triggerModifierVirtualKey = pressedModifierVirtualKey;
				_start = point;
				_current = point;
				_timer?.Dispose();
				_timer = new System.Threading.Timer(delegate
				{
					Activate(gestureId);
				}, null, Math.Max(100, _holdDelayProvider()), -1);
			}
			Logger.Trace("Hold tracking started at " + point);
			// Other hook-based add-ins still observe the physical event through the
			// hook chain, while the target window receives either a complete replayed
			// short click or no click for a wheel gesture. Never deliver an unmatched
			// right-button DOWN to the CAD drawing window.
			return SuppressAfterNext(nCode, wParam, lParam);
		}
		if (!_tracking)
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		if (num == 512)
		{
			lock (_stateSync)
			{
				if (_tracking)
				{
					_current = point;
				}
			}
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		if (IsTriggerUp(num, mouseData, _triggerButton))
		{
			bool active;
			Point start;
			int releasedModifierVirtualKey;
			lock (_stateSync)
			{
				_timer?.Dispose();
				_timer = null;
				active = _active;
				start = _start;
				releasedModifierVirtualKey = _triggerModifierVirtualKey;
				_tracking = false;
				_active = false;
				_gestureId++;
			}
			if (!active)
			{
				ReplayClick(_triggerButton, start);
			}
			else
			{
				Logger.Trace("Hold released in AutoCAD hook at " + point);
				ThreadPool.QueueUserWorkItem(delegate
				{
					if (!WaitForModifierRelease(releasedModifierVirtualKey, 3000))
					{
						Logger.Info("轮盘触发修饰键长时间未释放，本次提交已取消，避免把按键状态带入 AutoCAD 命令。");
						_cancel();
						return;
					}
					// 物理按键已经抬起后，给 AutoCAD 主线程一个很短的时间处理 KEYUP，
					// 再启动命令，避免 Shift 临时正交覆盖变成透明 ORTHOMODE 提示。
					if (releasedModifierVirtualKey != 0) Thread.Sleep(50);
					_commit(point);
				});
			}
			return SuppressAfterNext(nCode, wParam, lParam);
		}
		return CallNextHookEx(_hook, nCode, wParam, lParam);
	}

	internal static bool TryGetTriggerButton(int message, uint mouseData, string triggerMode, out TriggerButton button,
		out int modifierVirtualKey)
	{
		button = TriggerButton.Right;
		string text = TriggerModes.Normalize(triggerMode);
		modifierVirtualKey = RequiredModifierKeyForMode(text);
		if (message == WmXButtonDown)
		{
			var side = (mouseData >> 16) & 0xffff;
			if (side == 1 && text == TriggerModes.XButton1Hold) { button = TriggerButton.XButton1; return true; }
			if (side == 2 && text == TriggerModes.XButton2Hold) { button = TriggerButton.XButton2; return true; }
			return false;
		}
		if (message == 519 && text == "middle-hold")
		{
			button = TriggerButton.Middle;
			return true;
		}
		if (message != 516)
		{
			return false;
		}
		if (text == "shift-right-hold")
		{
			return IsKeyDown(VkShift);
		}
		if (text == "alt-right-hold")
		{
			return IsKeyDown(VkMenu);
		}
		if (text == "ctrl-right-hold")
		{
			return IsKeyDown(VkControl);
		}
		return text == "right-hold";
	}

	private static bool IsAnyTriggerDown(int message)
	{
		if (message == 516 || message == 519 || message == WmXButtonDown)
		{
			return true;
		}
		return false;
	}

	private static bool IsAnyTriggerUp(int message)
	{
		if (message == 517 || message == 520 || message == WmXButtonUp)
		{
			return true;
		}
		return false;
	}

	internal static bool IsTriggerUp(int message, uint mouseData, TriggerButton button)
	{
		if (button == TriggerButton.XButton1 || button == TriggerButton.XButton2)
			return message == WmXButtonUp && ((mouseData >> 16) & 0xffff) == (button == TriggerButton.XButton1 ? 1u : 2u);
		if (button != TriggerButton.Middle)
		{
			return message == 517;
		}
		return message == 520;
	}

	private static bool IsKeyDown(int virtualKey)
	{
		return (GetKeyState(virtualKey) & 0x8000) != 0;
	}

	private void Activate(int gestureId)
	{
		Point start;
		Point current;
		TriggerButton triggerButton;
		lock (_stateSync)
		{
			if (!_tracking || _active || _disposed || gestureId != _gestureId)
			{
				return;
			}
			_timer?.Dispose();
			_timer = null;
			start = _start;
			current = _current;
			triggerButton = _triggerButton;
		}
		var triggerMode = TriggerModes.Normalize(_triggerModeProvider());
		if (!IsInAutoCadDrawingArea(current)
		    || ShouldBypassForActiveCommand(triggerMode, _cadCommandActiveProvider()))
		{
			// Keep tracking until the physical UP arrives. The normal short-click
			// replay then gives the target a complete DOWN/UP pair instead of leaving
			// either CAD or another add-in in a half-click state.
			Logger.Trace("Hold activation cancelled because CAD context changed.");
			return;
		}
		lock (_stateSync)
		{
			if (!_tracking || _active || _disposed || gestureId != _gestureId) return;
			_active = true;
			start = _start;
			current = _current;
			triggerButton = _triggerButton;
		}
		Logger.Trace("Hold activated. Start=" + start + " Current=" + current);
		_show(start, ToMouseButtons(triggerButton));
	}

	internal static bool ShouldBypassForActiveCommand(string triggerMode, bool commandActive) =>
		commandActive;

	internal static bool PreservesPairedRightClickDelivery => true;

	internal static bool CallsNextHookBeforeSuppression => true;

	internal static bool RestrictsTriggerToMdiDrawingArea => true;

	internal static bool RequiresEnabledForegroundMainWindow => true;

	private IntPtr SuppressAfterNext(int nCode, IntPtr wParam, IntPtr lParam)
	{
		CallNextHookEx(_hook, nCode, wParam, lParam);
		return (IntPtr)1;
	}

	internal static int RequiredModifierKeyForMode(string triggerMode)
	{
		string mode = TriggerModes.Normalize(triggerMode);
		if (mode == TriggerModes.ShiftRightHold) return VkShift;
		if (mode == TriggerModes.AltRightHold) return VkMenu;
		if (mode == TriggerModes.CtrlRightHold) return VkControl;
		return 0;
	}

	private bool WaitForModifierRelease(int virtualKey, int timeoutMs)
	{
		if (virtualKey == 0) return true;
		DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
		while (!_disposed && DateTime.UtcNow < deadline)
		{
			if ((NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) == 0) return true;
			Thread.Sleep(10);
		}
		return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) == 0;
	}

	internal static MouseButtons ToMouseButtons(TriggerButton button)
	{
		if (button == TriggerButton.XButton1) return MouseButtons.XButton1;
		if (button == TriggerButton.XButton2) return MouseButtons.XButton2;
		if (button != TriggerButton.Middle)
		{
			return MouseButtons.Right;
		}
		return MouseButtons.Middle;
	}

	private bool IsInAutoCadDrawingArea(Point point)
	{
		IntPtr intPtr = _autoCADWindow;
		if (intPtr == IntPtr.Zero || !NativeMethods.GetWindowRect(intPtr, out var rect))
		{
			return false;
		}
		if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top + 90 || point.Y >= rect.Bottom)
		{
			return false;
		}
		IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
		if (!NativeMethods.IsWindowEnabled(intPtr) || foregroundWindow != intPtr)
		{
			return false;
		}
		IntPtr intPtr2 = NativeMethods.WindowFromPoint(new NativeMethods.POINT
		{
			X = point.X,
			Y = point.Y
		});
		return intPtr2 != IntPtr.Zero
		       && IsSameProcessWindow(intPtr2, intPtr)
		       && IsMdiDrawingAreaWindow(intPtr2, intPtr);
	}

	private static bool IsMdiDrawingAreaWindow(IntPtr candidate, IntPtr mainWindow)
	{
		var current = candidate;
		for (var depth = 0; current != IntPtr.Zero && depth < 64; depth++)
		{
			var className = GetWindowClass(current);
			if (IsExcludedInteractiveWindowClass(className)) return false;
			if (className.Equals("MDIClient", StringComparison.OrdinalIgnoreCase)) return true;
			if (current == mainWindow) return false;
			current = NativeMethods.GetParent(current);
		}
		return false;
	}

	internal static bool IsDrawingAreaClassPath(IEnumerable<string> ancestorClasses)
	{
		foreach (var className in ancestorClasses ?? Array.Empty<string>())
		{
			if (IsExcludedInteractiveWindowClass(className)) return false;
			if (string.Equals(className, "MDIClient", StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	private static bool IsExcludedInteractiveWindowClass(string className)
	{
		var name = (className ?? "").Trim();
		return name.Equals("#32770", StringComparison.OrdinalIgnoreCase)
		       || name.Equals("Edit", StringComparison.OrdinalIgnoreCase)
		       || name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
		       || name.StartsWith("WindowsForms10.", StringComparison.OrdinalIgnoreCase)
		       || name.StartsWith("HwndWrapper[", StringComparison.OrdinalIgnoreCase)
		       || name.StartsWith("AfxControlBar", StringComparison.OrdinalIgnoreCase)
		       || name.Equals("ToolbarWindow32", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetWindowClass(IntPtr window)
	{
		var buffer = new char[256];
		var length = NativeMethods.GetClassName(window, buffer, buffer.Length);
		return length > 0 ? new string(buffer, 0, length) : "";
	}

	private bool IsSameProcessWindow(IntPtr candidate, IntPtr autoCADHwnd)
	{
		if (candidate == IntPtr.Zero)
		{
			return false;
		}
		NativeMethods.GetWindowThreadProcessId(autoCADHwnd, out var processId);
		if (processId == 0)
		{
			return false;
		}
		_autoCADProcessId = processId;
		NativeMethods.GetWindowThreadProcessId(candidate, out var processId2);
		return processId2 == _autoCADProcessId;
	}

	private void ReplayClick(TriggerButton button, Point point)
	{
		// The physical DOWN was suppressed. If focus left the drawing in the
		// meantime, discard the gesture instead of injecting a click into another app.
		if (_disposed || !IsInAutoCadDrawingArea(point)) return;
		NativeMethods.SetCursorPos(point.X, point.Y);
		UIntPtr marker = new UIntPtr(unchecked((ulong)ReplayMarker.ToInt64()));
		GetReplayParameters(button, out var down, out var up, out var data);
		NativeMethods.mouse_event(down, 0u, 0u, data, marker);
		NativeMethods.mouse_event(up, 0u, 0u, data, marker);
	}

	internal static uint ReadExtendedMouseData(IntPtr pointer) =>
		unchecked((uint)Marshal.ReadInt32(pointer, (int)Marshal.OffsetOf<MouseInfoEx>(nameof(MouseInfoEx.MouseData))));

	internal static int ExtendedMouseDataOffset => (int)Marshal.OffsetOf<MouseInfoEx>(nameof(MouseInfoEx.MouseData));

	internal static void GetReplayParameters(TriggerButton button, out uint down, out uint up, out uint data)
	{
		data = button == TriggerButton.XButton1 ? 1u : button == TriggerButton.XButton2 ? 2u : 0u;
		down = data != 0 ? 0x80u : button == TriggerButton.Middle ? 0x20u : 0x08u;
		up = data != 0 ? 0x100u : button == TriggerButton.Middle ? 0x40u : 0x10u;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll")]
	private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern short GetKeyState(int nVirtKey);
}
