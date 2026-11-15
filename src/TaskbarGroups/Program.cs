using System.Drawing;
using TaskbarGroups.Editor;
using TaskbarGroups.Model;
using TaskbarGroups.Popup;
using TaskbarGroups.Shell;

namespace TaskbarGroups;

static class Program
{
    public static bool TestMode;

    /// <summary>
    ///   TaskbarGroups.exe              → editor
    ///   TaskbarGroups.exe &lt;group&gt;      → popup for that group (what taskbar pins run)
    ///   TaskbarGroups.exe --edit &lt;group&gt; → editor, opening that group
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // Grab the pointer position first thing: it's where the user clicked the taskbar button.
        Native.GetCursorPos(out var cp);
        var cursor = new Point(cp.X, cp.Y);
        Timing.Mark("main");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Fatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);
        ApplicationConfiguration.Initialize();
        Timing.Mark("init");

        if (args.Length > 0 && args[0] == "--shot") return Testing.Run(args[1..]);
        if (args.Length > 0 && !args[0].StartsWith("--")) return RunPopup(args[0], cursor);
        return RunEditor(args.Length >= 2 && args[0] == "--edit" ? args[1] : null);
    }

    static int RunPopup(string name, Point cursor)
    {
        Native.SetCurrentProcessExplicitAppUserModelID(Paths.AumidPrefix + name);

        Group? g = null;
        try { g = Group.Load(name); }
        catch (Exception ex) { Paths.LogError(ex, "Loading group " + name); }
        Timing.Mark("loaded");

        if (g == null || g.Items.Count == 0)
        {
            MessageBox.Show(g == null
                    ? $"The group \"{name}\" doesn't exist anymore.\n\nOpen Taskbar Groups to create it again, and unpin this button."
                    : $"The group \"{name}\" has no programs in it.\n\nOpen Taskbar Groups to add some.",
                "Taskbar Groups", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        Application.Run(new PopupForm(g, cursor));
        return 0;
    }

    static int RunEditor(string? editName)
    {
        Native.SetCurrentProcessExplicitAppUserModelID(Paths.EditorAumid);
        Application.SetColorMode(SystemColorMode.Dark);

        using var mutex = new Mutex(true, @"Local\TaskbarGroups.Editor." + Paths.AppDir.GetHashCode(), out bool first);
        if (!first)
        {
            var hwnd = Native.FindWindow(null, MainForm.WindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, 9 /* SW_RESTORE */);
                Native.SetForegroundWindow(hwnd);
            }
            return 0;
        }

        Application.Run(new MainForm(editName));
        return 0;
    }

    static void Fatal(Exception? ex)
    {
        if (ex == null) return;
        Paths.LogError(ex, "Unhandled");
        MessageBox.Show($"Something went wrong:\n\n{ex.Message}\n\nDetails were saved to:\n{Paths.ErrorLog}",
            "Taskbar Groups", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
