using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class FreeDesignerRegression
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static Dictionary<int,RectangleF> Cells(RadialMenuControl c)
    {
        var bounds=typeof(RadialMenuControl).GetMethod("GetMenuBounds",Flags).Invoke(c,null);
        var cells=(IEnumerable)typeof(RadialMenuControl).GetMethod("BuildFreeCells",Flags).Invoke(c,new[]{bounds});
        var result=new Dictionary<int,RectangleF>();
        foreach(var cell in cells)
        {
            var hit=cell.GetType().GetField("Hit").GetValue(cell);
            var slot=(int)hit.GetType().GetProperty("Slot").GetValue(hit);
            result[slot]=(RectangleF)cell.GetType().GetField("Bounds").GetValue(cell);
        }
        return result;
    }
    internal static void Run(Action<bool,string> check,string output=null)
    {
        var legacy=JsonCompat.Deserialize<MenuItemConfig>("{\"Slot\":0}");
        check(legacy.CellSizePercent==100 && legacy.IconRatioPercent==55 && legacy.FreeX==null,"legacy designer defaults preserve original cell size");
        var config=new PluginConfig();ConfigStore.Normalize(config);
        var scene=config.Menus[SceneKeys.Global];scene.StyleId="free";
        foreach(var r in scene.Rings)r.Enabled=r.Index==0;
        var ring=scene.Rings[0];ring.Slots=3;
        ring.Items=new List<MenuItemConfig>{new(){Slot=0,Label="圆",FreeX=-.25,FreeY=-.12},new(){Slot=1,Label="心",CellShape="Heart",FreeX=.1,FreeY=.12}};
        using var c=new RadialMenuControl(Color.White){Size=new Size(600,600),ScaleMenuForPreview=true,EnableCellSelection=true};
        c.SetMenu(config,scene);c.CreateControl();
        var initial=Cells(c);var snapshot=JsonCompat.Serialize(config);
        typeof(RadialMenuControl).GetMethod("OnMouseDown",Flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,(int)(initial[0].X+initial[0].Width/2),(int)(initial[0].Y+initial[0].Height/2),0)});
        typeof(RadialMenuControl).GetMethod("OnMouseUp",Flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,0,0,0)});
        check(snapshot==JsonCompat.Serialize(config),"free cell click changes selection only");
        var empty=initial[2];
        typeof(RadialMenuControl).GetMethod("OnMouseDown",Flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,(int)(empty.X+empty.Width/2),(int)(empty.Y+empty.Height/2),0)});
        typeof(RadialMenuControl).GetMethod("OnMouseUp",Flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,0,0,0)});
        check(snapshot==JsonCompat.Serialize(config),"empty free cell click does not create configuration");
        check(!c.TryPlaceCell(0,0,Point.Round(new PointF(initial[1].X+initial[1].Width/2,initial[1].Y+initial[1].Height/2))),"free placement rejects overlap");
        check(snapshot==JsonCompat.Serialize(config),"rejected placement leaves all configuration unchanged");
        check(c.TrySetCellSize(0,0,125),"individual free cell resize accepted with clearance");
        var resized=Cells(c);
        check(resized[1]==initial[1] && resized[0].Width>initial[0].Width,"individual resize leaves neighbor geometry unchanged");
        check(c.TrySetCellSize(0,2,75) && ring.Items.Any(i=>i.Slot==2),"empty free cell supports individual resize");
        var target=ring.Items[0];var oldX=target.FreeX;target.FreeX=-.44;
        var refusal=JsonCompat.Serialize(config);
        check(!c.TrySetCellSize(0,0,250) && refusal==JsonCompat.Serialize(config),"rejected enlargement preserves every cell property");
        target.FreeX=oldX;
        var stable=Cells(c);ring.Slots++;
        var added=Cells(c);
        check(Math.Abs(added[0].Width-stable[0].Width)<.01 && Math.Abs(added[1].Width-stable[1].Width)<.01,"new free cell keeps existing sizes");
        var before=Cells(c);check(c.NudgeFreeLayout(),"manual fine alignment succeeds");var after=Cells(c);
        var menu=(RectangleF)typeof(RadialMenuControl).GetMethod("GetMenuBounds",Flags).Invoke(c,null);
        var limit=2.01*menu.Width/scene.MenuSizePx;
        check(before.All(kv=>Math.Abs(after[kv.Key].X-kv.Value.X)<=limit && Math.Abs(after[kv.Key].Y-kv.Value.Y)<=limit),"fine alignment moves at most two logical pixels per axis");
        var invalid=JsonCompat.Serialize(config);check(!c.TryPlaceCell(0,0,new Point(-100,-100)) && invalid==JsonCompat.Serialize(config),"out of bounds drag leaves config unchanged");
        var clone=JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));ConfigStore.Normalize(clone);
        check(clone.Menus[SceneKeys.Global].Rings[0].Items.Any(i=>i.Slot==2 && i.CellSizePercent==75 && string.IsNullOrEmpty(i.Value)),"empty designer cell survives configuration normalization");
        check(clone.Menus[SceneKeys.Global].StyleId=="free" && clone.Menus[SceneKeys.Global].Rings[0].Items[1].CellShape=="Heart","free style and shape survive serialization");
        using var image=new Bitmap(20,20);using(var g=Graphics.FromImage(image))g.Clear(Color.Magenta);
        var item=ring.Items[0];item.IconData=CommandIconImage.Encode(image,new RectangleF(0,0,20,20));item.ContentMode="ImageOnly";
        foreach(var style in new[]{"free","orbit","01","classic"})
        {
            scene.StyleId=style;c.SetMenu(config,scene);c.DrawHover=false;
            using var bitmap=new Bitmap(600,600);c.DrawToBitmap(bitmap,new Rectangle(0,0,600,600));
            var pixels=0;for(var y=0;y<600;y++)for(var x=0;x<600;x++){var p=bitmap.GetPixel(x,y);if(p.R>200&&p.B>200&&p.G<90)pixels++;}
            check(pixels>50,style+" full-cell image mode renders background");
            if(output!=null)bitmap.Save(Path.Combine(output,"designer-"+style+".png"));
        }
        scene.StyleId="free";c.SetMenu(config,scene);
        var dragBounds=Cells(c)[0];var start=Point.Round(new PointF(dragBounds.X+dragBounds.Width/2,dragBounds.Y+dragBounds.Height/2));
        var beforeDrag=JsonCompat.Serialize(config);
        typeof(RadialMenuControl).GetMethod("OnMouseDown",Flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,start.X,start.Y,0)});
        typeof(RadialMenuControl).GetMethod("OnMouseMove",Flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,0,start.X+4,start.Y+4,0)});
        typeof(RadialMenuControl).GetMethod("OnKeyDown",Flags).Invoke(c,new object[]{new KeyEventArgs(Keys.Escape)});
        check(beforeDrag==JsonCompat.Serialize(config) && !c.Capture,"escape cancels free movement and releases capture");
        typeof(RadialMenuControl).GetMethod("OnMouseMove",Flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,0,start.X+14,start.Y+14,0)});
        check(beforeDrag==JsonCompat.Serialize(config),"cancelled free drag never resumes on later mouse move");
    }
}
