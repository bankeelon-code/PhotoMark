using System.Buffers.Binary;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace PhotoMark;

public sealed class LoadedPhoto(Bitmap pixels, bool isPng, List<byte[]> colorChunks) : IDisposable
{
    public Bitmap Pixels { get; } = pixels;
    public bool IsPng { get; } = isPng;
    public List<byte[]> ColorChunks { get; } = colorChunks;
    public void Dispose() => Pixels.Dispose();
}

public static class Imaging
{
    public static bool Supports(string path) => new[] { ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static LoadedPhoto Load(string path, int maxEdge = 0)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var original = Image.FromStream(stream, false, true);
        bool png = original.RawFormat.Guid == ImageFormat.Png.Guid;
        if (!png && original.RawFormat.Guid != ImageFormat.Jpeg.Guid)
            throw new InvalidDataException("仅支持真正的 JPG / PNG 文件。");
        if ((long)original.Width * original.Height > 80_000_000)
            throw new InvalidDataException("照片超过 8000 万像素，请先缩小后重试。");
        // CMYK values cannot safely be paired with their original profile after GDI+ RGB conversion.
        if (!png && IsFourComponentJpeg(stream))
            throw new InvalidDataException("暂不支持 CMYK 照片，请先转换为 RGB。");
        if (original.PropertyIdList.Contains(0x0112))
        {
            byte[]? value = original.GetPropertyItem(0x0112)?.Value;
            int orientation = value is { Length: >= 2 } ? BitConverter.ToUInt16(value, 0) : 1;
            original.RotateFlip(orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            });
        }
        double scale = maxEdge > 0 ? Math.Min(1, maxEdge / (double)Math.Max(original.Width, original.Height)) : 1;
        var pixels = new Bitmap(Math.Max(1, (int)Math.Round(original.Width * scale)), Math.Max(1, (int)Math.Round(original.Height * scale)), PixelFormat.Format32bppArgb);
        try
        {
            if (original.HorizontalResolution > 0 && original.VerticalResolution > 0)
                pixels.SetResolution(original.HorizontalResolution, original.VerticalResolution);
            using var g = Graphics.FromImage(pixels);
            Quality(g);
            g.CompositingMode = CompositingMode.SourceCopy;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(original, new Rectangle(0, 0, pixels.Width, pixels.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            stream.Position = 0;
            return new LoadedPhoto(pixels, png, ReadColorChunks(stream, png));
        }
        catch { pixels.Dispose(); throw; }
    }

    public static void Quality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
    }

