using System.ComponentModel;
using System.Diagnostics;
using TaskbarGroups.Model;

namespace TaskbarGroups;

static class Launcher
{
    /// <summary>Starts a program, folder, link or Store app. Returns false if it didn't start.</summary>
    public static bool Launch(ShortcutItem item, bool forceAdmin = false)
    {
        if (!item.Exists)
        {
            MessageBox.Show($"\"{item.DisplayName}\" can't be found:\n\n{item.FilePath}\n\nIt may have been moved or uninstalled. Edit the group to fix it.",
                "Taskbar Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = item.ShellPath,
            Arguments = item.Arguments ?? "",
            UseShellExecute = true,
        };

        // A .lnk/.url carries its own "Start in" folder; only override it for plain files.
        var ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
        if (!item.IsWindowsApp && ext is not ".lnk" and not ".url" && Directory.Exists(item.WorkingDirectory))
            psi.WorkingDirectory = item.WorkingDirectory;

        if ((item.RunAsAdmin || forceAdmin) && !item.IsWindowsApp && !item.IsFolder)
            psi.Verb = "runas";

        try
        {
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // user said no to the UAC prompt
        }
        catch (Exception ex)
        {
            Paths.LogError(ex, "Launching " + item.FilePath);
            MessageBox.Show($"Couldn't open \"{item.DisplayName}\":\n\n{ex.Message}", "Taskbar Groups",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    public static void OpenLocation(ShortcutItem item)
    {
        try
        {
            if (item.IsWindowsApp)
                Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder") { UseShellExecute = true });
            else
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }
        catch (Exception ex) { Paths.LogError(ex, "Open location"); }
    }

    public static void ShowInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { Paths.LogError(ex, "Show in Explorer"); }
    }
}
