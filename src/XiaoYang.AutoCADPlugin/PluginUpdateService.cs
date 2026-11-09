using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace XiaoYang.AutoCADPlugin;

internal sealed class UpdateProductProfile
{
    public string ProductId { get; }
    public string BundleName { get; }
    public string KernelPrefix { get; }
    private UpdateProductProfile(string id, string bundle, string kernel) { ProductId = id; BundleName = bundle; KernelPrefix = kernel; }
    public static readonly UpdateProductProfile AutoCAD = new("xiaoyang-autocad-wheel", "小阳CAD轮盘插件.bundle", "MrZ.XiaoYangRadialMenu.AutoCAD");
    public static readonly UpdateProductProfile ZwCAD = new("xiaoyang-zwcad-wheel", "小阳ZWCAD轮盘插件.bundle", "MrZ.XiaoYangRadialMenu.ZWCAD");
#if ZWCAD
    public static UpdateProductProfile Current => ZwCAD;
#else
    public static UpdateProductProfile Current => AutoCAD;
#endif
}

internal sealed class UpdateManifest
{
    public int SchemaVersion { get; set; }
    public string ProductId { get; set; }
    public string Version { get; set; }
    public string PackageUrl { get; set; }
    public long Size { get; set; }
    public string Sha256 { get; set; }
    public string Notes { get; set; }
}

internal sealed class UpdateProgress
{
    public string Stage { get; }
    public long BytesReceived { get; }
    public long TotalBytes { get; }
    public UpdateProgress(string stage, long received, long total) { Stage = stage; BytesReceived = received; TotalBytes = total; }
}

internal sealed class ReadyUpdate
{
    private readonly string ownedDirectory;
    public string Directory { get; }
    public string InstallerPath => Path.Combine(Directory, "install.ps1");
    public UpdateManifest Manifest { get; }
    internal ReadyUpdate(string directory, string ownedDirectory, UpdateManifest manifest) { Directory = directory; this.ownedDirectory = ownedDirectory; Manifest = manifest; }
    public void Discard() { if (System.IO.Directory.Exists(ownedDirectory)) System.IO.Directory.Delete(ownedDirectory, true); }
}

