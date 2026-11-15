using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using TaskbarGroups.Icons;
using TaskbarGroups.Shell;

namespace TaskbarGroups.Model;

/// <summary>Everything that writes a group to disk: config, icons, and the taskbar shortcut.</summary>
static class GroupStore
{
    static readonly Regex ValidName = new(@"^[\p{L}\p{N} \-]+$");

    /// <summary>Returns an error message, or null if the name is fine.</summary>
    public static string? ValidateName(string displayName, string? currentFolder)
    {
        var name = displayName.Trim();
        if (name.Length == 0) return "Give the group a name.";
        if (name.Length > 60) return "That name is too long (max 60 characters).";
        if (!ValidName.IsMatch(name)) return "Use only letters, numbers, spaces and dashes in the name.";

        var folder = Group.ToFolderName(name);
        bool sameGroup = currentFolder != null && string.Equals(folder, currentFolder, StringComparison.OrdinalIgnoreCase);
        if (!sameGroup && Directory.Exists(Paths.GroupDir(folder))) return "There is already a group with that name.";
        return null;
    }

    /// <param name="oldName">Folder name before editing (null for a new group).</param>
    /// <param name="newImage">Newly chosen group image, or null to keep the current one.</param>
    public static void Save(Group g, string? oldName, Bitmap? newImage)
    {
        Directory.CreateDirectory(Paths.ConfigDir);
        Directory.CreateDirectory(Paths.ShortcutsDir);

        if (oldName != null && oldName != g.Name)
        {
            var oldDir = Paths.GroupDir(oldName);
            if (Directory.Exists(oldDir))
            {
                // Two-step move so case-only renames work too.
                var tmp = Paths.GroupDir(g.Name + "_renaming_" + Environment.TickCount64);
                Directory.Move(oldDir, tmp);
                Directory.Move(tmp, g.Dir);
            }
            TryDelete(Group.ShortcutPathFor(oldName));
        }

        Directory.CreateDirectory(g.Dir);

        if (newImage != null || !File.Exists(g.IconFile))
        {
            using var img = newImage != null ? Gfx.ResizeSquare(newImage, 256) : Gfx.DefaultGroupImage();
            img.Save(g.ImageFile, ImageFormat.Png);
            IcoWriter.Save(img, g.IconFile);
        }

        g.SaveXml();
        IconCache.Rebuild(g);
        WriteShortcut(g);
        ShellInfo.NotifyIconsChanged();
    }

    public static void WriteShortcut(Group g)
    {
        Directory.CreateDirectory(Paths.ShortcutsDir);
        ShellInfo.CreateShortcut(g.ShortcutFile, Paths.ExePath, g.Name, g.Dir, g.IconFile, g.Aumid,
            "Taskbar group: " + g.DisplayName);
    }

    /// <summary>Recreates shortcuts that are missing or point at an old exe location.</summary>
    public static void RepairShortcuts(IEnumerable<Group> groups)
    {
        foreach (var g in groups)
        {
            try
            {
                var target = File.Exists(g.ShortcutFile) ? ShellInfo.GetLinkTarget(g.ShortcutFile) : null;
                if (!string.Equals(target, Paths.ExePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(g.IconFile))
                    {
                        using var img = Gfx.DefaultGroupImage();
                        img.Save(g.ImageFile, ImageFormat.Png);
                        IcoWriter.Save(img, g.IconFile);
                    }
                    WriteShortcut(g);
                }
            }
            catch (Exception ex) { Paths.LogError(ex, "Repairing shortcut for " + g.Name); }
        }
    }

    /// <summary>Moves the group's files to the Recycle Bin (recoverable).</summary>
    public static void Delete(Group g)
    {
        if (Directory.Exists(g.Dir))
            FileSystem.DeleteDirectory(g.Dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        if (File.Exists(g.ShortcutFile))
            FileSystem.DeleteFile(g.ShortcutFile, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) { Paths.LogError(ex, "Deleting " + file); }
    }
}
