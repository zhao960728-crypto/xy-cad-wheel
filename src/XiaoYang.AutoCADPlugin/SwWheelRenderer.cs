using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYang.AutoCADPlugin;

internal sealed partial class RadialMenuControl
{
    // SW-style direction guides: north is the centre of slot zero, unlike the
    // classic wheel whose slot zero begins at north. Only presentation changes.
    private void DrawSwWheel(Graphics graphics, RectangleF bounds)
    {
        if (!ScaleMenuForPreview || !DrawBackdrop) { DrawSwWheelCore(graphics, bounds); return; }
        var labels = DrawLabels; var foreground = DrawForeground;
        var guides = DrawGuides; var hover = DrawHover;
        try
        {
            using var backdrop = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            using (var backgroundGraphics = Graphics.FromImage(backdrop))
            {
                backgroundGraphics.SmoothingMode = graphics.SmoothingMode;
                DrawLabels = false; DrawForeground = false;
                DrawSwWheelCore(backgroundGraphics, bounds);
            }
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = RuntimeCompat.Clamp(_scene.SurfaceOpacityPercent, 5, 100) / 100f });
            graphics.DrawImage(backdrop, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel, attributes);
            DrawBackdrop = false; DrawGuides = false; DrawHover = false;
            DrawLabels = labels; DrawForeground = foreground;
            DrawSwWheelCore(graphics, bounds);
        }
        finally
        {
            DrawBackdrop = true; DrawGuides = guides; DrawHover = hover;
            DrawLabels = labels; DrawForeground = foreground;
        }
    }

    // The single-ring SW presentation needs a broad transparent centre. This is
    // display geometry only: the saved centre radius is never rewritten. The
    // shared layout builder also uses this value for hit testing.
    private float AdjustSwInnerRadius(float original, RectangleF bounds, int ringCount) =>
        _scene.StyleId == "01" && ringCount == 1 ? Math.Max(original, bounds.Width * .29f) : original;

    private void DrawSwWheelCore(Graphics graphics, RectangleF bounds)
    {
        var center = new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var rings = BuildRingLayouts(bounds, _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList());
        if (rings.Count == 0) return;
        if (DrawBackdrop)
        {
            using var surface = Appearance.CreateBrush(bounds);
            using var annulus = new GraphicsPath { FillMode = FillMode.Alternate };
            annulus.AddEllipse(RectAround(center, rings[rings.Count - 1].OuterRadius));
            annulus.AddEllipse(RectAround(center, rings[0].InnerRadius));
            graphics.FillPath(surface, annulus);
        }
        foreach (var layout in rings)
        {
            var ring = layout.Ring;
            var sweep = 360f / Math.Max(1, ring.Slots);
            var radius = (layout.InnerRadius + layout.OuterRadius) / 2;
            if (DrawGuides)
            {
                using var guide = new Pen(Color.FromArgb(150, Appearance.GetBorderColor()), 1f);
                DrawCircle(graphics, guide, center, layout.OuterRadius - 1);
                if (ReferenceEquals(layout, rings[0])) DrawCircle(graphics, guide, center, layout.InnerRadius + 1);
            }
            for (var slot = 0; slot < ring.Slots; slot++)
            {
                var angle = -90 + slot * sweep;
                if (DrawBackdrop)
                {
                    using var imageClip = CreateSlotPath(center, layout.InnerRadius, layout.OuterRadius, angle - sweep / 2, sweep);
                    DrawCellBackground(graphics, ring.Items.FirstOrDefault(i => i.Slot == slot), imageClip);
                }
                var highlighted = _hover.RingIndex == ring.Index && _hover.Slot == slot;
                var selected = (!BatchSelectionMode && _selected.RingIndex == ring.Index && _selected.Slot == slot) || BatchSelection.Contains((ring.Index, slot));
                if (DrawHover && (highlighted || selected))
                {
                    using var fill = new SolidBrush(Color.FromArgb(highlighted ? 135 : 95,
                        highlighted ? Appearance.GetHoverColor() : Appearance.GetSelectedColor()));
                    FillSlot(graphics, fill, center, layout.InnerRadius, layout.OuterRadius, angle - sweep / 2, sweep);
                }
                if (!DrawLabels || !DrawForeground) continue;
                var item = ring.Items.FirstOrDefault(i => i.Slot == slot);
                var at = PointOnCircle(center, radius, angle);
                if (item == null)
                {
                    if (EnableCellSelection)
                    {
                        using var pen = new Pen(Color.FromArgb(130, Appearance.GetBorderColor()), 1);
                        graphics.DrawLine(pen, at.X - 3, at.Y, at.X + 3, at.Y);
                        graphics.DrawLine(pen, at.X, at.Y - 3, at.X, at.Y + 3);
                    }
                    continue;
                }
                // Keep the SW symbols compact; spare ring space belongs to
                // separation and readable captions, not enlarged artwork.
                var tangentSpace = ring.Slots <= 2 ? layout.Width * 1.4f : 2 * radius * (float)Math.Sin(Math.PI / ring.Slots);
                // A square's corners extend sqrt(2) times farther than its
                // edge midpoints. Keep the full label rectangle inside both
                // the radial and angular limits of the sector clip.
                var side = Math.Max(1, Math.Min(Math.Max(1, layout.Width - 4) * .70f, tangentSpace * .70f));
                var card = new RectangleF(at.X - side / 2, at.Y - side / 2, side, side);
                using var clip = CreateSlotPath(center, layout.InnerRadius + 2, layout.OuterRadius - 2, angle - sweep / 2, sweep);
                DrawSwContent(graphics, item, card, clip, bounds.Width / Math.Max(1f, EffectiveMenuSizePx()));
            }
        }
        if (DrawLabels && DrawForeground)
        {
            // Keep the drawing visible through the centre; only a small close
            // mark indicates cancellation, with no filled disc or large caption.
            var markSize = Math.Max(4f, Math.Min(7f, bounds.Width / 85f));
            using var close = new Pen(ParseTextColor(_config.TextColor), Math.Max(1f, bounds.Width / 400f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(close, center.X - markSize, center.Y - markSize, center.X + markSize, center.Y + markSize);
            graphics.DrawLine(close, center.X - markSize, center.Y + markSize, center.X + markSize, center.Y - markSize);
        }
    }

    private void DrawSwContent(Graphics graphics, MenuItemConfig item, RectangleF card, GraphicsPath clip, float scale)
    {
        var mode = ResolveCellContentMode(item);
        if (mode == "ImageOnly") return;
        if (mode == "TextOnly") { DrawItemCard(graphics, item, card, clip, mode, scale); return; }
        if (mode == "ImageAndText" || mode == "IconAndText" || item.IconRatioPercent != 55) { DrawItemCard(graphics, item, card, clip, mode, scale); return; }
        if (HasCellBackground(item))
        {
            if (mode != "IconOnly" && mode != "ImageOnly") DrawItemCard(graphics, item, card, clip, "TextOnly", scale);
            return;
        }
        var icon = GetEmbeddedIcon(item.IconData);
        // Catalog's zero-sized draw is a no-op after exact command matching.
        // It lets Auto distinguish a real command symbol from the old initial
        // letter fallback without adding another catalog lookup implementation.
        var hasIcon = icon != null || TryDrawSemanticIcon(graphics, item, PointF.Empty, 0);
        var iconSize = Math.Min(Math.Min(card.Width, card.Height), Math.Min(32f * scale, Math.Max(20f * scale, card.Height * .38f)));
        var gap = 4 * scale;
        var requestedText = item.FontSizePx > 0 ? item.FontSizePx * scale
            : Math.Max(Math.Max(1, _config.FontSizePx) * scale, Math.Min(72f * scale, card.Height * .22f));
        var textEffect = string.IsNullOrWhiteSpace(item.TextEffect) ? _config.TextEffect : item.TextEffect;
        var effectPadding = TextEffects.OverflowPadding(textEffect);
        var family = string.IsNullOrWhiteSpace(item.FontFamily) ? _config.FontFamily : item.FontFamily;
        float textHeight;
        int lineCount;
        using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
        {
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip | StringFormatFlags.NoWrap;
            var metrics = GetCardTextLayout(graphics, item.Label ?? "", family, item.FontBold ?? _config.FontBold,
                requestedText, new SizeF(card.Width, 0), scale, format, fit: false);
            lineCount = metrics.Lines.Length;
            textHeight = metrics.LineHeight * Math.Min(2, Math.Max(1, lineCount)) + effectPadding * 2;
        }
        if (mode == "Auto" || string.IsNullOrWhiteSpace(mode))
            mode = !hasIcon ? "TextOnly"
                : lineCount <= 2 && iconSize + gap + textHeight <= card.Height ? "IconAndText" : "TextOnly";
        if (mode == "TextOnly")
        {
            DrawItemCard(graphics, item, card, clip, "TextOnly", scale);
            return;
        }
        var showText = mode != "IconOnly";
        // Explicit image+text remains image+text. Limit the label area instead of
        // growing the symbol when a user chooses a spacious ring.
        if (showText) textHeight = Math.Max(1, Math.Min(textHeight, card.Height - iconSize - gap));
        var totalHeight = iconSize + (showText ? gap + textHeight : 0);
        var top = card.Top + Math.Max(0, (card.Height - totalHeight) / 2);
        var iconCenter = new PointF(card.Left + card.Width / 2, top + iconSize / 2);
        var state = graphics.Save();
        try
        {
            graphics.SetClip(clip, CombineMode.Intersect);
            if (icon != null) DrawIcon(graphics, icon, iconCenter, iconSize);
            else DrawCadDirectionIcon(graphics, item, iconCenter, iconSize);
        }
        finally { graphics.Restore(state); }
        if (showText)
            DrawItemCard(graphics, item, new RectangleF(card.Left, top + iconSize + gap, card.Width, textHeight), clip, "TextOnly", scale);
    }

    private void DrawCadDirectionIcon(Graphics g, MenuItemConfig item, PointF center, float size)
    {
        if (TryDrawSemanticIcon(g, item, center, size)) return;
        var state = g.Save();
        try
        {
            g.TranslateTransform(center.X - size / 2, center.Y - size / 2);
            g.ScaleTransform(size / 32, size / 32);
            var command = ResolveIconCommand(item).Trim();
            var label = (item.Label ?? "").Trim();
            if (label.Length == 0) label = command.Length == 0 ? "?" : command;
            using var font = CreateWheelFont(_config.FontFamily, 22, true);
            using var brush = new SolidBrush(ParseTextColor(HasTextColor(item.TextColor) ? item.TextColor : _config.TextColor));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(System.Globalization.StringInfo.GetNextTextElement(label), font, brush, new RectangleF(0, 0, 32, 32), format);
        }
        finally { g.Restore(state); }
    }

    internal string ResolveCellContentMode(MenuItemConfig item)
    {
        var mode = ConfigStore.NormalizeCellContentMode(item?.ContentMode);
        return mode == "Inherit" ? _scene.ContentMode : mode;
    }

    private bool TryDrawSemanticIcon(Graphics graphics, MenuItemConfig item, PointF center, float size)
    {
        var custom = item.Kind == ActionKinds.CustomCommand ? _config.CustomCommands?.FirstOrDefault(c => c.Id == item.Value) : null;
        return WheelIconCatalog.TryDrawSemantic(graphics, ResolveIconCommand(item), item.Label, custom?.Name ?? "", custom?.Category ?? "", center, size);
    }

    internal string ResolveIconCommand(MenuItemConfig item)
    {
        if (item == null) return "";
        if (string.Equals(item.Kind, ActionKinds.CustomCommand, StringComparison.OrdinalIgnoreCase))
            return _config.CustomCommands?.FirstOrDefault(command => string.Equals(command.Id, item.Value, StringComparison.OrdinalIgnoreCase))?.CommandName ?? "";
        return item.Value ?? "";
    }
}
