using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace XiaoYang.AutoCADPlugin;

internal sealed class WheelAppearance
{
    public string FillMode { get; set; } = "Solid";
    public string StartColor { get; set; } = "#FFFFFF";
    public string EndColor { get; set; } = "#DDF4F5";
    public int GradientAngle { get; set; } = 90;
    public string BorderColor { get; set; } = "#D7DFE6";
    public string HoverColor { get; set; } = "#008CA2";
    public string SelectedColor { get; set; } = "#008CA2";

    public void Normalize()
    {
        FillMode = string.Equals(FillMode, "Gradient", StringComparison.OrdinalIgnoreCase) ? "Gradient" : "Solid";
        StartColor = NormalizeColor(StartColor, "#FFFFFF");
        EndColor = NormalizeColor(EndColor, "#DDF4F5");
        BorderColor = NormalizeColor(BorderColor, "#D7DFE6");
        HoverColor = NormalizeColor(HoverColor, "#008CA2");
        SelectedColor = NormalizeColor(SelectedColor, "#008CA2");
        GradientAngle = ((GradientAngle % 360) + 360) % 360;
    }

    public WheelAppearance Clone() => new WheelAppearance
    {
        FillMode = FillMode, StartColor = StartColor, EndColor = EndColor,
        GradientAngle = GradientAngle, BorderColor = BorderColor,
        HoverColor = HoverColor, SelectedColor = SelectedColor
    };

    // Caller owns the returned brush. Colors remain opaque; menu opacity is managed separately.
    public Brush CreateBrush(RectangleF bounds)
    {
        var start = ParseColor(StartColor, Color.White);
        if (!string.Equals(FillMode, "Gradient", StringComparison.OrdinalIgnoreCase)
            || bounds.Width <= 0 || bounds.Height <= 0)
            return new SolidBrush(start);
        return new LinearGradientBrush(bounds, start, ParseColor(EndColor, Color.FromArgb(221, 244, 245)), GradientAngle);
    }

    public Color GetBorderColor() => ParseColor(BorderColor, Color.FromArgb(215, 223, 230));
    public Color GetHoverColor() => ParseColor(HoverColor, Color.FromArgb(0, 140, 162));
    public Color GetSelectedColor() => ParseColor(SelectedColor, Color.FromArgb(0, 140, 162));
    public static string ToHex(Color color) => "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");

    public static Color ParseColor(string value, Color fallback)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 7 && text[0] == '#' && int.TryParse(text.Substring(1), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var rgb))
            return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        return fallback;
    }

    private static string NormalizeColor(string value, string fallback) => ToHex(ParseColor(value, ParseColor(fallback, Color.White)));
}
