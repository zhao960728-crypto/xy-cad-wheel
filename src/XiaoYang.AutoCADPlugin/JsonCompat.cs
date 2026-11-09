#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace XiaoYang.AutoCADPlugin;

internal static class JsonCompat
{
    public static string Serialize(object value, bool indented = true)
    {
#if NETFRAMEWORK
        var serializer = CreateFrameworkSerializer();
        return serializer.Serialize(value);
#else
        return JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNameCaseInsensitive = true
        });
#endif
    }

    public static T Deserialize<T>(string json)
    {
#if NETFRAMEWORK
        return CreateFrameworkSerializer().Deserialize<T>(json);
#else
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
#endif
    }

#if NETFRAMEWORK
    private static JavaScriptSerializer CreateFrameworkSerializer() => new()
    {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 256
    };
#endif
}
