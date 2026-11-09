using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class RadialOverlayHost : IDisposable
{
	private readonly Action<MenuItemConfig> _selected;

	private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(initialState: false);

	private readonly object _moveSync = new object();

	private Thread? _thread;

	private ApplicationContext? _context;

	private RadialMenuWindow? _window;

	private Point _latestCursor;

	private int _moveVersion;

	private bool _movePosted;

	private bool _disposed;
	private long _lastShowPipelineTicks;
	private long _lastPrimeTicks;
	private int _primeCompletedCount;

	public bool IsReady => !_disposed && _ready.IsSet && _window != null && !_window.IsDisposed && _window.IsHandleCreated;
	public double LastShowPipelineMs => TicksToMs(Interlocked.Read(ref _lastShowPipelineTicks));
	public double LastPrimeMs => TicksToMs(Interlocked.Read(ref _lastPrimeTicks));
	public int PrimeCompletedCount => Volatile.Read(ref _primeCompletedCount);

	public RadialOverlayHost(Action<MenuItemConfig> selected)
	{
		_selected = selected;
	}

	public void Start()
	{
		if (_thread == null)
		{
			_thread = new Thread(Run)
			{
				IsBackground = true,
				Name = "XiaoYangCleanRadialOverlay"
			};
			_thread.SetApartmentState(ApartmentState.STA);
			_thread.Start();
		}
	}

	public void Show(PluginConfig config, string sceneKey, Point center, bool dragMode, MouseButtons triggerButton)
	{
		Start();
		long timestamp = Stopwatch.GetTimestamp();
		if (!_ready.Wait(2000) || _window == null || _disposed)
		{
			Logger.Error("Overlay show failed. Ready=" + _ready.IsSet + " WindowNull=" + (_window == null) + " Disposed=" + _disposed);
		}
		else
		{
			Send(delegate(RadialMenuWindow window)
			{
				window.ShowMenu(config, sceneKey, center, dragMode, triggerButton);
			}, 1500);
			var elapsedTicks = Stopwatch.GetTimestamp() - timestamp;
			Interlocked.Exchange(ref _lastShowPipelineTicks, elapsedTicks);
			Logger.Trace("Overlay show pipeline ms=" + TicksToMs(elapsedTicks));
		}
	}

	public void Update(Point cursor)
	{
		bool flag = false;
		lock (_moveSync)
		{
			_latestCursor = cursor;
			_moveVersion++;
			if (!_movePosted)
			{
				_movePosted = true;
				flag = true;
			}
		}
		if (flag)
		{
			Post(ProcessLatestCursor);
		}
	}

	public void Commit(Point cursor)
	{
		Start();
		if (!_ready.Wait(2000) || _window == null || _disposed)
		{
			Logger.Error("Overlay commit failed. Ready=" + _ready.IsSet + " WindowNull=" + (_window == null) + " Disposed=" + _disposed);
		}
		else
		{
			Send(delegate(RadialMenuWindow window)
			{
				window.CommitCursor(cursor);
			}, 1500);
		}
	}

	public void ReloadConfig()
	{
		Post(delegate(RadialMenuWindow window)
		{
			window.ReloadConfig();
		});
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		try
		{
			Post(delegate(RadialMenuWindow window)
			{
				window.Close();
				_context?.ExitThread();
			});
		}
		catch
		{
			_context?.ExitThread();
		}

		// AutoCAD 2026 unloads .NET plug-ins while CoreCLR is shutting down.
		// Wait for the overlay STA thread to finish before releasing its wait
		// handle; otherwise the background thread can still touch disposed managed
		// state during process teardown and trigger a CoreCLR access violation.
		var thread = _thread;
		if (thread != null && thread != Thread.CurrentThread && thread.IsAlive)
		{
			try { thread.Join(3000); } catch { }
		}
		if (thread == null || !thread.IsAlive) _ready.Dispose();
	}

	public void Cancel()
	{
		Post(window => window.Hide());
	}

	public void Prime(PluginConfig config, string sceneKey)
	{
		Start();
		var scene = config.Menus.TryGetValue(SceneKeys.Global, out SceneMenuConfig value)
			? value
			: config.Menus.Values.First();
		var menuSize = Math.Max(260, Math.Min(900, scene.MenuSizePx > 0 ? scene.MenuSizePx : config.MenuSizePx));
		var size = new Size(menuSize, menuSize);

		// Keep the overlay STA responsive. The expensive first-pass text fitting is
		// performed on a separate STA and stored in a shared, thread-safe cache.
		// A right-click can therefore never queue behind the warm-up work.
		if (_ready.Wait(2000) && _window != null && !_disposed)
		{
			Post(window => window.PrimeMenu(config, sceneKey));
		}

		var warmThread = new Thread((ThreadStart)delegate
		{
			try
			{
				long timestamp = Stopwatch.GetTimestamp();
				RadialMenuControl.WarmSharedRenderCache(config, scene, size);
				var elapsedTicks = Stopwatch.GetTimestamp() - timestamp;
				Interlocked.Exchange(ref _lastPrimeTicks, elapsedTicks);
				Interlocked.Increment(ref _primeCompletedCount);
				Logger.Trace("Overlay render cache primed ms=" + TicksToMs(elapsedTicks));
			}
			catch (Exception ex)
			{
				Logger.Error("Overlay render cache warm-up failed", ex);
			}
		})
		{
			IsBackground = true,
			Name = "XiaoYangRadialCacheWarmup"
		};
		warmThread.SetApartmentState(ApartmentState.STA);
		warmThread.Start();
	}

	private void Run()
	{
		try
		{
			_context = new ApplicationContext();
			_window = new RadialMenuWindow(_selected);
			_window.CreateControl();
			_ = _window.Handle;
			_ready.Set();
			Application.Run(_context);
		}
		catch (Exception ex)
		{
			Logger.Error("Overlay thread failed", ex);
			_ready.Set();
		}
	}

	private void Post(Action<RadialMenuWindow> action)
	{
		RadialMenuWindow window = _window;
		if (window == null || window.IsDisposed || !window.IsHandleCreated)
		{
			return;
		}
		try
		{
			window.BeginInvoke((Action)delegate
			{
				action(window);
			});
		}
		catch (InvalidOperationException)
		{
		}
	}

	private void ProcessLatestCursor(RadialMenuWindow window)
	{
		Point latestCursor;
		int moveVersion;
		lock (_moveSync)
		{
			latestCursor = _latestCursor;
			moveVersion = _moveVersion;
		}
		window.UpdateCursor(latestCursor);
		bool flag = false;
		lock (_moveSync)
		{
			if (_moveVersion == moveVersion)
			{
				_movePosted = false;
			}
			else
			{
				flag = true;
			}
		}
		if (flag)
		{
			Post(ProcessLatestCursor);
		}
	}

	private static double ElapsedMs(long started)
	{
		return (double)(Stopwatch.GetTimestamp() - started) * 1000.0 / (double)Stopwatch.Frequency;
	}

	private static double TicksToMs(long ticks) =>
		(double)ticks * 1000.0 / (double)Stopwatch.Frequency;

	private void Send(Action<RadialMenuWindow> action, int timeoutMs)
	{
		RadialMenuWindow window = _window;
		if (window == null || window.IsDisposed || !window.IsHandleCreated)
		{
			return;
		}
		if (!window.InvokeRequired)
		{
			action(window);
			return;
		}
		// A queued callback can outlive the caller's timeout. Task completion has
		// no disposable wait handle for that callback to access after the timeout.
		var done = new TaskCompletionSource<bool>();
		var dispatchState = 0; // pending / running / cancelled before dispatch
		try
		{
			window.BeginInvoke((Action)delegate
			{
				if (Interlocked.CompareExchange(ref dispatchState, 1, 0) != 0) return;
				try
				{
					if (!_disposed && !window.IsDisposed) action(window);
				}
				catch (Exception ex2)
				{
					Logger.Error("Overlay invoke failed", ex2);
				}
				finally
				{
					done.TrySetResult(true);
				}
			});
			if (!done.Task.Wait(timeoutMs))
			{
				Interlocked.CompareExchange(ref dispatchState, 2, 0);
				Logger.Error("Overlay invoke timed out.");
			}
		}
		catch (InvalidOperationException)
		{
		}
	}
}
