using System.Drawing;
using System.Drawing.Imaging;

namespace TaskbarGroups.Icons;

/// <summary>Writes a multi-resolution .ico (PNG-compressed entries) so the taskbar icon is sharp at any DPI.</summary>
static class IcoWriter
{
    static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    public static void Save(Image source, string path)
    {
        var pngs = new List<(int size, byte[] data)>();
        foreach (var s in Sizes)
        {
            using var bmp = Gfx.ResizeSquare(source, s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add((s, ms.ToArray()));
        }

        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);          // reserved
            w.Write((ushort)1);          // type: icon
            w.Write((ushort)pngs.Count);
            int offset = 6 + 16 * pngs.Count;
            foreach (var (size, data) in pngs)
            {
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);        // palette
                w.Write((byte)0);        // reserved
                w.Write((ushort)1);      // planes
                w.Write((ushort)32);     // bpp
                w.Write(data.Length);
                w.Write(offset);
                offset += data.Length;
            }
            foreach (var (_, data) in pngs) w.Write(data);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
