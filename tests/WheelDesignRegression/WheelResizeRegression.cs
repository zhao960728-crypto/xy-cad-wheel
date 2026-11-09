using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class WheelResizeRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var config = new PluginConfig(); ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Global]; scene.StyleId = "free";
        foreach (var ring in scene.Rings) ring.Enabled = ring.Index == 0;
        scene.Rings[0].Slots = 2;
        scene.Rings[0].Items = new() {
            new() { Slot=0, Label="测试", Value="LINE", FreeX=-.25, FreeY=0 },
            new() { Slot=1, Label="相邻", Value="CIRCLE", FreeX=.25, FreeY=0 } };
        using var control = new RadialMenuControl(Color.White) { Size=new Size(600,600), ScaleMenuForPreview=true, EnableCellSelection=true };
        control.SetMenu(config,scene); control.CreateControl(); control.SelectCell(0,0);
        var first = scene.Rings[0].Items[0]; var other=JsonCompat.Serialize(scene.Rings[0].Items[1]);
        Wheel(control,120); check(first.CellSizePercent==100,"wheel outside resize mode leaves cell unchanged");
        control.ResizeMode=true;
        check(Wheel(control,120).Handled && first.CellSizePercent==105,"resize wheel up increases selected cell by five percent and consumes scroll");
        check(JsonCompat.Serialize(scene.Rings[0].Items[1])==other,"resize wheel leaves neighboring configured cell unchanged");
        Wheel(control,-120); check(first.CellSizePercent==100,"resize wheel down decreases selected cell");
        Wheel(control,60); check(first.CellSizePercent==100,"partial high-resolution wheel tick waits for complete notch");
        Wheel(control,60); check(first.CellSizePercent==105,"partial wheel deltas accumulate into one step");
        control.SelectCell(0,1); Wheel(control,60); check(scene.Rings[0].Items[1].CellSizePercent==100,"new selected cell resets wheel remainder");
        control.ResizeMode=false; control.ResizeMode=true; Wheel(control,60);
        check(scene.Rings[0].Items[1].CellSizePercent==100,"new resize session resets partial wheel notch");
        control.SelectCell(0,0); Wheel(control,60); control.SelectCell(0,1); control.SelectCell(0,0); Wheel(control,60);
        check(first.CellSizePercent==105,"switching away and back resets partial wheel notch without intermediate scrolling");
        control.ResizeMode=false; control.ResizeMode=true;
        var snapshot=JsonCompat.Serialize(config);
        control.BatchSelectionMode=true; Wheel(control,120);
        check(JsonCompat.Serialize(config)==snapshot,"batch selection does not accidentally resize single cell");
        control.BatchSelectionMode=false; control.EnableCellSelection=false; Wheel(control,120);
        check(JsonCompat.Serialize(config)==snapshot,"live command wheel cannot edit configuration");
        control.EnableCellSelection=true; Wheel(control,120,new Point(-5,-5));
        check(JsonCompat.Serialize(config)==snapshot,"wheel outside preview does not edit selected cell");
        control.SelectCell(0,0); first.CellSizePercent=25; Wheel(control,-120);
        check(first.CellSizePercent==25,"wheel resize respects minimum size");
        first.CellSizePercent=100; scene.Rings[0].Items[1].FreeX=-.155;
        snapshot=JsonCompat.Serialize(config); Wheel(control,1200);
        check(JsonCompat.Serialize(config)==snapshot,"collision-rejected wheel resize preserves complete configuration");
        RestoreOriginalSizeRegression(check);
    }

    private static void RestoreOriginalSizeRegression(Action<bool,string> check)
    {
        foreach(var style in new[]{"orbit","06","free"})
        {
            var config=new PluginConfig();ConfigStore.Normalize(config);
            var scene=config.Menus[SceneKeys.Global];scene.StyleId=style;scene.MenuSizePx=520;
            scene.Rings=Enumerable.Range(0,style=="free"?4:3).Select(r=>new RingConfig {Index=r,Enabled=true,Slots=style=="free"?16:32,
                Items=Enumerable.Range(0,style=="free"?16:32).Select(s=>new MenuItemConfig {Slot=s,Value="LINE",Label="格"}).ToList()}).ToList();
            using var c=new RadialMenuControl(Color.White){Size=new Size(600,600),ScaleMenuForPreview=true,EnableCellSelection=true,ResizeMode=true};
            c.SetMenu(config,scene);c.CreateControl();c.SelectCell(0,0);
            var cells=StyleBounds(c);
            foreach(var key in cells.Keys.Where(k=>k.Slot>=0).ToArray())
            {
                check(c.TrySetCellSize(key.Ring,key.Slot,95),style+" dense layout cell "+key+" shrinks without false collision");
                check(c.TrySetCellSize(key.Ring,key.Slot,100),style+" dense layout cell "+key+" restores original 100 percent");
            }
            var restored=StyleBounds(c);
            check(cells.All(kv=>Near(kv.Value,restored[kv.Key])),style+" shrink and restore preserve the full original geometry");
            c.SelectCell(0,0);Wheel(c,-120);Wheel(c,120);
            check(scene.Rings[0].Items[0].CellSizePercent==100,style+" wheel down and up return exactly to original size");
            if(style=="free")
            {
                var roundTrip=JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));ConfigStore.Normalize(roundTrip);
                c.SetMenu(roundTrip,roundTrip.Menus[SceneKeys.Global]);
                check(c.TrySetCellSize(0,0,95)&&c.TrySetCellSize(0,0,100),"saved dense free layout retains reversible resizing");
            }
        }
        // Two circles at an exact diagonal eight-pixel gap have overlapping
        // bounding squares. That must not prohibit returning to the valid size.
        var type=typeof(RadialMenuControl);
        var cellType=type.GetNestedType("StyleCell",BindingFlags.NonPublic);
        var hitType=type.GetNestedType("HitResult",BindingFlags.NonPublic);
        object Cell(float x,float y)
        {
            var cell=Activator.CreateInstance(cellType,true);
            cellType.GetField("Bounds").SetValue(cell,new RectangleF(x,y,40,40));
            cellType.GetField("Round").SetValue(cell,true);
            cellType.GetField("Item").SetValue(cell,new MenuItemConfig());
            return cell;
        }
        var target=Cell(0,0);var distance=48f/(float)Math.Sqrt(2);var neighbor=Cell(distance,distance);
        var array=Array.CreateInstance(cellType,1);array.SetValue(neighbor,0);
        var method=type.GetMethod("HasShapeClearance",BindingFlags.Static|BindingFlags.NonPublic);
        check((bool)method.Invoke(null,new object[]{target,new RectangleF(0,0,40,40),array,8f}),"diagonal round cells use circular clearance rather than bounding boxes");
        check(!(bool)method.Invoke(null,new object[]{target,new RectangleF(-3,-3,46,46),array,8f}),"circular clearance still rejects actual gap violations");
    }

    private static bool Near(RectangleF a,RectangleF b) => Math.Abs(a.X-b.X)<.03f && Math.Abs(a.Y-b.Y)<.03f
        && Math.Abs(a.Width-b.Width)<.03f && Math.Abs(a.Height-b.Height)<.03f;
    private static System.Collections.Generic.Dictionary<(int Ring,int Slot),RectangleF> StyleBounds(RadialMenuControl c)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(RadialMenuControl);var bounds=type.GetMethod("GetMenuBounds",flags).Invoke(c,null);
        var cells=(System.Collections.IEnumerable)type.GetMethod("BuildStyleCells",flags).Invoke(c,new[]{bounds});
        var result=new System.Collections.Generic.Dictionary<(int Ring,int Slot),RectangleF>();
        foreach(var cell in cells)
        {
            var hit=cell.GetType().GetField("Hit").GetValue(cell);
            result[((int)hit.GetType().GetProperty("RingIndex").GetValue(hit),(int)hit.GetType().GetProperty("Slot").GetValue(hit))]
                =(RectangleF)cell.GetType().GetField("Bounds").GetValue(cell);
        }
        return result;
    }
    private static HandledMouseEventArgs Wheel(RadialMenuControl control,int delta,Point? point=null)
    {
        var p=point ?? new Point(300,300);
        var e=new HandledMouseEventArgs(MouseButtons.None,0,p.X,p.Y,delta);
        typeof(RadialMenuControl).GetMethod("OnMouseWheel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{e});
        return e;
    }
}
