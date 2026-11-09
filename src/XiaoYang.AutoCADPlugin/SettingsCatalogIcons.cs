using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class SettingsWindow
{
    // CadCommandInfo is an immutable snapshot shared by repeated settings windows.
    // Weak keys let refreshed/removed command snapshots be collected naturally.
    private static readonly ConditionalWeakTable<CadCommandInfo, CatalogIconMatch> CatalogIconMatches = new();
    private sealed class CatalogIconMatch { internal string Id; }
    private ImageList _catalogImages;
    private readonly List<Bitmap> _catalogImageSources = new();
    private bool _catalogIconEventsHooked;

    // Resolve the actual execution token, never the localized label or an alias
    // that may have been reassigned to a different third-party command.
    internal static string CatalogIconId(object item)
    {
        if (item is CadCommandInfo cad)
            return CatalogIconMatches.GetValue(cad, command => new CatalogIconMatch
            {
                Id = WheelIconCatalog.TryGetSemanticIconId(command.GlobalName, command.DisplayName, command.Help, command.Group, out var cadId)
                    ? cadId : command.IsCoreCommand ? "gear" : "toolbox"
            }).Id;
        if (item is CustomCommandConfig custom)
            return WheelIconCatalog.TryGetSemanticIconId(custom.CommandName, custom.Name, custom.DisplayNameOverride, custom.Category, out var customId)
                ? customId : "pencil";
        return "folder";
    }

    private void RefreshCatalogIcons()
    {
        if (_catalogTree.IsDisposed) return;
        EnsureCatalogImages();
        foreach (var node in EnumerateTreeNodes(_catalogTree.Nodes)) ApplyCatalogIcon(node);
    }

    private void EnsureCatalogImages()
    {
        if (!_catalogIconEventsHooked)
        {
            _catalogIconEventsHooked = true;
            _catalogTree.Disposed += (_, _) => DisposeCatalogImages();
            DpiChanged += (_, _) => RefreshCatalogIcons();
        }
        int size = Math.Min(256, Math.Max(16, ScaleLogical(20)));
        if (_catalogImages == null || _catalogImages.ImageSize.Width != size)
        {
            _catalogTree.ImageList = null;
            DisposeCatalogImages();
            _catalogImages = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit, TransparentColor = Color.Transparent };
            _catalogTree.ImageList = _catalogImages;
        }
        _catalogTree.ItemHeight = Math.Max(ScaleLogical(30), size + ScaleLogical(4));
    }

    private void ApplyCatalogIcon(TreeNode node)
    {
        var id = CatalogIconId(node.Tag);
        if (!_catalogImages.Images.ContainsKey(id))
        {
            var bitmap = WheelIconCatalog.Render(id, _catalogImages.ImageSize.Width);
            // ImageList may create its native handle lazily. Keep original
            // bitmaps alive until its disposal, then release both owners.
            _catalogImageSources.Add(bitmap);
            _catalogImages.Images.Add(id, bitmap);
        }
        if (node.ImageKey != id) node.ImageKey = id;
        if (node.SelectedImageKey != id) node.SelectedImageKey = id;
    }

    private void DisposeCatalogImages()
    {
        _catalogImages?.Dispose();
        _catalogImages = null;
        foreach (var bitmap in _catalogImageSources) bitmap.Dispose();
        _catalogImageSources.Clear();
    }
}