// Only downloads and validates into a new private staging directory. Never loads package code or changes a host installation.
internal sealed class PluginUpdateService : IDisposable
{
    internal const long MaximumPackageBytes = 256L * 1024 * 1024;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;
    private const int MaximumEntries = 4096;
    private readonly UpdateProductProfile profile;
    private readonly HttpClient client;
    private readonly string stagingRoot;
    private readonly IBackupUpdateSource backup;
    public bool UsedBackup { get; private set; }
    public PluginUpdateService(UpdateProductProfile profile = null, HttpMessageHandler handler = null, string stagingRoot = null, IBackupUpdateSource backup = null)
    {
        this.profile = profile ?? UpdateProductProfile.Current;
        this.backup = backup;
        this.stagingRoot = stagingRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XiaoYang", "Updates");
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }, true) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XiaoYangWheelUpdater/1.0");
    }

    public async Task<UpdateManifest> CheckAsync(Version currentVersion, CancellationToken cancellationToken, IProgress<UpdateProgress> progress = null)
    {
        if (currentVersion == null) throw new ArgumentNullException(nameof(currentVersion));
        UsedBackup = false;
        try { return await CheckPrimaryAsync(currentVersion, cancellationToken).ConfigureAwait(false); }
        catch (Exception primaryError) when (backup != null && !cancellationToken.IsCancellationRequested)
        {
            UsedBackup = true;
            progress?.Report(new UpdateProgress("主站检查失败，自动检查蓝奏云备用源…", 0, 0));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                var manifest = await backup.CheckAsync(currentVersion, timeout.Token).ConfigureAwait(false);
                ValidateManifest(manifest);
                return ParseVersion(manifest.Version) > currentVersion ? manifest : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { throw new InvalidDataException("主站检查失败：" + primaryError.Message + "\n蓝奏云备用检查失败：" + error.Message + "\n更新失败，请稍后重试。", error); }
        }
    }

    private async Task<UpdateManifest> CheckPrimaryAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        if (currentVersion == null) throw new ArgumentNullException(nameof(currentVersion));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await GetAsync(new Uri("https://mc931217.top/downloads/" + profile.ProductId + ".json"), timeout.Token).ConfigureAwait(false);
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var bytes = new MemoryStream();
            await CopyLimitedAsync(stream, bytes, 64 * 1024, null, timeout.Token).ConfigureAwait(false);
            var json = Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            RejectHtml(json);
            UpdateManifest manifest;
            try { manifest = JsonCompat.Deserialize<UpdateManifest>(json); }
            catch (Exception ex) { throw new InvalidDataException("更新清单格式无效，请稍后重试。", ex); }
            ValidateManifest(manifest);
            return ParseVersion(manifest.Version) > currentVersion ? manifest : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("检查更新超时，请检查网络后重试。"); }
    }

    public async Task<ReadyUpdate> PrepareAsync(UpdateManifest manifest, IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        if (UsedBackup) return await PrepareCoreAsync(manifest, progress, cancellationToken, true).ConfigureAwait(false);
        try { return await PrepareCoreAsync(manifest, progress, cancellationToken).ConfigureAwait(false); }
        catch (Exception primaryError) when (backup != null && !cancellationToken.IsCancellationRequested)
        {
            UsedBackup = true;
            progress?.Report(new UpdateProgress("主站下载失败，自动切换蓝奏云", 0, manifest.Size));
            try { return await PrepareCoreAsync(manifest, progress, cancellationToken, true).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { throw new InvalidDataException("主站下载失败：" + primaryError.Message + "\n蓝奏云备用下载失败：" + error.Message + "\n更新失败，请稍后重试。", error); }
        }
    }

    private async Task<ReadyUpdate> PrepareCoreAsync(UpdateManifest manifest, IProgress<UpdateProgress> progress, CancellationToken cancellationToken, bool useBackup = false)
    {
        ValidateManifest(manifest);
        // Snapshot mutable input so validation and use cannot observe different metadata.
        manifest = JsonCompat.Deserialize<UpdateManifest>(JsonCompat.Serialize(manifest));
        ValidateManifest(manifest);
        var ownedDirectory = Path.Combine(stagingRoot, profile.ProductId, manifest.Version + "-" + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFullPath(ownedDirectory).Length >= 248)
                throw new InvalidDataException("更新准备目录路径过长，无法安全兼容旧版 CAD。请将安装包下载到较短目录后手动安装。");
            Directory.CreateDirectory(ownedDirectory);
            var archivePath = Path.Combine(ownedDirectory, "package.zip");
            if (useBackup)
            {
                if (backup == null) throw new InvalidOperationException("备用更新源未配置。");
                await backup.DownloadAsync(manifest, archivePath, progress, timeout.Token).ConfigureAwait(false);
                if (new FileInfo(archivePath).Length != manifest.Size) throw new InvalidDataException("备用安装包长度与清单不一致。");
            }
            else using (var response = await GetAsync(new Uri(manifest.PackageUrl), timeout.Token).ConfigureAwait(false))
            {
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != manifest.Size)
                    throw new InvalidDataException("更新包长度与清单不一致，请重试或联系发布者。");
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var count = await CopyLimitedAsync(input, output, manifest.Size,
                    n => progress?.Report(new UpdateProgress("正在下载", n, manifest.Size)), timeout.Token).ConfigureAwait(false);
                if (count != manifest.Size) throw new InvalidDataException("更新包下载不完整，请重试。");
            }
            progress?.Report(new UpdateProgress("正在校验", manifest.Size, manifest.Size));
            using (var file = File.OpenRead(archivePath))
            {
                var prefix = new byte[512];
                var length = file.Read(prefix, 0, prefix.Length);
                RejectHtml(Encoding.UTF8.GetString(prefix, 0, length).TrimStart('\uFEFF', ' ', '\r', '\n', '\t'));
                file.Position = 0;
                using var sha = SHA256.Create();
                var hashBuffer = new byte[81920];
                int read;
                while ((read = file.Read(hashBuffer, 0, hashBuffer.Length)) > 0)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    sha.TransformBlock(hashBuffer, 0, read, hashBuffer, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var digest = BitConverter.ToString(sha.Hash).Replace("-", "");
                if (!string.Equals(digest, manifest.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包 SHA256 校验失败，已停止更新，请重新下载。");
            }
            timeout.Token.ThrowIfCancellationRequested();
            var extracted = Path.Combine(ownedDirectory, "p");
            Directory.CreateDirectory(extracted);
            progress?.Report(new UpdateProgress("正在准备安装", manifest.Size, manifest.Size));
            await Task.Run(() => ExtractSafe(archivePath, extracted, timeout.Token), timeout.Token).ConfigureAwait(false);
            var root = ValidatePackage(extracted, manifest);
            timeout.Token.ThrowIfCancellationRequested();
            File.Delete(archivePath);
            return new ReadyUpdate(root, ownedDirectory, manifest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Cleanup(ownedDirectory);
            throw new TimeoutException("下载更新超时，已取消本次准备，请重试。");
        }
        catch { Cleanup(ownedDirectory); throw; }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        try
        {
            // Automatic redirects are disabled; injected handlers must not silently redirect either.
            if (response.RequestMessage?.RequestUri != null && response.RequestMessage.RequestUri != uri)
                throw new InvalidDataException("更新服务器返回了重定向，已停止下载。");
            if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException("更新清单或安装包尚未发布，请稍后重试。");
            if (response.Content?.Headers.ContentType?.MediaType?.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidDataException("更新服务器返回了网页或人机验证页面，请稍后重试或联系管理员开放更新直链。");
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new InvalidDataException("更新服务器返回了重定向，已停止下载。");
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    private void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest == null || manifest.SchemaVersion != 1) throw new InvalidDataException("不支持此更新清单格式。");
        if (manifest.ProductId != profile.ProductId) throw new InvalidDataException("更新清单与当前 CAD 产品不匹配。");
        ParseVersion(manifest.Version);
        if (manifest.Size <= 0 || manifest.Size > MaximumPackageBytes) throw new InvalidDataException("更新包大小无效或超过限制。");
        if (!Regex.IsMatch(manifest.Sha256 ?? "", "\\A[0-9a-fA-F]{64}\\z")) throw new InvalidDataException("更新包 SHA256 信息无效。");
        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !string.Equals(url.Host, "mc931217.top", StringComparison.OrdinalIgnoreCase) || !url.IsDefaultPort ||
            url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidDataException("更新包必须使用指定服务器的 HTTPS 直链。");
        var path = Uri.UnescapeDataString(url.AbsolutePath);
        if (!path.StartsWith("/downloads/", StringComparison.Ordinal) || path.Substring(11).IndexOfAny(new[] { '/', '\\', ':', '\0', '%', '?', '#' }) >= 0 || path.Any(char.IsControl) ||
            !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.Substring(11).Length <= 4)
            throw new InvalidDataException("更新包直链必须指向 downloads 中的单个 ZIP 文件。");
    }

    internal static Version ParseVersion(string text)
    {
        if (!Regex.IsMatch(text ?? "", "\\A(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\z") || !Version.TryParse(text, out var version))
            throw new InvalidDataException("更新版本必须为有效的四段版本号。");
        return version;
    }

    private static void RejectHtml(string text)
    {
        if (text.StartsWith("<", StringComparison.Ordinal)) throw new InvalidDataException("更新服务器返回了网页或人机验证页面，请稍后重试或联系管理员开放更新直链。");
    }

    private static async Task<long> CopyLimitedAsync(Stream input, Stream output, long maximum, Action<long> progress, CancellationToken token)
    {
        var buffer = new byte[81920];
        long count = 0;
        // Some legacy HTTP streams do not promptly honour ReadAsync cancellation; disposal interrupts a stalled read.
        using var registration = token.Register(() => input.Dispose());
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
            {
                count += read;
                if (count > maximum) throw new InvalidDataException("服务器数据超过允许大小，已停止下载。");
                await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                progress?.Invoke(count);
            }
            token.ThrowIfCancellationRequested();
            return count;
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
    }

    private static void ExtractSafe(string archivePath, string destination, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries) throw new InvalidDataException("安装包文件数量无效。");
        // Release ZIPs have one decorative package-name directory. Strip only a common,
        // unambiguous wrapper to avoid spending the legacy Windows path budget on it.
        var normalizedNames = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
        var firstSlash = normalizedNames[0].IndexOf('/');
        var wrapper = firstSlash > 0 ? normalizedNames[0].Substring(0, firstSlash + 1) : null;
        if (wrapper != null && (!normalizedNames.All(n => n.StartsWith(wrapper, StringComparison.Ordinal)) ||
            !normalizedNames.Contains(wrapper + "install.ps1", StringComparer.OrdinalIgnoreCase))) wrapper = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            var parts = name.TrimEnd('/').Split('/');
            if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ") ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(p, "\\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\\.|$)", RegexOptions.IgnoreCase)))
                throw new InvalidDataException("安装包含有不安全的文件路径。");
            if (!seen.Add(string.Join("/", parts))) throw new InvalidDataException("安装包含有重复文件路径。");
            if (parts.Any(p => p.Equals("Data", StringComparison.OrdinalIgnoreCase) || p.Equals("Logs", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("更新包不得包含用户 Data 或 Logs 数据。");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if ((unixType != 0 && unixType != 0x8000 && unixType != 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("安装包含有链接或特殊文件。");
            total = checked(total + entry.Length);
            if (total > MaximumExpandedBytes || entry.Length > MaximumPackageBytes) throw new InvalidDataException("安装包解压大小超过限制。");
            if (wrapper != null)
            {
                name = name.Substring(wrapper.Length);
                if (name.Length == 0) continue;
                parts = name.TrimEnd('/').Split('/');
            }
            var target = Path.GetFullPath(Path.Combine(destination, Path.Combine(parts)));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包路径越界。");
            // CAD's .NET Framework process may not opt in to long paths even on a modern OS.
            // Check explicitly instead of surfacing its misleading DirectoryNotFoundException.
            if (target.Length >= 260 || Path.GetDirectoryName(target).Length >= 248 || (name.EndsWith("/") && target.Length >= 248))
                throw new InvalidDataException("更新准备目录路径过长，无法安全兼容旧版 CAD。请将安装包下载到较短目录后手动安装。");
            if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using var source = entry.Open();
            using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                written += read;
                if (written > entry.Length) throw new InvalidDataException("安装包文件解压长度不符。");
                file.Write(buffer, 0, read);
            }
            if (written != entry.Length) throw new InvalidDataException("安装包文件不完整。");
        }
    }

    private string ValidatePackage(string extracted, UpdateManifest manifest)
    {
        var installers = Directory.GetFiles(extracted, "install.ps1", SearchOption.AllDirectories);
        if (installers.Length != 1) throw new InvalidDataException("安装包必须包含唯一的 install.ps1。");
        var root = Path.GetDirectoryName(installers[0]);
        var bundle = Path.Combine(root, profile.BundleName);
        var bundles = Directory.GetDirectories(extracted, "*.bundle", SearchOption.AllDirectories);
        if (!Directory.Exists(bundle) || bundles.Length != 1 || !string.Equals(bundles[0], bundle, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包与当前 CAD 产品不匹配。");
        if (!File.Exists(Path.Combine(root, "installer-compat.ps1"))) throw new InvalidDataException("安装包缺少兼容安装脚本。");
        var version = ParseVersion(manifest.Version);
        ValidateContactResources(bundle, version);
        if (profile == UpdateProductProfile.AutoCAD)
        {
            var xml = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(Path.Combine(bundle, "PackageContents.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            xml.Load(reader);
            var package = xml.DocumentElement;
            if (package?.Name != "ApplicationPackage" || package.GetAttribute("AutodeskProduct") != "AutoCAD" || ParseVersion(package.GetAttribute("AppVersion")) != version)
                throw new InvalidDataException("安装包产品或版本与更新清单不一致。");
            if (!File.Exists(Path.Combine(root, "installer-zh-CN.json"))) throw new InvalidDataException("安装包缺少安装语言文件。");
        }
        var kernels = Directory.GetFiles(bundle, "MrZ.XiaoYangRadialMenu.*.dll", SearchOption.AllDirectories);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (profile == UpdateProductProfile.AutoCAD)
        {
            if (version >= new Version(2, 2, 0, 84))
            {
                foreach (var year in new[] { "2010", "2011", "2012", "2013" })
                    expected.Add(Path.Combine(bundle, "Contents", year != "2013" ? "NetFramework35" : "NetFramework48", profile.KernelPrefix + year + ".dll"));
                var legacy = Path.Combine(bundle, "Contents", "NetFramework35");
                var helper = Path.Combine(legacy, "XYCAD.LegacyUpdater.exe");
                if (!File.Exists(Path.Combine(legacy, "Theraot.Core.dll")) || !File.Exists(helper) || !File.Exists(helper + ".config") ||
                    AssemblyName.GetAssemblyName(helper).Version != version)
                    throw new InvalidDataException("安装包旧版 CAD 运行组件缺失或版本不正确。");
            }
            foreach (var year in new[] { "2014", "2015", "2016", "2017", "2018", "2019", "2020", "2021-2024", "2025", "2026", "2027" })
            {
                if (year == "2027" && version < new Version(2, 2, 0, 80)) continue;
                expected.Add(Path.Combine(bundle, "Contents", year == "2025" || year == "2026" ? "Net8" : year == "2027" ? "Net10" : "NetFramework48", profile.KernelPrefix + year + ".dll"));
            }
        }
        else
        {
            for (var year = 2020; year <= 2026; year++)
                expected.Add(Path.Combine(bundle, "Contents", "NetFramework48", "ZWCAD" + year, profile.KernelPrefix + year + ".dll"));
        }
        if (kernels.Length != expected.Count || kernels.Any(k => !expected.Contains(k)))
            throw new InvalidDataException("安装包宿主内核缺失、混用或目录不正确。");
        if (profile == UpdateProductProfile.AutoCAD)
        {
            var xml = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(Path.Combine(bundle, "PackageContents.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            xml.Load(reader);
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode entry in xml.SelectNodes("/ApplicationPackage/Components/ComponentEntry"))
            {
                var module = entry.Attributes?["ModuleName"]?.Value;
                if (string.IsNullOrWhiteSpace(module)) throw new InvalidDataException("安装包宿主模块引用缺失。");
                var path = Path.GetFullPath(Path.Combine(bundle, module.Replace('/', Path.DirectorySeparatorChar)));
                if (!expected.Contains(path) || !referenced.Add(path)) throw new InvalidDataException("安装包宿主模块引用不正确。");
            }
            if (!referenced.SetEquals(expected)) throw new InvalidDataException("安装包宿主模块引用不完整。");
        }
        foreach (var dll in Directory.GetFiles(extracted, "*.dll", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(dll);
            if (Regex.IsMatch(name, "\\A(?:acmgd|acdbmgd|accoremgd|AdWindows|AcWindows|ZwManaged|ZwDatabaseMgd|ZwWindows)\\.dll\\z", RegexOptions.IgnoreCase))
                throw new InvalidDataException("更新包不得分发 CAD 宿主 API DLL。");
        }
        foreach (var kernel in kernels)
        {
            var name = Path.GetFileNameWithoutExtension(kernel);
            var assembly = AssemblyName.GetAssemblyName(kernel);
            if (assembly.Name != name || assembly.Version != version) throw new InvalidDataException("轮盘内核版本与更新清单不一致。");
        }
        return root;
    }

    internal static void ValidateContactResources(string bundle, Version version)
    {
        // Earlier releases embed the images in each kernel. Shared resources are mandatory from .83.
        if (version < new Version(2, 2, 0, 83)) return;
        foreach (var name in new[] { "抖音.png", "加我.png" })
        {
            var path = Path.Combine(bundle, "Contents", "Resources", "Contact", name);
            if (!File.Exists(path)) throw new InvalidDataException("安装包缺少共享联系图片：" + name);
            using var stream = File.OpenRead(path);
            var header = new byte[24];
            if (stream.Length < 33 || stream.Read(header, 0, header.Length) != header.Length ||
                !header.Take(16).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 }) ||
                header[16] > 127 || header[20] > 127 ||
                !header.Skip(16).Take(4).Any(b => b != 0) || !header.Skip(20).Take(4).Any(b => b != 0))
                throw new InvalidDataException("安装包共享联系图片 PNG 头无效：" + name);
        }
    }

    private static void Cleanup(string ownedDirectory)
    {
        // This path is a newly generated per-attempt directory, never the installed bundle or user data.
        if (Directory.Exists(ownedDirectory)) Directory.Delete(ownedDirectory, true);
    }
    public void Dispose() { client.Dispose(); backup?.Dispose(); }
}
