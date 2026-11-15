using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaskbarGroups.Model;

sealed class ShortcutItem
{
    public string FilePath = "";
    public bool IsWindowsApp;
    public string Name = "";
    public string Arguments = "";
    public string WorkingDirectory = "";
    public bool RunAsAdmin;

    public string DisplayName => Name.Trim().Length > 0 ? Name.Trim() : DefaultName(FilePath, IsWindowsApp);

    public bool Exists => IsWindowsApp || File.Exists(FilePath) || Directory.Exists(FilePath);

    public bool IsFolder => !IsWindowsApp && Directory.Exists(FilePath);

    /// <summary>Shell parsing name used for icons and launching.</summary>
    public string ShellPath => IsWindowsApp ? @"shell:AppsFolder\" + FilePath : FilePath;

    /// <summary>Unique per target (two different "launcher.exe" files no longer share a cached icon).</summary>
    public string IconKey(int px)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(FilePath.ToLowerInvariant() + "|" + IsWindowsApp));
        return Convert.ToHexString(bytes, 0, 8) + "_" + px;
    }

    public static string DefaultName(string path, bool isApp)
    {
        if (isApp) return Shell.ShellInfo.GetDisplayName(@"shell:AppsFolder\" + path) ?? path;
        if (Directory.Exists(path)) return new DirectoryInfo(path).Name is { Length: > 0 } n ? n : path;
        return Path.GetFileNameWithoutExtension(path);
    }

    public ShortcutItem Clone() => (ShortcutItem)MemberwiseClone();
}

/// <summary>
/// One taskbar group. Stored in config\&lt;Name&gt;\ObjectData.xml in the same XML layout the
/// original app used, so configs move freely between the old and new versions.
/// </summary>
sealed class Group
{
    public const string DarkColor = "#1F1F1F";
    public const string LightColor = "#E6E6E6";
    public static readonly int[] IconSizes = [24, 32, 48];

    /// <summary>Folder name (spaces stored as underscores, like the original).</summary>
    public string Name = "";
    public string ColorString = DarkColor;
    public bool AllowOpenAll = true;
    public List<ShortcutItem> Items = [];
    public int Columns = 6;
    /// <summary>Transparency in percent (0 = solid). Stored as "Opacity" for compatibility.</summary>
    public double Transparency = 10;
    public bool ShowLabels;
    public int IconSize = 32;

    public string DisplayName => Regex.Replace(Name, "_+", " ");
    public string Dir => Paths.GroupDir(Name);
    public string IconFile => Path.Combine(Dir, "GroupIcon.ico");
    public string ImageFile => Path.Combine(Dir, "GroupImage.png");
    public string IconCacheDir => Path.Combine(Dir, "Icons");
    public string ShortcutFile => ShortcutPathFor(Name);
    public string Aumid => Paths.AumidPrefix + Name;

    public static string ShortcutPathFor(string name) =>
        Path.Combine(Paths.ShortcutsDir, Regex.Replace(name, "_+", " ") + ".lnk");

    public static string ToFolderName(string displayName) => Regex.Replace(displayName.Trim(), @"\s+", "_");

    public Color BackColor
    {
        get
        {
            try { return ColorTranslator.FromHtml(ColorString); }
            catch { return ColorTranslator.FromHtml(DarkColor); }
        }
    }

    public static IEnumerable<Group> LoadAll()
    {
        if (!Directory.Exists(Paths.ConfigDir)) yield break;
        foreach (var dir in Directory.GetDirectories(Paths.ConfigDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            Group? g = null;
            try { g = Load(Path.GetFileName(dir)); }
            catch (Exception ex) { Paths.LogError(ex, "Loading group " + dir); }
            if (g != null) yield return g;
        }
    }

    public static Group? Load(string folderName)
    {
        var file = Path.Combine(Paths.GroupDir(folderName), "ObjectData.xml");
        if (!File.Exists(file)) return null;

        var root = XDocument.Load(file).Root!;
        string S(XElement e, string n) => e.Element(n)?.Value ?? "";
        bool B(XElement e, string n, bool def) => bool.TryParse(e.Element(n)?.Value, out var v) ? v : def;
        double D(string n, double def) =>
            double.TryParse(root.Element(n)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        var g = new Group
        {
            // The folder name is the source of truth (pins pass it as the argument).
            Name = folderName,
            ColorString = S(root, "ColorString") is { Length: > 0 } c ? c : DarkColor,
            AllowOpenAll = B(root, "allowOpenAll", false),
            Columns = Math.Clamp((int)D("Width", 6), 1, 20),
            Transparency = Math.Clamp(D("Opacity", 10), 0, 90),
            ShowLabels = B(root, "ShowLabels", false),
            IconSize = (int)D("IconSize", 32),
        };
        if (!IconSizes.Contains(g.IconSize)) g.IconSize = 32;

        foreach (var e in root.Element("ShortcutList")?.Elements("ProgramShortcut") ?? [])
        {
            g.Items.Add(new ShortcutItem
            {
                FilePath = S(e, "FilePath"),
                IsWindowsApp = B(e, "isWindowsApp", false),
                Name = S(e, "name"),
                Arguments = S(e, "Arguments"),
                WorkingDirectory = S(e, "WorkingDirectory"),
                RunAsAdmin = B(e, "RunAsAdmin", false),
            });
        }
        return g;
    }

    /// <summary>Writes ObjectData.xml atomically (temp file + replace) so a crash can't corrupt it.</summary>
    public void SaveXml()
    {
        Directory.CreateDirectory(Dir);
        string Bool(bool b) => b ? "true" : "false";
        var doc = new XDocument(
            new XElement("Category",
                new XElement("Name", Name),
                new XElement("ColorString", ColorString),
                new XElement("allowOpenAll", Bool(AllowOpenAll)),
                new XElement("ShortcutList", Items.Select(i =>
                    new XElement("ProgramShortcut",
                        new XElement("Arguments", i.Arguments),
                        new XElement("WorkingDirectory", i.WorkingDirectory),
                        new XElement("FilePath", i.FilePath),
                        new XElement("isWindowsApp", Bool(i.IsWindowsApp)),
                        new XElement("name", i.Name),
                        new XElement("RunAsAdmin", Bool(i.RunAsAdmin))))),
                new XElement("Width", Columns.ToString(CultureInfo.InvariantCulture)),
                new XElement("Opacity", Transparency.ToString(CultureInfo.InvariantCulture)),
                new XElement("ShowLabels", Bool(ShowLabels)),
                new XElement("IconSize", IconSize.ToString(CultureInfo.InvariantCulture))));

        var target = Path.Combine(Dir, "ObjectData.xml");
        var tmp = target + ".tmp";
        doc.Save(tmp);
        File.Move(tmp, target, overwrite: true);
    }

    public Group Clone()
    {
        var g = (Group)MemberwiseClone();
        g.Items = Items.Select(i => i.Clone()).ToList();
        return g;
    }
}
