using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class EditorWorkflowRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool,string> check)
    {
        var input=new PluginConfig(); ConfigStore.Normalize(input);
        input.Menus[SceneKeys.Global].StyleId="orbit";
        var saves=0;
        using var window=new SettingsWindow(input,new CadCommandCatalog(),_=>saves++,loadCatalog:false);
        window.StartPosition=FormStartPosition.Manual; window.Location=new Point(-20000,-20000);window.ShowInTaskbar=false;
        window.ClientSize=new Size(1600,1000);
        window.Show(); Application.DoEvents();
        var preview=Field<RadialMenuControl>(window,"_preview");
        var pages=Field<TabControl>(window,"_pages");
        var draft=Field<PluginConfig>(window,"_config");
        var wheel=draft.Menus[SceneKeys.Global];
        var typography=Descendants(window).OfType<TabPage>().Single(p=>p.Text=="文字与图片");
        var cellTabs=(TabControl)typography.Parent;
        var generalTabs=(TabControl)cellTabs.Parent.Parent;
        Action open=()=>{pages.SelectedIndex=3;generalTabs.SelectedTab=(TabPage)cellTabs.Parent;cellTabs.SelectedTab=typography;Invoke(window,"ShowBatchCellAppearance");};
        open(); preview.BatchSelection.Add((0,0)); pages.SelectedIndex=1;
        check(!preview.BatchSelectionMode&&preview.BatchSelection.Count==0&&preview.EnableCellSelection,"leaving batch for command management restores ordinary preview selection");
        open(); cellTabs.SelectedIndex=0;
        check(!preview.BatchSelectionMode,"switching from batch to command binding ends hidden batch mode");
        open(); generalTabs.SelectedIndex=0;
        check(!preview.BatchSelectionMode,"switching from batch to layout restores add remove and shape tools");
        open(); Invoke(window,"SaveAllPendingSettings");
        check(!preview.BatchSelectionMode&&saves==1,"save exits temporary batch selection mode");
        var practice=Field<Button>(window,"_practiceButton"); Click(practice);
        check(!preview.EnableCellSelection,"practice mode activates before transition check");
        pages.SelectedIndex=1;
        check(preview.EnableCellSelection&&practice.Text=="练习模式","leaving practice restores command binding and practice button");
        pages.SelectedIndex=3; var resize=Field<Button>(window,"_resizeCellButton");Click(resize);
        check(preview.ResizeMode,"resize mode activates before transition check");
        pages.SelectedIndex=1;
        check(!preview.ResizeMode&&resize.Text=="调整单格大小","leaving resize ends handles and restores button text");
        foreach(var ring in wheel.Rings) {ring.Enabled=ring.Index==0;ring.Items.Clear();}
        wheel.Rings[0].Slots=3;
        wheel.Rings[0].Items.Add(new MenuItemConfig{Slot=0,Value="LINE",Label="直线"});
        wheel.Rings[0].Items.Add(new MenuItemConfig{Slot=1,FontSizePx=20,CellShape="Heart"});
        wheel.Rings[0].Items.Add(new MenuItemConfig{Slot=2,FontSizePx=20});
        Invoke(window,"SelectFirstEmptyWheelCell");
        var target=Field<object>(preview,"_selected");
        check((int)target.GetType().GetProperty("Slot").GetValue(target)==1,"styled empty position remains available for automatic command assignment");
        wheel.Rings[0].Items[1].Value="CIRCLE";
        Invoke(window,"SelectFirstEmptyWheelCell"); target=Field<object>(preview,"_selected");
        check((int)target.GetType().GetProperty("Slot").GetValue(target)==2,"consecutive assignment advances to next styled empty slot");
        pages.SelectedIndex=3;
        window.PerformLayout(); Application.DoEvents();
        preview.SetMenu(draft,wheel);
        var styled=wheel.Rings[0].Items[2]; styled.FontFamily="宋体";styled.FontSizePx=23;styled.TextColor="#123456";styled.FontBold=true;
        Point? drop=null;
        for(var y=0;y<preview.Height&&drop==null;y+=2)
            for(var x=0;x<preview.Width;x+=2){var hit=preview.HitCell(new Point(x,y));if(hit!=null&&hit.RingIndex==0&&hit.Slot==2){drop=new Point(x,y);break;}}
        check(drop.HasValue,"styled empty cell is reachable for command drag binding ("+preview.Size+", style="+wheel.StyleId+", ring="+wheel.Rings[0].Index+", slots="+wheel.Rings[0].Slots+")");
        var screen=preview.PointToScreen(drop.Value); var data=new DataObject();data.SetData(typeof(CadCommandInfo),new CadCommandInfo{GlobalName="WORKFLOW_DRAG",DisplayName="拖入功能"});
        typeof(Control).GetMethod("OnDragDrop",Private).Invoke(preview,new object[]{new DragEventArgs(data,0,screen.X,screen.Y,DragDropEffects.Copy,DragDropEffects.Copy)});
        var bound=wheel.Rings[0].Items.Single(i=>i.Slot==2);
        check(bound.Value=="WORKFLOW_DRAG"&&bound.FontSizePx==23&&bound.FontFamily=="宋体"&&bound.TextColor=="#123456"&&bound.FontBold==true,
            "dragging a command preserves batch font size family color and bold");
        window.Hide();
    }
    static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Private).GetValue(o);
    static void Invoke(object o,string n)=>o.GetType().GetMethod(n,Private).Invoke(o,null);
    static void Click(Button b)=>typeof(Button).GetMethod("OnClick",Private).Invoke(b,new object[]{EventArgs.Empty});
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var x in Descendants(c))yield return x;}}
}
