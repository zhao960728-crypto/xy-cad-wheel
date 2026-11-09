namespace XiaoYang.AutoCADPlugin;

internal static class ProductMetadata
{
    public static string DisplayVersion => typeof(ProductMetadata).Assembly.GetName().Version.ToString();
}
