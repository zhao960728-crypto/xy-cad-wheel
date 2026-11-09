using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoYang.AutoCADPlugin;

internal interface IBackupUpdateSource : IDisposable
{
    Task<UpdateManifest> CheckAsync(Version current, CancellationToken token);
    Task DownloadAsync(UpdateManifest manifest, string destination, IProgress<UpdateProgress> progress, CancellationToken token);
}

internal sealed class BackupUpdateSettings
{
    public string FolderUrl { get; set; }
    public string ShareCode { get; set; }
    public DownloadLink[] DownloadLinks { get; set; }
}

internal sealed class DownloadLink
{
    public string Name { get; set; }
    public string Url { get; set; }
    public string Code { get; set; }

    internal bool IsValid => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Host == "pan.quark.cn" || uri.Host == "pan.baidu.com" || uri.Host == "wwapt.lanzoum.com");

    internal static DownloadLink[] Read(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 8192) return Array.Empty<DownloadLink>();
        return (JsonCompat.Deserialize<BackupUpdateSettings>(File.ReadAllText(path))?.DownloadLinks ?? Array.Empty<DownloadLink>())
            .Where(link => link != null && link.IsValid).Take(3).ToArray();
    }
}

// Parse only the known mobile download protocol. Never evaluate downloaded JavaScript.
internal sealed class LanzouUpdateSource : IBackupUpdateSource
{
    internal const string SettingsFile = "update-backup.json";
    private const string UserAgent = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    private readonly UpdateProductProfile profile;
    private readonly BackupUpdateSettings settings;
    private readonly Uri folder;
    private readonly CookieContainer cookies = new();
    private readonly HttpClient client;
    private List<LanzouFile> files;
    internal sealed class LanzouFile { public int t { get; set; } public string id { get; set; } public string name_all { get; set; } }
    private sealed class Listing { public int zt { get; set; } public List<LanzouFile> text { get; set; } }

