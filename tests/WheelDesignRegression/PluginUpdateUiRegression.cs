using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class PluginUpdateUiRegression
{
    internal static void Run(Action<bool, string> check, string output)
    {
        foreach (var scenario in new[] { "latest", "html", "network", "cancel", "cancel-host-context", "backup-fails" })
        {
            var handler = new Handler(scenario);
            var backup = scenario == "backup-fails" ? new FailedBackup() : null;
            using var window = new PluginUpdateDialog(() => new PluginUpdateService(handler: handler, backup: backup),
                (_, _, _) => throw new InvalidOperationException("UI failure tests must never launch installer"),
                () => new[] {
                    new DownloadLink { Name = "夸克", Url = "https://pan.quark.cn/s/test", Code = "test" },
                    new DownloadLink { Name = "百度", Url = "https://pan.baidu.com/s/test", Code = "test" },
                    new DownloadLink { Name = "蓝奏云", Url = "https://wwapt.lanzoum.com/btest", Code = "test" },
                    new DownloadLink { Name = "invalid", Url = "file:///C:/test", Code = "test" }
                });
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(-20000, -20000);
            window.ShowInTaskbar = false;
            var previousContext = SynchronizationContext.Current;
            try
            {
                if (scenario == "cancel-host-context") SynchronizationContext.SetSynchronizationContext(new DeferredHostContext());
                window.Show();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            Application.DoEvents();
            if (scenario.StartsWith("cancel"))
            {
                check(Field<bool>(window, "_busy"), "cancel: check runs without blocking window");
                Field<Button>(window, "_cancel").PerformClick();
            }
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Field<bool>(window, "_busy") && DateTime.UtcNow < deadline)
            { Application.DoEvents(); Thread.Sleep(10); }
            check(!Field<bool>(window, "_busy"), scenario + ": returns to idle");
            check(Field<Button>(window, "_action").Enabled, scenario + ": retry remains enabled");
            check(Field<ReadyUpdate>(window, "_ready") == null, scenario + ": no installer may be handed off");
            check(handler.Calls == 1, scenario + ": check makes one manifest request only");
            check(window.GetType().GetField("_localPackage", BindingFlags.NonPublic | BindingFlags.Instance) == null,
                scenario + ": no manual package selection");
            if (backup != null) check(backup.Calls == 1 && Field<Label>(window, "_status").Text.Contains("失败"), "failed primary automatically attempts backup then reports failure");
            var downloads = Field<FlowLayoutPanel>(window, "_downloads");
            var failed = scenario == "html" || scenario == "network" || scenario == "backup-fails";
            check(downloads.Visible == failed, scenario + ": download links only on failure");
            if (failed) check(downloads.Controls.Count == 3, scenario + ": three validated links, invalid scheme rejected");
            var text = Field<Label>(window, "_status").Text + Field<Label>(window, "_details").Text;
            check(text.Contains(scenario == "latest" ? "最新" : scenario == "html" ? "验证" : scenario.StartsWith("cancel") ? "取消" : scenario == "backup-fails" ? "backup-test" : "network-test"),
                scenario + ": user sees accurate result");
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
            bitmap.Save(Path.Combine(output, "update-" + scenario + ".png"));
            window.Close();
        }
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name,
        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);

    private sealed class FailedBackup : IBackupUpdateSource
    {
        internal int Calls;
        public Task<UpdateManifest> CheckAsync(Version current, CancellationToken token) { Calls++; throw new IOException("backup-test unavailable"); }
        public Task DownloadAsync(UpdateManifest manifest, string destination, IProgress<UpdateProgress> progress, CancellationToken token) => throw new Exception("must not download after failed check");
        public void Dispose() { }
    }

    private sealed class DeferredHostContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object state) { /* Host waits for command completion. */ }
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly string scenario;
        internal int Calls;
        internal Handler(string scenario) { this.scenario = scenario; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (scenario.StartsWith("cancel")) await Task.Delay(Timeout.Infinite, token);
            if (scenario == "network" || scenario == "backup-fails") throw new HttpRequestException("network-test：离线网络错误");
            if (scenario == "html") return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("<html>verification</html>", Encoding.UTF8, "text/html") };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonCompat.Serialize(new UpdateManifest
                {
                    SchemaVersion = 1, ProductId = UpdateProductProfile.Current.ProductId,
                    Version = ProductMetadata.DisplayVersion, Size = 1, Sha256 = new string('0', 64),
                    PackageUrl = "https://mc931217.top/downloads/test.zip"
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
