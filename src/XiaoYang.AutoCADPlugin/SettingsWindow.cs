using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow : Form
{
    private static readonly Color Canvas = Color.FromArgb(244, 246, 248);
    private static readonly Color Surface = Color.White;
    private static readonly Color Ink = Color.FromArgb(30, 41, 53);
    private static readonly Color Muted = Color.FromArgb(99, 115, 129);
    private static readonly Color Border = Color.FromArgb(215, 223, 230);
    private static readonly Color Accent = Color.FromArgb(0, 112, 132);
    private static readonly Color AccentDark = Color.FromArgb(0, 104, 125);
    private static readonly Color Header = Color.FromArgb(28, 38, 49);

    private readonly CadCommandCatalog _catalog;
    private readonly Action<PluginConfig> _saved;
    private readonly Action _requestLocalCommandRestore;
    private readonly Action _requestMenuRestore;
    private PluginConfig _config;
    private List<ManagedExtensionConfig> _lastAppliedExtensions;
    private bool _applyAllPendingSettings;
    private IReadOnlyList<CadCommandInfo> _allCommands = Array.Empty<CadCommandInfo>();
    private readonly TreeView _catalogTree = new();
    private readonly TextBox _search = new();
    private readonly CheckBox _expandSearchCommandTree = new();
    private readonly Label _catalogStatus = new();
    private readonly Label _catalogDetails = new();
    private readonly List<PictureBox> _contactPictures = new();
    private bool _checkingCatalogTree;
    private bool _catalogLoadPending;
    private bool _batchMode;
    private readonly Panel _batchBar = new();
    private readonly Label _batchStatus = new();
    private readonly Button _batchToggle = new();
    private Panel _catalogAssignmentBar;
    private readonly AssignmentTarget _catalogTarget = new();
    private readonly AssignmentTarget _customTarget = new();

    private readonly ComboBox _customList = new();
    private readonly TextBox _customName = new();
    private readonly ComboBox _customCategory = new();
    private readonly TextBox _commandName = new();
    private string _editingCustomId = "";

    private readonly ComboBox _trigger = new();
    private readonly NumericUpDown _holdDelay = new();
    private readonly NumericUpDown _menuSize = new();
    private readonly NumericUpDown _centerRadius = new();
    private readonly NumericUpDown _cellFontSize = new();
    private readonly CheckBox _autoFitCellFont = new();
    private readonly ComboBox _fontFamily = new();
    private readonly Button _applyCurrentFont = new();
    private readonly Button _applyAllFonts = new();
    private readonly LinkLabel _resetFontFamily = new();
    private readonly CheckBox _fontBold = new();
    private readonly ComboBox _textEffect = new();
    private readonly Button _applyCurrentTextStyle = new();
    private readonly Button _applyAllTextStyles = new();
    private readonly LinkLabel _resetTextStyle = new();
    private readonly Button _applyPlainTextStyle = new();
    private bool _updatingCellAppearance;
    private readonly TrackBar _menuOpacity = new();
    private readonly Label _menuOpacityValue = new();
    private readonly Label _imeBasicStatus = new();
    private readonly NumericUpDown _ringCount = new();
    private readonly TableLayoutPanel _ringEditors = new();
    private readonly List<NumericUpDown> _ringSlotEditors = new();
    private readonly List<NumericUpDown> _ringDiameterEditors = new();
    private bool _updatingRingLayout;
    private bool _loadingUi;
    private readonly DataGridView _wheelGrid = new();
    private readonly Label _wheelSummary = new();
    private readonly TabControl _pages = new();
    private readonly Label _pageTitle = new();
    private readonly Label _pageDescription = new();
    private readonly FlowLayoutPanel _pageSections = new() { Dock = DockStyle.Top, Height = 44, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
    private readonly Button _sectionPrimary = new() { AutoSize = true };
    private readonly Button _sectionSecondary = new() { AutoSize = true };
    private readonly Button _previewToggle = new();
    private Action _applyResponsiveChrome;
    private bool _compactPreviewOpen;
    private int _lastSynchronizedPage = -1;
    private readonly Dictionary<int, Button> _navButtons = new();
    private readonly RadialMenuControl _preview = new(Color.White) { Dock = DockStyle.Fill, ScaleMenuForPreview = true };
    private readonly TextBox _backgroundPath = new();
    private readonly TrackBar _backgroundOpacity = new();
    private readonly Label _backgroundOpacityValue = new();
    private readonly Label _previewSelection = new();
    private readonly TextBox _textColor = new();
    private readonly Panel _textColorSwatch = new();
    private readonly LinkLabel _chooseTextColor = new();
    private readonly LinkLabel _chooseAllTextColor = new();
    private readonly LinkLabel _resetTextColor = new();

    public SettingsWindow(PluginConfig config, CadCommandCatalog catalog, Action<PluginConfig> saved,
        bool loadCatalog = true, Action requestLocalCommandRestore = null, Action requestMenuRestore = null)
    {
        _config = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(config));
        _lastAppliedExtensions = JsonCompat.Deserialize<List<ManagedExtensionConfig>>(JsonCompat.Serialize(_config.ManagedExtensions ?? new List<ManagedExtensionConfig>()));
        _catalog = catalog;
        _saved = updated =>
        {
            var applied = JsonCompat.Deserialize<PluginConfig>(JsonCompat.Serialize(updated));
            if (!_applyAllPendingSettings)
                applied.ManagedExtensions = JsonCompat.Deserialize<List<ManagedExtensionConfig>>(JsonCompat.Serialize(_lastAppliedExtensions));
            saved(applied);
            if (_applyAllPendingSettings)
                _lastAppliedExtensions = JsonCompat.Deserialize<List<ManagedExtensionConfig>>(JsonCompat.Serialize(applied.ManagedExtensions ?? new List<ManagedExtensionConfig>()));
            _savedSnapshot = JsonCompat.Serialize(applied, false);
            UpdateSaveStatus();
        };
        _requestLocalCommandRestore = requestLocalCommandRestore;
        _requestMenuRestore = requestMenuRestore;
        Text = $"小阳CAD轮盘插件设置 · {CadHostInfo.DisplayName}";
        StartPosition = FormStartPosition.Manual;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.Sizable;
        // All literal layout sizes below are authored at 96 DPI. AutoCAD 2026+
        // can host this form in a high-DPI process; without a design baseline,
        // WinForms can render larger text while leaving rows and buttons at their
        // original pixel size.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Canvas;
        MinimumSize = new Size(980, 620);
        Size = new Size(1320, 800);
        BuildUi();
        ApplyTheme(this);
        StyleGrid(_wheelGrid);
        PerformAutoScale();
        FitToWorkingArea(Screen.FromPoint(Cursor.Position).WorkingArea);
        LoadConfigToUi();
        ApplyPluginAppearance();
        HookPreviewEvents();
        HookWheelDragDrop();
        RefreshPreview();
        SelectFirstEmptyWheelCell();
        SelectPage(3);
        InitializeResponsiveSettingsLayout();
        _savedSnapshot = JsonCompat.Serialize(_config, false);
        UpdateSaveStatus();
        FormClosing += (_, e) =>
        {
            ApplyAppearanceToConfig();
            if (JsonCompat.Serialize(_config, false) == _savedSnapshot) return;
            var choice = MessageBox.Show(this, "是否保存本次尚未应用的调整？", "未保存的设置", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) e.Cancel = true;
            else if (choice == DialogResult.Yes) SaveAllPendingSettings();
        };
        if (loadCatalog)
        {
            // The default page is the wheel editor. Reuse a completed host
            // snapshot immediately, but build thousands of tree rows only when
            // command management/search is requested, on the same CAD thread.
            if (_catalog.IsLoaded) _allCommands = _catalog.GetAll(false);
            _catalogLoadPending = true;
            _cellCommandSearch.TextChanged += (_, _) =>
            {
                if (_catalogLoadPending && !string.IsNullOrWhiteSpace(_cellCommandSearch.Text))
                { EnsureSettingsCatalog(); SearchCellCommands(); }
            };
        }
    }

    internal static int ScaleLogicalForDpi(int logicalPixels, int dpi) =>
        Math.Max(0, (int)Math.Round(logicalPixels * Math.Max(96, dpi) / 96d));

    private int ScaleLogical(int logicalPixels) => ScaleLogicalForDpi(logicalPixels, DeviceDpi);

    internal static bool UseCompactSettingsLayout(int clientWidth, int dpi) =>
        // Reserve the complete 640 px editor, navigation, preview and page
        // margins before showing both panes. The old 1260 px breakpoint left
        // the final ring-size input outside its parent's visible bounds.
        clientWidth < ScaleLogicalForDpi(640 + 176 + 400 + 48 + 96, dpi);

    private Padding ScaleLogical(Padding padding) => new(
        ScaleLogical(padding.Left), ScaleLogical(padding.Top),
        ScaleLogical(padding.Right), ScaleLogical(padding.Bottom));

    private void FitToWorkingArea(Rectangle workingArea)
    {
        var bounds = InitialSettingsBounds(workingArea, DeviceDpi);
        MinimumSize = new Size(Math.Min(ScaleLogical(980), bounds.Width), Math.Min(ScaleLogical(620), bounds.Height));
        Bounds = bounds;
    }

    internal static Rectangle InitialSettingsBounds(Rectangle workingArea, int dpi)
    {
        // Leave visible desktop around the initial window even on small or
        // high-DPI displays. The existing compact layout keeps the editor usable.
        var width = Math.Max(1, Math.Min(ScaleLogicalForDpi(1320, dpi), (int)(workingArea.Width * .86)));
        var height = Math.Max(1, Math.Min(ScaleLogicalForDpi(800, dpi), (int)(workingArea.Height * .86)));
        return new Rectangle(workingArea.Left + (workingArea.Width - width) / 2,
            workingArea.Top + (workingArea.Height - height) / 2, width, height);
    }

    private void BuildUi()
    {
        _pages.Dock = DockStyle.Fill;
        _pages.Appearance = TabAppearance.FlatButtons;
        _pages.ItemSize = new Size(0, 1);
        _pages.SizeMode = TabSizeMode.Fixed;
        _pages.TabStop = false;
        _pages.TabPages.Add(BuildBasicTab());
        _pages.TabPages.Add(BuildCatalogTab());
        _pages.TabPages.Add(BuildCustomTab());
        _pages.TabPages.Add(BuildGeneralTab());
        _pages.TabPages.Add(BuildContactTab());
        _pages.TabPages.Add(BuildHelpTab());
        _pages.TabPages.Add(BuildMaintenanceTab());
        _pages.TabPages.Add(BuildExtensionManagerTab());
        _pages.TabPages.Add(BuildInstalledPluginInventoryTab());
        _pages.SelectedIndexChanged += (_, _) => SyncSelectedPage();

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 176, BackColor = Header, Tag = "header" };
        var brand = new Panel { Dock = DockStyle.Top, Height = 124, Padding = new Padding(18, 22, 12, 8), BackColor = Header, Tag = "header" };
        var title = new Label
        {
            Text = "小阳 CAD 轮盘",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            Tag = "header-title"
        };
        var subtitle = new Label
        {
            Text = $"{CadHostInfo.DisplayName}\n设置与命令管理",
            Dock = DockStyle.Top,
            Height = 48,
            ForeColor = Color.FromArgb(201, 213, 222),
            Tag = "header-subtitle"
        };
        brand.Controls.Add(subtitle);
        brand.Controls.Add(title);
        sidebar.Controls.Add(CreateNavButton("帮助与支持", 5));
        sidebar.Controls.Add(CreateNavButton("备份与维护", 6));
        sidebar.Controls.Add(CreateNavButton("功能管理器", 7));
        sidebar.Controls.Add(CreateNavButton("触发与输入", 0));
        sidebar.Controls.Add(CreateNavButton("命令管理", 1));
        sidebar.Controls.Add(CreateNavButton("轮盘编辑", 3));
        sidebar.Controls.Add(brand);

        var previewPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 400,
            Padding = new Padding(16, 20, 16, 20),
            BackColor = Color.FromArgb(232, 240, 242),
            Tag = "preview"
        };
        var previewHeader = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.Transparent };
        var previewTitle = new Label
        {
            Text = "轮盘预览",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = Ink,
            Tag = "preview-title"
        };
        _previewSelection.Text = "点击轮盘格子可自动选择位置";
        _previewSelection.Dock = DockStyle.Fill;
        _previewSelection.ForeColor = AccentDark;
        _previewSelection.Tag = "preview-selection";
        previewHeader.Controls.Add(_previewSelection);
        previewHeader.Controls.Add(previewTitle);
        var previewSurface = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8), Tag = "preview-surface" };
        _preview.Dock = DockStyle.None;
        previewSurface.Controls.Add(_preview);
        void LayoutPreview()
        {
            var area = previewSurface.ClientRectangle;
            var padding = previewSurface.Padding;
            _preview.Bounds = Rectangle.FromLTRB(
                area.Left + padding.Left,
                area.Top + padding.Top,
                Math.Max(area.Left + padding.Left, area.Right - padding.Right),
                Math.Max(area.Top + padding.Top, area.Bottom - padding.Bottom));
            _preview.Invalidate();
        }
        previewSurface.SizeChanged += (_, _) => LayoutPreview();
        previewSurface.Layout += (_, _) => LayoutPreview();
        previewPanel.SizeChanged += (_, _) => LayoutPreview();
        previewPanel.Controls.Add(previewSurface);
        previewPanel.Controls.Add(BuildPreviewCellTools());
        previewPanel.Controls.Add(previewHeader);
        LayoutPreview();

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(24, 0, 24, 0) };
        _pageTitle.Dock = DockStyle.Top;
        _pageTitle.Height = 38;
        _pageTitle.TextAlign = ContentAlignment.MiddleLeft;
        _pageTitle.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
        _pageDescription.Dock = DockStyle.Fill;
        _pageDescription.ForeColor = Muted;
        _pageDescription.AutoEllipsis = true;
        _pageDescription.Tag = "muted";
        var headingText = new Panel { Dock = DockStyle.Fill };
        headingText.Controls.Add(_pageDescription);
        headingText.Controls.Add(_pageTitle);
        var heading = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(0, 12, 0, 8) };
        _previewToggle.Text = "查看轮盘";
        _previewToggle.AccessibleName = "切换轮盘预览";
        _previewToggle.Dock = DockStyle.Fill;
        var previewTools = new Panel { Dock = DockStyle.Right, Width = 112, Padding = new Padding(0, 10, 0, 12) };
        previewTools.Controls.Add(_previewToggle);
        _previewToggle.Click += (_, _) => { _compactPreviewOpen = !_compactPreviewOpen; _applyResponsiveChrome?.Invoke(); };
        heading.Controls.Add(headingText);
        heading.Controls.Add(previewTools);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 68, Padding = new Padding(0, 12, 0, 12), BackColor = Canvas, ColumnCount = 3, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        var save = new Button { Text = "应用并保存", Dock = DockStyle.Fill, Tag = "primary", Margin = new Padding(8, 0, 0, 0) };
        save.Click += (_, _) => SaveAndApply();
        var close = new Button { Text = "关闭", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var path = new Label
        {
            Text = "调整完成后，点击“应用并保存”。",
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Muted,
            Tag = "muted"
        };
        footer.Controls.Add(_saveStatus, 0, 0);
        footer.Controls.Add(close, 1, 0);
        footer.Controls.Add(save, 2, 0);
        _sectionPrimary.Click += (_, _) => SelectPage(_pages.SelectedIndex is 1 or 2 ? 1 : 5);
        _sectionSecondary.Click += (_, _) => SelectPage(_pages.SelectedIndex is 1 or 2 ? 2 : 4);
        _pageSections.Controls.Add(_sectionPrimary);
        _pageSections.Controls.Add(_sectionSecondary);
        center.Controls.Add(_pages);
        center.Controls.Add(footer);
        center.Controls.Add(_pageSections);
        center.Controls.Add(heading);
        Controls.Add(center);
        Controls.Add(previewPanel);
        Controls.Add(sidebar);

        void ApplyResponsiveChrome()
        {
            var compact = UseCompactSettingsLayout(ClientSize.Width, ResponsiveSettingsDpi);
            var canPreview = _pages.SelectedIndex >= 1 && _pages.SelectedIndex <= 3;
            sidebar.Width = ScaleLogicalForDpi(compact ? 152 : 176, ResponsiveSettingsDpi);
            center.Padding = compact
                ? ScaleLogical(new Padding(16, 0, 16, 0))
                : ScaleLogical(new Padding(24, 0, 24, 0));
            SuspendLayout();
            center.SuspendLayout();
            var previewParent = compact ? center : (Control)this;
            if (previewPanel.Parent != previewParent) previewParent.Controls.Add(previewPanel);
            previewPanel.Dock = compact ? DockStyle.Fill : DockStyle.Right;
            previewPanel.Width = ScaleLogicalForDpi(400, ResponsiveSettingsDpi);
            previewPanel.Visible = canPreview && (!compact || _compactPreviewOpen);
            _pages.Visible = !compact || !canPreview || !_compactPreviewOpen;
            _previewToggle.Visible = compact && canPreview;
            previewTools.Visible = compact && canPreview;
            _previewToggle.Text = _compactPreviewOpen ? "返回设置" : "查看轮盘";
            if (compact) previewPanel.BringToFront();
            else { center.BringToFront(); previewPanel.SendToBack(); sidebar.SendToBack(); }
            center.ResumeLayout(true);
            ResumeLayout(true);
        }

        _applyResponsiveChrome = ApplyResponsiveChrome;
        ApplyResponsiveChrome();
        ClientSizeChanged += (_, _) => ApplyResponsiveChrome();
    }

    private Button CreateNavButton(string text, int pageIndex)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 46,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Header,
            ForeColor = Color.White,
            Tag = "nav"
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => SelectPage(pageIndex);
        _navButtons[pageIndex] = button;
        return button;
    }

    private void SelectPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pages.TabPages.Count) return;
        _compactPreviewOpen = false;
        _pages.SelectedIndex = pageIndex;
        SyncSelectedPage();
    }

    private void SyncSelectedPage()
    {
        var pageIndex = _pages.SelectedIndex;
        if (pageIndex >= 0 && pageIndex != _lastSynchronizedPage)
        {
            _lastSynchronizedPage = pageIndex;
            EndPreviewEditing();
        }
        if (pageIndex == 1 || pageIndex == 2) EnsureSettingsCatalog();
        if (pageIndex == 4) EnsureContactImages();
        _pageTitle.Text = pageIndex switch
        {
            0 => "触发与输入",
            1 => "命令管理",
            2 => "命令管理",
            3 => "轮盘编辑",
            4 => "帮助与支持",
            6 => "备份与维护",
            7 => "功能管理器",
            8 => "现有插件",
            _ => "帮助与支持"
        };
        _pageDescription.Text = pageIndex switch
        {
            0 => "设置触发方式与输入法自动切换。",
            1 => "查找命令，选择轮盘位置，再添加到格子。",
            2 => "管理自己的命令，或通过模板批量导入。",
            3 => "选择样式；布局、整体外观与当前命令分别设置。",
            4 => "感谢你对小阳 CAD 轮盘的支持。",
            6 => "备份完整设置、检查快捷命令、一键更新与恢复默认。",
            7 => "添加自己的扩展，勾选需要随轮盘加载的项目。",
            8 => "读取现有插件及当前 CAD 加载状态，管理可识别的卸载入口。",
            _ => "查看使用方法、文件位置与故障排查。"
        };
        var navIndex = pageIndex == 2 ? 1 : pageIndex == 4 ? 5 : pageIndex == 8 ? 7 : pageIndex;
        _pageSections.Visible = pageIndex is 1 or 2 or 4 or 5;
        _sectionPrimary.Text = pageIndex is 1 or 2 ? "命令库" : "使用说明";
        _sectionSecondary.Text = pageIndex is 1 or 2 ? "自定义命令" : "作者与联系";
        _sectionPrimary.BackColor = pageIndex is 1 or 5 ? AccentDark : Surface;
        _sectionPrimary.ForeColor = pageIndex is 1 or 5 ? Color.White : Ink;
        _sectionSecondary.BackColor = pageIndex is 2 or 4 ? AccentDark : Surface;
        _sectionSecondary.ForeColor = pageIndex is 2 or 4 ? Color.White : Ink;
        foreach (var pair in _navButtons)
        {
            pair.Value.BackColor = pair.Key == navIndex ? AccentDark : Header;
            pair.Value.ForeColor = Color.White;
            pair.Value.AccessibleDescription = pair.Key == navIndex ? "当前页面" : "打开" + pair.Value.Text;
        }
        _applyResponsiveChrome?.Invoke();
        ApplyPluginAppearance();
    }

    private TabPage BuildContactTab()
    {
        var page = new TabPage("作者与联系") { Padding = new Padding(8), BackColor = Canvas, AutoScroll = true };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 18), BackColor = Surface, Tag = "surface" };
        var title = new Label
        {
            Text = "作者与联系",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Ink
        };
        var message = new Label
        {
            Text = "自用插件，免费分享，不收费、不商用。\n" +
                   "后续大概率只做优化，不再新增大功能。\n" +
                   "你安装的第三方插件功能命令，需要填写到固定模板后导入。\n" +
                   "作者：小阳。欢迎关注交流或通过微信联系。",
            Dock = DockStyle.Top,
            Height = 108,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            ForeColor = Ink
        };
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 286,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 12, 0, 0),
            BackColor = Surface
        };
        for (var index = 0; index < 2; index++)
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        AddContactCard(cards, 0, "小阳爱学习", "抖音.png");
        AddContactCard(cards, 1, "微信联系", "加我.png");
        content.Controls.Add(cards);
        content.Controls.Add(message);
        content.Controls.Add(title);
        page.Controls.Add(content);
        return page;
    }

    private void AddContactCard(TableLayoutPanel cards, int column, string caption, string resourceFileName)
    {
        var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6), Padding = new Padding(10), BackColor = Color.FromArgb(248, 250, 252) };
        var picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            Tag = resourceFileName
        };
        _contactPictures.Add(picture);
        // PictureBox does not own its Image. Decode only when this page is
        // opened, then release the native bitmap with its window.
        picture.Disposed += (_, _) => { picture.Image?.Dispose(); picture.Image = null; };
        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Bottom,
            Height = 34,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Ink
        };
        card.Controls.Add(picture);
        card.Controls.Add(label);
        cards.Controls.Add(card, column, 0);
    }

    private void EnsureContactImages()
    {
        foreach (var picture in _contactPictures)
            if (!picture.IsDisposed && picture.Image == null)
                picture.Image = LoadSharedContactImage((string)picture.Tag);
    }

    private static Image LoadSharedContactImage(string fileName)
    {
        if (fileName != "抖音.png" && fileName != "加我.png") return null;
        var path = Path.Combine(PluginPaths.ContactResourceDirectory, fileName);
        try
        {
            // All host-year kernels read the same original PNGs in their bundle.
            // Clone while the stream is open so the window holds no file lock.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            Logger.Error("读取作者联系图片失败，请重新安装完整插件包：" + path, ex);
            return null;
        }
    }

    private TabPage BuildHelpTab()
    {
        var version = ProductMetadata.DisplayVersion;
        var cadName = CadHostInfo.ProductName;
#if ZWCAD
        var compatibilityText = "  当前安装包支持中望CAD 2020–2026 x64（.NET Framework 4.8），每个年份均使用对应 ZRX.NET API 单独编译内核。\n" +
                                "  安装器只向本机实际安装的年份注册对应 DLL；各版本共用配置格式，但配置文件按年份分开保存。\n";
#elif AUTOCAD_WIN32
        var compatibilityText = "  当前独立安装包支持 AutoCAD 2010 32 位（R18.0 / .NET Framework 3.5）。\n" +
                                "  本包使用独立目录和配置，后续升级请继续使用 AutoCAD2010-Win32 独立包。\n";
#else
        var compatibilityText = "  当前安装包支持 AutoCAD 2014–2024 x64（.NET Framework 4.8）、AutoCAD 2025–2026 x64（.NET 8）和 AutoCAD 2027 x64（.NET 10），会按当前 CAD API 系列自动加载对应 DLL。\n" +
                                "  DLL 按 NetFramework48、Net8 与 Net10 三个目录分组；安装器只保留本机已安装 CAD 年份对应的 DLL。\n";
#endif
        var page = new TabPage("使用说明") { Padding = new Padding(8), BackColor = Canvas };
        var help = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Surface,
            Font = new Font("Microsoft YaHei UI", 10F),
            ForeColor = Ink,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            Text = $"小阳 CAD 轮盘 · {CadHostInfo.DisplayName}\n版本：{version}\n\n" +
                   "一、设置导航与保存\n" +
                   "  轮盘编辑：选择样式，在“尺寸与布局”“背景与配色”“图标与文字”中调整。窄窗口可用“查看轮盘”切换预览。\n" +
                   "  命令管理：页内分为“命令库”和“自定义命令”；触发与输入：设置鼠标触发、长按时间、输入法、插件界面配色和手动菜单栏恢复；功能管理器：登记并启用外部扩展；备份与维护：导入、导出、重置和快捷命令检查；帮助与支持：使用说明与作者与联系。\n" +
                   "  外观和布局调整会先显示在预览中；底部显示是否仍有未保存的调整。“应用并保存”提交全部调整。关闭时可保存、放弃尚未应用的调整或取消关闭。已通过具体操作保存的改名、分类、命令或文字设置不因放弃剩余草稿而撤销。\n\n" +
                   "二、五种轮盘样式\n" +
                   "  经典轮盘：保留原有多圈扇形、文字和操作方向，旧配置默认使用此样式。\n" +
                   "  图标轮盘：命令按方向排列，北向为首个位置；指向时突出显示。支持自动图文，按图标及格子空间自动安排；底板透明度独立调整，图标与文字保持清晰。\n" +
                   "  分组面板：以页签选择分组，当前组的所有命令一次展示。\n" +
                   "  节点轮盘：每个入口对应第 N 圈，先指向入口展开，再选择该圈命令；中心“返回”可回到入口。节点之间不绘制连接线，优先沿第一圈尽量排满；命令较多时向内错位穿插第二圈，仍放不下时继续扩展第三圈，不分页。\n" +
                   "  节点轮盘与独立格轮盘可直接在“尺寸与布局”的每圈行中调整格子大小，范围为 50%–150%。节点轮盘同一入口展开出的各圈格子一样大，共用该入口对应行的大小设置；同圈格子之间及相邻圈之间至少留出 8 px，放大受可用间距限制。\n" +
                   "  独立格轮盘：各圈的独立圆格直接同时展示，不经过节点轮盘的分组入口；不分页、不连线。可分别调整各圈直径、容量及当前圈格子大小，底板透明度独立设置。\n" +
                   "  分组面板、节点轮盘和独立格轮盘均不分页；容量较大时会调整排列和间距。五种样式共用命令绑定，切换样式不清除命令或原外观设置。\n" +
                   "  经典轮盘、图标轮盘和独立格轮盘使用直径、中心半径及各圈容量。分组面板使用分组数量、容量和页签标题；节点轮盘使用圈数、每圈容量和圈入口标题。当前样式无作用的设置会隐藏，已有配置仍保留。\n\n" +
                   "三、显示方式、图标与配色\n" +
                   "  图标轮盘、分组面板、节点轮盘及独立格轮盘可在“图标与文字”中选择“自动图文”“仅图标”“仅文字”或“图标与文字”。自动图文按图标及格子空间自动安排；经典轮盘保持原来的文字显示。\n" +
                   "  先在预览中选中一个已绑定命令的位置，再点“自定义命令图标 · 选图 / 裁剪”。可选自己的图片，使用完整图片等比例缩放，或拖选区域后裁剪并等比例缩放；确认前可查看预览，也可更换或清除图标。图标数据随设置备份保存。\n" +
                   "  文字字号、自动适配、字体、加粗、特效和颜色仍可设置当前格、统一全部格或恢复。背景与配色中的调色盘支持单色、渐变及方向，也可调整边线、悬停与选中色。经典轮盘保留原透明度和背景图片设置。\n\n" +
                   "四、命令管理与分配\n" +
                   "  命令库保留分类树、来源筛选和搜索。分类单击选择、双击展开；选择具体命令后可改显示名称，执行命令保持不变。分类可新建、改名；“批量操作”支持全选、反选、批量分类和删除允许删除的自定义项目。\n" +
                   "  先在轮盘预览中选择位置，再点“添加到所选格子”，或把命令拖入预览。拖动已绑定的位置可交换命令。缩小容量时移出的命令保存在“未分配命令”中，可重新放回。练习模式只显示选中提示，不执行 CAD 命令。\n" +
                   "  自定义命令可逐个新建、修改、删除，保留归属分类。执行命令支持纯数字和数字开头，例如 0、1、3DORBIT、SUN-173；填写单个命令名，不填写脚本。\n" +
                   $"  批量导入仅使用插件提供的 {CommandImportService.TemplateFileName}。点“保存模板”取得文件；固定表头为：执行命令 | 功能描述 | 归属分类。从第 2 行填写，执行命令必填；另两项留空时采用命令名和当前分类。导入前先保存工作簿，不直接导入 CSV 或 TXT。\n" +
                   $"  “重新读取本地命令”或 XYRESTORELOCALCMDS 只读当前 {cadName} 主 CUI、已加载局部 CUIX 和 PGP，重建命令缓存；不会改写用户的命令源文件。“备份与维护”的快捷命令检查可查看重复、占用和已有定义。\n\n" +
                   "五、鼠标触发与输入法\n" +
                   "  “触发与输入”可选右键长按、中键长按、鼠标侧键 1 或侧键 2 长按，以及 Shift / Alt / Ctrl + 右键长按。长按达到设定时间后打开轮盘；指向命令并松开执行。鼠标侧键需由设备发送相应侧键事件。\n" +
                   "  输入法设置可从“触发与输入”打开，也可执行 XYLPIME。可设置直接输入、普通命令、命令结束和文档切换时的英文偏好，选择具体输入法，并维护中文命令名单。\n" +
                   "  已有文字编辑按内容选择初始中英文；新建文字命令按中文名单处理。自动切换只作用于目标 CAD 前台窗口，不锁定其他软件输入法，用户仍可手动切换。\n\n" +
                   "六、功能管理器\n" +
                   "  用“添加扩展文件”或拖入多个文件登记自己的扩展，新项默认不启用。文件保留在原目录，便于使用同目录依赖。只勾选要随轮盘加载的扩展，再点击底部“应用并保存”；未应用的勾选不会被其他命令编辑操作顺带启用。\n" +
                   "  “刷新加载状态”查看当前结果。支持类型以当前宿主及文件选择框为准，宿主的信任与安全检查仍然有效。“卸载所选扩展”仅移除登记，不删除原文件，不卸载轮盘本体；已加载的扩展取消启用或移除后，需要重启 CAD 才会停止加载。\n\n" +
                   "七、备份与维护\n" +
                   "  设置备份包含轮盘绑定、自定义命令、分类、显示方式、外观、图标、背景、触发和扩展登记。扩展程序及同目录依赖仍保留在原位置，备份不会复制这些程序文件。\n" +
                   "  导出会包含设置页当前草稿，但不会因此启用扩展或替代“应用并保存”。导入和恢复默认会先自动备份当前设置，经确认后整体应用。\n" +
                   "  一键更新仅检查当前宿主的产品包，下载并校验后等待保存图纸、正常退出 CAD 再安装；不会强制关闭 CAD。更新窗口可取消等待，完成后需重新启动 CAD。\n\n" +
                   "八、常用命令与兼容\n" +
                   "  XYLP：打开轮盘；XYLPSZ：打开设置；XYLPIME：输入法设置。\n" +
                   "  XYREFRESHCMDS：刷新命令库；XYRESTORELOCALCMDS：重新读取本地命令；XYLPMENU：修复顶部轮盘菜单；XYLPZJ：插件自检。\n" +
                   compatibilityText +
                   $"  命令与别名只读取当前运行的 {cadName} 版本。模型空间、布局空间和块编辑器共用一个轮盘，命令通过 CAD 原生命令上下文执行。\n\n" +
                   "九、文件与故障排查\n" +
                   $"  配置：{ConfigStore.ConfigPath}\n" +
                   $"  运行日志：{Logger.LogPath}\n" +
                   $"  错误日志：{Logger.ErrorLogPath}\n" +
                   "  顶部菜单缺失时，在“触发与输入”点击“显示 CAD 菜单栏并恢复小阳菜单”，或执行 XYLPMENU。手动恢复会将 MENUBAR 设为 1；仅打开设置或正常启动不会执行此操作。功能异常时执行 XYLPZJ，并查看错误日志。"
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 8, 0, 0) };
        var openFolder = new Button { Text = "打开插件文件夹", AutoSize = true, Tag = "primary" };
        openFolder.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = PluginPaths.PluginDirectory,
            UseShellExecute = true
        });
        bottom.Controls.Add(openFolder);
        page.Controls.Add(help);
        page.Controls.Add(bottom);
        return page;
    }

    private TabPage BuildCatalogTab()
    {
        var page = new TabPage("命令库") { Padding = new Padding(12) };
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Surface,
            Tag = "surface"
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var searchRow = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 5, 4, 5),
            BackColor = Surface,
            Tag = "surface"
        };
        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 2, 0, 2),
            BackColor = Surface,
            Tag = "surface"
        };
        var searchOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 2, 0, 0),
            BackColor = Surface,
            Tag = "surface"
        };
        RuntimeCompat.SetTextBoxCueBanner(_search, "搜索功能或英文命令");
        _search.Dock = DockStyle.Fill;
        _commandSource.DropDownStyle = ComboBoxStyle.DropDownList;
        _commandSource.Items.AddRange(new object[] { "全部来源", "CAD 命令", "第三方命令", "自定义命令" });
        _commandSource.SelectedIndex = 0;
        _commandSource.Dock = DockStyle.Right;
        _commandSource.Width = 126;
        _commandSource.SelectedIndexChanged += (_, _) => FilterCatalog();
        _search.TextChanged += (_, _) => FilterCatalog();
        _expandSearchCommandTree.Text = "搜索命令树展开";
        _expandSearchCommandTree.AutoSize = true;
        _expandSearchCommandTree.Margin = new Padding(0, 2, 0, 0);
        _expandSearchCommandTree.CheckedChanged += (_, _) =>
        {
            if (_loadingUi) return;
            _config.ExpandSearchCommandTree = _expandSearchCommandTree.Checked;
            FilterCatalog();
        };
        var refresh = new Button { Name = "CatalogAction", Text = "刷新", Dock = DockStyle.Fill };
        refresh.Click += (_, _) => LoadCatalog(true);
        var restoreLocal = new Button { Name = "CatalogAction", Text = "重新读取本地命令", Dock = DockStyle.Fill };
        restoreLocal.Click += (_, _) => RequestLocalCommandRestore();
        var addCategory = new Button { Name = "CatalogAction", Text = "新建分类", Dock = DockStyle.Fill };
        addCategory.Click += (_, _) => AddCategory();
        var delete = new Button { Name = "CatalogAction", Text = "删除", Dock = DockStyle.Fill, Tag = "danger" };
        delete.Click += (_, _) => DeleteCatalogSelection();
        var rename = new Button { Name = "CatalogAction", Text = "改名", Dock = DockStyle.Fill };
        rename.Click += (_, _) => RenameSelectedCatalogCommand();
        _batchToggle.Text = "批量操作";
        _batchToggle.Name = "CatalogAction";
        _batchToggle.AutoSize = false;
        _batchToggle.Dock = DockStyle.Fill;
        _batchToggle.Click += (_, _) => SetBatchMode(!_batchMode);
        searchRow.Controls.Add(_search);
        searchRow.Controls.Add(_commandSource);
        foreach (var button in new[] { delete, rename, _batchToggle, addCategory, restoreLocal, refresh })
        {
            button.Dock = DockStyle.None;
            button.AutoSize = true;
            button.Margin = new Padding(3);
            actionRow.Controls.Add(button);
        }
        searchOptions.Controls.Add(_expandSearchCommandTree);
        top.Controls.Add(searchRow, 0, 0);
        top.Controls.Add(actionRow, 0, 1);
        top.Controls.Add(searchOptions, 0, 2);

        _catalogTree.Dock = DockStyle.Fill;
        _catalogTree.BorderStyle = BorderStyle.FixedSingle;
        _catalogTree.FullRowSelect = true;
        _catalogTree.HideSelection = false;
        _catalogTree.ShowLines = true;
        _catalogTree.ShowPlusMinus = true;
        _catalogTree.ShowRootLines = false;
        _catalogTree.CheckBoxes = false;
        _catalogTree.ItemHeight = 30;
        _catalogTree.AfterSelect += (_, _) => ShowCatalogDetails();
        _catalogTree.AfterCheck += CatalogTreeAfterCheck;
        _catalogTree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node.Tag is CadCommandInfo or CustomCommandConfig) AssignSelectedCatalogItem();
        };

        _catalogDetails.Dock = DockStyle.Bottom;
        _catalogDetails.Height = 48;
        _catalogDetails.Padding = new Padding(10, 6, 10, 6);
        _catalogDetails.BackColor = Color.FromArgb(247, 249, 251);
        _catalogDetails.ForeColor = Muted;
        _catalogDetails.AutoEllipsis = true;
        _catalogDetails.Text = "展开分类，选择一个命令";

        _catalogAssignmentBar = BuildAssignmentBar("添加到所选格子", _catalogTarget, AssignSelectedCatalogItem);
        BuildBatchBar();
        _catalogStatus.Dock = DockStyle.Bottom;
        _catalogStatus.Height = 26;
        _catalogStatus.Padding = new Padding(8, 4, 8, 4);
        _catalogStatus.ForeColor = Muted;
        page.Controls.Add(_catalogTree);
        page.Controls.Add(_catalogDetails);
        page.Controls.Add(_catalogStatus);
        page.Controls.Add(_batchBar);
        page.Controls.Add(_catalogAssignmentBar);
        page.Controls.Add(top);
        return page;
    }

    private TabPage BuildCustomTab()
    {
        var page = new TabPage("自定义命令") { Padding = new Padding(12), AutoScroll = true };
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 7,
            Height = 440,
            Padding = new Padding(18, 22, 18, 14),
            BackColor = Surface,
            Tag = "surface"
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var selector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        selector.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        selector.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _customList.Dock = DockStyle.Fill;
        _customList.DropDownStyle = ComboBoxStyle.DropDownList;
        _customList.DisplayMember = nameof(CustomCommandConfig.Name);
        _customList.SelectedIndexChanged += (_, _) => LoadSelectedCustom();
        var template = new Button { Text = "保存模板", Dock = DockStyle.Fill };
        template.Click += (_, _) => SaveCommandImportTemplate();
        var import = new Button { Text = "批量导入", Dock = DockStyle.Fill };
        import.Click += (_, _) => ImportCustomCommands();
        var add = new Button { Text = "新建", Dock = DockStyle.Fill };
        add.Click += (_, _) => BeginNewCustom();
        var delete = new Button { Text = "删除", Dock = DockStyle.Fill, Tag = "danger" };
        delete.Click += (_, _) => DeleteSelectedCustom();
        selector.Controls.Add(_customList, 0, 0);
        selector.SetColumnSpan(_customList, 2);
        selector.Controls.Add(add, 2, 0);
        selector.Controls.Add(delete, 3, 0);
        selector.Controls.Add(template, 0, 1);
        selector.Controls.Add(import, 1, 1);
        var formatHint = new Label { Text = "仅固定 XLSX 模板", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted };
        selector.Controls.Add(formatHint, 2, 1);
        selector.SetColumnSpan(formatHint, 2);
        AddEditorRow(editor, 0, "已保存命令", selector);
        AddEditorRow(editor, 1, "功能描述", _customName);
        var categoryEditor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        categoryEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        categoryEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _customCategory.DropDownStyle = ComboBoxStyle.DropDownList;
        _customCategory.Dock = DockStyle.Fill;
        var addCustomCategory = new Button { Text = "新建分类", Dock = DockStyle.Fill };
        addCustomCategory.Click += (_, _) =>
        {
            var category = AddCategory();
            if (category.Length > 0) _customCategory.SelectedItem = category;
        };
        categoryEditor.Controls.Add(_customCategory, 0, 0);
        categoryEditor.Controls.Add(addCustomCategory, 1, 0);
        AddEditorRow(editor, 2, "归属分类", categoryEditor);
        AddEditorRow(editor, 3, "执行命令", _commandName);
        _commandName.CharacterCasing = CharacterCasing.Upper;
        _commandName.ImeMode = ImeMode.Disable;

        var hint = new Label
        {
            Text = "只填写英文命令名，例如 S3、SUN-173、S197。滑向轮盘格并松开后直接触发，不经过插件文本框、模拟键盘或输入法。",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = "muted"
        };
        editor.Controls.Add(hint, 0, 4);
        editor.SetColumnSpan(hint, 2);

        var assignPanel = BuildAssignmentBar("应用并添加到轮盘", _customTarget, () =>
        {
            if (SaveCustom()) AssignSelectedCustom();
        });
        editor.Controls.Add(assignPanel, 0, 5);
        editor.SetColumnSpan(assignPanel, 2);

        var note = new Label
        {
            Text = "先在轮盘预览中选择格子，再添加命令。添加后自动选择下一个空格。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AccentDark
        };
        editor.Controls.Add(note, 0, 6);
        editor.SetColumnSpan(note, 2);
        page.Controls.Add(editor);
        return page;
    }

    private TabPage BuildBasicTab()
    {
        var page = new TabPage("基础") { Padding = new Padding(16), AutoScroll = true };
        var title = new Label { Text = "触发与常用设置", Dock = DockStyle.Top, Height = 44, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(0, 8, 0, 12), ColumnCount = 2, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _trigger.DropDownStyle = ComboBoxStyle.DropDownList;
        _trigger.DisplayMember = nameof(Choice.Label);
        foreach (var pair in TriggerModes.All)
            _trigger.Items.Add(new Choice(pair.Key, pair.Value));
        ConfigureNumeric(_holdDelay, 100, 3000, 10);
        AddEditorRow(table, 0, "触发方式", _trigger);
        AddEditorRow(table, 1, "长按时间 ms", _holdDelay);
        var audit = new Button { Text = "检查全部快捷命令重复 / 占用", Dock = DockStyle.Left, AutoSize = true };
        audit.Click += (_, _) => ShowShortcutUsageReport();
        AddEditorRow(table, 2, "输入法自动切换", BuildImeBasicEditor());
        AddEditorRow(table, 3, "插件界面配色", BuildPluginAppearanceControl());
        var menuRestore = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, MinimumSize = new Size(0, 70) };
        var restoreMenu = new Button { Text = "显示 CAD 菜单栏并恢复小阳菜单", AutoSize = true, Enabled = _requestMenuRestore != null };
        restoreMenu.Click += (_, _) => _requestMenuRestore?.Invoke();
        menuRestore.Controls.Add(restoreMenu);
        menuRestore.Controls.Add(new Label { Text = "仅点击时将 MENUBAR 设为 1，并恢复小阳菜单。", AutoSize = true, MaximumSize = new Size(450, 0), ForeColor = Muted, Margin = new Padding(3, 6, 3, 6) });
        AddEditorRow(table, 4, "菜单栏恢复", menuRestore);

        var hint = new Label
        {
            Text = "触发和输入法设置对全部轮盘样式共同生效。外观请在“轮盘编辑”中调整，导入、导出和重置请进入“备份与维护”。",
            Dock = DockStyle.Top,
            Height = 52,
            ForeColor = Muted,
            Tag = "muted"
        };
        page.Controls.Add(hint);
        page.Controls.Add(table);
        page.Controls.Add(title);
        return page;
    }

    private Control BuildCellFontSizeEditor()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 36 };
        panel.MaximumSize = new Size(260, 0);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        _cellFontSize.Dock = DockStyle.Fill;
        _autoFitCellFont.Text = "自动适配格子";
        _autoFitCellFont.AutoSize = true;
        _autoFitCellFont.Dock = DockStyle.Fill;
        _autoFitCellFont.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_cellFontSize, 0, 0);
        panel.Controls.Add(_autoFitCellFont, 1, 0);
        return panel;
    }

    private Control BuildImeBasicEditor()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 38 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        _imeBasicStatus.Dock = DockStyle.Fill;
        _imeBasicStatus.TextAlign = ContentAlignment.MiddleLeft;
        _imeBasicStatus.ForeColor = Muted;
        _imeBasicStatus.Tag = "muted";
        var open = new Button { Text = "打开输入法设置", Dock = DockStyle.Fill, Tag = "primary" };
        open.Click += (_, _) => OpenImeSettingsFromBasicPage();
        panel.Controls.Add(_imeBasicStatus, 0, 0);
        panel.Controls.Add(open, 1, 0);
        return panel;
    }

    private void OpenImeSettingsFromBasicPage()
    {
        _config.ImeSwitch ??= new ImeSwitchConfig();
        using var dialog = new ImeSettingsDialog(_config.ImeSwitch, updated =>
        {
            _config.ImeSwitch = updated;
            _saved(_config);
            UpdateImeBasicStatus();
        });
        dialog.ShowDialog(this);
    }

    private void UpdateImeBasicStatus()
    {
        var enabled = _config.ImeSwitch?.Enabled == true;
        var commandCount = _config.ImeSwitch?.TextCommands?.Count ?? 0;
        var preference = _config.ImeSwitch?.DirectInputUsesEnglish == true
            ? "直接输入英文 / 勾选命令中文"
            : "勾选命令中文";
        _imeBasicStatus.Text = enabled
            ? $"已启用 · {preference} · {commandCount} 个中文输入命令"
            : "当前未启用 · 点击右侧按钮进行设置";
        _imeBasicStatus.ForeColor = enabled ? AccentDark : Muted;
    }

    private Control BuildFontFamilyEditor()
    {
        var panel = CreateResponsiveEditorFlow();
        _fontFamily.Dock = DockStyle.Fill;
        _fontFamily.DropDownStyle = ComboBoxStyle.DropDownList;
        LoadInstalledFontFamilies();
        _applyCurrentFont.Text = "当前格";
        _applyCurrentFont.Dock = DockStyle.Fill;
        _applyCurrentFont.Click += (_, _) => ApplySelectedCellFontFamily();
        _applyAllFonts.Text = "全部格";
        _applyAllFonts.Dock = DockStyle.Fill;
        _applyAllFonts.Click += (_, _) => ApplyAllCellFontFamilies();
        _resetFontFamily.Text = "恢复";
        _resetFontFamily.AutoSize = false;
        _resetFontFamily.Dock = DockStyle.Fill;
        _resetFontFamily.TextAlign = ContentAlignment.MiddleCenter;
        _resetFontFamily.LinkColor = AccentDark;
        _resetFontFamily.Click += (_, _) => ResetSelectedCellFontFamily();
        panel.Controls.Add(_fontFamily);
        panel.Controls.Add(_applyCurrentFont);
        panel.Controls.Add(_applyAllFonts);
        panel.Controls.Add(_resetFontFamily);
        PrepareResponsiveEditorFlow(panel);
        return panel;
    }


    private void LoadInstalledFontFamilies()
    {
        var names = new List<string> { "宋体" };
        try
        {
            using var fonts = new InstalledFontCollection();
            names.AddRange(fonts.Families.Select(family => family.Name));
        }
        catch { }
        _fontFamily.Items.Clear();
        foreach (var name in names
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(name => name.Equals("宋体", StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                     .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase))
            _fontFamily.Items.Add(name);
        SelectFontFamilyInEditor("宋体");
    }

    private Control BuildTextStyleEditor()
    {
        var panel = CreateResponsiveEditorFlow();
        _fontBold.Text = "加粗";
        _fontBold.Dock = DockStyle.Fill;
        _fontBold.TextAlign = ContentAlignment.MiddleLeft;
        _textEffect.Dock = DockStyle.Fill;
        _textEffect.DropDownStyle = ComboBoxStyle.DropDownList;
        _textEffect.DisplayMember = nameof(Choice.Label);
        foreach (var pair in TextEffects.All) _textEffect.Items.Add(new Choice(pair.Key, pair.Value));
        _applyCurrentTextStyle.Text = "当前格";
        _applyCurrentTextStyle.Dock = DockStyle.Fill;
        _applyCurrentTextStyle.Click += (_, _) => ApplySelectedCellTextStyle();
        _applyAllTextStyles.Text = "全部格";
        _applyAllTextStyles.Dock = DockStyle.Fill;
        _applyAllTextStyles.Click += (_, _) => ApplyAllCellTextStyles();
        _resetTextStyle.Text = "恢复";
        _resetTextStyle.AutoSize = false;
        _resetTextStyle.Dock = DockStyle.Fill;
        _resetTextStyle.TextAlign = ContentAlignment.MiddleCenter;
        _resetTextStyle.LinkColor = AccentDark;
        _resetTextStyle.Click += (_, _) => ResetSelectedCellTextStyle();
        _applyPlainTextStyle.Text = "普通文字";
        _applyPlainTextStyle.Dock = DockStyle.Fill;
        _applyPlainTextStyle.Click += (_, _) => ApplyPlainTextStyle();
        panel.Controls.Add(_fontBold);
        panel.Controls.Add(_textEffect);
        panel.Controls.Add(_applyCurrentTextStyle);
        panel.Controls.Add(_applyAllTextStyles);
        panel.Controls.Add(_resetTextStyle);
        panel.Controls.Add(_applyPlainTextStyle);
        PrepareResponsiveEditorFlow(panel);
        return panel;
    }


    private Control BuildMenuOpacityEditor()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Height = 38 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        _menuOpacity.Minimum = 20;
        _menuOpacity.Maximum = 100;
        _menuOpacity.TickStyle = TickStyle.None;
        _menuOpacity.Dock = DockStyle.Fill;
        _menuOpacityValue.Dock = DockStyle.Fill;
        _menuOpacityValue.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_menuOpacity, 0, 0);
        panel.Controls.Add(_menuOpacityValue, 1, 0);
        return panel;
    }

    private Control BuildTextColorPicker()
    {
        var panel = CreateResponsiveEditorFlow();
        _textColor.ReadOnly = true;
        _textColor.Dock = DockStyle.Fill;
        _textColorSwatch.Dock = DockStyle.Fill;
        _textColorSwatch.Margin = new Padding(6, 4, 6, 4);
        _textColorSwatch.BorderStyle = BorderStyle.FixedSingle;
        _textColorSwatch.Cursor = Cursors.Hand;
        _textColorSwatch.AccessibleName = "打开当前格文字色盘";
        _textColorSwatch.Click += (_, _) => ChooseTextColor(applyToAll: false);
        _chooseTextColor.Text = "当前格色盘";
        _chooseTextColor.AutoSize = false;
        _chooseTextColor.Dock = DockStyle.Fill;
        _chooseTextColor.TextAlign = ContentAlignment.MiddleCenter;
        _chooseTextColor.LinkColor = AccentDark;
        _chooseTextColor.Click += (_, _) => ChooseTextColor(applyToAll: false);
        _chooseAllTextColor.Text = "全部格色盘";
        _chooseAllTextColor.AutoSize = false;
        _chooseAllTextColor.Dock = DockStyle.Fill;
        _chooseAllTextColor.TextAlign = ContentAlignment.MiddleCenter;
        _chooseAllTextColor.LinkColor = AccentDark;
        _chooseAllTextColor.Click += (_, _) => ChooseTextColor(applyToAll: true);
        _resetTextColor.Text = "恢复";
        _resetTextColor.AutoSize = false;
        _resetTextColor.Dock = DockStyle.Fill;
        _resetTextColor.TextAlign = ContentAlignment.MiddleCenter;
        _resetTextColor.LinkColor = AccentDark;
        _resetTextColor.Click += (_, _) => ResetSelectedCellTextColor();
        panel.Controls.Add(_textColor);
        panel.Controls.Add(_textColorSwatch);
        panel.Controls.Add(_chooseTextColor);
        panel.Controls.Add(_chooseAllTextColor);
        panel.Controls.Add(_resetTextColor);
        PrepareResponsiveEditorFlow(panel);
        return panel;
    }


    private Control BuildWheelClearTools()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(0, 10, 0, 6),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true
        };
        var clearCell = new Button { Text = "清空当前格子", AutoSize = true };
        clearCell.Click += (_, _) => ClearSelectedWheelCell();
        var clearWheel = new Button { Text = "清空当前轮盘", AutoSize = true, Tag = "danger" };
        clearWheel.Click += (_, _) => ClearCurrentWheel();
        panel.Controls.Add(clearCell);
        panel.Controls.Add(clearWheel);
        return panel;
    }

    private Control BuildSettingsTools()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(0, 12, 0, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true
        };
        var export = new Button { Text = "导出设置备份", AutoSize = true };
        export.Click += (_, _) => ExportSettingsBackup();
        var import = new Button { Text = "导入设置备份", AutoSize = true };
        import.Click += (_, _) => ImportSettingsBackup();
        var reset = new Button { Text = "恢复默认设置", AutoSize = true, Tag = "danger" };
        reset.Click += (_, _) => ResetSettings();
        panel.Controls.Add(export);
        panel.Controls.Add(import);
        panel.Controls.Add(reset);
        var update = new Button { Text = "一键更新", AutoSize = true, Name = "OneClickUpdate" };
        update.Click += (_, _) =>
        {
            using var dialog = new PluginUpdateDialog();
            dialog.ShowDialog(this);
        };
        panel.Controls.Add(update);
        return panel;
    }

    private Control BuildBackgroundPicker()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Height = 36 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        _backgroundPath.Dock = DockStyle.Fill;
        _backgroundPath.ReadOnly = true;
        var choose = new Button { Text = "选图", Dock = DockStyle.Fill };
        choose.Click += (_, _) => ChooseBackground();
        var clear = new Button { Text = "清除背景", Dock = DockStyle.Fill };
        clear.Click += (_, _) => ClearBackground();
        panel.Controls.Add(_backgroundPath, 0, 0);
        panel.Controls.Add(choose, 1, 0);
        panel.Controls.Add(clear, 2, 0);
        return panel;
    }

    private Control BuildBackgroundOpacityEditor()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 38 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        _backgroundOpacity.Minimum = 0;
        _backgroundOpacity.Maximum = 100;
        _backgroundOpacity.TickStyle = TickStyle.None;
        _backgroundOpacity.Dock = DockStyle.Fill;
        _backgroundOpacityValue.Dock = DockStyle.Fill;
        _backgroundOpacityValue.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_backgroundOpacity, 0, 0);
        panel.Controls.Add(_backgroundOpacityValue, 1, 0);
        return panel;
    }

    private Panel BuildAssignmentBar(string buttonText, AssignmentTarget assignment, Action action)
    {
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(10), BackColor = Color.FromArgb(235, 242, 245), Tag = "assignment" };
        var assign = new Button { Text = buttonText, AutoSize = true, Dock = DockStyle.Right, Tag = "primary" };
        assign.Click += (_, _) => action();
        ConfigureNumeric(assignment.Ring, 1, RingConfig.MaximumRingCount, 1);
        ConfigureNumeric(assignment.Slot, 1, 32, 1);
        assignment.Ring.ValueChanged += (_, _) => UpdateAssignmentSlotLimit(assignment);

        assignment.Ring.Visible = false;
        assignment.Slot.Visible = false;
        assignment.Status.Dock = DockStyle.Fill;
        assignment.Status.TextAlign = ContentAlignment.MiddleLeft;
        assignment.Status.ForeColor = AccentDark;
        assignment.Status.Text = "位置：第 1 圈 / 第 1 格（点轮盘预览选择）";
        panel.Controls.Add(assignment.Status);
        panel.Controls.Add(assign);
        return panel;
    }

    private void BuildBatchBar()
    {
        _batchBar.Dock = DockStyle.Bottom;
        _batchBar.Height = 58;
        _batchBar.Padding = new Padding(10);
        _batchBar.BackColor = Color.FromArgb(235, 242, 245);
        _batchBar.Visible = false;
        var move = new Button { Text = "批量分类", AutoSize = true, Dock = DockStyle.Right, Tag = "primary" };
        move.Click += (_, _) => BatchMoveToCategory();
        var invert = new Button { Text = "反选", AutoSize = true, Dock = DockStyle.Right };
        invert.Click += (_, _) => InvertBatchChecks();
        var all = new Button { Text = "全选", AutoSize = true, Dock = DockStyle.Right };
        all.Click += (_, _) => SelectAllBatchChecks();
        _batchStatus.Dock = DockStyle.Fill;
        _batchStatus.TextAlign = ContentAlignment.MiddleLeft;
        _batchStatus.ForeColor = AccentDark;
        _batchStatus.Text = "已选择 0 项";
        _batchBar.Controls.Add(_batchStatus);
        _batchBar.Controls.Add(move);
        _batchBar.Controls.Add(invert);
        _batchBar.Controls.Add(all);
    }

    private void EnsureSettingsCatalog()
    {
        if (_catalogLoadPending) LoadCatalog(false);
    }

    private void LoadCatalog(bool force)
    {
        _catalogLoadPending = false;
        try
        {
            UseWaitCursor = true;
            _allCommands = _catalog.GetAll(force);
            FilterCatalog();
            UpdateCatalogStatus();
        }
        catch (Exception ex)
        {
            Logger.Error($"读取 {CadHostInfo.ProductName} 命令目录失败。", ex);
            MessageBox.Show(this, ex.Message, "命令读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    internal void ReloadCommandCatalogFromCache() => LoadCatalog(false);

    private void RequestLocalCommandRestore()
    {
        if (_requestLocalCommandRestore == null)
        {
            MessageBox.Show(this, "重新读取本地命令只能在 CAD 内运行。", "重新读取本地命令",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _catalogStatus.Text = "正在重新读取当前主 CUI、局部 CUIX 和 PGP 命令快照…";
        _requestLocalCommandRestore();
    }

    internal void ApplyLocalCommandRestoreResult(LocalCommandRestoreResult result)
    {
        _allCommands = result.Commands;
        FilterCatalog();
        UpdateCatalogStatus();
        MessageBox.Show(this,
            $"已重新读取本地命令 {result.CommandCount} 个，其中 CAD/小阳命令 {result.NativeCommandCount} 个、第三方命令 {result.ThirdPartyCommandCount} 个。\n" +
            $"当前 {CadHostInfo.PgpFileName} 仅被读取，未删除、改写或重载任何快捷键；轮盘配置和自定义命令保持不变。",
            "重新读取本地命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void FilterCatalog()
    {
        var keyword = _search.Text.Trim();
        var builtInCommands = _allCommands
            .Where(command => command.CanCallDirectly)
            .Where(command => _commandSource.SelectedIndex != 3 && (_commandSource.SelectedIndex != 1 || command.IsCoreCommand) && (_commandSource.SelectedIndex != 2 || !command.IsCoreCommand))
            .Where(command => keyword.Length == 0
                || GetCommandDisplayName(command).Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || command.DisplayName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || command.GlobalName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || command.Aliases.Any(alias => alias.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                || CommandCategoryCatalog.Categorize(command, _config).Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || command.Help.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            .GroupBy(command => command.GlobalName, StringComparer.OrdinalIgnoreCase)
            .Select(CadCommandCatalog.SelectBestLocalizedCandidate)
            .ToList();
        var customCommands = _config.CustomCommands
            .Where(command => _commandSource.SelectedIndex <= 0 || _commandSource.SelectedIndex == 3)
            .Where(command => keyword.Length == 0
                || command.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || GetCommandDisplayName(command).Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || command.CommandName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || command.Category.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        var categoryOrder = CommandCategoryCatalog.GetAll(_config);
        var categorizedCommands = builtInCommands.ToLookup(command => CommandCategoryCatalog.Categorize(command, _config), StringComparer.CurrentCultureIgnoreCase);
        var categorizedCustom = customCommands.ToLookup(command => command.Category, StringComparer.CurrentCultureIgnoreCase);

        EnsureCatalogImages();
        var roots = new List<TreeNode>();
        foreach (var category in categoryOrder)
        {
            var commands = categorizedCommands[category]
                .OrderBy(GetCommandDisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(command => command.GlobalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var custom = categorizedCustom[category]
                .OrderBy(command => command.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(command => command.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var isManagedCategory = !CommandCategoryCatalog.IsBuiltIn(category);
            if (commands.Count + custom.Count == 0 && !isManagedCategory) continue;

            var root = new TreeNode($"{category}  ({commands.Count + custom.Count})")
            {
                ForeColor = AccentDark,
                Tag = new CategoryNodeTag(category,
                    isManagedCategory && !category.Equals(CommandCategoryCatalog.DefaultCustomCategory, StringComparison.CurrentCultureIgnoreCase))
            };
            foreach (var command in commands)
            {
                var aliasText = command.Aliases.Count == 0
                    ? ""
                    : $" · 快捷键 {string.Join("/", command.Aliases)}";
                root.Nodes.Add(new TreeNode($"{CatalogDisplayLabel(command.DisplayName, GetCommandDisplayName(command))}    [{command.GlobalName}{aliasText}]")
                {
                    Tag = command,
                    ToolTipText = command.Help
                });
            }
            foreach (var command in custom)
            {
                root.Nodes.Add(new TreeNode($"★ {CatalogDisplayLabel(command.Name, GetCommandDisplayName(command))}    [{command.CommandName}]")
                {
                    Tag = command,
                    ForeColor = AccentDark,
                    ToolTipText = "自定义命令"
                });
            }
            // Assign icons while detached: setting ImageKey on thousands of
            // live TreeNodes sends a native update for every individual row.
            ApplyCatalogIcon(root);
            foreach (TreeNode node in root.Nodes) ApplyCatalogIcon(node);
            roots.Add(root);
        }
        _catalogTree.BeginUpdate();
        try
        {
            _catalogTree.Nodes.Clear();
            _catalogTree.Nodes.AddRange(roots.ToArray());
        }
        finally { _catalogTree.EndUpdate(); }

        if (keyword.Length > 0 && _expandSearchCommandTree.Checked)
            _catalogTree.ExpandAll();
        ResetCatalogTreeToTop();
        _catalogDetails.Text = builtInCommands.Count + customCommands.Count == 0
            ? "没有找到匹配命令"
            : "展开分类，选择一个命令";
        UpdateCatalogStatus();
    }

    private void ResetCatalogTreeToTop()
    {
        _catalogTree.SelectedNode = null;
        if (_catalogTree.Nodes.Count == 0) return;
        _catalogTree.TopNode = _catalogTree.Nodes[0];
        _catalogTree.Nodes[0].EnsureVisible();
    }

    private void ShowCatalogDetails()
    {
        if (_catalogTree.SelectedNode?.Tag is CadCommandInfo command)
        {
            var help = string.IsNullOrWhiteSpace(command.Help) ? CadHostInfo.ProductName + " 命令" : command.Help.Trim();
            var displayName = GetCommandDisplayName(command);
            var renamed = displayName.Equals(command.DisplayName, StringComparison.CurrentCulture) ? "" : " · 原名称：" + command.DisplayName;
            _catalogDetails.Text = $"{displayName}  ·  {command.GlobalName}{renamed}\n{help}";
            return;
        }
        if (_catalogTree.SelectedNode?.Tag is CustomCommandConfig custom)
        {
            _catalogDetails.Text = $"{CatalogDisplayLabel(custom.Name, GetCommandDisplayName(custom))}  ·  {custom.CommandName}\n自定义命令 · {custom.Category}";
            return;
        }
        _catalogDetails.Text = _catalogTree.SelectedNode?.Text ?? "展开分类，选择一个命令";
    }

    private void UpdateCatalogStatus()
    {
        var callable = _allCommands.Where(command => command.CanCallDirectly)
            .Select(command => command.GlobalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var thirdParty = _allCommands.Count(command =>
            CommandCategoryCatalog.Categorize(command, _config) == CommandCategoryCatalog.ThirdPartyCategory);
        _catalogStatus.Text = $"CAD 命令 {callable} 个 · 第三方命令 {thirdParty} 个 · 自定义命令 {_config.CustomCommands.Count} 个 · 分类 {CommandCategoryCatalog.GetAll(_config).Count} 个";
    }

    private void AssignSelectedCatalogItem()
    {
        if (_catalogTree.SelectedNode?.Tag is CadCommandInfo command)
        {
            AssignToWheel(GetCommandDisplayName(command), ActionKinds.CadCommand, command.GlobalName, _catalogTarget);
            return;
        }
        if (_catalogTree.SelectedNode?.Tag is CustomCommandConfig custom)
        {
            AssignToWheel(GetCommandDisplayName(custom), ActionKinds.CustomCommand, custom.Id, _catalogTarget);
            return;
        }
        // 分类节点只负责选择和展开/收起。双击分类时 TreeView 会自行切换展开状态，
        // 不应把分类误当成可执行命令，也不弹出干扰提示。
        if (_catalogTree.SelectedNode?.Tag is CategoryNodeTag) return;
        MessageBox.Show(this, "请展开分类并选择具体命令，不能直接添加分类节点。", "未选择命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string GetCommandDisplayName(CadCommandInfo command)
    {
        if (command == null) return "";
        var key = CadCommandCatalog.NormalizeCommandName(command.GlobalName);
        return _config.CommandDisplayNameOverrides != null
               && _config.CommandDisplayNameOverrides.TryGetValue(key, out var renamed)
               && !string.IsNullOrWhiteSpace(renamed)
            ? renamed.Trim()
            : command.DisplayName;
    }

    private void RenameSelectedCatalogCommand()
    {
        switch (_catalogTree.SelectedNode?.Tag)
        {
            case CadCommandInfo command:
                RenameBuiltInCommand(command);
                break;
            case CustomCommandConfig custom:
                RenameCustomCommand(custom);
                break;
            default:
                MessageBox.Show(this, "请选择一个具体命令后再改名。分类名称请使用“新建分类”或分类管理功能。",
                    "未选择命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
        }
    }

    private void RenameBuiltInCommand(CadCommandInfo command)
    {
        var key = CadCommandCatalog.NormalizeCommandName(command.GlobalName);
        using var dialog = new CommandRenameDialog(command.GlobalName, GetCommandDisplayName(command), command.DisplayName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.RestoreOriginalName)
            _config.CommandDisplayNameOverrides.Remove(key);
        else
            _config.CommandDisplayNameOverrides[key] = dialog.CommandDisplayName;
        var displayName = dialog.RestoreOriginalName ? command.DisplayName : dialog.CommandDisplayName;
        UpdateWheelLabels(ActionKinds.CadCommand, key, displayName);
        SaveRenamedCommandAndRefresh(key, customId: "");
    }

    private void RenameCustomCommand(CustomCommandConfig command)
    {
        using var dialog = new CommandRenameDialog(command.CommandName, GetCommandDisplayName(command), command.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        command.DisplayNameOverride = dialog.RestoreOriginalName ? "" : dialog.CommandDisplayName;
        UpdateWheelLabels(ActionKinds.CustomCommand, command.Id, GetCommandDisplayName(command));
        RefreshCustomList();
        SaveRenamedCommandAndRefresh(commandName: "", customId: command.Id);
    }

    private void UpdateWheelLabels(string kind, string value, string displayName)
    {
        foreach (var item in _config.Menus.Values
                     .Where(menu => menu?.Rings != null)
                     .SelectMany(menu => menu.Rings.Where(ring => ring?.Items != null).SelectMany(ring => ring.Items)
                         .Concat(menu.UnassignedItems ?? new List<MenuItemConfig>()))
                     .Where(item => item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
                                    && (kind == ActionKinds.CadCommand
                                        ? CadCommandCatalog.NormalizeCommandName(item.Value).Equals(value, StringComparison.OrdinalIgnoreCase)
                                        : item.Value.Equals(value, StringComparison.OrdinalIgnoreCase))))
            item.Label = displayName;
    }

    private void SaveRenamedCommandAndRefresh(string commandName, string customId)
    {
        _saved(_config);
        RefreshWheelGrid();
        RefreshPreview();
        FilterCatalog();
        SelectCatalogCommandNode(commandName, customId);
    }

    private void SelectCatalogCommandNode(string commandName, string customId)
    {
        foreach (TreeNode root in _catalogTree.Nodes)
        foreach (TreeNode node in root.Nodes)
        {
            var match = node.Tag is CadCommandInfo builtIn
                ? commandName.Length > 0 && CadCommandCatalog.NormalizeCommandName(builtIn.GlobalName)
                    .Equals(commandName, StringComparison.OrdinalIgnoreCase)
                : node.Tag is CustomCommandConfig custom && customId.Length > 0
                  && custom.Id.Equals(customId, StringComparison.OrdinalIgnoreCase);
            if (!match) continue;
            root.Expand();
            _catalogTree.SelectedNode = node;
            node.EnsureVisible();
            return;
        }
    }

    private void AssignSelectedCustom()
    {
        var command = _config.CustomCommands.FirstOrDefault(item => item.Id == _editingCustomId);
        if (command == null)
        {
            MessageBox.Show(this, "请先选择或保存一个自定义命令。", "未选择", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        AssignToWheel(GetCommandDisplayName(command), ActionKinds.CustomCommand, command.Id, _customTarget);
    }

    private void AssignToWheel(string label, string kind, string value, AssignmentTarget assignment)
    {
        var assignedRing = (int)assignment.Ring.Value;
        var assignedSlot = (int)assignment.Slot.Value;
        var ring = ResolveTargetRing(assignment);
        var slot = assignedSlot - 1;
        var previousItem = ring.Items.FirstOrDefault(item => item.Slot == slot);
        if (previousItem != null && (previousItem.Kind != kind || previousItem.Value != value))
            _config.Menus[SceneKeys.Global].UnassignedItems.Add(previousItem);
        var previousColor = previousItem?.TextColor ?? "";
        var previousFontSize = previousItem?.FontSizePx ?? 0;
        var previousFontFamily = previousItem?.FontFamily ?? "";
        ring.Items.RemoveAll(item => item.Slot == slot);
        ring.Items.Add(new MenuItemConfig
        {
            Slot = slot,
            Label = label,
            IconData = previousItem != null && previousItem.Kind == kind && previousItem.Value == value ? previousItem.IconData : "",
            ImageMode = previousItem != null && previousItem.Kind == kind && previousItem.Value == value ? previousItem.ImageMode : "Icon",
            ContentMode = previousItem?.ContentMode ?? "Inherit",
            IconRatioPercent = previousItem?.IconRatioPercent ?? 55,
            CellSizePercent = previousItem?.CellSizePercent ?? 100,
            CellShape = previousItem?.CellShape ?? "Circle",
            FreeX = previousItem?.FreeX,
            FreeY = previousItem?.FreeY,
            Kind = kind,
            Value = value,
            TextColor = previousColor,
            FontSizePx = previousFontSize,
            FontFamily = previousFontFamily,
            FontBold = previousItem?.FontBold,
            TextEffect = previousItem?.TextEffect ?? ""
        });
        _saved(_config);
        RefreshWheelGrid();
        RefreshPreview();
        LoadSelectedCellAppearance();
        SelectFirstEmptyWheelCell();
        MessageBox.Show(this, $"已添加到全局轮盘：第 {assignedRing} 圈 / 第 {assignedSlot} 格。已自动选择下一个空格。",
            "分配完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private RingConfig ResolveTargetRing(AssignmentTarget assignment)
    {
        var scene = _config.Menus[SceneKeys.Global];
        var index = (int)assignment.Ring.Value - 1;
        while (scene.Rings.Count <= index)
            scene.Rings.Add(new RingConfig { Index = scene.Rings.Count, Slots = 8 });
        var ring = scene.Rings[index];
        ring.Slots = Math.Max(ring.Slots, (int)assignment.Slot.Value);
        return ring;
    }

    private void LoadConfigToUi()
    {
        _loadingUi = true;
        try
        {
        var triggerChoices = _trigger.Items.Cast<Choice>().ToList();
        var triggerIndex = triggerChoices.FindIndex(choice =>
            choice.Value.Equals(_config.TriggerMode, StringComparison.OrdinalIgnoreCase));
        _trigger.SelectedIndex = Math.Max(0, triggerIndex);
        _holdDelay.Value = RuntimeCompat.Clamp(_config.HoldDelayMs, (int)_holdDelay.Minimum, (int)_holdDelay.Maximum);
        _menuSize.Value = RuntimeCompat.Clamp(_config.MenuSizePx, (int)_menuSize.Minimum, (int)_menuSize.Maximum);
        _centerRadius.Value = RuntimeCompat.Clamp(_config.CenterRadiusPx, (int)_centerRadius.Minimum, (int)_centerRadius.Maximum);
        _cellFontSize.Value = RuntimeCompat.Clamp(_config.FontSizePx, (int)_cellFontSize.Minimum, (int)_cellFontSize.Maximum);
        _menuOpacity.Value = RuntimeCompat.Clamp(_config.OpacityPercent, _menuOpacity.Minimum, _menuOpacity.Maximum);
        _menuOpacityValue.Text = _menuOpacity.Value + "%";
        _expandSearchCommandTree.Checked = _config.ExpandSearchCommandTree;
        UpdateImeBasicStatus();
        if (_config.Menus.TryGetValue(SceneKeys.Global, out var wheel))
        {
            _wheelStyle.SelectedIndex = wheel.StyleId == "01" ? 1 : wheel.StyleId == "11" ? 2 : wheel.StyleId == "06" ? 3 : wheel.StyleId == "orbit" ? 4 : wheel.StyleId == "free" ? 5 : 0;
            _backgroundPath.Text = wheel.BackgroundImagePath ?? "";
            _backgroundOpacity.Value = RuntimeCompat.Clamp(wheel.BackgroundOpacityPercent, _backgroundOpacity.Minimum, _backgroundOpacity.Maximum);
            _backgroundOpacityValue.Text = _backgroundOpacity.Value + "%";
            _ringCount.Value = RuntimeCompat.Clamp(wheel.Rings.Count(ring => ring.Enabled), 1, RingConfig.MaximumRingCount);
            _catalogTarget.Ring.Maximum = _ringCount.Value;
            _customTarget.Ring.Maximum = _ringCount.Value;
            RebuildRingEditors();
            UpdateStyleEditors();
        }
        RefreshCustomList();
        RefreshWheelGrid();
        RefreshManagedExtensionRows();
        }
        finally { _loadingUi = false; }
    }

    private void RefreshCustomList()
    {
        var selected = _editingCustomId;
        _customList.DataSource = null;
        _customList.DataSource = _config.CustomCommands.ToList();
        _customList.DisplayMember = nameof(CustomCommandConfig.Name);
        if (selected.Length > 0)
            _customList.SelectedItem = _config.CustomCommands.FirstOrDefault(item => item.Id == selected);
        RefreshCategoryOptions();
    }

    private void RefreshCategoryOptions(string preferred = null)
    {
        preferred ??= _customCategory.SelectedItem as string;
        var categories = CommandCategoryCatalog.GetAll(_config).ToList();
        _customCategory.DataSource = null;
        _customCategory.DataSource = categories;
        var match = categories.FirstOrDefault(category => category.Equals(preferred, StringComparison.CurrentCultureIgnoreCase))
                    ?? CommandCategoryCatalog.DefaultCustomCategory;
        _customCategory.SelectedItem = match;
    }

    private void LoadSelectedCustom()
    {
        if (_customList.SelectedItem is not CustomCommandConfig command) return;
        _editingCustomId = command.Id;
        _customName.Text = command.Name;
        _commandName.Text = command.CommandName;
        RefreshCategoryOptions(command.Category);
    }

    private void BeginNewCustom()
    {
        _customList.SelectedIndex = -1;
        _editingCustomId = "";
        _customName.Text = "";
        _commandName.Text = "";
        RefreshCategoryOptions(CommandCategoryCatalog.DefaultCustomCategory);
        _customName.Focus();
    }

    private bool SaveCustom()
    {
        var name = _customName.Text.Trim();
        var rawCommandName = _commandName.Text.Trim();
        if (name.Length == 0 || rawCommandName.Length == 0)
        {
            MessageBox.Show(this, "请填写功能描述和执行命令。", "内容不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!CadCommandCatalog.IsSafeCommandName(rawCommandName))
        {
            MessageBox.Show(this, "请填写单个执行命令，例如 0、1、3DORBIT 或 SUN-173；支持纯数字和数字开头，不支持空格、分号或脚本。",
                "命令格式不正确", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var commandName = CadCommandCatalog.NormalizeCommandName(rawCommandName);
        var category = CommandCategoryCatalog.NormalizeName(_customCategory.SelectedItem as string);
        if (category.Length == 0) category = CommandCategoryCatalog.DefaultCustomCategory;

        var command = _config.CustomCommands.FirstOrDefault(item => item.Id == _editingCustomId);
        if (command == null)
        {
            command = new CustomCommandConfig();
            _config.CustomCommands.Add(command);
            _editingCustomId = command.Id;
        }
        command.Name = name;
        command.CommandName = commandName;
        command.Category = category;
        command.Arguments = new List<string>();
        command.IconPath = "";
        command.ShowIconOnly = false;
        command.Silent = true;
        command.DisableDialogs = false;
        UpdateWheelLabels(ActionKinds.CustomCommand, command.Id, GetCommandDisplayName(command));
        RefreshCustomList();
        RefreshWheelGrid();
        RefreshPreview();
        _saved(_config);
        FilterCatalog();
        return true;
    }

    private void DeleteSelectedCustom()
    {
        if (_customList.SelectedItem is not CustomCommandConfig command) return;
        if (MessageBox.Show(this, $"删除“{command.Name}”？引用它的轮盘格也会失效。", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _config.CustomCommands.Remove(command);
        foreach (var scene in _config.Menus.Values)
            foreach (var ring in scene.Rings)
                ring.Items.RemoveAll(item => item.Kind == ActionKinds.CustomCommand && item.Value == command.Id);
        BeginNewCustom();
        RefreshCustomList();
        RefreshPreview();
        _saved(_config);
        FilterCatalog();
    }

    private void DeleteCatalogSelection()
    {
        if (_batchMode)
        {
            DeleteCheckedCatalogItems();
            return;
        }
        switch (_catalogTree.SelectedNode?.Tag)
        {
            case CategoryNodeTag:
                DeleteSelectedCategory();
                break;
            case CustomCommandConfig command:
                _customList.SelectedItem = _config.CustomCommands.FirstOrDefault(item => item.Id == command.Id);
                DeleteSelectedCustom();
                break;
            case CadCommandInfo:
                MessageBox.Show(this, CadHostInfo.ProductName + " 命令不能删除。可进入“批量操作”修改分类。", "不能删除",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            default:
                MessageBox.Show(this, "请选择要删除的自定义分类或自定义命令。", "没有选择",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
        }
    }

    private string AddCategory()
    {
        var category = PromptCategoryName();
        if (category.Length == 0) return "";
        if (CommandCategoryCatalog.GetAll(_config).Any(item => item.Equals(category, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, $"分类“{category}”已经存在。", "分类已存在", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return category;
        }
        _config.CommandCategories.Add(category);
        _saved(_config);
        RefreshCategoryOptions(category);
        FilterCatalog();
        SelectCategoryNode(category);
        return category;
    }

    private string PromptCategoryName()
    {
        using var dialog = new Form
        {
            Text = "新建命令分类",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(380, 138),
            Font = Font,
            BackColor = Surface
        };
        var label = new Label { Text = "分类名称", Left = 18, Top = 20, Width = 74, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        var input = new TextBox { Left = 96, Top = 21, Width = 264, MaxLength = 24 };
        var ok = new Button { Text = "确定", Left = 190, Top = 78, Width = 80, DialogResult = DialogResult.OK, Tag = "primary" };
        var cancel = new Button { Text = "取消", Left = 280, Top = 78, Width = 80, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange(new Control[] { label, input, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        ApplyTheme(dialog);
        input.Focus();
        if (dialog.ShowDialog(this) != DialogResult.OK) return "";
        var category = CommandCategoryCatalog.NormalizeName(input.Text);
        if (category.Length > 0) return category;
        MessageBox.Show(this, "分类名称不能为空。", "无法新建", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return "";
    }

    private void DeleteSelectedCategory()
    {
        if (_catalogTree.SelectedNode?.Tag is not CategoryNodeTag category || !category.CanDelete)
        {
            MessageBox.Show(this, "请选择一个自行创建的分类。内置分类和“自定义命令”不能删除。",
                "不能删除", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var commands = _config.CustomCommands.Where(command => command.Category.Equals(category.Name, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var builtInOverrides = _config.CommandCategoryOverrides
            .Where(pair => pair.Value.Equals(category.Name, StringComparison.CurrentCultureIgnoreCase))
            .Select(pair => pair.Key)
            .ToList();
        var prompt = commands.Count == 0 && builtInOverrides.Count == 0
            ? $"删除分类“{category.Name}”？"
            : $"分类“{category.Name}”中有 {commands.Count} 个自定义命令、{builtInOverrides.Count} 个 CAD 命令。删除后，自定义命令移到“{CommandCategoryCatalog.DefaultCustomCategory}”，CAD 命令恢复自动分类。是否继续？";
        if (MessageBox.Show(this, prompt, "删除分类", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var command in commands) command.Category = CommandCategoryCatalog.DefaultCustomCategory;
        foreach (var commandName in builtInOverrides) _config.CommandCategoryOverrides.Remove(commandName);
        _config.CommandCategories.RemoveAll(item => item.Equals(category.Name, StringComparison.CurrentCultureIgnoreCase));
        _saved(_config);
        RefreshCategoryOptions(CommandCategoryCatalog.DefaultCustomCategory);
        FilterCatalog();
    }

    private void CatalogTreeAfterCheck(object sender, TreeViewEventArgs e)
    {
        if (_checkingCatalogTree) return;
        _checkingCatalogTree = true;
        try
        {
            if (!_batchMode)
            {
                e.Node.Checked = false;
                return;
            }
            if (e.Node.Tag is CategoryNodeTag)
            {
                foreach (TreeNode child in e.Node.Nodes)
                    child.Checked = e.Node.Checked;
            }
        }
        finally
        {
            _checkingCatalogTree = false;
            UpdateBatchStatus();
        }
    }

    private void SetBatchMode(bool enabled)
    {
        _batchMode = enabled;
        _batchToggle.Text = enabled ? "退出批量" : "批量操作";
        _catalogTree.CheckBoxes = enabled;
        _batchBar.Visible = enabled;
        if (_catalogAssignmentBar != null) _catalogAssignmentBar.Visible = !enabled;
        if (!enabled) ClearBatchChecks();
        UpdateBatchStatus();
    }

    private void ClearBatchChecks()
    {
        _checkingCatalogTree = true;
        try
        {
            foreach (var node in EnumerateTreeNodes(_catalogTree.Nodes)) node.Checked = false;
        }
        finally { _checkingCatalogTree = false; }
    }

    private void SelectAllBatchChecks()
    {
        _checkingCatalogTree = true;
        try
        {
            foreach (var node in EnumerateTreeNodes(_catalogTree.Nodes))
                if (node.Tag is CadCommandInfo or CustomCommandConfig) node.Checked = true;
        }
        finally { _checkingCatalogTree = false; }
        UpdateBatchStatus();
    }

    private void InvertBatchChecks()
    {
        _checkingCatalogTree = true;
        try
        {
            foreach (var node in EnumerateTreeNodes(_catalogTree.Nodes))
                if (node.Tag is CadCommandInfo or CustomCommandConfig) node.Checked = !node.Checked;
        }
        finally { _checkingCatalogTree = false; }
        UpdateBatchStatus();
    }

    private void UpdateBatchStatus()
    {
        if (!_batchMode) return;
        var selected = EnumerateTreeNodes(_catalogTree.Nodes)
            .Count(node => node.Checked && node.Tag is CadCommandInfo or CustomCommandConfig);
        _batchStatus.Text = $"已选择 {selected} 个命令 · 可批量分类；上方“删除”只删除自定义项目";
    }

    private void BatchMoveToCategory()
    {
        var selectedNodes = EnumerateTreeNodes(_catalogTree.Nodes)
            .Where(node => node.Checked && node.Tag is CadCommandInfo or CustomCommandConfig)
            .ToList();
        if (selectedNodes.Count == 0)
        {
            MessageBox.Show(this, "请先勾选需要批量分类的命令。", "没有选择命令", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var category = PromptTargetCategory();
        if (category.Length == 0) return;
        var builtIn = 0;
        var custom = 0;
        foreach (var node in selectedNodes)
        {
            if (node.Tag is CadCommandInfo command)
            {
                _config.CommandCategoryOverrides[command.GlobalName] = category;
                builtIn++;
            }
            else if (node.Tag is CustomCommandConfig customCommand)
            {
                customCommand.Category = category;
                custom++;
            }
        }
        if (!CommandCategoryCatalog.IsBuiltIn(category)
            && !_config.CommandCategories.Contains(category, StringComparer.CurrentCultureIgnoreCase))
            _config.CommandCategories.Add(category);
        _saved(_config);
        RefreshCategoryOptions(category);
        FilterCatalog();
        MessageBox.Show(this, $"已将 {builtIn} 个 CAD 命令、{custom} 个自定义命令归入“{category}”。", "批量分类完成",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string PromptTargetCategory()
    {
        using var dialog = new Form
        {
            Text = "选择目标分类",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(420, 150),
            Font = Font,
            BackColor = Surface
        };
        var label = new Label { Text = "目标分类", Left = 18, Top = 22, Width = 76, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        var combo = new ComboBox { Left = 98, Top = 22, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(CommandCategoryCatalog.GetAll(_config).Cast<object>().ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        var ok = new Button { Text = "确定", Left = 228, Top = 88, Width = 80, DialogResult = DialogResult.OK, Tag = "primary" };
        var cancel = new Button { Text = "取消", Left = 318, Top = 88, Width = 80, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange(new Control[] { label, combo, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        ApplyTheme(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK ? CommandCategoryCatalog.NormalizeName(combo.SelectedItem as string) : "";
    }

    private void DeleteCheckedCatalogItems()
    {
        var checkedNodes = EnumerateTreeNodes(_catalogTree.Nodes).Where(node => node.Checked).ToList();
        var categories = checkedNodes
            .Select(node => node.Tag)
            .OfType<CategoryNodeTag>()
            .Where(category => category.CanDelete)
            .Select(category => category.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var commandIds = checkedNodes
            .Select(node => node.Tag)
            .OfType<CustomCommandConfig>()
            .Select(command => command.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var command in _config.CustomCommands.Where(command => categories.Contains(command.Category)))
            commandIds.Add(command.Id);

        if (categories.Count == 0 && commandIds.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要删除的自定义分类或自定义命令。" + CadHostInfo.ProductName + " 内置分类和命令不会被删除。",
                "没有可删除项目", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var prompt = $"确定批量删除 {categories.Count} 个自定义分类和 {commandIds.Count} 个自定义命令吗？\n\n勾选分类时，该分类内的自定义命令会一并删除。";
        if (MessageBox.Show(this, prompt, "批量删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _config.CustomCommands.RemoveAll(command => commandIds.Contains(command.Id));
        _config.CommandCategories.RemoveAll(category => categories.Contains(category));
        foreach (var commandName in _config.CommandCategoryOverrides
                     .Where(pair => categories.Contains(pair.Value))
                     .Select(pair => pair.Key)
                     .ToList())
            _config.CommandCategoryOverrides.Remove(commandName);
        foreach (var scene in _config.Menus.Values)
            foreach (var ring in scene.Rings)
                ring.Items.RemoveAll(item => item.Kind == ActionKinds.CustomCommand && commandIds.Contains(item.Value));

        _editingCustomId = "";
        _saved(_config);
        BeginNewCustom();
        RefreshCustomList();
        RefreshWheelGrid();
        RefreshPreview();
        FilterCatalog();
        MessageBox.Show(this, $"已删除 {categories.Count} 个分类、{commandIds.Count} 个命令。", "批量删除完成",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static IEnumerable<TreeNode> EnumerateTreeNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateTreeNodes(node.Nodes)) yield return child;
        }
    }

    private void SelectCategoryNode(string category)
    {
        foreach (TreeNode node in _catalogTree.Nodes)
        {
            if (node.Tag is not CategoryNodeTag tag || !tag.Name.Equals(category, StringComparison.CurrentCultureIgnoreCase)) continue;
            _catalogTree.SelectedNode = node;
            node.EnsureVisible();
            return;
        }
    }

    private void SaveCommandImportTemplate()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "保存固定的命令批量导入模板",
            Filter = "Excel 工作簿|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            FileName = CommandImportService.TemplateFileName
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            CommandImportService.WriteTemplate(dialog.FileName);
            MessageBox.Show(this, "固定模板已保存。请保持第一行和前三列不变，从第 2 行开始填写命令。",
                "模板已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("保存命令批量导入模板失败。", ex);
            MessageBox.Show(this, ex.Message, "保存模板失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowShortcutUsageReport()
    {
        var report = BuildShortcutUsageReport();
        using var dialog = new Form
        {
            Text = "快捷命令重复 / 占用查询",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(760, 520),
            Size = new Size(900, 640),
            Font = Font,
            BackColor = Canvas,
            ShowInTaskbar = false
        };
        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Ink,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            Text = report,
            DetectUrls = false
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.OK, Tag = "primary" };
        var copy = new Button { Text = "复制结果", AutoSize = true };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(report); }
            catch (Exception ex) { Logger.Error("复制快捷命令查询结果失败。", ex); }
        };
        footer.Controls.Add(close);
        footer.Controls.Add(copy);
        dialog.Controls.Add(text);
        dialog.Controls.Add(footer);
        dialog.AcceptButton = close;
        ApplyTheme(dialog);
        dialog.ShowDialog(this);
    }

    private string BuildShortcutUsageReport()
    {
        var pgp = ReadPgpAliases(out var pgpFiles);
        var builtIn = _allCommands
            .Where(command => !string.IsNullOrWhiteSpace(command.GlobalName))
            .GroupBy(command => CadCommandCatalog.NormalizeCommandName(command.GlobalName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var pluginCommands = new HashSet<string>(new[] { "XYLP", "XYLPSZ", "XYLPZJ", "XYLPMENU", "XYLPSTART", "XYREFRESHCMDS", "XYRESTORELOCALCMDS", "XYLPIME", "XYLPIMETOGGLE" }, StringComparer.OrdinalIgnoreCase);
        var duplicates = _config.CustomCommands
            .Where(command => CadCommandCatalog.IsSafeCommandName(command.CommandName))
            .GroupBy(command => CadCommandCatalog.NormalizeCommandName(command.CommandName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var names = _config.CustomCommands
            .Select(command => CadCommandCatalog.NormalizeCommandName(command.CommandName))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        names = names
            .OrderBy(name => duplicates.TryGetValue(name, out var count) && count > 1 || pluginCommands.Contains(name) ? 0
                : !builtIn.ContainsKey(name) && !pgp.ContainsKey(name) ? 1 : 2)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var duplicateTotal = names.Count(name => duplicates.TryGetValue(name, out var count) && count > 1);
        var reservedTotal = names.Count(pluginCommands.Contains);
        var cuiTotal = names.Count(builtIn.ContainsKey);
        var pgpTotal = names.Count(pgp.ContainsKey);

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("小阳CAD轮盘插件 · 快捷命令重复 / 占用查询");
        builder.AppendLine($"查询范围：自定义命令 {names.Count} 个；CUI 命令 {builtIn.Count} 个；PGP 文件 {pgpFiles.Count} 个");
        builder.AppendLine($"结果摘要：重复名称 {duplicateTotal} 个；插件保留命令冲突 {reservedTotal} 个；CUI 已定义 {cuiTotal} 个；PGP 别名 {pgpTotal} 个");
        builder.AppendLine("说明：只显示已发现的重复、占用或已有定义；没有命中 CUI/PGP 的命令已自动省略。");
        builder.AppendLine(new string('─', 64));

        if (names.Count == 0)
        {
            builder.AppendLine("暂无可查询的自定义命令。请先填写一个英文执行命令，或保存自定义命令。");
        }
        var reported = 0;
        foreach (var name in names)
        {
            var findings = new List<string>();
            if (duplicates.TryGetValue(name, out var duplicateCount) && duplicateCount > 1)
                findings.Add($"【重复】自定义列表中有 {duplicateCount} 条使用同一执行命令");
            if (pluginCommands.Contains(name))
            {
                findings.Add("【占用】这是小阳CAD轮盘插件的保留命令");
            }
            if (builtIn.TryGetValue(name, out var command))
            {
                findings.Add($"【已定义】{CadHostInfo.ProductName} CUI：{command.DisplayName} [{command.GlobalName}]");
            }
            if (pgp.TryGetValue(name, out var aliases))
            {
                foreach (var alias in aliases.Distinct(StringComparer.OrdinalIgnoreCase))
                    findings.Add($"【PGP 别名】{name} → {alias}");
            }
            if (findings.Count == 0) continue;
            var customIds = _config.CustomCommands
                .Where(command => CadCommandCatalog.NormalizeCommandName(command.CommandName).Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(command => command.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var placements = _config.Menus.Values.SelectMany(menu => menu.Rings)
                .SelectMany(ring => ring.Items.Select(item => new { ring.Index, Item = item }))
                .Where(entry => (entry.Item.Kind == ActionKinds.CustomCommand && customIds.Contains(entry.Item.Value))
                                || (entry.Item.Kind == ActionKinds.CadCommand
                                    && CadCommandCatalog.NormalizeCommandName(entry.Item.Value).Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Select(entry => $"第 {entry.Index + 1} 圈 / 第 {entry.Item.Slot + 1} 格")
                .ToList();
            if (placements.Count > 0)
                findings.Add($"【轮盘位置】{string.Join("、", placements)}" + (placements.Count > 1 ? "（多处使用）" : ""));
            reported++;
            builder.AppendLine(name);
            foreach (var finding in findings) builder.AppendLine("  " + finding);
            builder.AppendLine();
        }
        if (names.Count > 0 && reported == 0)
            builder.AppendLine("未发现重复、插件保留命令冲突、CUI 已定义命令或 PGP 别名。\n");
        if (pgpFiles.Count > 0)
        {
            builder.AppendLine("PGP 来源：");
            foreach (var file in pgpFiles) builder.AppendLine("  " + file);
        }
        else builder.AppendLine($"未找到 {CadHostInfo.PgpFileName}；PGP 别名未参与本次查询。");
        return builder.ToString();
    }

    private Dictionary<string, List<string>> ReadPgpAliases(out List<string> sources)
    {
        sources = string.IsNullOrWhiteSpace(_catalog.PgpPath)
            ? new List<string>()
            : new List<string> { _catalog.PgpPath };
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _catalog.GetPgpAliases())
        {
            if (!result.TryGetValue(pair.Key, out var values)) result[pair.Key] = values = new List<string>();
            values.Add(pair.Value);
        }
        return result;
    }

    private void ImportCustomCommands()
    {
        using var dialog = new OpenFileDialog
        {
            Title = $"批量导入命令（仅支持固定模板：{CommandImportService.TemplateFileName}）",
            Filter = "固定 Excel 模板|*.xlsx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var parsed = CommandImportService.Read(dialog.FileName);
            var added = 0;
            var updated = 0;
            var categoriesAdded = 0;
            var errors = parsed.Errors.ToList();
            var fallbackCategory = CommandCategoryCatalog.NormalizeName(_customCategory.SelectedItem as string);
            if (fallbackCategory.Length == 0) fallbackCategory = CommandCategoryCatalog.DefaultCustomCategory;
            CustomCommandConfig last = null;
            foreach (var row in parsed.Rows)
            {
                if (!CadCommandCatalog.IsSafeCommandName(row.CommandName))
                {
                    errors.Add($"第 {row.SourceRow} 行：命令“{row.CommandName}”不是有效的单个命令名（支持纯数字；不支持空格、分号或脚本）");
                    continue;
                }
                var commandName = CadCommandCatalog.NormalizeCommandName(row.CommandName);
                var category = CommandCategoryCatalog.NormalizeName(row.Category);
                if (category.Length == 0) category = fallbackCategory;
                if (!CommandCategoryCatalog.IsBuiltIn(category)
                    && !_config.CommandCategories.Any(item => item.Equals(category, StringComparison.CurrentCultureIgnoreCase)))
                {
                    _config.CommandCategories.Add(category);
                    categoriesAdded++;
                }

                var command = _config.CustomCommands.FirstOrDefault(item => item.CommandName.Equals(commandName, StringComparison.OrdinalIgnoreCase));
                if (command == null)
                {
                    command = new CustomCommandConfig();
                    _config.CustomCommands.Add(command);
                    added++;
                }
                else updated++;
                command.Name = row.Name.Trim();
                command.CommandName = commandName;
                command.Category = category;
                command.Arguments = new List<string>();
                command.IconPath = "";
                command.ShowIconOnly = false;
                command.Silent = true;
                command.DisableDialogs = false;
                last = command;
            }
            _editingCustomId = last?.Id ?? _editingCustomId;
            _saved(_config);
            RefreshCustomList();
            FilterCatalog();
            RefreshPreview();
            var details = errors.Count == 0 ? "" : "\n\n未导入：\n" + string.Join("\n", errors.Take(6));
            if (errors.Count > 6) details += $"\n另有 {errors.Count - 6} 条错误，请检查文件。";
            var categoryNote = categoriesAdded == 0 ? "" : $"；文件中新建分类 {categoriesAdded} 个";
            MessageBox.Show(this, $"命令导入完成：新增 {added} 个，更新 {updated} 个，跳过 {errors.Count} 个{categoryNote}。{details}",
                "批量导入命令", MessageBoxButtons.OK, errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Logger.Error("批量导入自定义命令失败。", ex);
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveAndApply()
    {
        ApplyAppearanceToConfig();
        SaveAllPendingSettings();
        MessageBox.Show(this, "配置已保存并立即应用。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveAllPendingSettings()
    {
        EndPreviewEditing();
        _applyAllPendingSettings = true;
        try { _saved(_config); }
        finally { _applyAllPendingSettings = false; }
    }

    private void RefreshWheelGrid()
    {
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel))
        {
            _wheelGrid.DataSource = null;
            _wheelSummary.Text = "尚未建立全局轮盘";
            return;
        }

        var rows = wheel.Rings
            .OrderBy(ring => ring.Index)
            .SelectMany(ring => ring.Items
                .OrderBy(item => item.Slot)
                .Select(item => new WheelRow(
                    ring.Index,
                    item.Slot,
                    ring.Index + 1,
                    item.Slot + 1,
                    item.Label,
                    item.Kind == ActionKinds.CustomCommand
                        ? _config.CustomCommands.FirstOrDefault(command => command.Id == item.Value)?.CommandName ?? "自定义命令"
                        : item.Value)))
            .ToList();
        _wheelGrid.DataSource = new BindingList<WheelRow>(rows);
        _wheelSummary.Text = rows.Count == 0
            ? "暂无命令，可从“命令库”或“自定义命令”添加"
            : $"已配置 {rows.Count} 个位置 · 所有 CAD 环境共用";
    }

    private void RemoveSelectedWheelItem()
    {
        if (_wheelGrid.CurrentRow?.DataBoundItem is not WheelRow row) return;
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        var ring = wheel.Rings.FirstOrDefault(item => item.Index == row.RingIndex);
        if (ring == null) return;
        ring.Items.RemoveAll(item => item.Slot == row.SlotIndex);
        _saved(_config);
        RefreshWheelGrid();
        RefreshPreview();
    }

    private void ChooseBackground()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择轮盘背景图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _backgroundPath.Text = dialog.FileName;
        RefreshPreview();
    }

    private void ClearBackground()
    {
        _backgroundPath.Clear();
        RefreshPreview();
    }

    private void ChooseTextColor(bool applyToAll)
    {
        if (!applyToAll && !TryGetSelectedWheelItem(out _))
        {
            MessageBox.Show(this, "当前格子没有命令。请先添加命令，或在轮盘预览选择一个已有命令的格子。",
                "无法设置颜色", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        TryGetSelectedWheelItem(out var selectedItem);
        var initial = applyToAll || string.IsNullOrWhiteSpace(selectedItem?.TextColor)
            ? _config.TextColor
            : selectedItem.TextColor;
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = ParseHtmlColor(initial, Ink)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        ApplyTextColor(color, applyToAll);
    }

    private void ApplyTextColor(string color, bool applyToAll)
    {
        if (applyToAll)
        {
            _config.TextColor = color;
            foreach (var item in GetAllWheelItems()) item.TextColor = "";
        }
        else
        {
            if (!TryGetSelectedWheelItem(out var selectedItem)) return;
            selectedItem.TextColor = color;
        }
        RefreshCellAppearanceChanges();
    }

    private void ResetSelectedCellTextColor()
    {
        if (!TryGetSelectedWheelItem(out var item)) return;
        item.TextColor = "";
        RefreshCellAppearanceChanges();
    }

    private bool TryGetSelectedWheelItem(out MenuItemConfig item)
    {
        item = null;
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return false;
        var ringIndex = (int)_catalogTarget.Ring.Value - 1;
        var slotIndex = (int)_catalogTarget.Slot.Value - 1;
        item = wheel.Rings.FirstOrDefault(ring => ring.Index == ringIndex)?.Items.FirstOrDefault(entry => entry.Slot == slotIndex);
        return item != null;
    }

    private void LoadSelectedCellAppearance()
    {
        var exists = TryGetSelectedWheelItem(out var item);
        _loadingDesignFields = true;
        try { _cellIconRatio.Value = Math.Max(10, Math.Min(90, item?.IconRatioPercent ?? 55)); }
        finally { _loadingDesignFields = false; }
        _updatingCellContentMode = true;
        try
        {
            _cellContentMode.Enabled = exists;
            _cellContentMode.SelectedIndex = CellContentIndex(item?.ContentMode);
        }
        finally { _updatingCellContentMode = false; }
        _textColorSwatch.Enabled = exists;
        _chooseTextColor.Enabled = exists;
        _chooseAllTextColor.Enabled = true;
        _resetTextColor.Enabled = exists && !string.IsNullOrWhiteSpace(item?.TextColor);
        _applyCurrentFont.Enabled = exists;
        _applyAllFonts.Enabled = true;
        _resetFontFamily.Enabled = exists && !string.IsNullOrWhiteSpace(item?.FontFamily);
        _applyCurrentTextStyle.Enabled = exists;
        _applyAllTextStyles.Enabled = true;
        _applyPlainTextStyle.Enabled = true;
        _resetTextStyle.Enabled = exists && (item?.FontBold.HasValue == true || !string.IsNullOrWhiteSpace(item?.TextEffect));
        _updatingCellAppearance = true;
        _autoFitCellFont.Enabled = exists;
        _autoFitCellFont.Checked = !exists || item.FontSizePx <= 0;
        _cellFontSize.Enabled = exists && item.FontSizePx > 0;
        _cellFontSize.Value = exists && item.FontSizePx > 0
            ? RuntimeCompat.Clamp(item.FontSizePx, (int)_cellFontSize.Minimum, (int)_cellFontSize.Maximum)
            : RuntimeCompat.Clamp(_config.FontSizePx, (int)_cellFontSize.Minimum, (int)_cellFontSize.Maximum);
        SelectFontFamilyInEditor(exists && !string.IsNullOrWhiteSpace(item.FontFamily)
            ? item.FontFamily
            : _config.FontFamily);
        _fontBold.Checked = exists ? item.FontBold ?? _config.FontBold : _config.FontBold;
        SelectTextEffectInEditor(exists && !string.IsNullOrWhiteSpace(item.TextEffect)
            ? item.TextEffect
            : _config.TextEffect);
        _updatingCellAppearance = false;
        if (!exists)
        {
            _textColor.Text = "空格（先添加命令）";
            _textColorSwatch.BackColor = Surface;
            return;
        }
        var custom = !string.IsNullOrWhiteSpace(item.TextColor);
        var color = custom ? item.TextColor : _config.TextColor;
        _textColor.Text = custom ? color : $"默认 {color}";
        _textColorSwatch.BackColor = ParseHtmlColor(color, Ink);
    }

    private void SelectFontFamilyInEditor(string familyName)
    {
        var value = string.IsNullOrWhiteSpace(familyName) ? "宋体" : familyName.Trim();
        var index = -1;
        for (var position = 0; position < _fontFamily.Items.Count; position++)
        {
            if (!string.Equals(_fontFamily.Items[position]?.ToString(), value, StringComparison.CurrentCultureIgnoreCase)) continue;
            index = position;
            break;
        }
        if (index < 0)
        {
            _fontFamily.Items.Add(value);
            index = _fontFamily.Items.Count - 1;
        }
        _fontFamily.SelectedIndex = index;
    }

    private string SelectedFontFamily()
    {
        var selected = (_fontFamily.SelectedItem?.ToString() ?? "").Trim();
        return selected.Length == 0 ? "宋体" : selected;
    }

    private void SelectTextEffectInEditor(string effect)
    {
        var normalized = TextEffects.Normalize(effect);
        var index = _textEffect.Items.Cast<Choice>().ToList()
            .FindIndex(choice => string.Equals(choice.Value, normalized, StringComparison.OrdinalIgnoreCase));
        _textEffect.SelectedIndex = Math.Max(0, index);
    }

    private string SelectedTextEffect() => _textEffect.SelectedItem is Choice choice
        ? choice.Value
        : TextEffects.None;

    private void ApplySelectedCellTextStyle()
    {
        if (!TryGetSelectedWheelItem(out var item))
        {
            MessageBox.Show(this, "当前格子没有命令。请先添加命令，或在轮盘预览选择一个已有命令的格子。",
                "无法设置文字样式", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        item.FontBold = _fontBold.Checked;
        item.TextEffect = SelectedTextEffect();
        RefreshCellAppearanceChanges();
    }

    private void ApplyAllCellTextStyles()
    {
        _config.FontBold = _fontBold.Checked;
        _config.TextEffect = SelectedTextEffect();
        foreach (var item in GetAllWheelItems())
        {
            item.FontBold = null;
            item.TextEffect = "";
        }
        RefreshCellAppearanceChanges();
    }

    private void ResetSelectedCellTextStyle()
    {
        if (!TryGetSelectedWheelItem(out var item)) return;
        item.FontBold = null;
        item.TextEffect = "";
        RefreshCellAppearanceChanges();
    }

    private void ApplyPlainTextStyle()
    {
        _fontBold.Checked = false;
        SelectTextEffectInEditor(TextEffects.None);
        _config.FontBold = false;
        _config.TextEffect = TextEffects.None;
        _config.TextColor = "#1E2935";
        foreach (var item in GetAllWheelItems())
        {
            item.FontBold = null;
            item.TextEffect = "";
            item.TextColor = "";
        }
        RefreshCellAppearanceChanges();
    }

    private void ApplySelectedCellFontFamily()
    {
        if (!TryGetSelectedWheelItem(out var item))
        {
            MessageBox.Show(this, "当前格子没有命令。请先添加命令，或在轮盘预览选择一个已有命令的格子。",
                "无法设置字体", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        item.FontFamily = SelectedFontFamily();
        RefreshCellAppearanceChanges();
    }

    private void ApplyAllCellFontFamilies()
    {
        _config.FontFamily = SelectedFontFamily();
        foreach (var item in GetAllWheelItems()) item.FontFamily = "";
        RefreshCellAppearanceChanges();
    }

    private void ResetSelectedCellFontFamily()
    {
        if (!TryGetSelectedWheelItem(out var item)) return;
        item.FontFamily = "";
        RefreshCellAppearanceChanges();
    }

    private IEnumerable<MenuItemConfig> GetAllWheelItems()
    {
        return _config.Menus.TryGetValue(SceneKeys.Global, out var wheel)
            ? wheel.Rings.SelectMany(ring => ring.Items)
            : Enumerable.Empty<MenuItemConfig>();
    }

    private void RefreshCellAppearanceChanges()
    {
        LoadSelectedCellAppearance();
        LoadSelectedCommandEditor();
        RefreshWheelGrid();
        RefreshPreview();
    }

    private void ApplySelectedCellFontSize()
    {
        if (_updatingCellAppearance || !TryGetSelectedWheelItem(out var item)) return;
        item.FontSizePx = _autoFitCellFont.Checked ? 0 : (int)_cellFontSize.Value;
        _cellFontSize.Enabled = !_autoFitCellFont.Checked;
        RefreshWheelGrid();
        RefreshPreview();
    }

    private static Color ParseHtmlColor(string value, Color fallback)
    {
        try { return ColorTranslator.FromHtml(value ?? ""); }
        catch { return fallback; }
    }

    private void ClearSelectedWheelCell()
    {
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        var ringIndex = (int)_catalogTarget.Ring.Value - 1;
        var slotIndex = (int)_catalogTarget.Slot.Value - 1;
        var ring = wheel.Rings.FirstOrDefault(item => item.Index == ringIndex);
        if (ring == null) return;
        var removed = ring.Items.RemoveAll(item => item.Slot == slotIndex);
        if (removed == 0)
        {
            MessageBox.Show(this, "当前选中的格子已经是空的。", "无需清空", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _saved(_config);
        RefreshWheelGrid();
        RefreshPreview();
        LoadSelectedCellAppearance();
        MessageBox.Show(this, $"已清空第 {ringIndex + 1} 圈 / 第 {slotIndex + 1} 格。", "清空完成",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearCurrentWheel()
    {
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        var count = wheel.Rings.Sum(ring => ring.Items.Count);
        if (count == 0)
        {
            MessageBox.Show(this, "当前轮盘没有已配置的格子。", "无需清空", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"确定清空当前轮盘上的全部 {count} 个格子吗？\n\n自定义命令和分类会保留。",
                "清空当前轮盘", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        foreach (var ring in wheel.Rings) ring.Items.Clear();
        _saved(_config);
        RefreshWheelGrid();
        RefreshPreview();
        SelectFirstEmptyWheelCell();
    }

    private void ExportSettingsBackup()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出小阳CAD轮盘插件设置备份",
            Filter = "小阳CAD轮盘插件设置备份|*.xylpbackup|JSON 文件|*.json",
            DefaultExt = "xylpbackup",
            AddExtension = true,
            FileName = $"小阳CAD轮盘插件设置-{DateTime.Now:yyyyMMdd-HHmm}.xylpbackup"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ApplyAppearanceToConfig();
            SettingsBackupService.Export(dialog.FileName, _config);
            MessageBox.Show(this, "设置备份已导出。背景图片也已包含在备份中。", "导出完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("导出设置备份失败。", ex);
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportSettingsBackup()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入小阳CAD轮盘插件设置备份",
            Filter = "小阳CAD轮盘插件设置备份|*.xylpbackup;*.json|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "导入会替换当前轮盘、命令、分类、背景和触发设置。是否继续？",
                "导入设置备份", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            ApplyAppearanceToConfig();
            var automaticBackup = SettingsBackupService.CreateAutomaticBackupPath("before-import");
            SettingsBackupService.Export(automaticBackup, _config);
            var importedConfig = SettingsBackupService.Import(dialog.FileName);
            EndPreviewEditing();
            _config = importedConfig;
            _editingCustomId = "";
            SaveAllPendingSettings();
            ReloadAllSettingsUi();
            MessageBox.Show(this, $"设置备份已导入并立即应用。\n原设置已自动备份到：\n{automaticBackup}", "导入完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("导入设置备份失败。", ex);
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetSettings()
    {
        if (MessageBox.Show(this, "恢复默认设置会重置轮盘、命令、分类、背景和触发方式。是否继续？",
                "恢复默认设置", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            ApplyAppearanceToConfig();
            var automaticBackup = SettingsBackupService.CreateAutomaticBackupPath("before-reset");
            SettingsBackupService.Export(automaticBackup, _config);
            EndPreviewEditing();
            _config = ConfigStore.CreateDefault();
            ConfigStore.Normalize(_config);
            _editingCustomId = "";
            SaveAllPendingSettings();
            ReloadAllSettingsUi();
            MessageBox.Show(this, $"已恢复默认设置。\n原设置已自动备份到：\n{automaticBackup}", "重置完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("恢复默认设置失败。", ex);
            MessageBox.Show(this, ex.Message, "重置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReloadAllSettingsUi()
    {
        LoadConfigToUi();
        ApplyPluginAppearance();
        RefreshCategoryOptions(CommandCategoryCatalog.DefaultCustomCategory);
        FilterCatalog();
        RefreshWheelGrid();
        RefreshPreview();
        SelectFirstEmptyWheelCell();
    }

    private void HookPreviewEvents()
    {
        _preview.EnableCellSelection = true;
        _preview.CellSelected += (_, e) =>
        {
            SelectWheelCell(e.RingIndex, e.Slot);
            if (_preview.BatchSelectionMode) UpdateBatchSelectionStatus();
            // Keep the preview open on press so dragging can start. A plain
            // click returns to the compact editor on release (drag hook).
        };
        _menuSize.ValueChanged += (_, _) => ApplyWheelGeometry();
        _centerRadius.ValueChanged += (_, _) => ApplyWheelGeometry();
        _cellFontSize.ValueChanged += (_, _) => ApplySelectedCellFontSize();
        _autoFitCellFont.CheckedChanged += (_, _) => ApplySelectedCellFontSize();
        _menuOpacity.ValueChanged += (_, _) =>
        {
            _menuOpacityValue.Text = _menuOpacity.Value + "%";
            RefreshPreview();
        };
        _backgroundOpacity.ValueChanged += (_, _) =>
        {
            _backgroundOpacityValue.Text = _backgroundOpacity.Value + "%";
            RefreshPreview();
        };
        _backgroundPath.TextChanged += (_, _) => RefreshPreview();
        _ringCount.ValueChanged += (_, _) => ApplyRingCount();
    }

    private void ApplyRingCount()
    {
        if (_loadingUi || _updatingRingLayout || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        var count = (int)_ringCount.Value;
        var previousCount = wheel.Rings.Count(ring => ring.Enabled);
        while (wheel.Rings.Count < count)
            wheel.Rings.Add(new RingConfig { Index = wheel.Rings.Count, Slots = 8, Enabled = true });
        for (var index = 0; index < wheel.Rings.Count; index++)
        {
            wheel.Rings[index].Index = index;
            wheel.Rings[index].Enabled = index < count;
        }
        wheel.MenuSizePx = (int)_menuSize.Value;
        wheel.CenterRadiusPx = (int)_centerRadius.Value;
        if (count > previousCount)
            RingDiameterLayout.SplitOutermostRing(wheel, previousCount);
        else
            RingDiameterLayout.Normalize(wheel);
        _updatingRingLayout = true;
        _catalogTarget.Ring.Maximum = count;
        _customTarget.Ring.Maximum = count;
        _updatingRingLayout = false;
        RebuildRingEditors();
        RefreshPreview();
        SelectFirstEmptyWheelCell();
    }

    private void RebuildRingEditors()
    {
        _updatingRingLayout = true;
        try
        {
            _ringEditors.SuspendLayout();
            _ringEditors.Controls.Clear();
            _ringSlotEditors.Clear();
            _ringDiameterEditors.Clear();
            if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
            var count = RuntimeCompat.Clamp((int)_ringCount.Value, 1, RingConfig.MaximumRingCount);
            while (wheel.Rings.Count < count)
                wheel.Rings.Add(new RingConfig { Index = wheel.Rings.Count, Slots = 8, Enabled = true });
            wheel.MenuSizePx = (int)_menuSize.Value;
            wheel.CenterRadiusPx = (int)_centerRadius.Value;
            RingDiameterLayout.Normalize(wheel);
            _ringEditors.RowCount = count;
            _ringEditors.RowStyles.Clear();
            var centerDiameter = wheel.CenterRadiusPx * 2;
            var step = Math.Max(1, Math.Min(RingDiameterLayout.MinimumDiameterStepPx,
                Math.Max(count, wheel.MenuSizePx - centerDiameter) / count));
            for (var index = 0; index < count; index++)
            {
                var ring = wheel.Rings[index];
                ring.Index = index;
                ring.Enabled = true;
                var ringLabel = new Label { Text = $"第{index + 1}圈", Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, MinimumSize = new Size(0, ScaleLogical(24)), Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft };
                var slotsLabel = new Label { Text = "格数", Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, MinimumSize = new Size(0, ScaleLogical(24)), Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleRight };
                var slots = new NumericUpDown { Dock = DockStyle.Fill };
                ConfigureNumeric(slots, 1, 32, 1);
                slots.Value = RuntimeCompat.Clamp(ring.Slots, 1, 32);
                var sizeLabel = new Label { Text = "直径", Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, MinimumSize = new Size(0, ScaleLogical(24)), Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleRight };
                var diameter = new NumericUpDown { Dock = DockStyle.Left, Width = 112, MaximumSize = new Size(120, 0) };
                var minimum = centerDiameter + step * (index + 1);
                var maximum = index == count - 1 ? 900 : wheel.MenuSizePx - step * (count - index - 1);
                ConfigureNumeric(diameter, Math.Min(minimum, maximum), Math.Max(minimum, maximum), 10);
                diameter.Value = RuntimeCompat.Clamp(ring.DiameterPx, (int)diameter.Minimum, (int)diameter.Maximum);
                var cellSizeLabel = new Label { Text = "当圈格子大小 %", Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, MinimumSize = new Size(0, ScaleLogical(24)), Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleRight };
                var cellSize = new NumericUpDown { Dock = DockStyle.Left, Width = ScaleLogical(80), MaximumSize = new Size(ScaleLogical(88), 0), Minimum = 50, Maximum = 150, Increment = 5 };
                cellSize.Value = RuntimeCompat.Clamp(ring.NodeInnerSizePercent, 50, 150);
                var captured = index;
                slots.ValueChanged += (_, _) => ApplyRingEditor(captured);
                diameter.ValueChanged += (_, _) => ApplyRingEditor(captured);
                cellSize.ValueChanged += (_, _) => ApplyRingCellSize(captured, (int)cellSize.Value);
                cellSize.TextChanged += (_, _) =>
                {
                    // NumericUpDown normally commits typed values on focus loss.
                    // Apply complete, valid input immediately; leave partial input alone.
                    if (!_loadingUi && !_updatingRingLayout && !_updatingNodeSizes &&
                        int.TryParse(cellSize.Text, out var typed) && typed >= cellSize.Minimum && typed <= cellSize.Maximum)
                        cellSize.Value = typed;
                };
                _ringSlotEditors.Add(slots);
                _ringDiameterEditors.Add(diameter);
                _ringEditors.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(38)));
                _ringEditors.Controls.Add(ringLabel, 0, index);
                _ringEditors.Controls.Add(slotsLabel, 1, index);
                _ringEditors.Controls.Add(slots, 2, index);
                _ringEditors.Controls.Add(sizeLabel, 3, index);
                _ringEditors.Controls.Add(diameter, 4, index);
                _ringEditors.Controls.Add(cellSizeLabel, 5, index);
                _ringEditors.Controls.Add(cellSize, 6, index);
            }
        }
        finally
        {
            _ringEditors.ResumeLayout(true);
            _updatingRingLayout = false;
            UpdateStyleEditors();
        }
    }

    private void ApplyRingEditor(int index)
    {
        if (_loadingUi || _updatingRingLayout || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        var ring = wheel.Rings.FirstOrDefault(item => item.Index == index);
        if (ring == null || index >= _ringSlotEditors.Count || index >= _ringDiameterEditors.Count) return;
        var requestedSlots = (int)_ringSlotEditors[index].Value;
        if (requestedSlots != ring.Slots)
        {
            if (requestedSlots > ring.Slots) WheelCellEditing.Insert(ring, ring.Slots, requestedSlots - ring.Slots);
            else WheelCellEditing.Remove(wheel, ring, Enumerable.Range(requestedSlots, ring.Slots - requestedSlots));
        }
        ring.DiameterPx = (int)_ringDiameterEditors[index].Value;
        var enabled = wheel.Rings.Where(item => item.Enabled).OrderBy(item => item.Index).ToList();
        if (enabled.Count > 0 && ReferenceEquals(ring, enabled[enabled.Count - 1]))
        {
            _updatingRingLayout = true;
            try
            {
                _menuSize.Value = RuntimeCompat.Clamp(ring.DiameterPx, (int)_menuSize.Minimum, (int)_menuSize.Maximum);
                _config.MenuSizePx = (int)_menuSize.Value;
                wheel.MenuSizePx = _config.MenuSizePx;
            }
            finally { _updatingRingLayout = false; }
        }
        RingDiameterLayout.Normalize(wheel);
        UpdateAssignmentSlotLimit(_catalogTarget);
        UpdateAssignmentSlotLimit(_customTarget);
        RebuildRingEditors();
        RefreshPreview();
        SelectFirstEmptyWheelCell();
    }

    private void ApplyRingCellSize(int index, int percent)
    {
        if (_loadingUi || _updatingRingLayout || _updatingNodeSizes || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)
            || (wheel.StyleId != "06" && wheel.StyleId != "orbit")) return;
        var ring = wheel.Rings.FirstOrDefault(item => item.Index == index);
        if (ring == null) return;
        ring.NodeInnerSizePercent = RuntimeCompat.Clamp(percent, 50, 150);
        var slot = (int)_catalogTarget.Ring.Value - 1 == index ? (int)_catalogTarget.Slot.Value - 1 : 0;
        RefreshPreview();
        var selectionEnabled = _preview.EnableCellSelection;
        _preview.EnableCellSelection = true;
        try { _preview.SelectCell(index, Math.Max(0, Math.Min(ring.Slots - 1, slot))); }
        finally { _preview.EnableCellSelection = selectionEnabled; }
        RefreshNodeSizeEditors();
    }

    private void ApplyWheelGeometry()
    {
        if (_loadingUi || _updatingRingLayout || !_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        _config.MenuSizePx = (int)_menuSize.Value;
        _config.CenterRadiusPx = (int)_centerRadius.Value;
        wheel.MenuSizePx = _config.MenuSizePx;
        wheel.CenterRadiusPx = _config.CenterRadiusPx;
        RingDiameterLayout.Normalize(wheel);
        RebuildRingEditors();
        RefreshPreview();
    }

    private void SelectWheelCell(int ringIndex, int slotIndex)
    {
        SetAssignmentTarget(_catalogTarget, ringIndex, slotIndex);
        SetAssignmentTarget(_customTarget, ringIndex, slotIndex);
        _previewSelection.Text = $"已选择：第 {ringIndex + 1} 圈 / 第 {slotIndex + 1} 格";
        if (_config.Menus.TryGetValue(SceneKeys.Global, out var wheel))
            _groupName.Text = wheel.Rings.FirstOrDefault(ring => ring.Index == ringIndex)?.Name ?? "";
        RefreshNodeSizeEditors();
        LoadSelectedCellAppearance();
        LoadSelectedCommandEditor();
    }

    private void SetAssignmentTarget(AssignmentTarget assignment, int ringIndex, int slotIndex)
    {
        assignment.Ring.Value = RuntimeCompat.Clamp(ringIndex + 1, (int)assignment.Ring.Minimum, (int)assignment.Ring.Maximum);
        UpdateAssignmentSlotLimit(assignment);
        assignment.Slot.Value = RuntimeCompat.Clamp(slotIndex + 1, (int)assignment.Slot.Minimum, (int)assignment.Slot.Maximum);
        assignment.Status.Text = $"位置：第 {ringIndex + 1} 圈 / 第 {slotIndex + 1} 格（点轮盘可更换）";
    }

    private void UpdateAssignmentSlotLimit(AssignmentTarget assignment)
    {
        var ringIndex = (int)assignment.Ring.Value - 1;
        var slots = 8;
        if (_config.Menus.TryGetValue(SceneKeys.Global, out var wheel))
            slots = wheel.Rings.FirstOrDefault(ring => ring.Index == ringIndex)?.Slots ?? 8;
        assignment.Slot.Maximum = RuntimeCompat.Clamp(slots, 1, 32);
        if (assignment.Slot.Value > assignment.Slot.Maximum)
            assignment.Slot.Value = assignment.Slot.Maximum;
    }

    private void SelectFirstEmptyWheelCell()
    {
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        foreach (var ring in wheel.Rings.Where(ring => ring.Enabled).OrderBy(ring => ring.Index))
        {
            for (var slot = 0; slot < ring.Slots; slot++)
            {
                if (ring.Items.Any(item => item.Slot == slot && !string.IsNullOrWhiteSpace(item.Value))) continue;
                _preview.SelectCell(ring.Index, slot);
                return;
            }
        }
        var first = wheel.Rings.Where(ring => ring.Enabled).OrderBy(ring => ring.Index).FirstOrDefault();
        if (first != null && first.Slots > 0) _preview.SelectCell(first.Index, 0);
    }

    private void ApplyAppearanceToConfig()
    {
        _config.TriggerMode = (_trigger.SelectedItem as Choice)?.Value ?? TriggerModes.ShiftRightHold;
        _config.HoldDelayMs = (int)_holdDelay.Value;
        _config.MenuSizePx = (int)_menuSize.Value;
        _config.CenterRadiusPx = (int)_centerRadius.Value;
        _config.OpacityPercent = _menuOpacity.Value;
        _config.ExpandSearchCommandTree = _expandSearchCommandTree.Checked;
        if (_config.Menus.TryGetValue(SceneKeys.Global, out var wheel))
        {
            wheel.MenuSizePx = _config.MenuSizePx;
            wheel.CenterRadiusPx = _config.CenterRadiusPx;
            wheel.BackgroundImagePath = _backgroundPath.Text.Trim();
            wheel.BackgroundOpacityPercent = _backgroundOpacity.Value;
            RingDiameterLayout.Normalize(wheel);
        }
    }

    private void RefreshPreview()
    {
        if (_loadingUi) return;
        if (!_config.Menus.TryGetValue(SceneKeys.Global, out var wheel)) return;
        ApplyAppearanceToConfig();
        _preview.BackColor = wheel.StyleId == "01" || wheel.StyleId == "orbit" || wheel.StyleId == "free" ? Color.FromArgb(43, 48, 56) : Color.White;
        _preview.SetMenu(_config, wheel);
        UpdateSaveStatus();
    }

    private static void DrawTab(object sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        var selected = e.Index == tabs.SelectedIndex;
        using var background = new SolidBrush(selected ? Surface : Canvas);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
        {
            using var accent = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Bottom - 3, e.Bounds.Width, 3);
        }
        using var font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(
            e.Graphics,
            tabs.TabPages[e.Index].Text,
            font,
            e.Bounds,
            selected ? Ink : Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 242);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.ColumnHeadersHeight = 38;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 238, 243);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.RowTemplate.Height = 34;
    }

    private static void ApplyTheme(Control root)
    {
        root.SuspendLayout();
        try
        {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case TabPage page:
                    page.BackColor = Surface;
                    page.ForeColor = Ink;
                    page.UseVisualStyleBackColor = false;
                    break;
                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = Border;
                    button.BackColor = Surface;
                    button.ForeColor = Ink;
                    button.Cursor = Cursors.Hand;
                    button.MinimumSize = new Size(86, 34);
                    button.Padding = new Padding(10, 2, 10, 2);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 240, 242);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(213, 229, 232);
                    if (button.Name == "CatalogAction")
                    {
                        button.MinimumSize = new Size(72, 34);
                        button.Padding = new Padding(6, 2, 6, 2);
                    }
                    if (Equals(button.Tag, "primary"))
                    {
                        button.BackColor = Accent;
                        button.ForeColor = Color.White;
                        button.FlatAppearance.BorderColor = AccentDark;
                        button.FlatAppearance.MouseOverBackColor = AccentDark;
                        button.FlatAppearance.MouseDownBackColor = Header;
                    }
                    else if (Equals(button.Tag, "danger"))
                    {
                        button.BackColor = Color.FromArgb(255, 242, 242);
                        button.ForeColor = Color.FromArgb(177, 47, 47);
                        button.FlatAppearance.BorderColor = Color.FromArgb(235, 190, 190);
                    }
                    else if (Equals(button.Tag, "nav"))
                    {
                        button.BackColor = Header;
                        button.ForeColor = Color.White;
                        button.FlatAppearance.BorderSize = 0;
                        button.MinimumSize = Size.Empty;
                        button.Padding = new Padding(22, 0, 0, 0);
                        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 61, 73);
                        button.FlatAppearance.MouseDownBackColor = AccentDark;
                    }
                    break;
                case TextBox textBox:
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    textBox.BackColor = Surface;
                    textBox.ForeColor = Ink;
                    break;
                case ComboBox comboBox:
                    comboBox.FlatStyle = FlatStyle.Flat;
                    comboBox.BackColor = Surface;
                    comboBox.ForeColor = Ink;
                    break;
                case NumericUpDown numeric:
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    numeric.BackColor = Surface;
                    numeric.ForeColor = Ink;
                    break;
                case ListBox listBox:
                    listBox.BorderStyle = BorderStyle.FixedSingle;
                    listBox.BackColor = Surface;
                    listBox.ForeColor = Ink;
                    listBox.ItemHeight = 30;
                    break;
                case Label label when Equals(label.Tag, "muted"):
                    label.ForeColor = Muted;
                    break;
                case Label label when label.Tag == null:
                    label.ForeColor = Ink;
                    break;
                case CheckBox checkBox:
                    checkBox.ForeColor = Ink;
                    break;
            }
            ApplyTheme(control);
        }
        }
        finally { root.ResumeLayout(true); }
    }

    private static void AddEditorRow(TableLayoutPanel table, int row, string label, Control control)
    {
        while (table.RowCount <= row) table.RowCount++;
        var caption = new Label { Text = label, AutoSize = true, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 5, 3, 5) };
        var preferredHeight = control.Height;
        if (row < table.RowStyles.Count && table.RowStyles[row].SizeType == SizeType.Absolute)
            preferredHeight = Math.Min(preferredHeight, Math.Max(24, (int)table.RowStyles[row].Height - 10));
        if (control is Panel && !control.AutoSize)
            control.MinimumSize = new Size(control.MinimumSize.Width, Math.Max(control.MinimumSize.Height, Math.Max(36, preferredHeight)));
        control.Dock = DockStyle.Top;
        var maxWidth = control is NumericUpDown ? 120 : control.Controls.OfType<TrackBar>().Any() ? 300 : 0;
        if (control.MaximumSize.Width > 0) maxWidth = maxWidth == 0 ? control.MaximumSize.Width : Math.Min(maxWidth, control.MaximumSize.Width);
        control.MaximumSize = new Size(maxWidth, 0);
        if (control is TextBox input && !input.Multiline)
        {
            // Keep native single-line editors readable even when a host layout
            // pass supplies a collapsed height. Recompute after ambient font
            // changes instead of fixing the editor to a particular DPI.
            void EnsureInputHeight(object sender, EventArgs args)
            {
                input.MinimumSize = new Size(input.MinimumSize.Width, input.PreferredHeight);
                if (input.Height < input.PreferredHeight) input.Height = input.PreferredHeight;
            }
            input.FontChanged += EnsureInputHeight;
            input.HandleCreated += EnsureInputHeight;
            EnsureInputHeight(input, EventArgs.Empty);
        }
        control.Margin = new Padding(3, 5, 3, 5);
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static void ConfigureNumeric(NumericUpDown control, int minimum, int maximum, int increment)
    {
        control.Minimum = minimum;
        control.Maximum = maximum;
        control.Increment = increment;
        control.Width = 78;
    }

    private sealed record Choice(string Value, string Label);

    private sealed record CategoryNodeTag(string Name, bool CanDelete);

    private sealed class AssignmentTarget
    {
        public NumericUpDown Ring { get; } = new();
        public NumericUpDown Slot { get; } = new();
        public Label Status { get; } = new();
    }

    private sealed record WheelRow(int RingIndex, int SlotIndex, int Ring, int Slot, string Label, string Command);
}