    public LanzouUpdateSource(UpdateProductProfile profile, BackupUpdateSettings settings, HttpMessageHandler handler = null)
    {
        this.profile = profile;
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (!Uri.TryCreate(settings.FolderUrl, UriKind.Absolute, out folder) || folder.Scheme != "https" ||
            folder.Host != "wwapt.lanzoum.com" || !folder.IsDefaultPort || folder.UserInfo.Length != 0 ||
            !Regex.IsMatch(folder.AbsolutePath, @"\A/b[a-zA-Z0-9]+\z") || folder.Query.Length != 0 || folder.Fragment.Length != 0)
            throw new InvalidDataException("蓝奏云备用文件夹配置无效。");
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }, true)
            { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    internal static LanzouUpdateSource FromFile(UpdateProductProfile profile, string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 8192) throw new InvalidDataException("备用更新配置过大。");
        return new LanzouUpdateSource(profile, JsonCompat.Deserialize<BackupUpdateSettings>(File.ReadAllText(path)));
    }

    public async Task<UpdateManifest> CheckAsync(Version current, CancellationToken token)
    {
        files = await ListAsync(token).ConfigureAwait(false);
        var pattern = @"\A" + Regex.Escape(profile.ProductId) + @"-(?<v>\d+\.\d+\.\d+\.\d+)\.update\.txt\z";
        var candidates = files.Select(f => new { File = f, Match = Regex.Match(f.name_all ?? "", pattern) })
            .Where(x => x.Match.Success).Select(x => new { x.File, Version = PluginUpdateService.ParseVersion(x.Match.Groups["v"].Value) })
            .OrderByDescending(x => x.Version).ToList();
        if (candidates.Count == 0) throw new InvalidDataException("蓝奏云尚未发布本产品的配套更新校验清单，更新失败。");
        var candidate = candidates[0];
        var data = await ReadFileAsync(candidate.File, 64 * 1024, token).ConfigureAwait(false);
        UpdateManifest manifest;
        try { manifest = JsonCompat.Deserialize<UpdateManifest>(Encoding.UTF8.GetString(data).TrimStart('\uFEFF')); }
        catch (Exception ex) { throw new InvalidDataException("蓝奏云校验清单格式无效。", ex); }
        if (manifest == null || manifest.ProductId != profile.ProductId || manifest.Version != candidate.Version.ToString(4))
            throw new InvalidDataException("蓝奏云清单产品或版本与文件名不一致。");
        // Let the shared updater validate the entire manifest, including older versions.
        return manifest;
    }

    public async Task DownloadAsync(UpdateManifest manifest, string destination, IProgress<UpdateProgress> progress, CancellationToken token)
    {
        files ??= await ListAsync(token).ConfigureAwait(false);
        var name = profile.BundleName.Substring(0, profile.BundleName.Length - ".bundle".Length) + "-" + manifest.Version + ".zip";
        var matches = files.Where(f => f.name_all == name).ToList();
        if (matches.Count != 1) throw new InvalidDataException("蓝奏云缺少对应版本安装包，或存在同名文件；已停止备用更新。");
        var uri = await ResolveFileAsync(matches[0], token).ConfigureAwait(false);
        using var response = await SendAsync(uri, null, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType?.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0)
            throw new InvalidDataException("蓝奏云下载仍返回验证网页，更新失败，请稍后重试。");
        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != manifest.Size)
            throw new InvalidDataException("蓝奏云安装包长度与清单不一致。");
        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await CopyAsync(input, output, manifest.Size, n => progress?.Report(new UpdateProgress("正在从蓝奏云下载", n, manifest.Size)), token).ConfigureAwait(false);
        if (output.Length != manifest.Size) throw new InvalidDataException("蓝奏云安装包下载不完整。");
    }

    private async Task<List<LanzouFile>> ListAsync(CancellationToken token)
    {
        var html = await TextAsync(folder, null, token).ConfigureAwait(false);
        var endpoint = Capture(html, @"url\s*:\s*['"" ]*(/filemoreajax\.php\?file=\d+)['""]");
        var body = ParseForm(html);
        body["pwd"] = settings.ShareCode ?? "";
        var result = new List<LanzouFile>();
        for (var page = 1; page <= 20; page++)
        {
            body["pg"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var json = await TextAsync(new Uri(folder, endpoint), body, token).ConfigureAwait(false);
            Listing listing;
            try { listing = JsonCompat.Deserialize<Listing>(json); }
            catch (Exception ex) { throw new InvalidDataException("蓝奏云文件列表不可读取。", ex); }
            if (listing?.zt != 1 || listing.text == null) throw new InvalidDataException("蓝奏云文件夹未能打开，请检查分享有效性和提取码。");
            result.AddRange(listing.text.Where(f => f.t == 0 && Regex.IsMatch(f.id ?? "", @"\A[a-zA-Z0-9]+\z")));
            if (listing.text.Count < 50) return result;
        }
        throw new InvalidDataException("蓝奏云文件夹过大，请为更新设置专用文件夹。");
    }

    internal static Dictionary<string, string> ParseForm(string html)
    {
        var block = Capture(html, @"data\s*:\s*\{([\s\S]*?)\}");
        var result = new Dictionary<string, string>();
        foreach (var key in new[] { "lx", "fid", "uid", "puid", "rep", "t", "k", "up", "ls" })
        {
            var raw = Capture(block, "['\"]" + key + "['\"]\\s*:\\s*([^,}]+)").Trim();
            if (raw.StartsWith("'") || raw.StartsWith("\"")) result[key] = raw.Trim('\'', '"');
            else if (Regex.IsMatch(raw, @"\A\d+\z")) result[key] = raw;
            else if (Regex.IsMatch(raw, @"\A[A-Za-z_$][\w$]*\z")) result[key] = Variable(html, raw);
            else throw new InvalidDataException("蓝奏云列表协议已变化，更新失败，请稍后重试。");
        }
        return result;
    }

    private async Task<Uri> ResolveFileAsync(LanzouFile file, CancellationToken token)
    {
        var html = await TextAsync(new Uri(folder, "/" + file.id), null, token).ConfigureAwait(false);
        var path = WebUtility.HtmlDecode(Capture(html, @"href=['""]([^'""]+)['""]\s+id=['""]downurl['""]"));
        var uri = new Uri(folder, path);
        if (uri.Host != folder.Host || !uri.AbsolutePath.StartsWith("/tp/", StringComparison.Ordinal))
            throw new InvalidDataException("蓝奏云下载入口不匹配。");
        var download = await TextAsync(uri, null, token).ConfigureAwait(false);
        var target = Variable(download, "vkjxld") + Variable(download, "hyggid") + Variable(download, "lanosso");
        if (!Uri.TryCreate(target, UriKind.Absolute, out var resolved) || !AllowedUri(resolved))
            throw new InvalidDataException("蓝奏云下载服务器不在允许范围内。");
        return resolved;
    }

    private async Task<byte[]> ReadFileAsync(LanzouFile file, long maximum, CancellationToken token)
    {
        var uri = await ResolveFileAsync(file, token).ConfigureAwait(false);
        using var response = await SendAsync(uri, null, token).ConfigureAwait(false);
        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        await CopyAsync(input, output, maximum, null, token).ConfigureAwait(false);
        return output.ToArray();
    }

    private async Task<string> TextAsync(Uri uri, Dictionary<string, string> body, CancellationToken token)
    {
        using var response = await SendAsync(uri, body, token).ConfigureAwait(false);
        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        await CopyAsync(input, output, 1024 * 1024, null, token).ConfigureAwait(false);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, Dictionary<string, string> body, CancellationToken token)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!AllowedUri(uri)) throw new InvalidDataException("蓝奏云下载跳转到未知服务器，已停止。");
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, uri);
            request.Headers.Referrer = folder;
            if (body != null) request.Content = new FormUrlEncodedContent(body);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            try
            {
                if (response.RequestMessage?.RequestUri != null && response.RequestMessage.RequestUri != uri)
                    throw new InvalidDataException("蓝奏云下载存在未检查的跳转。");
                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    if (body != null || response.Headers.Location == null) throw new InvalidDataException("蓝奏云列表返回异常跳转。");
                    uri = new Uri(uri, response.Headers.Location); response.Dispose(); continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType?.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var bytes = new MemoryStream();
                    await CopyAsync(input, bytes, 1024 * 1024, null, token).ConfigureAwait(false);
                    var data = bytes.ToArray();
                    var html = Encoding.UTF8.GetString(data);
                    var cookie = BrowserCookie(html);
                    if (cookie != null)
                    {
                        cookies.Add(new Cookie("acw_sc__v2", cookie, "/", uri.Host) { Secure = true });
                        response.Dispose(); continue;
                    }
                    response.Content.Dispose();
                    response.Content = new ByteArrayContent(data);
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
                }
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("蓝奏云网页验证或跳转未完成，更新失败，请稍后重试。");
    }

    internal bool AllowedUri(Uri uri) => uri != null && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Fragment.Length == 0 && (uri.Host == folder.Host || uri.Host == "lanrar.com" || uri.Host.EndsWith(".lanrar.com", StringComparison.OrdinalIgnoreCase));

    internal static string BrowserCookie(string html)
    {
        if (!html.Contains("acw_sc__v2")) return null;
        var match = Regex.Match(html, @"arg1\s*=\s*'([A-Fa-f0-9]{40})'");
        if (!match.Success) throw new InvalidDataException("蓝奏云网页验证协议已变化。");
        var order = new[] {15,35,29,24,33,16,1,38,10,9,19,31,40,27,22,23,25,13,6,11,39,18,20,8,14,21,32,26,2,30,7,4,17,5,3,28,34,37,12,36};
        var text = new string(order.Select(i => match.Groups[1].Value[i - 1]).ToArray());
        const string protocolKey = "3000176000856006061501533003690027800375";
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i += 2)
            result.Append((Convert.ToInt32(text.Substring(i, 2), 16) ^ Convert.ToInt32(protocolKey.Substring(i, 2), 16)).ToString("x2"));
        return result.ToString();
    }

    private static string Variable(string html, string name) => Capture(html, @"\bvar\s+" + Regex.Escape(name) + @"\s*=\s*['""]([^'""]*)['""]");
    private static string Capture(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException("蓝奏云页面格式已变化或需要网页验证，更新失败，请稍后重试。");
        return match.Groups[1].Value;
    }
    private static async Task CopyAsync(Stream source, Stream target, long maximum, Action<long> progress, CancellationToken token)
    {
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maximum) throw new InvalidDataException("蓝奏云响应大小超过允许范围。");
            await target.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
            progress?.Invoke(total);
        }
    }
    public void Dispose() => client.Dispose();
}
