using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace XiaoYang.AutoCADPlugin;

internal static class CommandIconImage
{
    public const int IconSize = 256;
    public const int MaximumEncodedLength = 1500000;

    public static Bitmap LoadFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp")
            throw new InvalidDataException("请选择 PNG、JPG 或 BMP 图片。");
        if (new FileInfo(path).Length > 20 * 1024 * 1024)
            throw new InvalidDataException("图片文件不能超过 20 MB，请先缩小图片。");
        using (var stream = File.OpenRead(path))
        using (var source = Image.FromStream(stream, true, true))
        {
            CheckDimensions(source);
            // Detach from the stream and original file; the source file is never changed.
            return new Bitmap(source);
        }
    }

    public static Bitmap Decode(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        if (data.Length > MaximumEncodedLength)
            throw new InvalidDataException("图标数据过大，请重新选择图片。");
        using (var stream = new MemoryStream(Convert.FromBase64String(data)))
        using (var source = Image.FromStream(stream, true, true))
        {
            CheckDimensions(source);
            return new Bitmap(source);
        }
    }

    public static string Encode(Image source, RectangleF region, bool background = false)
    {
        if (source == null) return "";
        region = RectangleF.Intersect(region, new RectangleF(0, 0, source.Width, source.Height));
        if (region.Width < 1 || region.Height < 1)
            throw new InvalidDataException("请拖动选择有效的图片区域。");
        var scale = Math.Min(IconSize / region.Width, IconSize / region.Height);
        var width = background ? Math.Max(1, (int)Math.Round(region.Width * scale)) : IconSize;
        var height = background ? Math.Max(1, (int)Math.Round(region.Height * scale)) : IconSize;
        using (var output = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(output))
        using (var stream = new MemoryStream())
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            var target = new RectangleF((width - region.Width * scale) / 2,
                (height - region.Height * scale) / 2, region.Width * scale, region.Height * scale);
            if (background)
            {
                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), region.X, region.Y,
                    region.Width, region.Height, GraphicsUnit.Pixel, attributes);
            }
            else graphics.DrawImage(source, target, region, GraphicsUnit.Pixel);
            output.Save(stream, ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
    }

    private static void CheckDimensions(Image image)
    {
        if (image.Width > 8192 || image.Height > 8192 || (long)image.Width * image.Height > 24000000)
            throw new InvalidDataException("图片尺寸过大，请缩小到 8192 像素以内且不超过 2400 万像素。");
    }
}
