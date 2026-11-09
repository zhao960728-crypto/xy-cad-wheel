namespace XiaoYang.AutoCADPlugin;

internal static class SceneKeys
{
    public const string Global = "cad.global";
    public const string Blank = "cad.blank";
    public const string Model = "cad.model";
    public const string Layout = "cad.layout";
    public const string BlockEditor = "cad.block-editor";

    public static readonly string[] All = { Global };

    public static readonly string[] Legacy = { Model, Layout, BlockEditor, Blank };

    public static string Display(string key) => key switch
    {
        Global => "全局轮盘",
        Blank => "无图纸",
        Model => "模型空间",
        Layout => "布局空间",
        BlockEditor => "块编辑器",
        _ => key
    };
}
