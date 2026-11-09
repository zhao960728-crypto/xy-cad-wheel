using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using XiaoYang.AutoCADPlugin;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    private static async Task Reject(Func<Task> action, string name) { try { await action(); } catch { checks++; return; } throw new Exception("accepted: " + name); }
    private static async Task<int> Main(string[] args)
    {
        var repo = Path.GetFullPath(args[0]);
        var root = Path.Combine(repo, "work", "lanzou-backup-74", "regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var profile in new[] { UpdateProductProfile.AutoCAD, UpdateProductProfile.ZwCAD })
        {
            var product = profile.BundleName.Replace(".bundle", "");
            var path = Path.Combine(repo, "dist", product + "-2.2.0.73.zip");
            var bytes = File.ReadAllBytes(path);
            using var sha = SHA256.Create();
            var manifest = new UpdateManifest { SchemaVersion = 1, ProductId = profile.ProductId, Version = "2.2.0.73", Size = bytes.Length,
                Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""), PackageUrl = "https://mc931217.top/downloads/" + Uri.EscapeDataString(Path.GetFileName(path)) };
            var before = File.GetLastWriteTimeUtc(path);
            var old = new Version("2.2.0.72");
            var sourceSettings = new BackupUpdateSettings { FolderUrl = "https://wwapt.lanzoum.com/b01gibcr6d", ShareCode = "fixture-code" };
            var api = new Handler((request, token) =>
            {
                token.ThrowIfCancellationRequested();
                var uriPath = request.RequestUri.AbsolutePath;
                string html;
                if (uriPath == "/b01gibcr6d") html = "var a='clock'; var b='key'; url:'/filemoreajax.php?file=123',data:{'lx':2,'fid':123,'uid':'456','puid':'p','rep':'0','t':a,'k':b,'up':1,'ls':1,'pwd':pwd}";
                else if (uriPath == "/filemoreajax.php") html = "{\"zt\":1,\"text\":[{\"t\":0,\"id\":\"fm\",\"name_all\":\"" + profile.ProductId + "-2.2.0.73.update.txt\"},{\"t\":0,\"id\":\"fz\",\"name_all\":\"" + Path.GetFileName(path) + "\"}]}";
                else if (uriPath == "/fm" || uriPath == "/fz") html = "<a href='/tp" + uriPath + "' id='downurl'>download</a>";
                else if (uriPath == "/tp/fm" || uriPath == "/tp/fz") html = "var vkjxld='https://developer2.lanrar.com/file/';var hyggid='" + uriPath.Substring(4) + "';var lanosso='';";
                else if (uriPath == "/file/fm") html = JsonCompat.Serialize(manifest);
                else if (uriPath == "/file/fz") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
                else throw new Exception("unexpected protocol request " + uriPath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, uriPath == "/file/fm" || uriPath == "/filemoreajax.php" ? "application/json" : "text/html") });
            });
            using (var service = new PluginUpdateService(profile, Offline(), root, new LanzouUpdateSource(profile, sourceSettings, api)))
            {
                var found = await service.CheckAsync(old, default);
                var ready = await service.PrepareAsync(found, null, default);
                Check(File.Exists(ready.InstallerPath) && api.Calls == 8, "folder listing, sidecar and package protocol complete automatically");
                ready.Discard();
            }
            var localManifest = Path.Combine(root, profile.ProductId + ".json");
            File.WriteAllText(localManifest, JsonCompat.Serialize(manifest));
            var primary = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonCompat.Serialize(manifest)) }));
            var backup = new Backup(manifest, bytes);
            using (var service = new PluginUpdateService(profile, primary, root, backup))
            {
                Check((await service.CheckAsync(old, default)).Version == manifest.Version && backup.Checks == 0, "healthy primary does not query backup");
            }
            primary = Offline(); backup = new Backup(manifest, bytes);
            using (var service = new PluginUpdateService(profile, primary, root, backup))
            {
                var found = await service.CheckAsync(old, default);
                Check(service.UsedBackup && backup.Checks == 1, "check automatically fails over");
                var ready = await service.PrepareAsync(found, null, default);
                Check(backup.Downloads == 1 && primary.Calls == 1 && File.Exists(ready.InstallerPath), "backup check proceeds directly to validated backup package");
                ready.Discard();
            }
            primary = Offline(); backup = new Backup(manifest, bytes);
            using (var service = new PluginUpdateService(profile, primary, root, backup))
            {
                var ready = await service.PrepareAsync(manifest, null, default);
                Check(service.UsedBackup && backup.Checks == 0 && backup.Downloads == 1, "download failure automatically switches preserving original manifest");
                ready.Discard();
            }
            backup = new Backup(manifest, bytes.Take(bytes.Length - 1).ToArray());
            using (var service = new PluginUpdateService(profile, Offline(), root, backup))
                await Reject(() => service.PrepareAsync(manifest, null, default), "truncated backup");
            var changed = bytes.ToArray(); changed[changed.Length / 2] ^= 1;
            using (var service = new PluginUpdateService(profile, Offline(), root, new Backup(manifest, changed)))
                await Reject(() => service.PrepareAsync(manifest, null, default), "backup SHA mismatch");
            using (var token = new CancellationTokenSource())
            {
                token.Cancel(); backup = new Backup(manifest, bytes);
                using var service = new PluginUpdateService(profile, Offline(), root, backup);
                await Reject(() => service.CheckAsync(old, token.Token), "cancel check");
                Check(backup.Checks == 0, "cancellation never triggers backup");
                await Reject(() => service.PrepareAsync(manifest, null, token.Token), "cancel prepare");
                Check(backup.Downloads == 0, "cancel download never triggers backup");
            }
            var wrong = JsonCompat.Deserialize<UpdateManifest>(JsonCompat.Serialize(manifest)); wrong.ProductId = "other-product";
            using (var service = new PluginUpdateService(profile, Offline(), root, new Backup(wrong, bytes)))
                await Reject(() => service.CheckAsync(old, default), "wrong backup product");
            using (var service = new PluginUpdateService(profile, Offline(), root, new Backup(manifest, bytes)))
                Check(await service.CheckAsync(new Version("2.2.0.74"), default) == null && service.UsedBackup, "older backup does not downgrade");
            Check(File.Exists(path) && File.GetLastWriteTimeUtc(path) == before, "user download retained");
            Check(!Directory.Exists(Path.Combine(root, profile.ProductId)) || Directory.GetDirectories(Path.Combine(root, profile.ProductId)).Length == 0, "all owned staging cleaned");
        }
        var settings = new BackupUpdateSettings { FolderUrl = "https://wwapt.lanzoum.com/b01gibcr6d", ShareCode = "fixture-code" };
        using (var source = new LanzouUpdateSource(UpdateProductProfile.AutoCAD, settings))
        {
            Check(source.AllowedUri(new Uri("https://developer2.lanrar.com/file/x")), "allow observed CDN");
            foreach (var url in new[] { "http://wwapt.lanzoum.com/x", "https://lanrar.com.evil.test/x", "https://127.0.0.1/x", "https://user:pass@wwapt.lanzoum.com/x", "https://wwapt.lanzoum.com:8443/x" })
                Check(!source.AllowedUri(new Uri(url)), "deny unexpected download destination");
        }
        var page = "var a='clock'; var b='key'; data:{'lx':2,'fid':123,'uid':'456','puid':'p','rep':'0','t':a,'k':b,'up':1,'ls':1,'pwd':pwd}";
        var form = LanzouUpdateSource.ParseForm(page);
        Check(form["t"] == "clock" && form["k"] == "key" && form["fid"] == "123", "parse changing mobile variable names without evaluating script");
        Check(LanzouUpdateSource.BrowserCookie("no challenge") == null, "ordinary page does not create cookie");
        Check(LanzouUpdateSource.BrowserCookie("arg1='8776E3A6249EF15187A735CBE865AA61A3AC89D0';acw_sc__v2").Length == 40, "known browser cookie protocol");
        Console.WriteLine("PASS " + checks + " backup update checks; " + root);
        File.WriteAllText(Path.Combine(root,"passed.txt"),"PASS " + checks);
        return 0;
    }
    private static Handler Offline() => new((_, token) => { token.ThrowIfCancellationRequested(); throw new HttpRequestException("primary offline fixture"); });
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run;
        internal int Calls;
        internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) { this.run = run; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return run(request, token); }
    }
    private sealed class Backup : IBackupUpdateSource
    {
        private readonly UpdateManifest manifest; private readonly byte[] bytes;
        internal int Checks, Downloads;
        internal Backup(UpdateManifest manifest, byte[] bytes) { this.manifest = manifest; this.bytes = bytes; }
        public Task<UpdateManifest> CheckAsync(Version current, CancellationToken token) { Checks++; token.ThrowIfCancellationRequested(); return Task.FromResult(manifest); }
        public Task DownloadAsync(UpdateManifest manifest, string destination, IProgress<UpdateProgress> progress, CancellationToken token)
        { Downloads++; token.ThrowIfCancellationRequested(); File.WriteAllBytes(destination, bytes); return Task.CompletedTask; }
        public void Dispose() { }
    }
}
