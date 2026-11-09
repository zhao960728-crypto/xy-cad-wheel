using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class RadialMenuControl
{
    internal HashSet<(int Ring, int Slot)> BatchSelection { get; } = new();
    internal bool BatchSelectionMode { get; set; }
    private bool _resizeMode;
    private int _wheelResizeRemainder;
    private HitResult _wheelResizeTarget = HitResult.None;
    internal bool ResizeMode
    {
        get => _resizeMode;
        set { _resizeMode = value; _wheelResizeRemainder = 0; _wheelResizeTarget = HitResult.None; }
    }
    internal event EventHandler? CellGeometryChanged;
    private HitResult _geometryDrag = HitResult.None;
    private bool _geometryResize;
    private Point _geometryStart;
    private RectangleF _geometryBounds;
    private int _geometryStartSize;
    private string? _geometrySnapshot;

    internal void EndGeometryEditing()
    {
        if (_geometryDrag.HasSlot) CancelGeometryDrag();
        ResizeMode = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!EnableCellSelection || !ResizeMode || BatchSelectionMode || !IsAlternativeStyle
            || !_selected.HasSlot || !ClientRectangle.Contains(e.Location))
        { _wheelResizeRemainder = 0; base.OnMouseWheel(e); return; }
        // Consume only the preview's explicit resize gesture. Wheel input elsewhere
        // retains normal scrolling, and the actual command wheel never edits data.
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        if (_geometryDrag.HasSlot) return;
        if (!_wheelResizeTarget.Equals(_selected))
        { _wheelResizeTarget = _selected; _wheelResizeRemainder = 0; }
        _wheelResizeRemainder += e.Delta;
        var steps = _wheelResizeRemainder / SystemInformation.MouseWheelScrollDelta;
        _wheelResizeRemainder %= SystemInformation.MouseWheelScrollDelta;
        if (steps == 0) return;
        var item = FindItem(_selected);
        var percent = RuntimeCompat.Clamp((item?.CellSizePercent ?? 100) + steps * 5, 25, 250);
        if (percent == (item?.CellSizePercent ?? 100)) return;
        TrySetCellSize(_selected.RingIndex, _selected.Slot, percent);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if(!Capture && _geometryDrag.HasSlot) CancelGeometryDrag();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.KeyCode==Keys.Escape && _geometryDrag.HasSlot)
        {
            CancelGeometryDrag();e.Handled=true;e.SuppressKeyPress=true;return;
        }
        base.OnKeyDown(e);
    }
    private void CancelGeometryDrag()
    {
        _geometryDrag=HitResult.None;
        if(_geometrySnapshot!=null)
        {
            _scene.Rings=JsonCompat.Deserialize<List<RingConfig>>(_geometrySnapshot);
            _geometrySnapshot=null;
            CellGeometryChanged?.Invoke(this,EventArgs.Empty);
        }
        Capture=false;Invalidate();
    }

    private List<StyleCell> ApplyCellSizes(List<StyleCell> cells)
    {
        foreach (var cell in cells.Where(c => c.Item != null))
        {
            var b = cell.Bounds;
            var size = RuntimeCompat.Clamp(cell.Item.CellSizePercent, 25, 250) / 100f;
            cell.Bounds = new RectangleF(b.X + b.Width * (1-size)/2, b.Y + b.Height * (1-size)/2, b.Width*size, b.Height*size);
        }
        return cells;
    }

    private List<StyleCell> BuildFreeCells(RectangleF bounds)
    {
        var cells = new List<StyleCell>();
        var entries = _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index)
            .SelectMany(r => Enumerable.Range(0, r.Slots).Select(s => (Ring:r, Slot:s)))
            .OrderByDescending(e => {var item=e.Ring.Items.FirstOrDefault(i=>i.Slot==e.Slot);return item?.FreeX.HasValue==true && item.FreeY.HasValue;}).ToList();
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(entries.Count)));
        var rows = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)columns));
        var spacing = bounds.Width * .88f / Math.Max(columns, rows);
        var baseSize = bounds.Width * .075f;
        var center = new PointF(bounds.Left + bounds.Width/2, bounds.Top + bounds.Height/2);
        var gap = 8f * bounds.Width / Math.Max(1, EffectiveMenuSizePx());
        for (int i=0; i<entries.Count; i++)
        {
            var entry = entries[i];
            var item = entry.Ring.Items.FirstOrDefault(it => it.Slot == entry.Slot);
            var size = baseSize * RuntimeCompat.Clamp(item?.CellSizePercent ?? 100, 25, 250) / 100f;
            size = Math.Min(bounds.Width * .8f, size);
            var x = item?.FreeX.HasValue == true && IsFinite(item.FreeX.Value)
                ? center.X + (float)item.FreeX.Value * bounds.Width : center.X + (i % columns - (columns-1)/2f) * spacing;
            var y = item?.FreeY.HasValue == true && IsFinite(item.FreeY.Value)
                ? center.Y + (float)item.FreeY.Value * bounds.Height : center.Y + (i / columns - (rows-1)/2f) * spacing;
            var cellBounds = new RectangleF(x-size/2, y-size/2, size,size);
            // Legacy, newly inserted, or externally edited coordinates are laid out
            // safely without rewriting user data as a side effect of painting.
            cellBounds = FitFreeBounds(cellBounds, bounds, cells, gap);
            cells.Add(new StyleCell { Hit = new HitResult(entry.Ring.Index, entry.Slot, false), Bounds=cellBounds,
                Round=true, Item=item, Label=item?.Label ?? $"空位 {entry.Slot+1}" });
        }
        return cells;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool HasClearance(RectangleF proposed, IEnumerable<StyleCell> cells, float gap)
    {
        // Layout and normalized coordinates round through single precision.
        // An exact eight-pixel gap must not become a collision after a round trip.
        var padding=Math.Max(0,gap-.02f)/2;
        var inflated=proposed; inflated.Inflate(padding, padding);
        return !cells.Any(c => { var other=c.Bounds; other.Inflate(padding,padding); return other.IntersectsWith(inflated); });
    }

    private static bool HasShapeClearance(StyleCell target, RectangleF proposed, IEnumerable<StyleCell> others, float gap)
    {
        var clearance=Math.Max(0,gap-.02f);
        bool Circle(StyleCell c) => c.Round && (c.Item==null || c.Item.CellShape=="Circle") && Math.Abs(c.Bounds.Width-c.Bounds.Height)<.02f;
        foreach(var other in others)
        {
            if(Circle(target) && Circle(other))
            {
                var dx=proposed.Left+proposed.Width/2-other.Bounds.Left-other.Bounds.Width/2;
                var dy=proposed.Top+proposed.Height/2-other.Bounds.Top-other.Bounds.Height/2;
                var minimum=(proposed.Width+other.Bounds.Width)/2+clearance;
                if(dx*dx+dy*dy<minimum*minimum) return false;
                continue;
            }
            // Non-circular cells use their actual visible outline, including
            // rounded corners and concave shapes, expanded by half the gap.
            using var first=ClearanceRegion(target,proposed,clearance);
            using var second=ClearanceRegion(other,other.Bounds,clearance);
            first.Intersect(second);
            using var identity=new Matrix();
            if(first.GetRegionScans(identity).Length>0) return false;
        }
        return true;
    }

    private static Region ClearanceRegion(StyleCell cell, RectangleF bounds, float gap)
    {
        using var path=CreateCellShape(bounds,cell.Item?.CellShape!="Circle" && cell.Item!=null
            ? cell.Item.CellShape : cell.Round?"Circle":"Square");
        var region=new Region(path);
        if(gap>0)
        {
            using var border=(GraphicsPath)path.Clone();
            using var pen=new Pen(Color.Black,gap) { LineJoin=LineJoin.Round };
            border.Widen(pen);region.Union(border);
        }
        return region;
    }
    private static RectangleF FitFreeBounds(RectangleF proposed, RectangleF bounds, List<StyleCell> cells, float gap)
    {
        proposed.X = Math.Max(bounds.Left, Math.Min(bounds.Right-proposed.Width, proposed.X));
        proposed.Y = Math.Max(bounds.Top, Math.Min(bounds.Bottom-proposed.Height, proposed.Y));
        if (HasClearance(proposed,cells,gap)) return proposed;
        var original=proposed;
        for (var attempt=0; attempt<12; attempt++)
        {
            var step = Math.Max(2, Math.Min(proposed.Width/3, 12));
            RectangleF? best = null; var distance=float.MaxValue;
            for (var y=bounds.Top; y+proposed.Height<=bounds.Bottom+.01f; y+=step)
                for (var x=bounds.Left; x+proposed.Width<=bounds.Right+.01f; x+=step)
                {
                    var test=new RectangleF(x,y,proposed.Width,proposed.Height);
                    var d=(x-original.X)*(x-original.X)+(y-original.Y)*(y-original.Y);
                    if(d<distance && HasClearance(test,cells,gap)) { best=test; distance=d; }
                }
            if(best.HasValue) return best.Value;
            proposed.Width*=.75f; proposed.Height*=.75f;
        }
        // An exceptionally crowded imported layout still must never overlap.
        return RectangleF.Empty;
    }

    private static GraphicsPath CreateCellShape(RectangleF b, string shape)
    {
        var p=new GraphicsPath();
        if (b.Width<=0 || b.Height<=0) return p;
        if(shape=="Heart")
        {
            p.AddBezier(b.Left+b.Width/2,b.Bottom,b.Left-b.Width*.25f,b.Top+b.Height*.42f,b.Left+b.Width*.02f,b.Top-b.Height*.25f,b.Left+b.Width/2,b.Top+b.Height*.22f);
            p.AddBezier(b.Left+b.Width/2,b.Top+b.Height*.22f,b.Right-b.Width*.02f,b.Top-b.Height*.25f,b.Right+b.Width*.25f,b.Top+b.Height*.42f,b.Left+b.Width/2,b.Bottom);
            p.CloseFigure();
        }
        else if(shape=="Star" || shape=="Hexagon" || shape=="Diamond")
        {
            var count=shape=="Star"?10:shape=="Hexagon"?6:4;
            var points=Enumerable.Range(0,count).Select(i => {
                var angle=-Math.PI/2+i*Math.PI*2/count;
                var radius=shape=="Star" && i%2==1?.46:1;
                return new PointF(b.Left+b.Width/2+(float)Math.Cos(angle)*b.Width/2*(float)radius,
                    b.Top+b.Height/2+(float)Math.Sin(angle)*b.Height/2*(float)radius);
            }).ToArray(); p.AddPolygon(points);
        }
        else if(shape=="Rounded")
        {
            var r=Math.Min(b.Width,b.Height)*.3f;
            p.AddArc(b.Left,b.Top,r,r,180,90);p.AddArc(b.Right-r,b.Top,r,r,270,90);
            p.AddArc(b.Right-r,b.Bottom-r,r,r,0,90);p.AddArc(b.Left,b.Bottom-r,r,r,90,90);p.CloseFigure();
        }
        else if(shape=="Square") p.AddRectangle(b);
        else p.AddEllipse(b);
        return p;
    }

    internal bool TrySetCellSize(int ring, int slot, int percent)
    {
        var item=_scene.Rings.FirstOrDefault(r=>r.Index==ring)?.Items.FirstOrDefault(i=>i.Slot==slot);
        if(!IsAlternativeStyle) return false;
        percent=RuntimeCompat.Clamp(percent,25,250);
        var cells=BuildStyleCells(GetMenuBounds());
        var cell=cells.FirstOrDefault(c=>c.Hit.RingIndex==ring && c.Hit.Slot==slot);
        if(cell==null) return false;
        var b=cell.Bounds; var factor=percent/(float)Math.Max(25,item?.CellSizePercent ?? 100);
        var proposed=new RectangleF(b.X+b.Width*(1-factor)/2,b.Y+b.Height*(1-factor)/2,b.Width*factor,b.Height*factor);
        var gap=8f*GetMenuBounds().Width/Math.Max(1,EffectiveMenuSizePx());
        var limit=GetMenuBounds();limit.Inflate(.02f,.02f);
        var clear=_scene.StyleId=="free" ? HasClearance(proposed,cells.Where(c=>c!=cell),gap)
            : HasShapeClearance(cell,proposed,cells.Where(c=>c!=cell),gap);
        if(!limit.Contains(proposed) || !clear) return false;
        if(_scene.StyleId=="free" && !FreeResizeKeepsGeometry(cells,cell,percent,proposed)) return false;
        if(_scene.StyleId=="free") PersistFreePositions(cells);
        item ??= EnsureGeometryItem(ring,slot);
        if(item==null) return false;
        item.CellSizePercent=percent;
        Invalidate(); CellGeometryChanged?.Invoke(this,EventArgs.Empty); return true;
    }

    private bool FreeResizeKeepsGeometry(List<StyleCell> before, StyleCell target, int percent, RectangleF proposed)
    {
        // Preview the same persisted coordinates that a successful resize uses.
        // Roll back every temporary field and placeholder even when fit fails.
        var originals=_scene.Rings.SelectMany(r=>r.Items.Select(i=>(Ring:r,Item:i,X:i.FreeX,Y:i.FreeY,Size:i.CellSizePercent))).ToList();
        try
        {
            PersistFreePositions(before);
            var item=EnsureGeometryItem(target.Hit.RingIndex,target.Hit.Slot);
            item.CellSizePercent=percent;
            var after=BuildFreeCells(GetMenuBounds());
            bool Same(RectangleF a,RectangleF b) => Math.Abs(a.X-b.X)<.03f && Math.Abs(a.Y-b.Y)<.03f
                && Math.Abs(a.Width-b.Width)<.03f && Math.Abs(a.Height-b.Height)<.03f;
            return before.All(old=>
            {
                var next=after.FirstOrDefault(c=>c.Hit.Equals(old.Hit));
                return next!=null && Same(next.Bounds,old==target?proposed:old.Bounds);
            });
        }
        finally
        {
            foreach(var ring in _scene.Rings) ring.Items.RemoveAll(i=>!originals.Any(o=>ReferenceEquals(o.Item,i)));
            foreach(var old in originals) {old.Item.FreeX=old.X;old.Item.FreeY=old.Y;old.Item.CellSizePercent=old.Size;}
            foreach(var cell in before) cell.Item=originals.FirstOrDefault(o=>o.Ring.Index==cell.Hit.RingIndex && o.Item.Slot==cell.Hit.Slot).Item;
        }
    }

    internal bool TryPlaceCell(int ring, int slot, Point clientPoint)
    {
        if(_scene.StyleId!="free") return false;
        var bounds=GetMenuBounds(); var cells=BuildFreeCells(bounds);
        var cell=cells.FirstOrDefault(c=>c.Hit.RingIndex==ring && c.Hit.Slot==slot);
        if(cell==null) return false;
        var proposed=new RectangleF(clientPoint.X-cell.Bounds.Width/2, clientPoint.Y-cell.Bounds.Height/2,cell.Bounds.Width,cell.Bounds.Height);
        var gap=8f*bounds.Width/Math.Max(1,EffectiveMenuSizePx());
        if(!bounds.Contains(proposed) || !HasClearance(proposed,cells.Where(c=>c!=cell),gap)) return false;
        PersistFreePositions(cells);
        cell.Item ??= EnsureGeometryItem(ring,slot);
        if(cell.Item==null) return false;
        cell.Item.FreeX=(clientPoint.X-bounds.Left)/bounds.Width-.5;
        cell.Item.FreeY=(clientPoint.Y-bounds.Top)/bounds.Height-.5;
        Invalidate();CellGeometryChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    internal bool NudgeFreeLayout()
    {
        if(_scene.StyleId!="free") return false;
        var bounds=GetMenuBounds();var cells=BuildFreeCells(bounds);
        var scale=bounds.Width/Math.Max(1,EffectiveMenuSizePx());
        var grid=4f*scale;var gap=8f*scale;
        if(grid<=0) return false;
        PersistFreePositions(cells);
        foreach(var cell in cells.Where(c=>c.Hit.HasSlot))
        {
            cell.Item ??= EnsureGeometryItem(cell.Hit.RingIndex,cell.Hit.Slot);
            if(cell.Item==null) continue;
            var b=cell.Bounds;var x=b.Left+b.Width/2;var y=b.Top+b.Height/2;
            // Round only to a four-logical-pixel lattice: each axis changes by
            // at most two pixels, with no sorting, grouping, or re-packing.
            var dx=(float)Math.Round((x-bounds.Left)/grid)*grid+bounds.Left-x;
            var dy=(float)Math.Round((y-bounds.Top)/grid)*grid+bounds.Top-y;
            foreach(var delta in new[]{new PointF(dx,dy),new PointF(dx,0),new PointF(0,dy)})
            {
                var proposed=b;proposed.Offset(delta);
                if(!bounds.Contains(proposed) || !HasClearance(proposed,cells.Where(c=>c!=cell),gap)) continue;
                cell.Bounds=proposed;
                cell.Item.FreeX=(proposed.Left+proposed.Width/2-bounds.Left)/bounds.Width-.5;
                cell.Item.FreeY=(proposed.Top+proposed.Height/2-bounds.Top)/bounds.Height-.5;
                break;
            }
        }
        Invalidate();CellGeometryChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    private void PersistFreePositions(List<StyleCell> cells)
    {
        var bounds=GetMenuBounds();
        foreach(var cell in cells.Where(c=>c.Hit.HasSlot))
        {
            cell.Item ??= EnsureGeometryItem(cell.Hit.RingIndex,cell.Hit.Slot);
            if(cell.Item==null) continue;
            cell.Item.FreeX=(cell.Bounds.Left+cell.Bounds.Width/2-bounds.Left)/bounds.Width-.5;
            cell.Item.FreeY=(cell.Bounds.Top+cell.Bounds.Height/2-bounds.Top)/bounds.Height-.5;
        }
    }

    private MenuItemConfig? EnsureGeometryItem(int ring, int slot)
    {
        var group=_scene.Rings.FirstOrDefault(r=>r.Index==ring);
        if(group==null || slot<0 || slot>=group.Slots) return null;
        var item=group.Items.FirstOrDefault(i=>i.Slot==slot);
        if(item==null) {item=new MenuItemConfig {Slot=slot};group.Items.Add(item);}
        return item;
    }

    private bool BeginGeometryDrag(MouseEventArgs e)
    {
        if(!EnableCellSelection || e.Button!=MouseButtons.Left) return false;
        var hit=HitTest(e.Location);
        if(BatchSelectionMode)
        {
            if(hit.HasSlot) { var key=(hit.RingIndex,hit.Slot); if(!BatchSelection.Add(key)) BatchSelection.Remove(key); }
            SelectCellAt(e.Location); Invalidate(); return true;
        }
        if(!IsAlternativeStyle || (!ResizeMode && _scene.StyleId!="free")) return false;
        if (ResizeMode && CanFocus) Focus();
        var cells=BuildStyleCells(GetMenuBounds());
        var selected=cells.FirstOrDefault(c=>c.Hit.Equals(_selected));
        var handle=ResizeMode && selected!=null && ResizeHandles(selected.Bounds).Any(r=>r.Contains(e.Location));
        if(handle) hit=_selected;
        else SelectCellAt(e.Location);
        var cell=cells.FirstOrDefault(c=>c.Hit.Equals(hit));
        if(cell==null) return true;
        if(ResizeMode && !handle) { Invalidate(); return true; }
        _geometrySnapshot=JsonCompat.Serialize(_scene.Rings);
        _geometryDrag=hit; _geometryResize=handle; _geometryStart=e.Location;
        _geometryBounds=cell.Bounds; _geometryStartSize=cell.Item?.CellSizePercent ?? 100;
        Capture=true; return true;
    }

    private bool MoveGeometryDrag(MouseEventArgs e)
    {
        if(!_geometryDrag.HasSlot) return false;
        var cells=BuildStyleCells(GetMenuBounds());
        var cell=cells.FirstOrDefault(c=>c.Hit.Equals(_geometryDrag));
        if(cell==null) return true;
        if(_geometryResize)
        {
            var center=new PointF(_geometryBounds.Left+_geometryBounds.Width/2,_geometryBounds.Top+_geometryBounds.Height/2);
            var side=2*Math.Max(Math.Abs(e.X-center.X),Math.Abs(e.Y-center.Y));
            TrySetCellSize(_geometryDrag.RingIndex,_geometryDrag.Slot,(int)Math.Round(_geometryStartSize*side/Math.Max(1,_geometryBounds.Width)));
        }
        else
        {
            var proposed=_geometryBounds; proposed.Offset(e.X-_geometryStart.X,e.Y-_geometryStart.Y);
            var bounds=GetMenuBounds(); var gap=8f*bounds.Width/Math.Max(1,EffectiveMenuSizePx());
            if(bounds.Contains(proposed) && HasClearance(proposed,cells.Where(c=>c!=cell),gap))
            {
                PersistFreePositions(cells);
                cell.Item ??= EnsureGeometryItem(_geometryDrag.RingIndex,_geometryDrag.Slot);
                if(cell.Item==null) return true;
                cell.Item.FreeX=(proposed.Left+proposed.Width/2-bounds.Left)/bounds.Width-.5;
                cell.Item.FreeY=(proposed.Top+proposed.Height/2-bounds.Top)/bounds.Height-.5;
                Invalidate(); CellGeometryChanged?.Invoke(this,EventArgs.Empty);
            }
        }
        return true;
    }

    private static RectangleF[] ResizeHandles(RectangleF b) => new[] {
        new RectangleF(b.Left-4,b.Top-4,8,8), new RectangleF(b.Right-4,b.Top-4,8,8),
        new RectangleF(b.Left-4,b.Bottom-4,8,8), new RectangleF(b.Right-4,b.Bottom-4,8,8) };
    private void DrawGeometryHandles(Graphics g)
    {
        if(!EnableCellSelection || !ResizeMode || !IsAlternativeStyle || BatchSelectionMode) return;
        var cell=BuildStyleCells(GetMenuBounds()).FirstOrDefault(c=>c.Hit.Equals(_selected));
        if(cell==null) return;
        using var pen=new Pen(Color.DeepSkyBlue,1.5f);
        var b=cell.Bounds; g.DrawRectangle(pen,b.X,b.Y,b.Width,b.Height);
        foreach(var handle in ResizeHandles(b)) {g.FillRectangle(Brushes.White,handle);g.DrawRectangle(pen,handle.X,handle.Y,handle.Width,handle.Height);}
    }
}
