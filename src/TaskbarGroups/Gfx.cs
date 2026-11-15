using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace TaskbarGroups;

static class Gfx
{
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void HighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static bool IsLight(Color c) => c.R * 0.2126 + c.G * 0.7152 + c.B * 0.0722 > 140;

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Loads an image fully into memory so the file is never locked.</summary>
    public static Bitmap LoadBitmap(string path)
    {
        using var ms = new MemoryStream(File.ReadAllBytes(path));
        using var tmp = new Bitmap(ms);
        return new Bitmap(tmp);
    }

    /// <summary>Scales an image into a square canvas, keeping aspect ratio and transparency.</summary>
    public static Bitmap ResizeSquare(Image src, int px)
    {
        var dst = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        HighQuality(g);
        float scale = Math.Min((float)px / src.Width, (float)px / src.Height);
        float w = src.Width * scale, h = src.Height * scale;
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
        g.DrawImage(src, new Rectangle((int)((px - w) / 2), (int)((px - h) / 2), (int)Math.Round(w), (int)Math.Round(h)),
            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        return dst;
    }

    /// <summary>The thin white "+" the original app shows when no icon was picked.</summary>
    public static Bitmap DefaultGroupImage(int px = 256)
    {
        var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        HighQuality(g);
        float t = Math.Max(1.5f, px / 26f), len = px * 0.46f, c = px / 2f;
        using var b = new SolidBrush(Color.White);
        g.FillRectangle(b, c - len / 2, c - t / 2, len, t);
        g.FillRectangle(b, c - t / 2, c - len / 2, t, len);
        return bmp;
    }

    /// <summary>Shown when a program in a group no longer exists.</summary>
    public static Bitmap MissingIcon(int px)
    {
        var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        HighQuality(g);
        var r = new RectangleF(px * 0.06f, px * 0.06f, px * 0.88f, px * 0.88f);
        using (var path = RoundedRect(r, px * 0.18f))
        using (var fill = new SolidBrush(Color.FromArgb(200, 90, 90, 90)))
        using (var pen = new Pen(Color.FromArgb(230, 200, 80, 80), Math.Max(1f, px / 16f)))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        using var f = new Font("Segoe UI", px * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("?", f, Brushes.White, r, sf);
        return bmp;
    }
}
