using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class RadialMenuControl
{
    private Point _previewPointer;
    private bool _previewPointerActive;
    private bool _previewMouseDown;
    internal Rectangle PreviewMagnifierBounds { get; private set; }

    private void DrawPreviewMagnifier(Graphics graphics)
    {
        PreviewMagnifierBounds = Rectangle.Empty;
        if (!ScaleMenuForPreview || !_previewPointerActive || !_hover.HasSlot ||
            _previewMouseDown || ShowInsertionPoints || BatchSelectionMode || ResizeMode || _scene.StyleId == "free") return;
        var item = FindItem(_hover);
        var dpi = DeviceDpi / 96f;
        var margin = Math.Max(4, (int)(8 * dpi));
        var width = Math.Min((int)(220 * dpi), ClientSize.Width - margin * 2);
        var height = Math.Min((int)(190 * dpi), ClientSize.Height - margin * 2);
        if (width < 60 || height < 60) return;
        // Paint only: the lens never captures the mouse or changes cell hit testing.
        var x = _previewPointer.X < ClientSize.Width / 2 ? ClientSize.Width - width - margin : margin;
        var y = _previewPointer.Y < ClientSize.Height / 2 ? ClientSize.Height - height - margin : margin;
        var bounds = new Rectangle(x, y, width, height);
        PreviewMagnifierBounds = bounds;
        using var background = new SolidBrush(Color.FromArgb(247, 250, 252));
        using var border = new Pen(WheelAccent, Math.Max(1, dpi));
        graphics.FillRectangle(background, bounds);
        graphics.DrawRectangle(border, bounds);
        using var headingFont = new Font(Font.FontFamily, Math.Max(9, 12 * dpi), FontStyle.Regular, GraphicsUnit.Pixel);
        var headingHeight = Math.Min(height / 3, (int)(28 * dpi));
        TextRenderer.DrawText(graphics, $"第 {_hover.RingIndex + 1} 圈 · 第 {_hover.Slot + 1} 格", headingFont,
            new Rectangle(x + margin, y + 3, width - margin * 2, headingHeight), WheelAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var content = new RectangleF(x + margin, y + headingHeight, width - margin * 2, height - headingHeight - margin);
        using var clip = new GraphicsPath();
        clip.AddRectangle(content);
        // A detached presentation copy prevents preview fitting from changing saved typography.
        var enlarged = item == null ? new MenuItemConfig { Label = "未分配命令" }
            : new MenuItemConfig { Label = item.Label, IconData = item.IconData, ImageMode = item.ImageMode,
                ContentMode = item.ContentMode, IconRatioPercent = item.IconRatioPercent, Kind = item.Kind, Value = item.Value,
                FontFamily = item.FontFamily, FontBold = item.FontBold };
        enlarged.FontSizePx = 0;
        enlarged.TextColor = "#1C2631";
        enlarged.TextEffect = TextEffects.None;
        var mode = item == null ? "TextOnly" : ResolveCellContentMode(item);
        if (mode == "IconOnly" || mode == "Auto") mode = "IconAndText";
        DrawCellBackground(graphics, enlarged, clip);
        DrawItemCard(graphics, enlarged, content, clip, mode, dpi);
    }
}