    static bool IsFourComponentJpeg(Stream stream)
    {
        long savedPosition = stream.Position;
        try
        {
            stream.Position = 2;
            while (stream.Position + 4 <= stream.Length)
            {
                if (stream.ReadByte() != 255) break;
                int marker; do { marker = stream.ReadByte(); } while (marker == 255);
                if (marker is 0xDA or 0xD9 or -1) break;
                int length = (stream.ReadByte() << 8) | stream.ReadByte();
                if (length < 2 || stream.Position + length - 2 > stream.Length) break;
                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC) && length >= 8)
                { stream.Seek(5, SeekOrigin.Current); return stream.ReadByte() == 4; }
                stream.Seek(length - 2, SeekOrigin.Current);
            }
            return false;
        }
        finally { stream.Position = savedPosition; }
    }

    public static Bitmap Render(Bitmap source, WatermarkSettings settings, Bitmap? logo = null)
    {
        var s = settings.Normalized();
        var result = (Bitmap)source.Clone();
        try
        {
            using var g = Graphics.FromImage(result);
            Quality(g);
            float shortEdge = Math.Min(result.Width,result.Height);
            using var originalShape = new GraphicsPath();
            PointF[] originalCorners = [];
            if(s.UseLogo)
            {
                if(logo==null) throw new InvalidOperationException("请先选择 PNG Logo。");
                float width=result.Width*s.LogoPercent/100f,height=width*logo.Height/logo.Width;
                originalShape.AddRectangle(new RectangleF(0,0,width,height));
                originalCorners=[new(0,0),new(width,0),new(0,height)];
            }
            else if(!string.IsNullOrWhiteSpace(s.Text))
            {
                using var font=new Font(s.FontName,Math.Max(1,shortEdge*s.SizePercent/100f),FontStyle.Regular,GraphicsUnit.Pixel);
                using var format=(StringFormat)StringFormat.GenericTypographic.Clone();
                originalShape.AddString(s.Text,font.FontFamily,(int)FontStyle.Regular,font.Size,PointF.Empty,format);
            }
            if(originalShape.PointCount==0)return result;
            var color=Color.FromArgb(s.ColorArgb);
            using var brush=new SolidBrush(Color.FromArgb((int)Math.Round(s.Opacity*2.55),color.R,color.G,color.B));
            using var attr=new ImageAttributes();
            attr.SetColorMatrix(new ColorMatrix{Matrix33=s.Opacity/100f});attr.SetWrapMode(WrapMode.TileFlipXY);
            foreach(var slot in WatermarkLayouts.Create(result.Size,s))
            {
                using var shape=(GraphicsPath)originalShape.Clone();
                var corners=(PointF[])originalCorners.Clone();
                using var rotate=new Matrix();rotate.Rotate(slot.Angle);shape.Transform(rotate);
                if(s.UseLogo)rotate.TransformPoints(corners);
                var bounds=shape.GetBounds();if(bounds.Width<=0||bounds.Height<=0)continue;
                float fit=Math.Min(1,Math.Min(slot.Area.Width/bounds.Width,slot.Area.Height/bounds.Height));
                float mw=bounds.Width*fit,mh=bounds.Height*fit;
                float x=slot.AlignX<0?slot.Area.Left:slot.AlignX>0?slot.Area.Right-mw:slot.Area.Left+(slot.Area.Width-mw)/2;
                float y=slot.AlignY<0?slot.Area.Top:slot.AlignY>0?slot.Area.Bottom-mh:slot.Area.Top+(slot.Area.Height-mh)/2;
                using var place=new Matrix(fit,0,0,fit,x-bounds.X*fit,y-bounds.Y*fit);
                shape.Transform(place);
                if(s.UseLogo)
                {
                    place.TransformPoints(corners);
                    g.DrawImage(logo!,corners,new RectangleF(0,0,logo!.Width,logo.Height),GraphicsUnit.Pixel,attr);
                }
                else g.FillPath(brush,shape);
            }
            return result;
        }
        catch{result.Dispose();throw;}
    }

    // Copy only color information, never EXIF, GPS, comments or XMP.
    // Pixel values remain in the source RGB space; do not enable GDI+ color conversion before reattaching ICC.
    public static List<byte[]> ReadColorChunks(Stream stream, bool png)
    {
        var result = new List<byte[]>();
        if (png)
        {
            stream.Position = 8;
            byte[] header = new byte[8];
            while (stream.Position + 12 <= stream.Length)
            {
                stream.ReadExactly(header);
                uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
                string kind = Encoding.ASCII.GetString(header, 4, 4);
                if (length > 16 * 1024 * 1024 || stream.Position + length + 4 > stream.Length) break;
                if (kind is "iCCP" or "sRGB" or "gAMA" or "cHRM")
                {
                    byte[] chunk = new byte[checked((int)length + 12)];
                    header.CopyTo(chunk, 0);
                    stream.ReadExactly(chunk.AsSpan(8));
                    result.Add(chunk);
                }
                else stream.Seek(length + 4L, SeekOrigin.Current);
                if (kind == "IEND") break;
            }
        }
        else
        {
            stream.Position = 2;
            while (stream.Position + 4 <= stream.Length)
            {
                if (stream.ReadByte() != 0xFF) break;
                int marker;
                do { marker = stream.ReadByte(); } while (marker == 0xFF);
                if (marker is 0xDA or 0xD9 or -1) break;
                int hi = stream.ReadByte(), lo = stream.ReadByte();
                int length = (hi << 8) | lo;
                if (length < 2 || stream.Position + length - 2 > stream.Length) break;
                byte[] payload = new byte[length - 2];
                stream.ReadExactly(payload);
                if (marker == 0xE2 && payload.Length >= 14 && Encoding.ASCII.GetString(payload, 0, 12) == "ICC_PROFILE\0")
                    result.Add(new byte[] { 0xFF, 0xE2, (byte)hi, (byte)lo }.Concat(payload).ToArray());
            }
        }
        return result;
    }

    static void SavePixels(Bitmap image, Stream stream, bool png)
    {
        if (png) image.Save(stream, ImageFormat.Png);
        else
        {
            var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
            image.Save(stream, encoder, parameters);
        }
    }

    public static string Export(string sourcePath, string outputFolder, WatermarkSettings settings, Bitmap? logo)
    {
        using var source = Load(sourcePath);
        using var rendered = Render(source.Pixels, settings, logo);
        Directory.CreateDirectory(outputFolder);
        string temporary = Path.Combine(outputFolder, ".photomark-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var encoded = new MemoryStream())
            {
                SavePixels(rendered, encoded, source.IsPng);
                encoded.Position = 0;
                using var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (!source.IsPng)
                {
                    target.WriteByte((byte)encoded.ReadByte()); target.WriteByte((byte)encoded.ReadByte());
                    foreach (var chunk in source.ColorChunks) target.Write(chunk);
                    encoded.CopyTo(target);
                }
                else
                {
                    // Fresh bitmap encoders may emit their own gAMA/sRGB. Keep the source tags instead.
                    var signature = new byte[8]; encoded.ReadExactly(signature); target.Write(signature);
                    var header = new byte[8];
                    while (encoded.Position < encoded.Length)
                    {
                        encoded.ReadExactly(header);
                        int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));
                        string kind = Encoding.ASCII.GetString(header, 4, 4);
                        byte[] payload = new byte[length + 4]; encoded.ReadExactly(payload);
                        if (!(source.ColorChunks.Count > 0 && kind is "iCCP" or "sRGB" or "gAMA" or "cHRM"))
                        { target.Write(header); target.Write(payload); }
                        if (kind == "IHDR") foreach (var chunk in source.ColorChunks) target.Write(chunk);
                    }
                }
                target.Flush(true);
            }
            string stem = Path.GetFileNameWithoutExtension(sourcePath) + "_水印";
            string ext = source.IsPng ? ".png" : ".jpg";
            for (int number = 0; ; number++)
            {
                string candidate = Path.Combine(outputFolder, stem + (number == 0 ? "" : $"_{number}") + ext);
                // Atomic no-overwrite rename protects originals and racing exports alike.
                try { File.Move(temporary, candidate, false); return candidate; }
                catch (IOException) when (File.Exists(candidate)) { }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record BatchItem(string Input, string? Output, string? Error);
public sealed record BatchProgress(int Completed, int Total, BatchItem Item);
public sealed record BatchResult(List<BatchItem> Items, bool Cancelled);

public static class BatchExporter
{
    public static BatchResult Run(IReadOnlyList<string> files, string folder, WatermarkSettings settings, IProgress<BatchProgress>? progress, CancellationToken cancellation)
    {
        var items = new List<BatchItem>();
        using var logo = settings.UseLogo ? Imaging.Load(settings.LogoPath) : null;
        if (logo != null && !logo.IsPng) throw new InvalidDataException("Logo 必须是 PNG 图片。");
        foreach (string file in files)
        {
            if (cancellation.IsCancellationRequested) break;
            BatchItem item;
            try { item = new(file, Imaging.Export(file, folder, settings, logo?.Pixels), null); }
            catch (Exception ex) { item = new(file, null, ex.Message); }
            items.Add(item);
            progress?.Report(new(items.Count, files.Count, item));
        }
        return new(items, cancellation.IsCancellationRequested && items.Count < files.Count);
    }
}
