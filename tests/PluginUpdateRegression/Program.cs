using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using XiaoYang.AutoCADPlugin;

internal static class Program
{
    private static int assertions;
    private static int cases;
    private static string root;
    private static string stage;
    private static readonly List<string> failures = new();
    private static readonly Dictionary<string, string> protectedFiles = new(StringComparer.OrdinalIgnoreCase);
    private static byte[] acad;
    private static byte[] zwcad;

    private static async Task<int> Main(string[] args)
    {
        var repo = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        root = Path.Combine(repo, "work", "one-click-update-71", "test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        stage = Path.Combine(root, "staging");
        Directory.CreateDirectory(stage);
        var fixtureDirectory = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(repo, "dist");
        acad = File.ReadAllBytes(Path.Combine(fixtureDirectory, "小阳CAD轮盘插件-2.2.0.70.zip"));
        zwcad = File.ReadAllBytes(Path.Combine(fixtureDirectory, "小阳ZWCAD轮盘插件-2.2.0.70.zip"));
        foreach (var relative in new[] { "user/Data/config.json", "user/Data/aliases.pgp", "user/Logs/prior.log", "staging/unrelated.txt" })
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "preserve " + relative, Encoding.UTF8);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            protectedFiles[path] = Hash(File.ReadAllBytes(path));
        }

        await ContactResourceCases(repo);

