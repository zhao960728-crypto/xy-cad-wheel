using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed class CommandIconDialog : Form
{
    private readonly ImageCanvas _canvas = new();
    private readonly PictureBox _preview = new();
    private readonly RadioButton _fit = new() { Text = "完整图片 · 等比例缩放", AutoSize = true, Checked = true };
    private readonly RadioButton _crop = new() { Text = "拖选区域 · 裁剪后等比例缩放", AutoSize = true };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    private readonly ComboBox _imageMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    public string ResultImageMode => _imageMode.SelectedIndex == 1 ? "Background" : "Icon";

    public string ResultIconData { get; private set; }

    public CommandIconDialog(string existingIconData, string existingImageMode = "Icon")
    {
        ResultIconData = existingIconData ?? "";
        Text = "格子图片";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 590);
        MinimumSize = new Size(620, 460);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(244, 246, 248);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8) };
        var choose = new Button { Text = "选择 / 更换图片", AutoSize = true };
        choose.Click += (_, _) => ChooseImage();
        options.Controls.Add(choose);
        var library = new Button { Text = "浏览内置图标库", AutoSize = true };
        library.Click += (_, _) =>
        {
            using (var dialog = new WheelIconLibraryDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _canvas.SetSource(WheelIconCatalog.Render(dialog.SelectedIconId, CommandIconImage.IconSize));
                _fit.Checked = true;
                RefreshPreview();
            }
        };
        options.Controls.Add(library);
        options.Controls.Add(_fit);
        options.Controls.Add(_crop);
        _imageMode.Items.AddRange(new object[] { "图标模式 · 完整显示", "背景图模式 · 铺满格子" });
        _imageMode.SelectedIndex = existingImageMode == "Background" ? 1 : 0;
        _imageMode.SelectedIndexChanged += (_, _) => RefreshPreview();
        options.Controls.Add(_imageMode);
        root.Controls.Add(options, 0, 0);
        var images = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        images.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
        _canvas.Dock = DockStyle.Fill;
        _canvas.SelectionChanged += (_, _) => RefreshPreview();
        images.Controls.Add(_canvas, 0, 0);
        var side = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 0, 0, 0) };
        side.Controls.Add(new Label { Text = "格子图片预览", AutoSize = true });
        _preview.Size = new Size(144, 144);
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.Paint += (_, e) => { if (_preview.Image == null) ImageCanvas.DrawChecker(e.Graphics, _preview.ClientRectangle); };
        side.Controls.Add(_preview);
        side.Controls.Add(new Label { Text = "图标：完整显示。\n背景：等比例铺满，\n超出边缘部分裁切。\n图片随设置备份保存。", AutoSize = true });
        images.Controls.Add(side, 1, 0);
        root.Controls.Add(images, 0, 1);
        root.Controls.Add(_status, 0, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "确定", AutoSize = true };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = "恢复默认图标", AutoSize = true };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); buttons.Controls.Add(reset);
        root.Controls.Add(buttons, 0, 3);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            try
            {
                ResultIconData = CommandIconImage.Encode(_canvas.Source, _canvas.SelectedRegion, ResultImageMode == "Background");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { ShowError(ex); }
        };
        reset.Click += (_, _) => { _canvas.SetSource(null); RefreshPreview(); };
        _fit.CheckedChanged += (_, _) => { _canvas.CropEnabled = _crop.Checked; _canvas.Invalidate(); RefreshPreview(); };
        _crop.CheckedChanged += (_, _) => { _canvas.CropEnabled = _crop.Checked; _canvas.Invalidate(); RefreshPreview(); };
        try { _canvas.SetSource(CommandIconImage.Decode(existingIconData)); RefreshPreview(); }
        catch (Exception ex) { _status.Text = "已有图标无法读取，可重新选择图片。"; _status.AccessibleDescription = ex.Message; }
        PerformAutoScale();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var margin = SettingsWindow.ScaleLogicalForDpi(12, DeviceDpi);
        var available = new Size(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
    }

    private void ChooseImage()
    {
        using (var dialog = new OpenFileDialog { Title = "选择命令图标", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { _canvas.SetSource(CommandIconImage.LoadFile(dialog.FileName)); RefreshPreview(); }
            catch (Exception ex) { ShowError(ex); }
        }
    }

    private void ShowError(Exception exception) => MessageBox.Show(this, "图片处理失败：" + exception.Message,
        "自定义图标", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void RefreshPreview()
    {
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        if (_canvas.Source == null) { _status.Text = "请选择图片；恢复默认后点击确定即可移除自定义图标。"; return; }
        try
        {
            using (var icon = CommandIconImage.Decode(CommandIconImage.Encode(_canvas.Source, _canvas.SelectedRegion, ResultImageMode == "Background")))
            {
                var bitmap = new Bitmap(144, 144);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    ImageCanvas.DrawChecker(graphics, new Rectangle(0, 0, 144, 144));
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var scale = ResultImageMode == "Background"
                        ? Math.Max(144f / icon.Width, 144f / icon.Height)
                        : Math.Min(144f / icon.Width, 144f / icon.Height);
                    graphics.DrawImage(icon, new RectangleF((144 - icon.Width * scale) / 2, (144 - icon.Height * scale) / 2, icon.Width * scale, icon.Height * scale));
                }
                _preview.Image = bitmap;
            }
            _status.Text = ResultImageMode == "Background" ? "背景图片等比例铺满格子，超出的部分裁切；文字显示由轮盘内容模式控制。" : _crop.Checked ? "在左侧图片上拖动选框。重新拖动可更改选取范围。" : "完整图片等比例放入图标区域，不拉伸、不裁切。";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _preview.Image?.Dispose(); _preview.Image = null; }
        base.Dispose(disposing);
    }

    private sealed class ImageCanvas : Control
    {
        public Bitmap Source { get; private set; }
        public bool CropEnabled { get; set; }
        public event EventHandler SelectionChanged;
        private RectangleF _selection;
        private PointF _anchor;
        private bool _dragging;
        public RectangleF SelectedRegion => Source == null ? RectangleF.Empty : CropEnabled && _selection.Width >= 1 && _selection.Height >= 1
            ? _selection : new RectangleF(0, 0, Source.Width, Source.Height);

        public ImageCanvas() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Cross; }
        public void SetSource(Bitmap source) { Source?.Dispose(); Source = source; _selection = RectangleF.Empty; Invalidate(); }
        private RectangleF ImageBounds()
        {
            if (Source == null) return RectangleF.Empty;
            var scale = Math.Min(Math.Max(1, Width - 16) / (float)Source.Width, Math.Max(1, Height - 16) / (float)Source.Height);
            return new RectangleF((Width - Source.Width * scale) / 2, (Height - Source.Height * scale) / 2, Source.Width * scale, Source.Height * scale);
        }
        private PointF SourcePoint(Point point)
        {
            var bounds = ImageBounds();
            return new PointF(Math.Max(0, Math.Min(Source.Width, (point.X - bounds.X) * Source.Width / bounds.Width)),
                Math.Max(0, Math.Min(Source.Height, (point.Y - bounds.Y) * Source.Height / bounds.Height)));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!CropEnabled || Source == null || e.Button != MouseButtons.Left || !ImageBounds().Contains(e.Location)) return;
            _anchor = SourcePoint(e.Location); _dragging = true; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            var end = SourcePoint(e.Location);
            _selection = RectangleF.FromLTRB(Math.Min(_anchor.X, end.X), Math.Min(_anchor.Y, end.Y), Math.Max(_anchor.X, end.X), Math.Max(_anchor.Y, end.Y));
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false; Capture = false; SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); DrawChecker(e.Graphics, ClientRectangle);
            if (Source == null)
            {
                TextRenderer.DrawText(e.Graphics, "选择图片后在这里预览或拖选区域", Font, ClientRectangle, Color.DimGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak); return;
            }
            var bounds = ImageBounds();
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Source, bounds);
            if (CropEnabled && _selection.Width >= 1 && _selection.Height >= 1)
            {
                var selection = new RectangleF(bounds.X + _selection.X * bounds.Width / Source.Width,
                    bounds.Y + _selection.Y * bounds.Height / Source.Height, _selection.Width * bounds.Width / Source.Width,
                    _selection.Height * bounds.Height / Source.Height);
                using (var mask = new Region(bounds))
                using (var shade = new SolidBrush(Color.FromArgb(125, 0, 0, 0)))
                using (var pen = new Pen(Color.FromArgb(0, 155, 175), 2))
                {
                    mask.Exclude(selection); e.Graphics.FillRegion(shade, mask);
                    e.Graphics.DrawRectangle(pen, selection.X, selection.Y, selection.Width, selection.Height);
                }
            }
        }
        internal static void DrawChecker(Graphics graphics, Rectangle rectangle)
        {
            graphics.FillRectangle(Brushes.White, rectangle);
            using (var brush = new SolidBrush(Color.FromArgb(225, 229, 233)))
                for (var y = rectangle.Top; y < rectangle.Bottom; y += 12)
                    for (var x = rectangle.Left; x < rectangle.Right; x += 12)
                        if (((x / 12) + (y / 12)) % 2 == 0) graphics.FillRectangle(brush, x, y, Math.Min(12, rectangle.Right - x), Math.Min(12, rectangle.Bottom - y));
        }
        protected override void Dispose(bool disposing) { if (disposing) Source?.Dispose(); base.Dispose(disposing); }
    }
}
