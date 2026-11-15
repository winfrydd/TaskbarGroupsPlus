namespace TaskbarGroups;

/// <summary>All locations are relative to the folder the exe lives in (portable install).</summary>
static class Paths
{
    public const string AppName = "Taskbar Groups+";

    /// <summary>GitHub "owner/repo" that releases and updates come from.</summary>
    public const string Repo = "winfrydd/TaskbarGroupsPlus";

    public static readonly Version Version =
        typeof(Paths).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(1, 0, 0);

    public static readonly string AppDir = AppContext.BaseDirectory.TrimEnd('\\');
    public static readonly string ExePath = Environment.ProcessPath ?? Path.Combine(AppDir, "TaskbarGroups.exe");
    public static string ConfigDir => Path.Combine(AppDir, "config");
    public static string ShortcutsDir => Path.Combine(AppDir, "Shortcuts");
    public static string GroupDir(string folderName) => Path.Combine(ConfigDir, folderName);
    public static string ErrorLog => Path.Combine(AppDir, "error.log");

    /// <summary>Same IDs as the original app, so taskbar pins made with it keep working.</summary>
    public const string AumidPrefix = "tjackenpacken.taskbarGroup.menu.";
    public const string EditorAumid = "tjackenpacken.taskbarGroup.main";

    public static void LogError(Exception ex, string context)
    {
        try { File.AppendAllText(ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\r\n\r\n"); }
        catch { /* logging must never crash the app */ }
    }
}
