using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

// No network or disk scan is performed while constructing the settings window.
internal sealed class PluginUpdateDialog : Form
{
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly Label _details = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly ProgressBar _progress = new() { Width = 510, Height = 22 };
    private readonly Button _action = new() { Text = "重试", AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "取消", AutoSize = true };
    private CancellationTokenSource _cancellation;
    private ReadyUpdate _ready;
    private bool _busy;
    private bool _handedOff;
    private readonly Func<PluginUpdateService> _serviceFactory;
    private readonly Action<string, string, string> _launch;
    private readonly Func<DownloadLink[]> _downloadLinks;
    private readonly FlowLayoutPanel _downloads = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };

    public PluginUpdateDialog(Func<PluginUpdateService> serviceFactory = null, Action<string, string, string> launch = null,
        Func<DownloadLink[]> downloadLinks = null)
    {
        _serviceFactory = serviceFactory ?? (() => new PluginUpdateService(backup: LanzouUpdateSource.FromFile(
            UpdateProductProfile.Current, Path.Combine(PluginPaths.PluginDirectory, LanzouUpdateSource.SettingsFile))));
        _launch = launch ?? PluginUpdateHandoff.Launch;
        _downloadLinks = downloadLinks ?? (() => DownloadLink.Read(Path.Combine(PluginPaths.PluginDirectory, LanzouUpdateSource.SettingsFile)));
        Text = "小阳轮盘 · 一键更新";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(558, 420);
        MinimumSize = new Size(460, 320);
        MaximizeBox = false;
        MinimizeBox = false;
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18),
            FlowDirection = FlowDirection.TopDown, WrapContents = false
        };
        content.Controls.Add(new Label { Text = CadHostInfo.PluginDisplayName + "\n当前已加载版本：" + ProductMetadata.DisplayVersion,
            AutoSize = true, Margin = new Padding(0, 0, 0, 16) });
        content.Controls.Add(_status);
        content.Controls.Add(_progress);
        content.Controls.Add(_details);
        content.Controls.Add(_downloads);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        actions.Controls.Add(_action);
        actions.Controls.Add(_cancel);
        content.Controls.Add(actions);
        Controls.Add(content);
        content.SizeChanged += (_, _) =>
        {
            var width = Math.Max(200, content.ClientSize.Width - content.Padding.Horizontal - 24);
            _status.MaximumSize = _details.MaximumSize = new Size(width, 0);
            _progress.Width = width;
            foreach (Control control in _downloads.Controls) control.MaximumSize = new Size(width, 0);
        };
        _cancel.Click += (_, _) => { if (_busy) _cancellation?.Cancel(); else Close(); };
        _action.Click += async (_, _) =>
        {
            if (_ready == null) await RunUpdateAsync();
            else BeginInstallation();
        };
        FormClosing += (_, e) =>
        {
            if (_busy) { e.Cancel = true; _cancellation?.Cancel(); }
        };
        FormClosed += (_, _) =>
        {
            if (_ready == null || _handedOff) return;
            try { _ready.Discard(); }
            catch (Exception ex) { Logger.Error("清理尚未安装的更新文件失败。", ex); }
        };
        Shown += async (_, _) => await RunUpdateAsync();
    }

    private Task RunUpdateAsync() => RunWithWindowContext(RunUpdateCoreAsync);

    private Task RunWithWindowContext(Func<Task> work)
    {
        // CAD may supply a context that defers callbacks until its command returns.
        // This dialog pumps WinForms messages while the CAD command remains active.
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            return work();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private async Task RunUpdateCoreAsync()
    {
        if (_busy || _handedOff) return;
        _busy = true;
        _downloads.Visible = false;
        _action.Enabled = false;
        _cancel.Text = "取消";
        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = "正在检查更新…";
        _details.Text = "先检查主站，失败后自动尝试蓝奏云备用源；均失败时显示原因，可稍后重试。";
        using (_cancellation = new CancellationTokenSource())
        {
            try
            {
                using var service = _serviceFactory();
                var checkProgress = new Progress<UpdateProgress>(p => { if (!IsDisposed && _busy) _status.Text = p.Stage; });
                var manifest = await service.CheckAsync(new Version(ProductMetadata.DisplayVersion), _cancellation.Token, checkProgress);
                if (manifest == null)
                {
                    _status.Text = service.UsedBackup ? "蓝奏云备用源暂未发现更高版本。" : "当前已是最新版本。";
                    _details.Text = service.UsedBackup ? "主站未能完成检查；已核对备用源清单，仍可稍后重试主站。" : "已按当前加载的程序集版本完成比较。";
                    return;
                }
                _status.Text = "发现新版本 " + manifest.Version + (service.UsedBackup ? "，正在从蓝奏云下载…" : "，正在下载…");
                _details.Text = manifest.Notes ?? "";
                _progress.Style = ProgressBarStyle.Continuous;
                var progress = new Progress<UpdateProgress>(p =>
                {
                    if (IsDisposed || !_busy) return;
                    _status.Text = p.Stage + "  " + (p.BytesReceived / 1048576d).ToString("0.0") + " / " +
                        (p.TotalBytes / 1048576d).ToString("0.0") + " MB";
                    _progress.Value = p.TotalBytes > 0 ? (int)Math.Min(100, p.BytesReceived * 100d / p.TotalBytes) : 0;
                });
                _ready = await service.PrepareAsync(manifest, progress, _cancellation.Token);
                ShowReady(manifest, service.UsedBackup ? "蓝奏云" : "主站");
            }
            catch (OperationCanceledException)
            {
                _status.Text = "更新已取消，当前安装保持不变。";
                _details.Text = "可点击重试重新检查。";
            }
            catch (Exception ex)
            {
                _status.Text = "更新失败，当前安装保持不变。";
                _details.Text = ex.Message;
                ShowDownloadLinks();
            }
            finally { FinishAttempt(); }
        }
    }

    private void ShowDownloadLinks()
    {
        while (_downloads.Controls.Count > 0) _downloads.Controls[0].Dispose();
        DownloadLink[] links;
        try { links = _downloadLinks(); }
        catch { return; } // A broken optional link list must not hide the original update failure.
        foreach (var link in links ?? Array.Empty<DownloadLink>())
        {
            if (link == null || !link.IsValid) continue;
            var label = new LinkLabel { AutoSize = true, MaximumSize = _details.MaximumSize,
                Margin = new Padding(0, 5, 0, 0), Text = (link.Name ?? "下载") + "：" + link.Url + "  提取码：" + link.Code };
            label.Links.Clear();
            label.Links.Add((link.Name ?? "下载").Length + 1, link.Url.Length);
            label.LinkClicked += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.Url) { UseShellExecute = true }); }
                catch { _details.Text = "无法打开浏览器，请复制上方失败信息下的下载链接。"; }
            };
            _downloads.Controls.Add(label);
        }
        _downloads.Visible = _downloads.Controls.Count > 0;
    }

    private void ShowReady(UpdateManifest manifest, string source)
    {
        _status.Text = "版本 " + manifest.Version + " 已从" + source + "准备并通过完整性与产品校验。";
        _details.Text = "点击“退出 CAD 后安装”后，请保存图纸和设置草稿，并正常退出当前产品的所有 CAD 窗口。\n" +
            "独立更新窗口会等待退出，等待时可取消；安装完成后请重新打开 CAD。\n" +
            "轮盘配置、命令、快捷键与日志将保留。";
        _action.Text = "退出 CAD 后安装";
        _progress.Value = 100;
    }

    private void FinishAttempt()
    {
        _busy = false;
        _cancellation = null;
        _progress.Style = ProgressBarStyle.Continuous;
        _action.Enabled = true;
        _cancel.Text = "关闭";
    }

    private void BeginInstallation()
    {
        try
        {
            _launch(_ready.Directory, _ready.Manifest.ProductId, _ready.Manifest.Version);
            _handedOff = true;
            _action.Enabled = false;
            _status.Text = "更新等待窗口已打开。请保存图纸与设置，再正常退出 CAD。";
            _details.Text = "请在独立更新窗口查看安装结果或取消等待。关闭此窗口不会关闭 CAD。";
        }
        catch (Exception ex)
        {
            _status.Text = "未能启动安装等待窗口，可重试。";
            _details.Text = ex.Message;
        }
    }
}
