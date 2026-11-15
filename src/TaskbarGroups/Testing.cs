using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TaskbarGroups.Editor;
using TaskbarGroups.Model;
using TaskbarGroups.Popup;

namespace TaskbarGroups;

/// <summary>
/// Developer screenshots: TaskbarGroups.exe --shot (popup|group) &lt;name&gt; &lt;out.png&gt;
///                                         --shot (editor|new) &lt;out.png&gt;
/// Renders the real window off to the side of the user's work and saves what it looks like.
/// </summary>
static class Testing
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    /// <summary>--shot selftest &lt;out.txt&gt;: exercises save / rename / delete on a throwaway group.</summary>
    static int SelfTest(string outFile)
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add((ok ? "PASS " : "FAIL ") + what);
        try
        {
            var src = Group.Load("X")!;
            Check(src.Items.Count == 5, "loads original config (5 items)");
            Check(src.Columns == 6 && Math.Abs(src.Transparency - 10) < 0.01 && src.ColorString == "#1F1F1F", "reads Width/Opacity/Color");

            var g = src.Clone();
            g.Name = "Selftest_Group";
            g.Items.Add(new ShortcutItem { FilePath = @"C:\does\not\exist.exe", Name = "Ghost" });
            g.Items.Add(new ShortcutItem { FilePath = @"C:\Windows", Name = "" });
            GroupStore.Save(g, null, null);
            Check(File.Exists(Path.Combine(g.Dir, "ObjectData.xml")), "writes ObjectData.xml");
            Check(File.Exists(g.IconFile) && new FileInfo(g.IconFile).Length > 1000, "writes multi-size GroupIcon.ico");
            Check(File.Exists(g.ShortcutFile), "writes taskbar shortcut " + Path.GetFileName(g.ShortcutFile));
            var lnkText = System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(g.ShortcutFile));
            Check(lnkText.Contains(g.Aumid), "shortcut carries AppUserModelID " + g.Aumid);
            Check(string.Equals(Shell.ShellInfo.GetLinkTarget(g.ShortcutFile), Paths.ExePath, StringComparison.OrdinalIgnoreCase), "shortcut targets this exe");
            int cached = Directory.Exists(g.IconCacheDir) ? Directory.GetFiles(g.IconCacheDir).Length : 0;
            Check(cached == g.Items.Count - 1, $"icon cache prebuilt ({cached} files, missing item skipped)");

            var back = Group.Load("Selftest_Group")!;
            Check(back.Items.Count == 7 && back.Items[6].DisplayName == "Windows" && back.Items[5].Name == "Ghost", "round-trips items and names");
            Check(!back.Items[5].Exists, "missing program detected");

            back.Name = "Selftest_Renamed";
            GroupStore.Save(back, "Selftest_Group", Gfx.DefaultGroupImage());
            Check(!Directory.Exists(Paths.GroupDir("Selftest_Group")) && Directory.Exists(back.Dir), "rename moves config folder");
            Check(!File.Exists(Group.ShortcutPathFor("Selftest_Group")) && File.Exists(back.ShortcutFile), "rename replaces shortcut");

            Check(GroupStore.ValidateName("x", null) != null, "rejects duplicate name (case-insensitive)");
            Check(GroupStore.ValidateName("bad/name", null) != null, "rejects special characters");
            Check(GroupStore.ValidateName("Games and stuff", null) == null, "accepts normal name");

            Directory.Delete(back.Dir, true);
            File.Delete(back.ShortcutFile);
            Check(!Directory.Exists(back.Dir), "cleanup");
        }
        catch (Exception ex) { log.Add("FAIL exception: " + ex); }
        File.WriteAllLines(outFile, log);
        return log.Any(l => l.StartsWith("FAIL")) ? 1 : 0;
    }

    /// <summary>--shot demo: groups made only of built-in Windows programs, for README screenshots.</summary>
    static int MakeDemo()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys = Environment.SystemDirectory;
        ShortcutItem It(string path, string name = "") => new() { FilePath = path, Name = name };

        var tools = new Group { Name = "Tools", ColorString = "#1F1F1F", Columns = 4, ShowLabels = true, IconSize = 32 };
        ShortcutItem App(string id, string name) => new() { FilePath = id, Name = name, IsWindowsApp = true };
        tools.Items.AddRange([
            It(Path.Combine(win, "explorer.exe"), "File Explorer"),
            It(Path.Combine(sys, "notepad.exe"), "Notepad"),
            App("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator"),
            App("Microsoft.Paint_8wekyb3d8bbwe!App", "Paint"),
            App("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Terminal"),
            It(Path.Combine(sys, "Taskmgr.exe"), "Task Manager"),
            App("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", "Settings"),
            It(Path.Combine(sys, "cmd.exe"), "Command Prompt"),
        ]);
        GroupStore.Save(tools, null, null);

        var folders = new Group { Name = "Folders", ColorString = "#2B3A55", Columns = 5, IconSize = 32 };
        foreach (var sf in new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.Desktop })
            folders.Items.Add(It(Environment.GetFolderPath(sf)));
        GroupStore.Save(folders, null, null);
        return 0;
    }

    public static int Run(string[] a)
    {
        Program.TestMode = true;
        if (a[0] == "selftest") return SelfTest(a[^1]);
        if (a[0] == "resave") { var rg = Group.Load(a[1])!; GroupStore.Save(rg, rg.Name, null); return 0; }
        if (a[0] == "demo") return MakeDemo();
        Application.SetColorMode(SystemColorMode.Dark);
        var center = Screen.PrimaryScreen!.Bounds;
        var pt = new Point(center.X + center.Width / 2, center.Y + center.Height / 2);

        Form form = a[0] switch
        {
            "popup" => new PopupForm(Group.Load(a[1])!, pt, preview: true),
            "group" => new GroupEditorForm(Group.Load(a[1])),
            "new" => new GroupEditorForm(null),
            "update" => new DarkPrompt("Version 1.1.0 is available",
                DarkPrompt.Highlights(File.ReadAllText(a[1])) + "\n\nYour groups and pins are kept.",
                ["Skip this version", "Not now", "Install"]) { StartPosition = FormStartPosition.CenterScreen },
            "ask" => new DarkPrompt("Check for updates?",
                "Only asks GitHub for the newest version number. Nothing is sent or installed without you.",
                ["No", "Yes"]) { StartPosition = FormStartPosition.CenterScreen },
            _ => new MainForm(null),
        };
        string outFile = a[^1];

        form.Shown += async (_, _) =>
        {
            await Task.Delay(600);
            using var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                PrintWindow(form.Handle, hdc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(outFile, ImageFormat.Png);
            Application.Exit();
        };
        Application.Run(form);
        return 0;
    }
}
