using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static TaskbarGroups.Shell.Native;

namespace TaskbarGroups.Shell;

/// <summary>Icons, names, links and drag-drop, all through the Windows shell.</summary>
static class ShellInfo
{
    static Guid IID_IShellItem = typeof(IShellItem).GUID;
    static Guid IID_IShellItemImageFactory = typeof(IShellItemImageFactory).GUID;
    static Guid IID_IShellItemArray = typeof(IShellItemArray).GUID;

    /// <summary>High-quality icon with alpha for a file, folder, .lnk, .url or shell:AppsFolder\ id.</summary>
    public static Bitmap? GetIcon(string shellPath, int px)
    {
        object? obj = null;
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            if (SHCreateItemFromParsingName(shellPath, IntPtr.Zero, ref IID_IShellItemImageFactory, out obj) != 0) return null;
            var factory = (IShellItemImageFactory)obj;
            if (factory.GetImage(new SIZE { cx = px, cy = px }, SIIGBF_ICONONLY, out hbmp) != 0 || hbmp == IntPtr.Zero)
                return null;
            return FromHBitmap(hbmp);
        }
        catch { return null; }
        finally
        {
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            if (obj != null) Marshal.ReleaseComObject(obj);
        }
    }

    public static string? GetDisplayName(string shellPath)
    {
        object? obj = null;
        try
        {
            if (SHCreateItemFromParsingName(shellPath, IntPtr.Zero, ref IID_IShellItem, out obj) != 0) return null;
            return ((IShellItem)obj).GetDisplayName(SIGDN.NORMALDISPLAY, out var p) == 0 ? TakeCoString(p) : null;
        }
        catch { return null; }
        finally { if (obj != null) Marshal.ReleaseComObject(obj); }
    }

    /// <summary>Copies a 32bpp HBITMAP keeping its alpha channel (Image.FromHbitmap drops it).</summary>
    static unsafe Bitmap? FromHBitmap(IntPtr hbmp)
    {
        if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            if (GetDIBits(hdc, hbmp, 0, (uint)h, data.Scan0, ref bmi, 0) == 0) { bmp.UnlockBits(data); bmp.Dispose(); return null; }

            // The shell may hand back premultiplied alpha, straight alpha, or no alpha at all. Detect which.
            byte* px = (byte*)data.Scan0;
            int n = w * h;
            bool anyAlpha = false, straight = false;
            for (int i = 0; i < n; i++)
            {
                byte a = px[i * 4 + 3];
                if (a != 0) anyAlpha = true;
                if (px[i * 4] > a || px[i * 4 + 1] > a || px[i * 4 + 2] > a) straight = true;
            }
            if (!anyAlpha)
            {
                for (int i = 0; i < n; i++) px[i * 4 + 3] = 255;
            }
            else if (!straight)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = px[i * 4 + 3];
                    if (a is 0 or 255) continue;
                    px[i * 4] = (byte)Math.Min(255, px[i * 4] * 255 / a);
                    px[i * 4 + 1] = (byte)Math.Min(255, px[i * 4 + 1] * 255 / a);
                    px[i * 4 + 2] = (byte)Math.Min(255, px[i * 4 + 2] * 255 / a);
                }
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    /// <summary>Creates a .lnk that carries an AppUserModelID, so the popup groups under its own taskbar pin.</summary>
    public static void CreateShortcut(string lnkPath, string target, string args, string workDir, string iconPath, string aumid, string description)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            link.SetArguments(args);
            link.SetWorkingDirectory(workDir);
            link.SetIconLocation(iconPath, 0);
            link.SetDescription(description);

            var store = (IPropertyStore)link;
            var pv = new PROPVARIANT { vt = 31 /* VT_LPWSTR */, p = Marshal.StringToCoTaskMemUni(aumid) };
            try
            {
                var key = PKEY_AppUserModel_ID;
                store.SetValue(ref key, ref pv);
                store.Commit();
            }
            finally { Marshal.FreeCoTaskMem(pv.p); }

            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    /// <summary>Target path of a .lnk, or null for links without a file target (Store apps etc).</summary>
    public static string? GetLinkTarget(string lnkPath)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch { return null; }
        finally { Marshal.ReleaseComObject(link); }
    }

    /// <summary>
    /// Items dropped from Explorer, the desktop or the Start menu. Store apps from Start have no file
    /// path; they come back as (AppUserModelID, isApp: true).
    /// </summary>
    public static List<(string path, bool isApp)> GetDroppedItems(System.Windows.Forms.IDataObject data)
    {
        var result = new List<(string, bool)>();
        if (data.GetData(System.Windows.Forms.DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            result.AddRange(files.Select(f => (f, false)));
            return result;
        }
        try
        {
            if (data is not System.Runtime.InteropServices.ComTypes.IDataObject com) return result;
            if (SHCreateShellItemArrayFromDataObject(com, ref IID_IShellItemArray, out var arr) != 0) return result;
            arr.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                arr.GetItemAt(i, out var item);
                if (item.GetDisplayName(SIGDN.FILESYSPATH, out var p) == 0 && TakeCoString(p) is { } path)
                    result.Add((path, false));
                else if (item.GetDisplayName(SIGDN.PARENTRELATIVEPARSING, out p) == 0 && TakeCoString(p) is { } id)
                    result.Add((id, true));
                Marshal.ReleaseComObject(item);
            }
            Marshal.ReleaseComObject(arr);
        }
        catch (Exception ex) { Paths.LogError(ex, "Reading dropped items"); }
        return result;
    }

    public static void NotifyIconsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
}
