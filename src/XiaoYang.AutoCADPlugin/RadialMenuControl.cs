using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class RadialMenuControl : Control
{
	private static readonly Color WheelSurface = Color.White;
	private static readonly Color WheelSurfaceRaised = Color.FromArgb(247, 249, 251);
	private static readonly Color WheelLine = Color.FromArgb(218, 224, 230);
	private static readonly Color WheelText = Color.FromArgb(28, 38, 49);
	private static readonly Color WheelAccent = Color.FromArgb(0, 140, 162);
	private static readonly float[] TextPlacementFractions = { 0.56f, 0.51f, 0.61f, 0.46f, 0.66f, 0.36f, 0.76f, 0.26f, 0.86f };
	private static readonly ConcurrentDictionary<string, FittedTextLayout> SharedTextLayoutCache = new(StringComparer.Ordinal);
	private readonly List<TextLayoutDiagnostic> _textDiagnostics = new();
	private int _textLayoutCacheHits;
	private int _textLayoutCacheMisses;

	internal sealed class TextLayoutDiagnostic
	{
		public string Label { get; init; } = "";
		public float LogicalFontSizePx { get; init; }
		public bool FullText { get; init; }
		public bool FitsBounds { get; init; }
		public int LineCount { get; init; }
	}

	private sealed class FittedTextLayout
	{
		public string[] Lines { get; init; } = Array.Empty<string>();
		public float FontSizePx { get; init; }
		public float LogicalFontSizePx { get; init; }
		public PointF Center { get; init; }
		public bool FullText { get; init; }
		public bool FitsBounds { get; init; }
	}

	private readonly struct HitResult : IEquatable<HitResult>
	{
		public static readonly HitResult None = new HitResult(-1, -1, center: false);

		public static readonly HitResult Center = new HitResult(-1, -1, center: true);

		public int RingIndex { get; }

		public int Slot { get; }

		public bool IsCenter { get; }

		public bool HasSlot
		{
			get
			{
				if (RingIndex >= 0)
				{
					return Slot >= 0;
				}
				return false;
			}
		}

		public HitResult(int ringIndex, int slot, bool center)
		{
			RingIndex = ringIndex;
			Slot = slot;
			IsCenter = center;
		}

		public bool Equals(HitResult other) => RingIndex == other.RingIndex && Slot == other.Slot && IsCenter == other.IsCenter;

		public override bool Equals(object? obj) => obj is HitResult other && Equals(other);

		public override int GetHashCode() => unchecked((RingIndex * 397 ^ Slot) * 397 ^ (IsCenter ? 1 : 0));
	}

	private sealed class RingLayout
	{
		public RingConfig Ring { get; }

		public float InnerRadius { get; }

		public float OuterRadius { get; }

		public float Width => Math.Max(1f, OuterRadius - InnerRadius);

		public RingLayout(RingConfig ring, float innerRadius, float outerRadius)
		{
			Ring = ring;
			InnerRadius = innerRadius;
			OuterRadius = outerRadius;
		}
	}

	private PluginConfig _config = new PluginConfig();

	private SceneMenuConfig _scene = new SceneMenuConfig();

	private HitResult _hover = HitResult.None;
	private HitResult _selected = HitResult.None;

	private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
	private Image? _backgroundImage;
	private string _loadedBackgroundPath = "";

	private bool _dragMode;

	public bool EnableCellSelection { get; set; }

	public bool DrawBackdrop { get; set; } = true;

	public bool DrawForeground { get; set; } = true;

	public bool DrawGuides { get; set; } = true;

	public bool DrawLabels { get; set; } = true;

	public bool DrawHover { get; set; } = true;

	// The settings preview has less room than the real overlay. In preview mode the
	// configured 260-900 px range is mapped proportionally into the available panel,
	// so changing the menu size remains visible instead of being clipped at panel size.
	public bool ScaleMenuForPreview { get; set; }

	internal IReadOnlyList<TextLayoutDiagnostic> TextDiagnostics => _textDiagnostics;
	internal int TextLayoutCacheHits => _textLayoutCacheHits;
	internal int TextLayoutCacheMisses => _textLayoutCacheMisses;

	public event EventHandler<MenuItemConfig>? ItemSelected;

	public event EventHandler? CancelRequested;

	public event EventHandler<RadialCellSelectedEventArgs>? CellSelected;

	public RadialMenuControl(Color transparentKey)
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = transparentKey;
		base.TabStop = true;
	}

	public void SetMenu(PluginConfig config, SceneMenuConfig scene, bool dragMode = false, bool reuseCache = false)
	{
		var keepRenderCache = reuseCache && ReferenceEquals(_config, config) && ReferenceEquals(_scene, scene);
		_config = config;
		_scene = scene;
		if (!EnableCellSelection) { _activeGroup = scene.StyleId == "11" ? scene.Rings.FirstOrDefault(r => r.Enabled)?.Index ?? -1 : -1; }
		_dragMode = dragMode;
		_hover = HitResult.None;
		if (!EnableCellSelection) _selected = HitResult.None;
		if (!keepRenderCache)
		{
			_textLayoutCacheHits = 0;
			_textLayoutCacheMisses = 0;
		}
		if (DrawBackdrop) ReloadBackground(force: !keepRenderCache);
		Invalidate();
	}

	internal void WarmRenderCache()
	{
		if (IsDisposed || Width <= 0 || Height <= 0) return;
		using var bitmap = new Bitmap(Width, Height);
		using var graphics = Graphics.FromImage(bitmap);
		using var args = new PaintEventArgs(graphics, new Rectangle(0, 0, Width, Height));
		OnPaint(args);
	}

	internal static void WarmSharedRenderCache(PluginConfig config, SceneMenuConfig scene, Size size)
	{
		using var control = new RadialMenuControl(Color.FromArgb(1, 2, 3))
		{
			Size = size,
			DrawBackdrop = false,
			DrawGuides = false,
			DrawHover = false,
			DrawForeground = true,
			DrawLabels = true,
			ScaleMenuForPreview = false
		};
		control.SetMenu(config, scene);
		control.WarmRenderCache();
	}

	protected override void OnSizeChanged(EventArgs e)
	{
		base.OnSizeChanged(e);
		if (ScaleMenuForPreview)
		{
			SharedTextLayoutCache.Clear();
			Invalidate();
			Parent?.Invalidate();
		}
	}

	public void UpdateCursor(Point point)
	{
		_previewPointer = point;
		_previewPointerActive = ClientRectangle.Contains(point);
		HitResult hitResult = HitTest(point);
		if (IsAlternativeStyle && hitResult.RingIndex >= 0 && hitResult.Slot == GroupSlot)
		{
			SetActiveGroup(hitResult.RingIndex);
		}
		if (!_hover.Equals(hitResult))
		{
			_hover = hitResult;
			if (!ScaleMenuForPreview)
			{
				var item = FindItem(hitResult);
				_styleToolTip.SetToolTip(this, item == null ? "" : item.Label + (string.IsNullOrWhiteSpace(item.Value) ? "" : "\n" + item.Value));
			}
			Invalidate();
			ViewChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	public void CommitAt(Point point)
	{
		HitResult hit = HitTest(point);
		if (IsAlternativeStyle && NavigateStyle(hit)) return;
		if (!hit.HasSlot)
		{
			Point point2 = point;
			Logger.Trace("Commit cancelled. No slot. Point=" + point2.ToString());
			this.CancelRequested?.Invoke(this, EventArgs.Empty);
			return;
		}
		MenuItemConfig menuItemConfig = FindItem(hit);
		if (menuItemConfig == null || string.IsNullOrWhiteSpace(menuItemConfig.Value))
		{
			Logger.Trace($"Commit cancelled. Empty slot. Point={point}, Ring={hit.RingIndex}, Slot={hit.Slot}");
			this.CancelRequested?.Invoke(this, EventArgs.Empty);
			return;
		}
		Logger.Trace($"Commit selected. Point={point}, Ring={hit.RingIndex}, Slot={hit.Slot}, Label={menuItemConfig.Label}, Kind={menuItemConfig.Kind}, Value={menuItemConfig.Value}");
		this.ItemSelected?.Invoke(this, menuItemConfig);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
        if (MoveGeometryDrag(e)) return;
		_previewMouseDown = e.Button != MouseButtons.None;
		UpdateCursor(e.Location);
		if (ScaleMenuForPreview) Invalidate();
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		_previewPointerActive = false;
		_hover = HitResult.None;
		_styleToolTip.SetToolTip(this, "");
		Invalidate();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (BeginGeometryDrag(e)) { base.OnMouseDown(e); return; }
        base.OnMouseDown(e);
		_previewMouseDown = true;
		if (ScaleMenuForPreview) Invalidate();
		MouseButtons button = e.Button;
		if (button == MouseButtons.Right || button == MouseButtons.Middle || button == MouseButtons.XButton1 || button == MouseButtons.XButton2)
		{
			if (!_dragMode)
			{
				this.CancelRequested?.Invoke(this, EventArgs.Empty);
			}
		}
		else if (e.Button == MouseButtons.Left)
		{
			if (EnableCellSelection)
			{
				SelectCellAt(e.Location);
			}
			else
			{
				CommitAt(e.Location);
			}
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_geometrySnapshot = null; _geometryDrag = HitResult.None; Capture = false;
        base.OnMouseUp(e);
		_previewMouseDown = false;
		if (ScaleMenuForPreview) Invalidate();
		// The hook and RadialMenuWindow's polling timer own gesture completion.
		// Both match the actual trigger button; an unrelated button release here
		// must never execute the hovered command.
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (ScaleMenuForPreview)
			e.Graphics.SetClip(ClientRectangle, CombineMode.Replace);
		_textDiagnostics.Clear();
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.TextRenderingHint = (!DrawBackdrop && !DrawGuides) ? TextRenderingHint.SingleBitPerPixelGridFit : TextRenderingHint.AntiAliasGridFit;
		RectangleF menuBounds = GetMenuBounds();
		if (_scene.StyleId == "01") { DrawSwWheel(e.Graphics, menuBounds); DrawInsertionPoints(e.Graphics); DrawPreviewMagnifier(e.Graphics); DrawGeometryHandles(e.Graphics); return; }
		if (IsAlternativeStyle) { DrawAlternativeStyle(e.Graphics, menuBounds); DrawInsertionPoints(e.Graphics); DrawPreviewMagnifier(e.Graphics); DrawGeometryHandles(e.Graphics); return; }
		if (DrawBackdrop)
		{
			DrawBackground(e.Graphics, menuBounds);
		}
		List<RingConfig> list = (from r in _scene.Rings
			where r.Enabled
			orderby r.Index
			select r).ToList();
		if (list.Count == 0)
		{
			if (DrawForeground)
			{
				DrawCenter(e.Graphics, menuBounds);
			}
			return;
		}
		PointF center = new PointF(menuBounds.Left + menuBounds.Width / 2f, menuBounds.Top + menuBounds.Height / 2f);
		List<RingLayout> list2 = BuildRingLayouts(menuBounds, list);
		using Pen pen = new Pen(WheelLine, 1f);
		using Pen pen2 = new Pen(WheelLine, 0.8f);
		using Pen pen3 = new Pen(WheelAccent, 1.4f);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(85, 0, 140, 162));
		using SolidBrush selectedBrush = new SolidBrush(Color.FromArgb(155, 0, 140, 162));
		foreach (RingLayout item in list2)
		{
			RingConfig ring = item.Ring;
			if (DrawForeground && DrawGuides)
			{
				DrawCircle(e.Graphics, pen, center, item.InnerRadius);
			}
			int slot;
			for (slot = 0; slot < ring.Slots; slot++)
			{
				float num = -90f + (float)slot * 360f / (float)ring.Slots;
				float num2 = 360f / (float)ring.Slots;
				if (DrawBackdrop)
				{
					using var imageClip = CreateSlotPath(center, item.InnerRadius, item.OuterRadius, num, num2);
					DrawCellBackground(e.Graphics, ring.Items.FirstOrDefault(i => i.Slot == slot), imageClip);
				}
				if (DrawHover && ((!BatchSelectionMode && _selected.RingIndex == ring.Index && _selected.Slot == slot) || BatchSelection.Contains((ring.Index, slot))))
				{
					FillSlot(e.Graphics, selectedBrush, center, item.InnerRadius, item.OuterRadius, num, num2);
				}
				if (DrawHover && _hover.RingIndex == ring.Index && _hover.Slot == slot)
				{
					FillSlot(e.Graphics, brush, center, item.InnerRadius, item.OuterRadius, num, num2);
				}
				if (!DrawForeground)
				{
					continue;
				}
				if (DrawGuides)
				{
					e.Graphics.DrawLine(pen2, PointOnCircle(center, item.InnerRadius, num), PointOnCircle(center, item.OuterRadius, num));
				}
				MenuItemConfig menuItemConfig = ring.Items.FirstOrDefault((MenuItemConfig i) => i.Slot == slot);
				if (DrawLabels && menuItemConfig != null)
				{
					float degrees = num + num2 / 2f;
					float radius = (item.InnerRadius + item.OuterRadius) / 2f;
					PointF center2 = PointOnCircle(center, radius, degrees);
					var colorValue = HasTextColor(menuItemConfig.TextColor) ? menuItemConfig.TextColor : _config.TextColor;
					var fontFamily = string.IsNullOrWhiteSpace(menuItemConfig.FontFamily) ? _config.FontFamily : menuItemConfig.FontFamily;
					var fontBold = menuItemConfig.FontBold ?? _config.FontBold;
					var textEffect = string.IsNullOrWhiteSpace(menuItemConfig.TextEffect)
						? _config.TextEffect
						: menuItemConfig.TextEffect;
                    if (HasCellBackground(menuItemConfig) && TextEffects.Normalize(textEffect) == TextEffects.None) textEffect = TextEffects.Outline;
					var clipPadding = Math.Max(0.8f, menuBounds.Width / Math.Max(1f, EffectiveMenuSizePx()) * 1.6f);
					using var clipPath = CreateSlotPath(center, item.InnerRadius + clipPadding, item.OuterRadius - clipPadding,
						num + 0.45f, Math.Max(0.1f, num2 - 0.9f));
                    // Inherited classic cells retain their original text/custom-image layout.
                    // Explicit per-cell modes can also use the command catalog's icon.
                    var cellMode = ConfigStore.NormalizeCellContentMode(menuItemConfig.ContentMode);
                    var classicMode = cellMode == "Inherit"
                        ? GetEmbeddedIcon(menuItemConfig.IconData) != null && !HasCellBackground(menuItemConfig) ? "IconAndText" : "TextOnly"
                        : cellMode;
                    if (ResolveCellContentMode(menuItemConfig) == "ImageOnly" || HasCellBackground(menuItemConfig) && classicMode == "IconOnly") continue;
                    if (!HasCellBackground(menuItemConfig) && classicMode != "TextOnly")
                    {
                        var tangentSpace = ring.Slots <= 2 ? item.Width : 2f * radius * (float)Math.Sin(Math.PI / ring.Slots);
                        var side = Math.Max(1, Math.Min(item.Width, tangentSpace) * .78f);
                        DrawItemCard(e.Graphics, menuItemConfig, new RectangleF(center2.X - side / 2, center2.Y - side / 2, side, side),
                            clipPath, classicMode, menuBounds.Width / Math.Max(1f, EffectiveMenuSizePx()));
                        continue;
                    }
					DrawFittedText(e.Graphics, menuItemConfig.Label, ParseTextColor(colorValue), fontBold, textEffect,
						center2, radius, item.Width, ring.Slots, menuItemConfig.FontSizePx, fontFamily,
						clipPath, center, item.InnerRadius + clipPadding, item.OuterRadius - clipPadding, degrees);
				}
			}
			if (DrawForeground && DrawGuides)
			{
				DrawCircle(e.Graphics, pen, center, item.OuterRadius);
			}
		}
		if (DrawForeground && DrawGuides && list2.Count > 0)
		{
			DrawCircle(e.Graphics, pen3, center, list2[list2.Count - 1].OuterRadius);
		}
		if (DrawForeground && (DrawGuides || DrawLabels))
		{
			DrawCenter(e.Graphics, menuBounds);
		}
		DrawInsertionPoints(e.Graphics);
		DrawPreviewMagnifier(e.Graphics); DrawGeometryHandles(e.Graphics);
	}

	public void SelectCell(int ringIndex, int slot)
	{
		if (!EnableCellSelection) return;
		var ring = _scene.Rings.FirstOrDefault(item => item.Index == ringIndex && item.Enabled);
		if (ring == null || slot < 0 || slot >= ring.Slots) return;
		var nextSelection = new HitResult(ringIndex, slot, center: false);
		if (!_selected.Equals(nextSelection)) { _wheelResizeRemainder = 0; _wheelResizeTarget = nextSelection; }
		_selected = nextSelection;
		if (IsAlternativeStyle) { _activeGroup = ringIndex; ViewChanged?.Invoke(this, EventArgs.Empty); }
		_hover = _selected;
		Invalidate();
		CellSelected?.Invoke(this, new RadialCellSelectedEventArgs(ringIndex, slot));
	}

	private void DrawCenter(Graphics g, RectangleF bounds)
	{
		PointF center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
		int centerRadius = EffectiveCenterRadiusPx();
		RectangleF rect = RectAround(center, centerRadius);
		if (DrawGuides)
		{
			using var fill = new SolidBrush(_hover.IsCenter
				? Color.FromArgb(234, 244, 248)
				: WheelSurfaceRaised);
			g.FillEllipse(fill, rect);
			using var pen = new Pen(_hover.IsCenter
				? Color.FromArgb(0, 150, 175)
				: Color.FromArgb(200, 210, 218),
				_hover.IsCenter ? 1.8f : 1.2f);
			g.DrawEllipse(pen, rect);
		}
		if (DrawLabels)
		{
			var centerFontSize = Math.Max(11f, Math.Min(22f, centerRadius * 0.66f));
			using Font font = CreateWheelFont(_config.FontFamily, centerFontSize, _config.FontBold);
			using SolidBrush text = new SolidBrush(ParseTextColor(_config.TextColor));
			var textBounds = Rectangle.Round(rect);
			TextRenderer.DrawText(g, "取消", font, textBounds, _hover.IsCenter
				? Color.FromArgb(0, 140, 162) : ParseTextColor(_config.TextColor),
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
				TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
				TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping);
		}
	}

	private void ReloadBackground(bool force = false)
	{
		var path = _scene.StyleId == "classic" ? (_scene.BackgroundImagePath ?? "").Trim() : "";
		if (!force && path.Equals(_loadedBackgroundPath, StringComparison.OrdinalIgnoreCase)) return;
		_backgroundImage?.Dispose();
		_backgroundImage = null;
		_loadedBackgroundPath = path;
		if (path.Length == 0 || !File.Exists(path)) return;
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var source = Image.FromStream(stream);
			_backgroundImage = new Bitmap(source);
		}
		catch (Exception ex)
		{
			Logger.Error("轮盘背景图片读取失败：" + path, ex);
		}
	}

	private void DrawBackground(Graphics graphics, RectangleF bounds)
	{
		using GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddEllipse(bounds);
		GraphicsState gstate = graphics.Save();
		graphics.SetClip(graphicsPath);
		using (SolidBrush brush = new SolidBrush(WheelSurface))
		{
			graphics.FillEllipse(brush, bounds);
		}
		if (_backgroundImage != null && _scene.BackgroundOpacityPercent > 0)
		{
			using var attributes = new ImageAttributes();
			var matrix = new ColorMatrix { Matrix33 = RuntimeCompat.Clamp(_scene.BackgroundOpacityPercent, 0, 100) / 100f };
			attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
			var destination = Rectangle.Round(Cover(bounds, _backgroundImage.Size));
			graphics.DrawImage(
				_backgroundImage,
				destination,
				0,
				0,
				_backgroundImage.Width,
				_backgroundImage.Height,
				GraphicsUnit.Pixel,
				attributes);
		}
		graphics.Restore(gstate);
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
		pevent.Graphics.Clear(BackColor);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_styleToolTip.Dispose();
			_backgroundImage?.Dispose();
			_backgroundImage = null;
			foreach (var icon in _iconCache.Values) icon.Dispose();
			_iconCache.Clear();
		}
		base.Dispose(disposing);
	}

	private HitResult HitTest(Point point)
	{
		if (IsAlternativeStyle) return HitAlternativeStyle(point);
		RectangleF menuBounds = GetMenuBounds();
		PointF pointF = new PointF(menuBounds.Left + menuBounds.Width / 2f, menuBounds.Top + menuBounds.Height / 2f);
		float num = (float)point.X - pointF.X;
		float num2 = (float)point.Y - pointF.Y;
		double dist = Math.Sqrt(num * num + num2 * num2);
		if (dist <= (double)EffectiveCenterRadiusPx())
		{
			return HitResult.Center;
		}
		List<RingConfig> list = (from r in _scene.Rings
			where r.Enabled
			orderby r.Index
			select r).ToList();
		if (list.Count == 0)
		{
			return HitResult.None;
		}
		double num3 = (double)menuBounds.Width / 2.0;
		if (dist > num3)
		{
			return HitResult.None;
		}
		RingLayout ringLayout = BuildRingLayouts(menuBounds, list).FirstOrDefault((RingLayout item) => dist >= (double)item.InnerRadius && dist <= (double)item.OuterRadius);
		if (ringLayout == null)
		{
			return HitResult.None;
		}
		RingConfig ring = ringLayout.Ring;
		double num4 = (Math.Atan2(num, 0f - num2) * 180.0 / Math.PI + 360.0) % 360.0;
		if (_scene.StyleId == "01") num4 = (num4 + 180.0 / ring.Slots) % 360.0;
		int slot = Math.Max(0, Math.Min(ring.Slots - 1, (int)Math.Floor(num4 / (360.0 / (double)ring.Slots))));
		return new HitResult(ring.Index, slot, center: false);
	}

	private void SelectCellAt(Point point)
	{
		HitResult hover = HitTest(point);
		if (IsAlternativeStyle && NavigateStyle(hover)) return;
		if (hover.HasSlot)
		{
			SelectCell(hover.RingIndex, hover.Slot);
		}
	}

	private MenuItemConfig? FindItem(HitResult hit)
	{
		return _scene.Rings.FirstOrDefault((RingConfig r) => r.Index == hit.RingIndex)?.Items.FirstOrDefault((MenuItemConfig i) => i.Slot == hit.Slot);
	}

	private RectangleF GetMenuBounds()
	{
		int num = DisplayMenuSizePx();
		return new RectangleF((float)(base.Width - num) / 2f, (float)(base.Height - num) / 2f, num, num);
	}

	private int DisplayMenuSizePx()
	{
		var available = Math.Max(120, Math.Min(base.Width, base.Height) - (ScaleMenuForPreview ? 16 : 0));
		if (!ScaleMenuForPreview) return Math.Min(available, Math.Max(120, EffectiveMenuSizePx()));
		var ratio = RuntimeCompat.Clamp((EffectiveMenuSizePx() - 260f) / (900f - 260f), 0f, 1f);
		var previewRatio = 0.86f + ratio * 0.14f;
		return Math.Max(120, Math.Min(available, (int)Math.Round(available * previewRatio)));
	}

	private static RectangleF RectAround(PointF center, float radius)
	{
		return new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
	}

	private static RectangleF Cover(RectangleF bounds, Size imageSize)
	{
		if (imageSize.Width <= 0 || imageSize.Height <= 0)
		{
			return bounds;
		}
		float num = Math.Max(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
		float num2 = (float)imageSize.Width * num;
		float num3 = (float)imageSize.Height * num;
		return new RectangleF(bounds.Left + (bounds.Width - num2) / 2f, bounds.Top + (bounds.Height - num3) / 2f, num2, num3);
	}

	private List<RingLayout> BuildRingLayouts(RectangleF bounds, List<RingConfig> rings)
	{
		List<RingLayout> list = new List<RingLayout>();
		float outerRadius = bounds.Width / 2f;
		float innerRadius = Math.Max(1f, EffectiveCenterRadiusPx());
		innerRadius = AdjustSwInnerRadius(innerRadius, bounds, rings.Count);
		float centerRadius = innerRadius;
		float scale = bounds.Width / Math.Max(1f, EffectiveMenuSizePx());
		int totalWeight = Math.Max(1, rings.Sum((RingConfig ring) => Math.Max(1, ring.SizeWeight)));
		int cumulativeWeight = 0;
		for (int index = 0; index < rings.Count; index++)
		{
			RingConfig ring = rings[index];
			cumulativeWeight += Math.Max(1, ring.SizeWeight);
			float configuredOuter = ring.DiameterPx > 0
				? ring.DiameterPx * 0.5f * scale
				: centerRadius + (outerRadius - centerRadius) * cumulativeWeight / totalWeight;
			float nextOuter = index == rings.Count - 1
				? outerRadius
				: Math.Min(outerRadius, Math.Max(innerRadius + 1f, configuredOuter));
			list.Add(new RingLayout(ring, innerRadius, nextOuter));
			innerRadius = nextOuter;
		}
		return list;
	}

	private int EffectiveMenuSizePx()
	{
		int size = _scene.MenuSizePx > 0 ? _scene.MenuSizePx : _config.MenuSizePx;
		return Math.Max(260, Math.Min(900, size));
	}

	private int EffectiveCenterRadiusPx()
	{
		int menuSize = EffectiveMenuSizePx();
		int radius = _scene.CenterRadiusPx > 0 ? _scene.CenterRadiusPx : _config.CenterRadiusPx;
		var effective = Math.Max(20, Math.Min(menuSize / 3, radius));
		effective = Math.Max(1, (int)Math.Round(effective * DisplayMenuSizePx() / (double)menuSize));
		return effective;
	}

	private static void DrawCircle(Graphics g, Pen pen, PointF center, float radius)
	{
		g.DrawEllipse(pen, RectAround(center, radius));
	}

	private static void FillSlot(Graphics g, Brush brush, PointF center, float inner, float outer, float start, float sweep)
	{
		using GraphicsPath graphicsPath = CreateSlotPath(center, inner, outer, start, sweep);
		g.FillPath(brush, graphicsPath);
	}

	private static GraphicsPath CreateSlotPath(PointF center, float inner, float outer, float start, float sweep)
	{
		var graphicsPath = new GraphicsPath();
		inner = Math.Max(0.1f, inner);
		outer = Math.Max(inner + 0.1f, outer);
		graphicsPath.AddArc(RectAround(center, outer), start, sweep);
		graphicsPath.AddArc(RectAround(center, inner), start + sweep, 0f - sweep);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static PointF PointOnCircle(PointF center, float radius, float degrees)
	{
		double num = (double)degrees * Math.PI / 180.0;
		return new PointF(center.X + (float)Math.Cos(num) * radius, center.Y + (float)Math.Sin(num) * radius);
	}

	private void DrawFittedText(Graphics graphics, string label, Color color, bool fontBold, string textEffect, PointF center, float radius, float ringWidth, int slots, int itemFontSizePx, string fontFamily,
		GraphicsPath cellPath, PointF wheelCenter, float innerRadius, float outerRadius, float cellAngle)
	{
		label = (label ?? "").Trim();
		if (label.Length == 0) return;
		var sweepRadians = Math.PI * 2.0 / Math.Max(1, slots);
		var pathBounds = cellPath.GetBounds();
		textEffect = TextEffects.Normalize(textEffect);
		var effectPadding = TextEffects.OverflowPadding(textEffect);
		// Imported wheels commonly contain a dense outer ring. Keep the small
		// visual margin, but give fitting the whole usable cell instead of
		// prematurely treating a label as truncated.
		float availableWidth = Math.Max(10f, Math.Min(pathBounds.Width * 0.96f,
			2f * Math.Max(10f, radius) * (float)Math.Sin(sweepRadians / 2.0) * 0.96f));
		float availableHeight = Math.Max(10f, Math.Min(pathBounds.Height * 0.96f, ringWidth * 0.96f));
		availableWidth = Math.Max(10f, availableWidth - effectPadding * 2f);
		availableHeight = Math.Max(10f, availableHeight - effectPadding * 2f);
		float previewScale = DisplayMenuSizePx() / (float)Math.Max(1, EffectiveMenuSizePx());
		float maxSize = itemFontSizePx > 0 ? itemFontSizePx * previewScale
            : Math.Max(.1f, Math.Min(72f * previewScale, availableHeight));
		var cacheKey = BuildTextLayoutCacheKey(label, fontFamily, fontBold, textEffect, itemFontSizePx, slots, previewScale,
			availableWidth, availableHeight, wheelCenter, innerRadius, outerRadius, cellAngle);
		if (!SharedTextLayoutCache.TryGetValue(cacheKey, out var layout))
		{
			_textLayoutCacheMisses++;
			string[] lines = new[] { label };
			SizeF textSize = SizeF.Empty;
			bool fullText = false;
			bool placementFound = false;
			float chosenSize = Math.Max(3f, 8f * previewScale);
			PointF chosenCenter = center;
			// Never shorten imported command labels. If a very dense cell cannot
			// hold the preferred minimum, continue down to a legible emergency
			// size and retain the complete command name inside that cell.
			var emergencyMinimum = Math.Min(maxSize, Math.Max(.5f, 2.5f * previewScale));
			// A half-pixel linear scan could create and measure 40-55 fonts for every
			// cell. On a populated wheel that made the first right-click wait several
			// seconds. Text fitting is monotonic enough for a half-pixel binary search,
			// reducing the cold path to about 6-7 candidates per cell.
			var low = Math.Max(1, (int)Math.Ceiling(emergencyMinimum * 2f));
			var high = Math.Max(low, (int)Math.Floor(maxSize * 2f));
			var bestStep = -1;
            if (itemFontSizePx > 0)
            {
                chosenSize = Math.Max(.1f, maxSize);
                placementFound = TryBuildTextLayoutAtSize(graphics, label, fontFamily, fontBold, chosenSize,
                    availableWidth, availableHeight, cellPath, wheelCenter, innerRadius, outerRadius, cellAngle,
                    out lines, out textSize, out fullText, out chosenCenter);
                if (placementFound && fullText)
                {
                    bestStep = 0;
                    low = high + 1;
                }
            }
			while (low <= high)
			{
				var step = (low + high) / 2;
				var size = step / 2f;
				var fits = TryBuildTextLayoutAtSize(graphics, label, fontFamily, fontBold, size, availableWidth,
					availableHeight, cellPath, wheelCenter, innerRadius, outerRadius, cellAngle,
					out var candidateLines, out var candidateSize, out var candidateFullText, out var candidateCenter);
				if (fits)
				{
					bestStep = step;
					chosenSize = size;
					lines = candidateLines;
					textSize = candidateSize;
					fullText = candidateFullText;
					placementFound = true;
					chosenCenter = candidateCenter;
					low = step + 1;
				}
				else
				{
					high = step - 1;
				}
			}
			if (bestStep < 0)
			{
				placementFound = TryBuildTextLayoutAtSize(graphics, label, fontFamily, fontBold, emergencyMinimum,
					availableWidth, availableHeight, cellPath, wheelCenter, innerRadius, outerRadius,
					cellAngle, out lines, out textSize, out fullText, out chosenCenter);
				chosenSize = emergencyMinimum;
				while ((!placementFound || !fullText) && chosenSize > .5f)
				{
					chosenSize = Math.Max(.5f, chosenSize * .75f);
					placementFound = TryBuildTextLayoutAtSize(graphics, label, fontFamily, fontBold, chosenSize,
						availableWidth, availableHeight, cellPath, wheelCenter, innerRadius, outerRadius,
						cellAngle, out lines, out textSize, out fullText, out chosenCenter);
				}
			}
			var fitsBounds = fullText && placementFound && textSize.Width <= availableWidth && textSize.Height <= availableHeight;
			layout = new FittedTextLayout
			{
				Lines = lines,
				FontSizePx = chosenSize,
				LogicalFontSizePx = chosenSize / Math.Max(0.01f, previewScale),
				Center = placementFound ? chosenCenter : center,
				FullText = fullText,
				FitsBounds = fitsBounds
			};
			if (SharedTextLayoutCache.Count > 4096) SharedTextLayoutCache.Clear();
			SharedTextLayoutCache[cacheKey] = layout;
		}
		else
		{
			_textLayoutCacheHits++;
		}
		_textDiagnostics.Add(new TextLayoutDiagnostic
		{
			Label = label,
			LogicalFontSizePx = layout.LogicalFontSizePx,
			FullText = layout.FullText,
			FitsBounds = layout.FitsBounds,
			LineCount = layout.Lines.Length
		});
		center = layout.Center;
		using var font = CreateWheelFont(fontFamily, layout.FontSizePx, fontBold);
		float lineHeight = MeasureTextHeight(graphics, font);
		float y = center.Y - (lineHeight * layout.Lines.Length) / 2f;
		var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
			TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping;
		var state = graphics.Save();
		graphics.SetClip(cellPath, CombineMode.Intersect);
		for (int i = 0; i < layout.Lines.Length; i++)
		{
			var lineBounds = Rectangle.Round(new RectangleF(center.X - availableWidth / 2f, y + i * lineHeight, availableWidth, lineHeight + 1f));
			DrawStyledText(graphics, layout.Lines[i], font, lineBounds, color, textEffect, flags);
		}
		graphics.Restore(state);
	}

	private static bool TryBuildTextLayoutAtSize(Graphics graphics, string label, string fontFamily, bool fontBold,
		float fontSize, float availableWidth, float availableHeight, GraphicsPath cellPath,
		PointF wheelCenter, float innerRadius, float outerRadius, float cellAngle,
		out string[] lines, out SizeF textSize, out bool fullText, out PointF placedCenter)
	{
		using var font = CreateWheelFont(fontFamily, fontSize, fontBold);
		lines = WrapText(graphics, label, font, availableWidth,
			MaxTextLines(graphics, font, availableHeight), out fullText);
		if (fullText) lines = BalanceLines(graphics, label, lines, font, availableWidth);
		textSize = MeasureLines(graphics, lines, font);
		if (!fullText || textSize.Width > availableWidth || textSize.Height > availableHeight)
		{
			placedCenter = PointF.Empty;
			return false;
		}
		if (TryPlaceText(cellPath, wheelCenter, innerRadius, outerRadius, cellAngle,
			textSize, font.Size, out placedCenter)) return true;
		// A wide line may fit the bounding box but cross a curved cell edge.
		// Try narrower complete wraps before shrinking the font further.
		foreach (var fraction in new[] { .85f, .70f, .55f, .40f, .25f })
		{
			var wrapWidth = Math.Min(availableWidth, textSize.Width) * fraction;
			var candidate = WrapText(graphics, label, font, wrapWidth,
				MaxTextLines(graphics, font, availableHeight), out var complete);
			if (!complete) continue;
			candidate = BalanceLines(graphics, label, candidate, font, wrapWidth);
			var measured = MeasureLines(graphics, candidate, font);
			if (measured.Width > availableWidth || measured.Height > availableHeight) continue;
			if (!TryPlaceText(cellPath, wheelCenter, innerRadius, outerRadius, cellAngle,
				measured, font.Size, out placedCenter)) continue;
			lines = candidate;
			textSize = measured;
			fullText = true;
			return true;
		}
		return false;
	}

	private static string BuildTextLayoutCacheKey(string label, string fontFamily, bool fontBold, string textEffect, int itemFontSizePx, int slots,
		float previewScale, float availableWidth, float availableHeight, PointF wheelCenter,
		float innerRadius, float outerRadius, float cellAngle)
	{
		return string.Join("\u001F", label, fontFamily ?? "", fontBold ? "bold" : "regular", TextEffects.Normalize(textEffect), itemFontSizePx.ToString(CultureInfo.InvariantCulture),
			slots.ToString(CultureInfo.InvariantCulture), CacheNumber(previewScale), CacheNumber(availableWidth),
			CacheNumber(availableHeight), CacheNumber(wheelCenter.X), CacheNumber(wheelCenter.Y),
			CacheNumber(innerRadius), CacheNumber(outerRadius), CacheNumber(cellAngle));
	}

	private static string CacheNumber(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	private static bool TryPlaceText(GraphicsPath cellPath, PointF wheelCenter, float innerRadius, float outerRadius,
		float angle, SizeF textSize, float fontSize, out PointF placedCenter)
	{
		var padding = Math.Max(1.2f, fontSize * 0.12f);
		var width = textSize.Width + padding * 2f;
		var height = textSize.Height + padding * 2f;
		foreach (var fraction in TextPlacementFractions)
		{
			var radius = innerRadius + Math.Max(0f, outerRadius - innerRadius) * fraction;
			var candidate = PointOnCircle(wheelCenter, radius, angle);
			var bounds = new RectangleF(candidate.X - width / 2f, candidate.Y - height / 2f, width, height);
			if (!RectangleInsidePath(cellPath, bounds)) continue;
			placedCenter = candidate;
			return true;
		}
		placedCenter = PointF.Empty;
		return false;
	}

	private static bool RectangleInsidePath(GraphicsPath path, RectangleF bounds)
	{
		if (!path.IsVisible(bounds.Left, bounds.Top)
			|| !path.IsVisible(bounds.Right, bounds.Top)
			|| !path.IsVisible(bounds.Left, bounds.Bottom)
			|| !path.IsVisible(bounds.Right, bounds.Bottom)
			|| !path.IsVisible(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f))
			return false;
		return true;
	}

	private Image? GetIcon(string? path)
	{
		path = (path ?? "").Trim();
		if (path.Length == 0 || !File.Exists(path)) return null;
		if (_iconCache.TryGetValue(path, out var cached)) return cached;
		try
		{
			using var source = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
			var image = new Bitmap(source);
			_iconCache[path] = image;
			return image;
		}
		catch (Exception ex)
		{
			Logger.Error("命令图标读取失败：" + path, ex);
			return null;
		}
	}

	private static void DrawIcon(Graphics graphics, Image icon, PointF center, float size)
	{
		var oldMode = graphics.InterpolationMode;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		var scale = Math.Min(size / Math.Max(1f, icon.Width), size / Math.Max(1f, icon.Height));
		var width = Math.Max(1f, icon.Width * scale);
		var height = Math.Max(1f, icon.Height * scale);
		graphics.DrawImage(icon, center.X - width / 2f, center.Y - height / 2f, width, height);
		graphics.InterpolationMode = oldMode;
	}

	private static int MaxTextLines(Graphics graphics, Font font, float availableHeight)
	{
		float lineHeight = Math.Max(1f, MeasureTextHeight(graphics, font));
		return Math.Max(1, Math.Min(24, (int)Math.Floor(availableHeight / lineHeight)));
	}

	private static string[] WrapText(Graphics graphics, string text, Font font, float availableWidth, int maxLines, out bool fullText, bool trimWithEllipsis = false)
	{
		List<string> lines = new List<string>();
		string current = "";
		fullText = true;
		var tokens = TokenizeForWrap(text)
			.SelectMany(token => token != "\n" && MeasureTextWidth(graphics, token, font) > availableWidth
				? token.Select(ch => ch.ToString())
				: new[] { token });
		foreach (var token in tokens)
		{
			if (token == "\n")
			{
				if (current.Trim().Length > 0) lines.Add(current.Trim());
				current = "";
				if (lines.Count >= maxLines)
				{
					fullText = false;
					break;
				}
				continue;
			}
			string candidate = current + token;
			if (current.Length > 0 && MeasureTextWidth(graphics, candidate, font) > availableWidth)
			{
				lines.Add(current.Trim());
				current = string.IsNullOrWhiteSpace(token) ? "" : token;
				if (lines.Count == maxLines)
				{
					fullText = false;
					break;
				}
			}
			else
			{
				current = candidate;
			}
		}
		if (fullText && current.Length > 0)
		{
			lines.Add(current.Trim());
		}
		lines = lines.Where(line => line.Length > 0).ToList();
		if (lines.Count == 0)
		{
			lines.Add(text.Length > 0 ? text.Substring(0, 1) : "");
		}
		if (lines.Count > maxLines)
		{
			fullText = false;
			lines = lines.Take(maxLines).ToList();
		}
		if (trimWithEllipsis && !fullText)
		{
			int last = lines.Count - 1;
			lines[last] = FitWithEllipsis(graphics, lines[last], font, availableWidth);
		}
		return lines.ToArray();
	}

	private static IEnumerable<string> TokenizeForWrap(string text)
	{
		for (var index = 0; index < (text ?? "").Length; index++)
		{
			var ch = text[index];
			if (ch == '\r') continue;
			if (ch == '\n')
			{
				yield return "\n";
				continue;
			}
			if (ch <= 127 && !char.IsWhiteSpace(ch))
			{
				var start = index;
				while (index + 1 < text.Length && text[index + 1] <= 127 && !char.IsWhiteSpace(text[index + 1])) index++;
				yield return text.Substring(start, index - start + 1);
				continue;
			}
			yield return ch.ToString();
		}
	}

	private static string[] BalanceLines(Graphics graphics, string text, string[] fallback, Font font, float availableWidth)
	{
		if (fallback.Length <= 1 || text.IndexOfAny(new[] { '\r', '\n' }) >= 0) return fallback;
		var value = text.Trim();
		if (value.Length < fallback.Length) return fallback;
		var balanced = new List<string>(fallback.Length);
		var offset = 0;
		for (var line = 0; line < fallback.Length; line++)
		{
			var remainingCharacters = value.Length - offset;
			var remainingLines = fallback.Length - line;
			var count = (int)Math.Ceiling(remainingCharacters / (double)remainingLines);
			var part = value.Substring(offset, Math.Min(count, remainingCharacters)).Trim();
			if (part.Length == 0 || MeasureTextWidth(graphics, part, font) > availableWidth) return fallback;
			balanced.Add(part);
			offset += count;
		}
		return offset >= value.Length ? balanced.ToArray() : fallback;
	}

	private static string FitWithEllipsis(Graphics graphics, string text, Font font, float availableWidth)
	{
		const string ellipsis = "…";
		string value = text ?? "";
		while (value.Length > 0 && MeasureTextWidth(graphics, value + ellipsis, font) > availableWidth)
		{
			value = value.Substring(0, value.Length - 1);
		}
		return value.Length == 0 ? ellipsis : value + ellipsis;
	}

	private static SizeF MeasureLines(Graphics graphics, string[] lines, Font font)
	{
		float width = 0f;
		foreach (string line in lines)
		{
			width = Math.Max(width, MeasureTextWidth(graphics, line, font));
		}
		return new SizeF(width, MeasureTextHeight(graphics, font) * lines.Length);
	}

	private static float MeasureTextWidth(Graphics graphics, string text, Font font)
	{
		var size = TextRenderer.MeasureText(graphics, text ?? "", font, new Size(32767, 32767),
			TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
		return size.Width;
	}

	private static float MeasureTextHeight(Graphics graphics, Font font)
	{
		return TextRenderer.MeasureText(graphics, "国", font, new Size(32767, 32767),
			TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
	}

	private static RectangleF Offset(RectangleF rect, float dx, float dy)
	{
		return new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
	}

	private static Rectangle Offset(Rectangle rect, int dx, int dy)
	{
		return new Rectangle(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
	}

	private static Font CreateWheelFont(string fontFamily, float size, bool bold = false)
	{
		var style = bold ? FontStyle.Bold : FontStyle.Regular;
		try
		{
			return new Font(string.IsNullOrWhiteSpace(fontFamily) ? "宋体" : fontFamily.Trim(), size, style, GraphicsUnit.Pixel);
		}
		catch
		{
			try { return new Font("宋体", size, style, GraphicsUnit.Pixel); }
			catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel); }
		}
	}

	private static void DrawStyledText(Graphics graphics, string text, Font font, Rectangle bounds, Color color,
		string effect, TextFormatFlags flags)
	{
		effect = TextEffects.Normalize(effect);
		if (effect == TextEffects.None)
		{
			DrawPlainText(graphics, text, font, bounds, color);
			return;
		}
		if (effect == TextEffects.Shadow || effect == TextEffects.WordArt)
			TextRenderer.DrawText(graphics, text, font, Offset(bounds, 2, 2), Color.FromArgb(145, 0, 0, 0), flags);
		if (effect == TextEffects.Outline || effect == TextEffects.WordArt)
		{
			var outline = IsLightColor(color) ? Color.FromArgb(185, 20, 28, 36) : Color.FromArgb(185, 255, 255, 255);
			foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
				TextRenderer.DrawText(graphics, text, font, Offset(bounds, offset.X, offset.Y), outline, flags);
		}
		TextRenderer.DrawText(graphics, text, font, bounds, color, flags);
	}

	private static void DrawPlainText(Graphics graphics, string text, Font font, Rectangle bounds, Color color)
	{
		TextRenderer.DrawText(graphics, text, font, bounds, color,
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
			TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
			TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping);
	}

	private static bool IsLightColor(Color color)
	{
		return color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 185;
	}

	private static bool HasTextColor(string value)
	{
		return !string.IsNullOrWhiteSpace(value);
	}

	private static Color ParseTextColor(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			return WheelText;
		}
		try
		{
			return ColorTranslator.FromHtml(text);
		}
		catch
		{
			return WheelText;
		}
	}
}
