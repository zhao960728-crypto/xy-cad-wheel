using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

namespace XiaoYang.AutoCADPlugin;

// All styles address the existing ring/slot pairs. Changing appearance never
// moves bindings or converts the user's wheel into a second command database.
internal sealed partial class RadialMenuControl
{
    private const int GroupSlot = -2, BackSlot = -5;
    private int _activeGroup = -1;
    private bool IsAlternativeStyle => _scene.StyleId == "11" || _scene.StyleId == "06" || _scene.StyleId == "orbit" || _scene.StyleId == "free";
    private WheelAppearance Appearance => _scene.Appearance ?? new WheelAppearance();
    internal event EventHandler? ViewChanged;
    private readonly System.Windows.Forms.ToolTip _styleToolTip = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 12000, ShowAlways = true };

    // Only measured values are shared; fonts, brushes and graphics stay owned
    // by the paint operation. Exact geometry and device units prevent previews
    // or another monitor from reusing a layout measured for different bounds.
    private static readonly ConcurrentDictionary<CardTextLayoutKey, CardTextLayout> SharedCardTextLayoutCache = new();

    private readonly struct CardTextLayoutKey : IEquatable<CardTextLayoutKey>
    {
        private readonly string _label, _family;
        private readonly bool _bold, _fit;
        private readonly float _fontSize, _scale, _dpiX, _dpiY, _pageScale;
        private readonly SizeF _bounds;
        private readonly GraphicsUnit _pageUnit;
        private readonly System.Drawing.Text.TextRenderingHint _textHint;

        public CardTextLayoutKey(Graphics graphics, string label, string family, bool bold,
            float fontSize, SizeF bounds, float scale, bool fit)
        {
            _label = label; _family = family ?? ""; _bold = bold; _fit = fit;
            _fontSize = fontSize; _bounds = bounds; _scale = scale;
            _dpiX = graphics.DpiX; _dpiY = graphics.DpiY;
            _pageUnit = graphics.PageUnit; _pageScale = graphics.PageScale; _textHint = graphics.TextRenderingHint;
        }

        public bool Equals(CardTextLayoutKey other) => _label == other._label && _family == other._family &&
            _bold == other._bold && _fit == other._fit && _fontSize.Equals(other._fontSize) &&
            _bounds.Equals(other._bounds) && _scale.Equals(other._scale) &&
            _dpiX.Equals(other._dpiX) && _dpiY.Equals(other._dpiY) && _pageUnit == other._pageUnit &&
            _pageScale.Equals(other._pageScale) && _textHint == other._textHint;

        public override bool Equals(object? obj) => obj is CardTextLayoutKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = _label.GetHashCode();
                hash = hash * 397 ^ _family.GetHashCode();
                hash = hash * 397 ^ _bold.GetHashCode();
                hash = hash * 397 ^ _fit.GetHashCode();
                hash = hash * 397 ^ _fontSize.GetHashCode();
                hash = hash * 397 ^ _bounds.GetHashCode();
                hash = hash * 397 ^ _scale.GetHashCode();
                hash = hash * 397 ^ _dpiX.GetHashCode();
                hash = hash * 397 ^ _dpiY.GetHashCode();
                hash = hash * 397 ^ (int)_pageUnit;
                hash = hash * 397 ^ _pageScale.GetHashCode();
                return hash * 397 ^ (int)_textHint;
            }
        }
    }

    private sealed class CardTextLayout
    {
        public string[] Lines { get; init; }
        public float[] LineWidths { get; init; }
        public float FontSize { get; init; }
        public float LineHeight { get; init; }
        public bool FitsBounds { get; init; }
    }

    private CardTextLayout GetCardTextLayout(Graphics graphics, string label, string family, bool bold,
        float fontSize, SizeF bounds, float scale, StringFormat format, bool fit = true)
    {
        var key = new CardTextLayoutKey(graphics, label, family, bold, fontSize, bounds, scale, fit);
        if (SharedCardTextLayoutCache.TryGetValue(key, out var cached))
        {
            _textLayoutCacheHits++;
            return cached;
        }
        _textLayoutCacheMisses++;
        var size = fit ? FitCardFontSize(graphics, label, family, bold, (int)fontSize, bounds, scale, format) : fontSize;
        using var font = CreateWheelFont(family, size, bold);
        var lines = WrapCardText(graphics, label, font, bounds.Width, format);
        var widths = new float[lines.Length];
        var lineHeight = graphics.MeasureString("国", font, 32767, format).Height;
        var fits = !fit || lines.Length * lineHeight <= bounds.Height + .01f;
        for (var index = 0; index < lines.Length; index++)
        {
            widths[index] = graphics.MeasureString(lines[index], font, 32767, format).Width;
            if (fit && widths[index] > bounds.Width + .01f) fits = false;
        }
        var layout = new CardTextLayout { Lines = lines, LineWidths = widths, FontSize = size, LineHeight = lineHeight, FitsBounds = fits };
        if (SharedCardTextLayoutCache.Count >= 4096) SharedCardTextLayoutCache.Clear();
        SharedCardTextLayoutCache[key] = layout;
        return layout;
    }

    private sealed class StyleCell
    {
        public HitResult Hit;
        public RectangleF Bounds;
        public bool Round;
        public string Label = "";
        public MenuItemConfig? Item;
    }

    internal void CopyNavigationFrom(RadialMenuControl source)
    {
        // Drag polling and the mouse hook both forward navigation. Only repaint
        // the foreground layer when its visible state actually changes.
        if (_activeGroup == source._activeGroup && _hover.Equals(source._hover)) return;
        _activeGroup = source._activeGroup;
        _hover = source._hover;
        Invalidate();
    }

    internal void EndDragMode() => _dragMode = false;

    internal bool ShowInsertionPoints { get; set; }

    internal List<(int Ring, int Slot, PointF Point)> CellInsertionPoints()
    {
        var result = new List<(int Ring, int Slot, PointF Point)>();
        var bounds = GetMenuBounds();
        var center = new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var rings = _scene.Rings.Where(r => r.Enabled && r.Slots < 32).OrderBy(r => r.Index).ToList();
        if (!IsAlternativeStyle)
        {
            foreach (var layout in BuildRingLayouts(bounds, _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList()))
            {
                if (layout.Ring.Slots >= 32) continue;
                for (int slot = 0; slot < layout.Ring.Slots; slot++)
                    result.Add((layout.Ring.Index, slot, PointOnCircle(center, (layout.InnerRadius + layout.OuterRadius) / 2,
                        -90 + (slot - (_scene.StyleId == "01" ? .5f : 0)) * 360f / layout.Ring.Slots)));
            }
            return result;
        }
        var cells = BuildStyleCells(bounds).Where(c => c.Hit.HasSlot).ToList();
        foreach (var ring in rings)
        {
            var row = cells.Where(c => c.Hit.RingIndex == ring.Index).OrderBy(c => c.Hit.Slot).ToList();
            for (int i = 0; i < row.Count; i++)
            {
                var previous = row[(i + row.Count - 1) % row.Count].Bounds;
                var current = row[i].Bounds;
                var a = new PointF(previous.Left + previous.Width / 2, previous.Top + previous.Height / 2);
                var b = new PointF(current.Left + current.Width / 2, current.Top + current.Height / 2);
                var point = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                if (row.Count == 1) point = new PointF(current.Right + 10, b.Y);
                else if (_scene.StyleId == "orbit" || _scene.StyleId == "06")
                {
                    var ra = Math.Sqrt((a.X - center.X) * (a.X - center.X) + (a.Y - center.Y) * (a.Y - center.Y));
                    var rb = Math.Sqrt((b.X - center.X) * (b.X - center.X) + (b.Y - center.Y) * (b.Y - center.Y));
                    if (Math.Abs(ra - rb) < 2)
                    {
                        var angleA = Math.Atan2(a.Y - center.Y, a.X - center.X) * 180 / Math.PI;
                        var angleB = Math.Atan2(b.Y - center.Y, b.X - center.X) * 180 / Math.PI;
                        var sweep = (angleB - angleA + 360) % 360;
                        point = PointOnCircle(center, (float)((ra + rb) / 2), (float)(angleA + sweep / 2));
                    }
                }
                // Do not offer insertions on another cell or a navigation button.
                if (cells.Any(c => c.Bounds.Contains(point))) continue;
                if (ClientRectangle.Contains(Point.Round(point))) result.Add((ring.Index, row[i].Hit.Slot, point));
            }
        }
        return result;
    }

    internal RadialCellSelectedEventArgs? HitInsertion(Point point)
    {
        var best = 14f * DeviceDpi / 96f;
        var distance = best * best;
        RadialCellSelectedEventArgs result = null;
        foreach (var gap in CellInsertionPoints())
        {
            var d = (gap.Point.X - point.X) * (gap.Point.X - point.X) + (gap.Point.Y - point.Y) * (gap.Point.Y - point.Y);
            if (d > distance) continue;
            distance = d;
            result = new RadialCellSelectedEventArgs(gap.Ring, gap.Slot);
        }
        return result;
    }

    private void DrawInsertionPoints(Graphics graphics)
    {
        if (!ShowInsertionPoints) return;
        using var fill = new SolidBrush(Color.FromArgb(0, 134, 153));
        using var pen = new Pen(Color.White, 1.5f);
        foreach (var gap in CellInsertionPoints())
        {
            var x = gap.Point.X; var y = gap.Point.Y;
            graphics.FillEllipse(fill, x - 6, y - 6, 12, 12);
            graphics.DrawLine(pen, x - 3, y, x + 3, y);
            graphics.DrawLine(pen, x, y - 3, x, y + 3);
        }
    }

    internal RadialCellSelectedEventArgs? HitCell(Point point)
    {
        var hit = HitTest(point);
        return hit.HasSlot ? new RadialCellSelectedEventArgs(hit.RingIndex, hit.Slot) : null;
    }

    internal Region CreateVisibleStyleRegion()
    {
        var bounds = GetMenuBounds();
        using var path = new GraphicsPath();
        if (_scene.StyleId == "01")
        {
            var inner = AdjustSwInnerRadius(EffectiveCenterRadiusPx(), bounds, _scene.Rings.Count(r => r.Enabled));
            var region = new Region(bounds);
            using var outside = new GraphicsPath();
            outside.AddEllipse(bounds);
            region.Intersect(outside);
            using var hole = new GraphicsPath();
            var center = new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            hole.AddEllipse(RectAround(center, inner));
            region.Exclude(hole);
            region.Union(RectAround(center, 8));
            return region;
        }
        if (!IsAlternativeStyle) path.AddEllipse(bounds);
        else if (_scene.StyleId == "11") path.AddRectangle(new RectangleF(bounds.Left + bounds.Width * .05f, bounds.Top + bounds.Height * .09f, bounds.Width * .90f, bounds.Height * .89f));
        else
        {
            foreach (var cell in BuildStyleCells(bounds))
            {
                using var cellPath = StylePath(cell);
                path.AddPath(cellPath, false);
            }
        }
        path.FillMode = FillMode.Winding;
        return new Region(path);
    }

    private void SetActiveGroup(int index)
    {
        if (_activeGroup == index) return;
        _activeGroup = index;
        _hover = HitResult.None;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool NavigateStyle(HitResult hit)
    {
        if (hit.Slot == GroupSlot) { SetActiveGroup(hit.RingIndex); return true; }
        if (hit.Slot == BackSlot)
        {
            _activeGroup = -1;
        }
        else return false;
        _hover = HitResult.None;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private List<StyleCell> BuildStyleCells(RectangleF bounds)
    {
        if (_scene.StyleId == "free") return BuildFreeCells(bounds);
        if (_scene.StyleId == "orbit") return ApplyCellSizes(BuildOrbitCells(bounds));
        var cells = new List<StyleCell>();
        var groups = _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList();
        var group = groups.FirstOrDefault(r => r.Index == _activeGroup);
        var center = new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var scale = bounds.Width;
        bool nodes = _scene.StyleId == "06";
        if (!nodes && group == null) group = groups.FirstOrDefault();
        if (nodes && group == null)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                var at = PointOnCircle(center, scale * .28f, -90 + i * 360f / groups.Count);
                cells.Add(new StyleCell { Hit = new HitResult(groups[i].Index, GroupSlot, false), Bounds = RectAround(at, scale * .10f), Round = true, Label = GroupName(groups[i]) });
            }
        }
        else if (!nodes)
        {
            var tabWidth = scale * .84f / Math.Max(1, groups.Count);
            for (var i = 0; i < groups.Count; i++)
                cells.Add(new StyleCell { Hit = new HitResult(groups[i].Index, GroupSlot, false), Bounds = new RectangleF(bounds.Left + scale * .08f + i * tabWidth, bounds.Top + scale * .12f, tabWidth - 3, scale * .09f), Label = GroupName(groups[i]) });
        }
        if (group != null)
        {
            var count = group.Slots;
            // Preview the actual node arrangement, including its physical gaps,
            // then scale it as a whole instead of repacking for the small panel.
            var nodeScale = ScaleMenuForPreview ? EffectiveMenuSizePx() : scale;
            var nodeCells = nodes ? BuildNodePositions(PointF.Empty, nodeScale, group) : null;
            for (var i = 0; i < count; i++)
            {
                int slot = i;
                RectangleF cellBounds;
                if (nodes)
                {
                    var logical = nodeCells![i];
                    var factor = scale / nodeScale;
                    cellBounds = new RectangleF(center.X + logical.X * factor, center.Y + logical.Y * factor,
                        logical.Width * factor, logical.Height * factor);
                }
                else
                {
                    var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * 1.25)));
                    var rows = (int)Math.Ceiling(count / (double)columns);
                    var width = scale * .84f / columns;
                    var height = scale * .60f / rows;
                    cellBounds = new RectangleF(bounds.Left + scale * .08f + i % columns * width,
                        bounds.Top + scale * .24f + i / columns * height, width - 4, height - 4);
                }
                var item = group.Items.FirstOrDefault(it => it.Slot == slot);
                cells.Add(new StyleCell { Hit = new HitResult(group.Index, slot, false), Bounds = cellBounds, Round = nodes, Item = item, Label = item?.Label ?? $"空位 {slot + 1}" });
            }
        }
        cells.Add(new StyleCell { Hit = nodes && group != null ? new HitResult(-1, BackSlot, false) : HitResult.Center,
            Bounds = nodes ? RectAround(center, scale * (group != null ? .055f : .074f)) : new RectangleF(center.X - scale * .12f, bounds.Top + scale * .88f, scale * .24f, scale * .07f),
            Round = nodes, Label = nodes && group != null ? "返回" : "取消" });
        return ApplyCellSizes(cells);
    }

    private static List<RectangleF> BuildNodePositions(PointF center, float scale, RingConfig group)
    {
        const float gap = 8f;
        // A ring's size setting has one baseline, regardless of its slot count.
        // Extra commands use the remaining rows before reducing button size.
        var radius = Math.Max(16f, scale * .095f) * RuntimeCompat.Clamp(group.NodeInnerSizePercent, 50, 150) / 100f;
        var capacities = new int[3];
        var orbits = new float[3];
        // Start with the requested diameter, add concentric rows, and only
        // shrink all buttons together if three rows cannot contain every item.
        while (true)
        {
            var total = 0;
            for (var row = 0; row < 3; row++)
            {
                var orbit = scale / 2 - radius - gap - row * (2 * radius + gap);
                orbits[row] = orbit;
                capacities[row] = orbit >= scale * .055f + radius + gap
                    ? Math.Max(1, (int)Math.Floor(Math.PI / Math.Asin(Math.Min(1, (radius + gap / 2) / orbit)) + .00001)) : 0;
                total += capacities[row];
            }
            if (total >= group.Slots || radius <= 1) break;
            radius = Math.Max(1, radius - .5f);
        }
        var result = new List<RectangleF>();
        var left = group.Slots;
        for (var row = 0; row < 3 && left > 0; row++)
        {
            var count = Math.Min(left, capacities[row]);
            if (count == 0) continue;
            var orbit = orbits[row];
            if (row == 0 && count == group.Slots)
                orbit = Math.Min(orbit, Math.Max(scale * .32f, count > 1 ? (radius + gap / 2) / (float)Math.Sin(Math.PI / count) : 0));
            var phase = -90f + (row == 0 ? 0 : 180f / Math.Max(1, capacities[row - 1]));
            for (var slot = 0; slot < count; slot++)
                result.Add(RectAround(PointOnCircle(center, orbit, phase + slot * 360f / count), radius));
            left -= count;
        }
        return result;
    }

    private string GroupName(RingConfig group) => string.IsNullOrWhiteSpace(group.Name)
        ? (_scene.StyleId == "06" ? $"第{group.Index + 1}圈" : $"第{group.Index + 1}组") : group.Name;

    private HitResult HitAlternativeStyle(Point point)
    {
        foreach (var cell in BuildStyleCells(GetMenuBounds()))
        {
            using var path = StylePath(cell);
            if (path.IsVisible(point)) return cell.Hit;
        }
        return HitResult.None;
    }

    private static GraphicsPath StylePath(StyleCell cell)
    {
        if (cell.Item != null && cell.Item.CellShape != "Circle") return CreateCellShape(cell.Bounds, cell.Item.CellShape);
        var path = new GraphicsPath();
        if (cell.Round) path.AddEllipse(cell.Bounds);
        else path.AddRectangle(cell.Bounds);
        return path;
    }

    private List<StyleCell> BuildOrbitCells(RectangleF bounds)
    {
        const float gap = 8f;
        var cells = new List<StyleCell>();
        var center = new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var rings = _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList();
        // Lay out at physical wheel size; previews scale that same geometry.
        var size = ScaleMenuForPreview ? EffectiveMenuSizePx() : bounds.Width;
        var factor = bounds.Width / Math.Max(1, size);
        var geometryScale = size / EffectiveMenuSizePx();
        var configuredCenter = RuntimeCompat.Clamp(_scene.CenterRadiusPx > 0 ? _scene.CenterRadiusPx : _config.CenterRadiusPx,
            20, EffectiveMenuSizePx() / 3) * geometryScale;
        var centerSize = Math.Max(8, Math.Min(configuredCenter - 4, size * .055f));
        var desired = new float[rings.Count];
        var preferred = new float[rings.Count];
        var sine = new float[rings.Count];
        var previous = configuredCenter;
        var totalWeight = Math.Max(1, rings.Sum(r => Math.Max(1, r.SizeWeight)));
        var cumulativeWeight = 0;
        for (var i = 0; i < rings.Count; i++)
        {
            cumulativeWeight += Math.Max(1, rings[i].SizeWeight);
            var outer = i == rings.Count - 1 ? size / 2 : rings[i].DiameterPx > 0
                ? rings[i].DiameterPx * .5f * geometryScale
                : configuredCenter + (size / 2 - configuredCenter) * cumulativeWeight / totalWeight;
            outer = Math.Min(size / 2, Math.Max(previous + 1, outer));
            // Diameter controls preferred orbit position, not the size baseline.
            // Uneven saved ring widths must not turn equally sized buttons into
            // tiny dots beside an oversized outer ring.
            desired[i] = Math.Min(52, (size / 2 - configuredCenter) / Math.Max(1, rings.Count) * .40f)
                * RuntimeCompat.Clamp(rings[i].NodeInnerSizePercent, 50, 150) / 100f;
            preferred[i] = (previous + outer) / 2;
            sine[i] = rings[i].Slots > 1 ? (float)Math.Sin(Math.PI / rings[i].Slots) : 1;
            previous = outer;
        }
        // The old per-ring half-gap cap saturated at 100%, so raising the value
        // could do nothing. Reposition neighbouring orbits before reducing size.
        // Only a physically overfull wheel scales all requested sizes to fit.
        var orbits = new float[rings.Count];
        bool Pack(float scale)
        {
            for (var i = 0; i < rings.Count; i++)
            {
                var radius = desired[i] * scale;
                var inner = i == 0 ? centerSize + gap + radius
                    : orbits[i - 1] + desired[i - 1] * scale + gap + radius;
                orbits[i] = Math.Max(inner, rings[i].Slots > 1 ? (radius + gap / 2) / sine[i] : inner);
            }
            return rings.Count == 0 || orbits[rings.Count - 1] + desired[rings.Count - 1] * scale + gap <= size / 2;
        }
        var fit = 1f;
        if (!Pack(fit))
        {
            var low = 0f; var high = 1f;
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var middle = (low + high) / 2;
                if (Pack(middle)) low = middle; else high = middle;
            }
            fit = low;
            Pack(fit);
        }
        for (var i = 0; i < rings.Count; i++)
            orbits[i] = Math.Max(orbits[i], Math.Max(preferred[i], i == 0 ? 0 : orbits[i - 1] + (desired[i - 1] + desired[i]) * fit + gap));
        for (var i = rings.Count - 1; i >= 0; i--)
            orbits[i] = Math.Min(orbits[i], i == rings.Count - 1 ? size / 2 - desired[i] * fit - gap
                : orbits[i + 1] - (desired[i + 1] + desired[i]) * fit - gap);
        for (var i = 0; i < rings.Count; i++)
        {
            var ring = rings[i];
            var radius = desired[i] * fit * factor;
            for (var slot = 0; slot < ring.Slots; slot++)
            {
                var item = ring.Items.FirstOrDefault(it => it.Slot == slot);
                var at = PointOnCircle(center, orbits[i] * factor, -90 + slot * 360f / ring.Slots);
                cells.Add(new StyleCell { Hit = new HitResult(ring.Index, slot, false), Bounds = RectAround(at, radius), Round = true, Item = item, Label = item?.Label ?? $"空位 {slot + 1}" });
            }
        }
        cells.Add(new StyleCell { Hit = HitResult.Center, Bounds = RectAround(center, centerSize * factor), Round = true, Label = "×" });
        return cells;
    }
    private void DrawAlternativeStyle(Graphics graphics, RectangleF bounds)
    {
        if (_scene.StyleId != "orbit" || !ScaleMenuForPreview || !DrawBackdrop) { DrawAlternativeStyleCore(graphics, bounds); return; }
        var labels = DrawLabels; var foreground = DrawForeground; var guides = DrawGuides; var hover = DrawHover;
        try
        {
            using var backdrop = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            using (var g = Graphics.FromImage(backdrop))
            {
                g.SmoothingMode = graphics.SmoothingMode;
                DrawLabels = false; DrawForeground = false;
                DrawAlternativeStyleCore(g, bounds);
            }
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = _scene.SurfaceOpacityPercent / 100f });
            graphics.DrawImage(backdrop, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel, attributes);
            DrawBackdrop = false; DrawGuides = false; DrawHover = false; DrawLabels = labels; DrawForeground = foreground;
            DrawAlternativeStyleCore(graphics, bounds);
        }
        finally { DrawBackdrop = true; DrawGuides = guides; DrawHover = hover; DrawLabels = labels; DrawForeground = foreground; }
    }

    private void DrawAlternativeStyleCore(Graphics graphics, RectangleF bounds)
    {
        var cells = BuildStyleCells(bounds);
        bool nodes = _scene.StyleId == "06" || _scene.StyleId == "orbit" || _scene.StyleId == "free";
        if (!nodes && DrawBackdrop)
        {
            var panel = new RectangleF(bounds.Left + bounds.Width * .05f, bounds.Top + bounds.Height * .09f, bounds.Width * .90f, bounds.Height * .89f);
            using var shadow = new SolidBrush(Color.FromArgb(25, 30, 42, 56));
            graphics.FillRectangle(shadow, panel.X + 2, panel.Y + 3, panel.Width, panel.Height);
            using var surface = Appearance.CreateBrush(bounds);
            graphics.FillRectangle(surface, panel);
            using var frame = new Pen(Appearance.GetBorderColor(), 1.4f);
            graphics.DrawRectangle(frame, panel.X, panel.Y, panel.Width, panel.Height);
            graphics.DrawLine(frame, panel.Left + bounds.Width * .025f, bounds.Top + bounds.Height * .222f,
                panel.Right - bounds.Width * .025f, bounds.Top + bounds.Height * .222f);
        }
        foreach (var cell in cells)
        {
            using var path = StylePath(cell);
            if (DrawBackdrop)
            {
                using var fill = Appearance.CreateBrush(cell.Bounds);
                graphics.FillPath(fill, path);
                DrawCellBackground(graphics, cell.Item, path);
            }
            if (DrawHover && (cell.Hit.Equals(_hover) || !BatchSelectionMode && cell.Hit.Equals(_selected) || BatchSelection.Contains((cell.Hit.RingIndex, cell.Hit.Slot)) || cell.Hit.Slot == GroupSlot && cell.Hit.RingIndex == _activeGroup))
            {
                using var hover = new SolidBrush(Color.FromArgb(120, (!BatchSelectionMode && cell.Hit.Equals(_selected) || BatchSelection.Contains((cell.Hit.RingIndex,cell.Hit.Slot))) ? Appearance.GetSelectedColor() : Appearance.GetHoverColor()));
                graphics.FillPath(hover, path);
            }
            if (DrawGuides)
            {
                using var pen = new Pen(Appearance.GetBorderColor(), 1.1f);
                graphics.DrawPath(pen, path);
            }
            if (DrawForeground && DrawLabels)
            {
                var inset = cell.Bounds;
                inset.Inflate(-cell.Bounds.Width * (cell.Round ? .15f : .04f), -cell.Bounds.Height * (cell.Round ? .15f : .04f));
                DrawItemCard(graphics, cell.Item ?? new MenuItemConfig { Label = cell.Label, FontSizePx = 0, FontBold = false, TextEffect = TextEffects.None }, inset, path,
                    cell.Item == null ? "TextOnly" : ResolveCellContentMode(cell.Item), bounds.Width / Math.Max(1f, EffectiveMenuSizePx()));
            }
        }
    }

    private bool HasCellBackground(MenuItemConfig item) => item != null && (item.ImageMode == "Background" || ResolveCellContentMode(item) == "ImageOnly" || ResolveCellContentMode(item) == "ImageAndText") && GetEmbeddedIcon(item.IconData) != null;

    private void DrawCellBackground(Graphics graphics, MenuItemConfig item, GraphicsPath clip)
    {
        if (!HasCellBackground(item)) return;
        var image = GetEmbeddedIcon(item.IconData);
        var state = graphics.Save();
        try
        {
            graphics.SetClip(clip, CombineMode.Intersect);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, Cover(clip.GetBounds(), image.Size));
        }
        finally { graphics.Restore(state); }
    }

    private Image? GetEmbeddedIcon(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        if (_iconCache.TryGetValue(data, out var cached)) return cached;
        try
        {
            if (data.Length > 6 * 1024 * 1024) return null;
            using var stream = new MemoryStream(Convert.FromBase64String(data));
            using var image = Image.FromStream(stream);
            if (image.Width > 4096 || image.Height > 4096) return null;
            var copy = new Bitmap(image);
            if (_iconCache.Count > 160) { foreach (var old in _iconCache.Values) old.Dispose(); _iconCache.Clear(); }
            _iconCache[data] = copy;
            return copy;
        }
        catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private void DrawItemCard(Graphics graphics, MenuItemConfig item, RectangleF bounds, GraphicsPath clip, string displayMode = "IconAndText", float scale = 1f)
    {
        if (HasCellBackground(item))
        {
            if (displayMode == "IconOnly" || displayMode == "ImageOnly") return;
            displayMode = "TextOnly";
        }
        if (displayMode == "ImageOnly") return;
        if (displayMode == "ImageAndText") displayMode = "TextOnly";
        if (displayMode == "Auto")
        {
            var hasIcon = GetEmbeddedIcon(item.IconData) != null || TryDrawSemanticIcon(graphics, item, PointF.Empty, 0);
            displayMode = hasIcon && bounds.Height >= 46 * scale && bounds.Width >= 36 * scale ? "IconAndText" : "TextOnly";
        }
        var state = graphics.Save();
        graphics.SetClip(clip, CombineMode.Intersect);
        // GDI TextRenderer and a GDI+ clip/transform disagreed on actual cell
        // measurements. Keep measurement and drawing entirely in GDI+ here.
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var showIcon = displayMode != "TextOnly";
        var showText = displayMode != "IconOnly";
        var family = string.IsNullOrWhiteSpace(item.FontFamily) ? _config.FontFamily : item.FontFamily;
        var bold = item.FontBold ?? _config.FontBold;
        var effect = string.IsNullOrWhiteSpace(item.TextEffect) ? _config.TextEffect : item.TextEffect;
        if (HasCellBackground(item) && TextEffects.Normalize(effect) == TextEffects.None) effect = TextEffects.Outline;
        var color = ParseTextColor(HasTextColor(item.TextColor) ? item.TextColor : _config.TextColor);
        var requested = (item.FontSizePx > 0 ? item.FontSizePx : Math.Max(1, _config.FontSizePx)) * scale;
        var textHeight = showText ? bounds.Height * (1 - RuntimeCompat.Clamp(item.IconRatioPercent, 10, 90) / 100f) : 0;
        if (showIcon)
        {
            var size = Math.Max(1, Math.Min(bounds.Width, showText ? Math.Max(scale, bounds.Height - textHeight - 3 * scale) : bounds.Height));
            var iconCenter = new PointF(bounds.Left + bounds.Width / 2,
                showText ? bounds.Top + size / 2 : bounds.Top + bounds.Height / 2);
            var icon = GetEmbeddedIcon(item.IconData);
            if (icon != null) DrawIcon(graphics, icon, iconCenter, size);
            else DrawCadDirectionIcon(graphics, item, iconCenter, size);
            if (showText) { bounds.Y += size + 3 * scale; bounds.Height = Math.Max(1, bounds.Height - size - 3 * scale); }
        }
        if (showText)
        {
            var effectPadding = TextEffects.OverflowPadding(effect);
            bounds.Inflate(-Math.Min(effectPadding, Math.Max(0, (bounds.Width - 1) / 2)),
                -Math.Min(effectPadding, Math.Max(0, (bounds.Height - 1) / 2)));
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip | StringFormatFlags.NoWrap;
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Near;
            var label = item.Label ?? "";
            var layout = GetCardTextLayout(graphics, label, family, bold, item.FontSizePx, bounds.Size, scale, format);
            var lines = layout.Lines;
            var lineHeight = layout.LineHeight;
            _textDiagnostics.Add(new TextLayoutDiagnostic
            {
                Label = label,
                LogicalFontSizePx = layout.FontSize / Math.Max(.01f, scale),
                FullText = true,
                FitsBounds = layout.FitsBounds,
                LineCount = lines.Length
            });
            using var font = CreateWheelFont(family, layout.FontSize, bold);
            var y = bounds.Top + Math.Max(0, (bounds.Height - lines.Length * lineHeight) / 2);
            using var ink = new SolidBrush(color);
            using var shadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0));
            using var outline = new SolidBrush(IsLightColor(color) ? Color.FromArgb(40, 48, 58) : Color.White);
            for (var index = 0; index < lines.Length; index++)
            {
                // Point drawing avoids GDI+'s rectangle line-admission test:
                // a measured typographic line can be fractionally shorter than
                // the font's required layout height and otherwise vanish whole.
                var width = layout.LineWidths[index];
                var line = new PointF(bounds.Left + (bounds.Width - width) / 2, y + index * lineHeight);
                if (effect == TextEffects.Shadow || effect == TextEffects.WordArt)
                    graphics.DrawString(lines[index], font, shadow, new PointF(line.X + 1, line.Y + 1), format);
                if (effect == TextEffects.Outline || effect == TextEffects.WordArt)
                    foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
                        graphics.DrawString(lines[index], font, outline, new PointF(line.X + offset.X, line.Y + offset.Y), format);
                graphics.DrawString(lines[index], font, ink, line, format);
            }
        }
        graphics.Restore(state);
    }

    // Zero is the persisted "automatic" setting; positive sizes are explicit
    // logical pixels. A manual size is the preferred maximum: the rendered
    // size may shrink to preserve the whole label without changing configuration.
    private static float FitCardFontSize(Graphics graphics, string label, string family, bool bold,
        int fontSizePx, SizeF bounds, float scale, StringFormat format)
    {
        scale = Math.Max(.01f, scale);
        var minimum = .1f;
        var low = minimum;
        var high = Math.Max(low, fontSizePx > 0 ? fontSizePx * scale : Math.Min(72f * scale, bounds.Height));
        var best = minimum;
        bool Fits(float size)
        {
            using var font = CreateWheelFont(family, size, bold);
            var lines = WrapCardText(graphics, label, font, bounds.Width, format);
            var height = graphics.MeasureString("国", font, 32767, format).Height * lines.Length;
            return height <= bounds.Height && lines.All(line =>
                graphics.MeasureString(line, font, 32767, format).Width <= bounds.Width);
        }
        if (Fits(high)) return high;
        for (var attempt = 0; attempt < 14; attempt++)
        {
            var size = (low + high) / 2;
            if (Fits(size)) { best = size; low = size; }
            else high = size;
        }
        return best;
    }

    private static string[] WrapCardText(Graphics graphics, string text, Font font, float width, StringFormat format)
    {
        var lines = new List<string>();
        var current = "";
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text.Replace("\r", ""));
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            if (element == "\n") { lines.Add(current); current = ""; continue; }
            if (current.Length > 0 && graphics.MeasureString(current + element, font, 32767, format).Width > Math.Max(1, width))
            { lines.Add(current); current = element; }
            else current += element;
        }
        if (current.Length > 0 || lines.Count == 0) lines.Add(current);
        return lines.ToArray();
    }
}
