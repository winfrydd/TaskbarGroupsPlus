using System.Drawing;
using System.Drawing.Imaging;
using TaskbarGroups.Model;
using TaskbarGroups.Shell;

namespace TaskbarGroups.Icons;

/// <summary>
/// Program icons are pre-rendered at the exact pixel size the popup needs and stored as PNGs in
/// config\&lt;group&gt;\Icons, so opening the popup never has to ask the shell for icons.
/// </summary>
static class IconCache
{
    public static Bitmap Get(Group g, ShortcutItem item, int px)
    {
        if (!item.Exists) return Gfx.MissingIcon(px);

        var file = Path.Combine(g.IconCacheDir, item.IconKey(px) + ".png");
        try
        {
            if (File.Exists(file)) return Gfx.LoadBitmap(file);
        }
        catch { /* corrupt cache file: rebuild below */ }

        var bmp = Extract(item, px);
        try
        {
            Directory.CreateDirectory(g.IconCacheDir);
            bmp.Save(file, ImageFormat.Png);
        }
        catch (Exception ex) { Paths.LogError(ex, "Writing icon cache " + file); }
        return bmp;
    }

    public static Bitmap Extract(ShortcutItem item, int px) =>
        (item.Exists ? ShellInfo.GetIcon(item.ShellPath, px) : null) ?? Gfx.MissingIcon(px);

    /// <summary>Pixel sizes the popup can need on the monitors currently connected.</summary>
    public static IEnumerable<int> NeededSizes(Group g) =>
        Screen.AllScreens
            .Select(s => Native.DpiAt(new Point(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2)))
            .Select(dpi => (int)Math.Round(g.IconSize * dpi / 96.0))
            .Distinct();

    public static void Rebuild(Group g)
    {
        try { if (Directory.Exists(g.IconCacheDir)) Directory.Delete(g.IconCacheDir, true); }
        catch (Exception ex) { Paths.LogError(ex, "Clearing icon cache"); }

        foreach (var px in NeededSizes(g))
            foreach (var item in g.Items)
                Get(g, item, px).Dispose();
    }
}