        await Case("AutoCAD newer manifest and correct product endpoint", async () =>
        {
            using var service = Service(UpdateProductProfile.AutoCAD, Json(Manifest(acad)), request => Equal(request.RequestUri.AbsolutePath, "/downloads/xiaoyang-autocad-wheel.json"));
            Equal((await service.CheckAsync(new Version("2.2.0.69"), default)).Version, "2.2.0.70");
        });
        await Case("ZWCAD newer manifest and separate product endpoint", async () =>
        {
            using var service = Service(UpdateProductProfile.ZwCAD, Json(Manifest(zwcad, UpdateProductProfile.ZwCAD)), request => Equal(request.RequestUri.AbsolutePath, "/downloads/xiaoyang-zwcad-wheel.json"));
            Equal((await service.CheckAsync(new Version("2.2.0.69"), default)).ProductId, UpdateProductProfile.ZwCAD.ProductId);
        });
        foreach (var version in new[] { "2.2.0.70", "2.2.0.71", "2.2.1.0", "3.0.0.0" })
            await Case("no downgrade for current " + version, async () =>
            {
                using var service = Service(UpdateProductProfile.AutoCAD, Json(Manifest(acad)));
                Check(await service.CheckAsync(new Version(version), default) == null, "must report current/latest");
            });
        foreach (var version in new[] { "2.2.0", "2.2.0.70.1", "2.02.0.70", "-1.2.0.70", "2.2.0.2147483648", "2.2.0.70-beta", " 2.2.0.70", "2.2.0.70\n" })
            await ManifestFailure("invalid four-part version " + version, m => m.Version = version);
        await Case("numeric fourth segment ordering", () => { Check(PluginUpdateService.ParseVersion("2.2.0.100") > PluginUpdateService.ParseVersion("2.2.0.99"), "numeric version ordering"); return Task.CompletedTask; });
        await ManifestFailure("wrong manifest product", m => m.ProductId = UpdateProductProfile.ZwCAD.ProductId);
        await ManifestFailure("unsupported manifest schema", m => m.SchemaVersion = 2);
        await ManifestFailure("zero size", m => m.Size = 0);
        await ManifestFailure("oversize", m => m.Size = PluginUpdateService.MaximumPackageBytes + 1);
        await ManifestFailure("invalid digest", m => m.Sha256 = "bad");
        foreach (var url in new[] { "http://mc931217.top/downloads/x.zip", "https://example.org/downloads/x.zip", "https://mc931217.top:444/downloads/x.zip", "https://user:pass@mc931217.top/downloads/x.zip", "https://mc931217.top/downloads/x.zip?q=1", "https://mc931217.top/downloads/x.zip#x", "https://mc931217.top/downloads/sub/x.zip", "https://mc931217.top/downloads/%2e%2e/x.zip", "https://mc931217.top/downloads/x.exe" })
            await ManifestFailure("reject non-direct URL " + url, m => m.PackageUrl = url);
        await CheckFailure("manifest HTML header", () => Response(Encoding.UTF8.GetBytes("<html>challenge</html>"), "text/html"), typeof(InvalidDataException), "网页");
        await CheckFailure("manifest HTML sniffing", () => Response(Encoding.UTF8.GetBytes("\uFEFF  <!DOCTYPE html><html>challenge</html>")), typeof(InvalidDataException), "网页");
        await CheckFailure("manifest malformed JSON", () => Response(Encoding.UTF8.GetBytes("not json")), typeof(InvalidDataException));
        await CheckFailure("manifest larger than 64KB", () => Response(new byte[65537]), typeof(InvalidDataException));
        await CheckFailure("manifest 404", () => Response(Array.Empty<byte>(), status: HttpStatusCode.NotFound), typeof(InvalidDataException), "尚未发布");
        await CheckFailure("manifest 404 HTML reports unpublished", () => Response(Encoding.UTF8.GetBytes("<html>Not Found</html>"), "text/html", HttpStatusCode.NotFound), typeof(InvalidDataException), "尚未发布");
        await CheckFailure("manifest 500", () => Response(Array.Empty<byte>(), status: HttpStatusCode.InternalServerError), typeof(HttpRequestException));
        await CheckFailure("manifest redirect", () => Response(Array.Empty<byte>(), status: HttpStatusCode.Redirect), typeof(InvalidDataException), "重定向");
        await Case("network failure propagated", async () =>
        {
            using var service = new PluginUpdateService(UpdateProductProfile.AutoCAD, new Handler((_, _) => throw new HttpRequestException("offline test")), stage);
            await Throws(() => service.CheckAsync(new Version("2.2.0.69"), default), typeof(HttpRequestException));
        });
        await Case("cancel checking manifest", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var service = new PluginUpdateService(UpdateProductProfile.AutoCAD, new Handler((_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(Response(Json(Manifest(acad)))); }), stage);
            await Throws(() => service.CheckAsync(new Version("2.2.0.69"), cancellation.Token), typeof(OperationCanceledException));
        });
        await PrepareFailure("package 404 cleans staging", acad, () => Response(Array.Empty<byte>(), status: HttpStatusCode.NotFound));
        await PrepareFailure("package HTML header cleans staging", acad, () => Response(acad, "text/html"));
        var html = Encoding.UTF8.GetBytes("\uFEFF  <html>Cloudflare verification</html>");
        await PrepareFailure("package HTML sniffing cleans staging", html, () => Response(html));
        await PrepareFailure("package length header mismatch", acad, () => { var response = Response(acad); response.Content.Headers.ContentLength = acad.Length - 1; return response; });
        await PrepareFailure("truncated package stream", acad, () => { var response = Response(acad.Take(acad.Length - 100).ToArray()); response.Content.Headers.ContentLength = acad.Length; return response; });
        await PrepareFailure("download exceeds manifest size", acad, () => { var response = Response(acad.Concat(new byte[10]).ToArray()); response.Content.Headers.ContentLength = acad.Length; return response; });
        await PrepareFailure("SHA256 mismatch", acad, () => Response(acad), m => m.Sha256 = new string('0', 64), "SHA256");
        await PrepareFailure("wrong ZWCAD archive under AutoCAD manifest", zwcad, () => Response(zwcad), message: "产品");
        await PrepareFailure("wrong AutoCAD archive under ZWCAD manifest", acad, () => Response(acad), profile: UpdateProductProfile.ZwCAD);
        await PrepareFailure("kernel or XML version mismatch", acad, () => Response(acad), m => m.Version = "2.2.0.71");
        foreach (var profile in new[] { UpdateProductProfile.AutoCAD, UpdateProductProfile.ZwCAD })
        {
            var original = profile == UpdateProductProfile.AutoCAD ? acad : zwcad;
            var missingKernel = RewriteZip(original, name => !name.EndsWith("2014.dll") && !name.EndsWith("ZWCAD2020.dll"));
            await PrepareFailure("missing supported kernel " + profile.ProductId, missingKernel, () => Response(missingKernel), profile: profile, message: "内核");
            var hostDll = RewriteZip(original, _ => true, "acmgd.dll");
            await PrepareFailure("forbidden host API DLL " + profile.ProductId, hostDll, () => Response(hostDll), profile: profile, message: "宿主 API");
            var noInstaller = RewriteZip(original, name => !name.EndsWith("installer-compat.ps1"));
            await PrepareFailure("missing compatibility installer " + profile.ProductId, noInstaller, () => Response(noInstaller), profile: profile, message: "兼容");
        }
        await PrepareFailure("non ZIP payload", Encoding.UTF8.GetBytes("not a zip"), () => Response(Encoding.UTF8.GetBytes("not a zip")));
        foreach (var entry in new[] { "../escape.txt", "/absolute.txt", "C:/escape.txt", "a/../../escape.txt", "a\\..\\escape.txt", "dir/NUL.txt", "dir/trailing./x", "dir/trailing /x", "dir/file:stream", "Data/settings.json", "x/dAtA/user.json", "Logs/log.txt", "x/lOgS/old.txt" })
        {
            var bytes = Zip(new[] { entry });
            await PrepareFailure("unsafe ZIP entry " + entry, bytes, () => Response(bytes));
        }
        var duplicate = Zip(new[] { "one.txt", "ONE.txt" });
        await PrepareFailure("case insensitive duplicate ZIP", duplicate, () => Response(duplicate), message: "重复");
        var symbolic = Zip(new[] { "symbolic" }, symlink: true);
        await PrepareFailure("symbolic link ZIP", symbolic, () => Response(symbolic), message: "链接");
        var empty = Zip(Array.Empty<string>());
        await PrepareFailure("empty ZIP", empty, () => Response(empty));
        var many = Zip(Enumerable.Range(0, 4097).Select(n => n + ".txt").ToArray());
        await PrepareFailure("excessive ZIP entries", many, () => Response(many));
        await Case("cancel download cleans only owned staging", async () =>
        {
            var before = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
            using var cancellation = new CancellationTokenSource();
            using var service = Service(UpdateProductProfile.AutoCAD, acad);
            await Throws(() => service.PrepareAsync(Manifest(acad), new InlineProgress(p => { if (p.Stage == "正在下载") cancellation.Cancel(); }), cancellation.Token), typeof(OperationCanceledException));
            Equal(string.Join("|", Directory.GetFiles(stage, "*", SearchOption.AllDirectories)), string.Join("|", before));
        });
        await Case("cancel before preparation creates no files", async () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            using var service = Service(UpdateProductProfile.AutoCAD, acad);
            await Throws(() => service.PrepareAsync(Manifest(acad), null, cancellation.Token), typeof(OperationCanceledException));
        });
        await Case("long staging path fails clearly and cleans up", async () =>
        {
            var longStage = Path.Combine(stage, new string('a', 80));
            using var service = new PluginUpdateService(UpdateProductProfile.AutoCAD, new Handler((_, _) => Task.FromResult(Response(acad))), longStage);
            await Throws(() => service.PrepareAsync(Manifest(acad), null, default), typeof(InvalidDataException), "路径过长");
            Check(!Directory.Exists(longStage) || !Directory.GetFiles(longStage, "*", SearchOption.AllDirectories).Any(), "long path failure leaves no files");
        });
        await RealPackage(UpdateProductProfile.AutoCAD, acad, "EF1C7770BC5217053472DBBDD7C7FA5AD3D957A9D05F2882478E2FABD89E12C6", 10);
        await RealPackage(UpdateProductProfile.ZwCAD, zwcad, "876FB71B7594E2AED707CF9AFB347BD99D045F2D79F987CE2A340B3020575E96", 7);
        if (args.Length > 2) await SharedResourcePackages(repo, Path.GetFullPath(args[2]));
        await Case("protected configs aliases logs unchanged", () => { foreach (var file in protectedFiles) { Equal(Hash(File.ReadAllBytes(file.Key)), file.Value); Check((File.GetAttributes(file.Key) & FileAttributes.ReadOnly) != 0, "read-only preserved"); } return Task.CompletedTask; });
        Console.WriteLine($"RESULT cases={cases} assertions={assertions} failures={failures.Count}; framework={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; artifacts={root}");
        foreach (var failure in failures) Console.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task RealPackage(UpdateProductProfile profile, byte[] bytes, string expectedHash, int kernelCount)
    {
        await Case("real 2.2.0.70 package " + profile.ProductId, async () =>
        {
            Equal(Hash(bytes), expectedHash);
            var progress = new List<UpdateProgress>();
            using var service = Service(profile, bytes);
            var ready = await service.PrepareAsync(Manifest(bytes, profile), new InlineProgress(p => progress.Add(p)), default);
            Check(File.Exists(ready.InstallerPath), "installer extracted");
            Equal(ready.Manifest.Version, "2.2.0.70");
            Equal(Directory.GetFiles(ready.Directory, "MrZ.XiaoYangRadialMenu.*.dll", SearchOption.AllDirectories).Length, kernelCount);
            Check(progress.Any(p => p.Stage == "正在下载" && p.BytesReceived == bytes.Length), "full download progress");
            Check(progress.Any(p => p.Stage == "正在校验"), "verify progress");
            Check(progress.Any(p => p.Stage == "正在准备安装"), "extraction progress");
            var extractRoot = ready.Directory;
            while (!new[] { "payload", "p" }.Contains(Path.GetFileName(extractRoot))) extractRoot = Path.GetDirectoryName(extractRoot) ?? throw new Exception("extraction root not found");
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var count = 0;
            foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith("/")))
            {
                var target = Path.Combine(extractRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                using var stream = entry.Open(); using var content = new MemoryStream(); stream.CopyTo(content);
                Equal(Hash(File.ReadAllBytes(target)), Hash(content.ToArray())); count++;
            }
            Equal(Directory.GetFiles(extractRoot, "*", SearchOption.AllDirectories).Length, count);
            Check(!Directory.GetFiles(Path.GetDirectoryName(extractRoot), "package.zip", SearchOption.AllDirectories).Any(), "download archive removed after validation");
            ready.Discard();
            Check(!Directory.Exists(extractRoot), "discard removes validated preparation");
        });
    }
    private static async Task ContactResourceCases(string repo)
    {
        var bundle = Path.Combine(root, "contact-resources.bundle");
        var contact = Path.Combine(bundle, "Contents", "Resources", "Contact");
        foreach (var version in new[] { "2.2.0.70", "2.2.0.82" })
            await Case("embedded contact resources remain compatible " + version, () =>
            {
                PluginUpdateService.ValidateContactResources(bundle, new Version(version));
                return Task.CompletedTask;
            });
        foreach (var profile in new[] { UpdateProductProfile.AutoCAD, UpdateProductProfile.ZwCAD })
        {
            var bytes = profile == UpdateProductProfile.AutoCAD ? acad : zwcad;
            await PrepareFailure("shared contact resources required before .83 preparation " + profile.ProductId,
                bytes, () => Response(bytes), m => m.Version = "2.2.0.83", "共享联系图片", profile);
        }
        Directory.CreateDirectory(contact);
        foreach (var name in new[] { "抖音.png", "加我.png" })
            File.Copy(Path.Combine(repo, "src", "XiaoYang.AutoCADPlugin", "Resources", name), Path.Combine(contact, name));
        await Case("original shared PNG headers accepted in .83", () =>
        {
            PluginUpdateService.ValidateContactResources(bundle, new Version("2.2.0.83"));
            return Task.CompletedTask;
        });
        foreach (var name in new[] { "抖音.png", "加我.png" })
        {
            var path = Path.Combine(contact, name);
            var original = File.ReadAllBytes(path);
            await Case("missing shared contact image rejected " + name, async () =>
            {
                File.Delete(path);
                try { await Throws(() => { PluginUpdateService.ValidateContactResources(bundle, new Version("2.2.0.83")); return Task.CompletedTask; }, typeof(InvalidDataException), "共享联系图片"); }
                finally { File.WriteAllBytes(path, original); }
            });
            foreach (var defect in new[] { "signature", "truncated", "zero width" })
                await Case("invalid shared PNG rejected " + name + " " + defect, async () =>
                {
                    var changed = (byte[])original.Clone();
                    if (defect == "signature") changed[0] = 0;
                    if (defect == "truncated") changed = changed.Take(24).ToArray();
                    if (defect == "zero width") Array.Clear(changed, 16, 4);
                    File.WriteAllBytes(path, changed);
                    try { await Throws(() => { PluginUpdateService.ValidateContactResources(bundle, new Version("2.2.0.83")); return Task.CompletedTask; }, typeof(InvalidDataException), "PNG"); }
                    finally { File.WriteAllBytes(path, original); }
                });
        }
    }
    private static async Task SharedResourcePackages(string repo, string releaseDirectory)
    {
        foreach (var profile in new[] { UpdateProductProfile.AutoCAD, UpdateProductProfile.ZwCAD })
        {
            var path = Path.Combine(releaseDirectory, profile.BundleName.Replace(".bundle", "") + "-2.2.0.83.zip");
            var bytes = File.ReadAllBytes(path);
            await Case("real .83 shared resource package " + profile.ProductId, async () =>
            {
                var manifest = Manifest(bytes, profile); manifest.Version = "2.2.0.83";
                using var service = Service(profile, bytes);
                var ready = await service.PrepareAsync(manifest, null, default);
                try
                {
                    var bundle = Path.Combine(ready.Directory, profile.BundleName);
                    Equal(Directory.GetFiles(bundle, "MrZ.XiaoYangRadialMenu.*.dll", SearchOption.AllDirectories).Length, profile == UpdateProductProfile.AutoCAD ? 11 : 7);
                    Check(!Directory.GetFiles(ready.Directory, "*.pdb", SearchOption.AllDirectories).Any(), "release contains no PDBs");
                    foreach (var name in new[] { "抖音.png", "加我.png" })
                        Equal(Hash(File.ReadAllBytes(Path.Combine(bundle, "Contents", "Resources", "Contact", name))), Hash(File.ReadAllBytes(Path.Combine(repo, "src", "XiaoYang.AutoCADPlugin", "Resources", name))));
                }
                finally { ready.Discard(); }
            });
            foreach (var name in new[] { "抖音.png", "加我.png" })
            {
                var suffix = "/Contents/Resources/Contact/" + name;
                var missing = RewriteZip(bytes, n => !n.Replace('\\', '/').EndsWith(suffix, StringComparison.Ordinal));
                await PrepareFailure("real .83 missing shared PNG rejected " + profile.ProductId + " " + name, missing, () => Response(missing), m => m.Version = "2.2.0.83", "共享联系图片", profile);
            }
        }
    }
    private static async Task ManifestFailure(string label, Action<UpdateManifest> edit)
    {
        var manifest = Manifest(acad); edit(manifest);
        await CheckFailure(label, () => Response(Json(manifest)), typeof(InvalidDataException));
    }
    private static async Task CheckFailure(string label, Func<HttpResponseMessage> response, Type type, string message = null)
    {
        await Case(label, async () => { using var service = new PluginUpdateService(UpdateProductProfile.AutoCAD, new Handler((_, _) => Task.FromResult(response())), stage); await Throws(() => service.CheckAsync(new Version("2.2.0.69"), default), type, message); });
    }
    private static async Task PrepareFailure(string label, byte[] bytes, Func<HttpResponseMessage> response, Action<UpdateManifest> edit = null, string message = null, UpdateProductProfile profile = null)
    {
        await Case(label, async () =>
        {
            profile ??= UpdateProductProfile.AutoCAD;
            var manifest = Manifest(bytes, profile); edit?.Invoke(manifest);
            var before = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            using var service = new PluginUpdateService(profile, new Handler((_, _) => Task.FromResult(response())), stage);
            await Throws(() => service.PrepareAsync(manifest, null, default), typeof(Exception), message);
            Equal(string.Join("|", Directory.GetFiles(stage, "*", SearchOption.AllDirectories).OrderBy(p => p)), string.Join("|", before));
        });
    }
    private static async Task Case(string label, Func<Task> action)
    {
        cases++;
        try { await action(); Console.WriteLine("PASS " + label); }
        catch (Exception ex) { failures.Add("FAIL " + label + ": " + ex); Console.WriteLine("FAIL " + label + ": " + ex.Message); }
    }
    private static async Task Throws(Func<Task> action, Type type, string message = null)
    {
        Exception caught = null;
        try { await action(); } catch (Exception ex) { caught = ex; }
        Check(caught != null, "expected exception " + type.Name);
        Check(type.IsAssignableFrom(caught.GetType()), "expected " + type.Name + ", got " + caught.GetType().Name);
        if (message != null) Check(caught.Message.Contains(message), "expected message " + message + ", got " + caught.Message);
    }
    private static UpdateManifest Manifest(byte[] bytes, UpdateProductProfile profile = null) => new() { SchemaVersion = 1, ProductId = (profile ?? UpdateProductProfile.AutoCAD).ProductId, Version = "2.2.0.70", PackageUrl = "https://mc931217.top/downloads/test-package.zip", Sha256 = Hash(bytes), Size = bytes.Length, Notes = "offline fixture" };
    private static byte[] Json(UpdateManifest manifest) => Encoding.UTF8.GetBytes(JsonCompat.Serialize(manifest));
    private static PluginUpdateService Service(UpdateProductProfile profile, byte[] bytes, Action<HttpRequestMessage> inspect = null) => new(profile, new Handler((request, _) => { inspect?.Invoke(request); return Task.FromResult(Response(bytes)); }), stage);
    private static HttpResponseMessage Response(byte[] bytes, string mediaType = "application/octet-stream", HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }
    private static byte[] Zip(string[] paths, bool symlink = false)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var path in paths) { var entry = zip.CreateEntry(path); if (symlink) entry.ExternalAttributes = unchecked((int)0xa1ff0000); using var writer = new StreamWriter(entry.Open()); writer.Write("fixture"); }
        return stream.ToArray();
    }
    private static byte[] RewriteZip(byte[] original, Func<string, bool> keep, string addedPath = null)
    {
        using var result = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read))
        using (var target = new ZipArchive(result, ZipArchiveMode.Create, true))
        {
            foreach (var entry in source.Entries.Where(e => keep(e.FullName)))
            {
                var copy = target.CreateEntry(entry.FullName);
                using var input = entry.Open(); using var output = copy.Open(); input.CopyTo(output);
            }
            if (addedPath != null) { using var output = new StreamWriter(target.CreateEntry(addedPath).Open()); output.Write("forbidden fixture"); }
        }
        return result.ToArray();
    }
    private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static void Equal<T>(T actual, T expected) => Check(Equals(actual, expected), $"Expected {expected}; actual {actual}");
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action;
        public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) => this.action = action;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
    private sealed class InlineProgress : IProgress<UpdateProgress>
    {
        private readonly Action<UpdateProgress> action;
        public InlineProgress(Action<UpdateProgress> action) => this.action = action;
        public void Report(UpdateProgress value) => action(value);
    }
}
