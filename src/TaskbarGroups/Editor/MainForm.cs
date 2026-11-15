using System.Drawing;
using TaskbarGroups.Icons;
using TaskbarGroups.Model;

namespace TaskbarGroups.Editor;

/// <summary>The window you get when opening TaskbarGroups.exe directly: your list of groups.</summary>
sealed class MainForm : Form
{
    public const string WindowTitle = Paths.AppName;

    readonly DarkList list = new();
    readonly Button btnNew = Theme.Button("New group", accent: true);
    readonly Button btnEdit = Theme.Button("Edit");
    readonly Button btnShow = Theme.Button("Show shortcut");
    readonly Button btnDelete = Theme.Button("Delete");
    readonly Button btnUpdate = Theme.Button("Update", accent: true);
    readonly Label lblVersion = Theme.Label($"Version {Paths.Version.ToString(3)}", dim: true);
    readonly LinkLabel lnkCheck = new() { Text = "Check for updates", AutoSize = true, BackColor = Color.Transparent };
    readonly Label lblEmpty = Theme.Label("No groups yet.\nClick \"New group\" to make your first one.", dim: true);
    readonly CheckBox chkAuto = Theme.CheckBox("Check on start");
    Updater.Release? pendingUpdate;
    bool loading = true;
    readonly string? openOnStart;

    List<Group> groups = [];
    readonly Dictionary<string, (Bitmap image, Bitmap[] minis)> visuals = [];

    public MainForm(string? openOnStart)
    {
        this.openOnStart = openOnStart;
        SuspendLayout();
        Theme.Prepare(this, 620, 470);
        Text = WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 470);
        KeyPreview = true;

        var title = Theme.Label("Your taskbar groups", header: true);
        title.Font = new Font("Segoe UI Semibold", 14f);
        title.Location = new Point(20, 14);
        var sub = Theme.Label("Each group is one taskbar button that opens a small panel of programs.", dim: true);
        sub.Location = new Point(22, 48);

        list.Location = new Point(20, 78);
        list.Size = new Size(440, 302);
        list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        list.DrawItem += DrawGroup;
        list.DoubleClick += (_, _) => EditSelected();
        list.SelectedIndexChanged += (_, _) => UpdateButtons();

        lblEmpty.Location = new Point(40, 110);
        lblEmpty.BackColor = Theme.Surface;

        int bx = 476, bw = 124;
        foreach (var (b, y) in new[] { (btnNew, 78), (btnEdit, 118), (btnShow, 158), (btnDelete, 198) })
        {
            b.Location = new Point(bx, y);
            b.Size = new Size(bw, 32);
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        }
        btnNew.Click += (_, _) => NewGroup();
        btnEdit.Click += (_, _) => EditSelected();
        btnShow.Click += (_, _) => ShowShortcut();
        btnDelete.Click += (_, _) => DeleteSelected();

        var hint = Theme.Label(
            "To put a group on your taskbar: select it, click \"Show shortcut\", then right-click the highlighted " +
            "file and choose \"Pin to taskbar\" (on Windows 11 it can be under \"Show more options\").", dim: true);
        hint.AutoSize = false;
        hint.Location = new Point(20, 392);
        hint.Size = new Size(580, 60);
        hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        btnUpdate.Location = new Point(bx, 250);
        btnUpdate.Size = new Size(bw, 32);
        btnUpdate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnUpdate.Visible = false;
        btnUpdate.Click += async (_, _) => await InstallUpdate();

        lblVersion.Location = new Point(bx + 2, 310);
        lblVersion.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        chkAuto.Location = new Point(bx + 2, 356);
        chkAuto.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        chkAuto.Visible = Updater.Configured;
        chkAuto.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            Settings.AutoCheckUpdates = chkAuto.Checked;
            Settings.Save();
            if (!chkAuto.Checked) btnUpdate.Visible = false;
        };
        lnkCheck.Location = new Point(bx + 2, 332);
        lnkCheck.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        lnkCheck.LinkColor = lnkCheck.ActiveLinkColor = Theme.Accent;
        lnkCheck.VisitedLinkColor = Theme.Accent;
        lnkCheck.LinkBehavior = LinkBehavior.HoverUnderline;
        lnkCheck.LinkClicked += async (_, _) => await CheckForUpdates(manual: true);
        lnkCheck.Visible = Updater.Configured;

        Controls.AddRange([title, sub, lblEmpty, list, btnNew, btnEdit, btnShow, btnDelete, btnUpdate, lblVersion, lnkCheck, chkAuto, hint]);
        lblEmpty.BringToFront();
        ResumeLayout(false);
        PerformLayout();
    }

    float S => DeviceDpi / 96f;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitle(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        list.ItemHeight = (int)(66 * S);
        Reload(null);
        GroupStore.RepairShortcuts(groups);
        Settings.Load();
        chkAuto.Checked = Settings.AutoCheckUpdates;
        loading = false;
        if (!Program.TestMode)
        {
            CheckWritable();
            if (Settings.AutoCheckUpdates) _ = CheckForUpdates(manual: false);
        }

        if (openOnStart != null)
        {
            int i = groups.FindIndex(g => g.Name.Equals(openOnStart, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                list.SelectedIndex = i;
                BeginInvoke(EditSelected);
            }
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        list.ItemHeight = (int)(66 * S);
        Reload(Selected?.Name);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter && Selected != null) { EditSelected(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete && Selected != null) { DeleteSelected(); e.Handled = true; }
        else if (e.KeyCode == Keys.N && e.Control) { NewGroup(); e.Handled = true; }
    }

    Group? Selected => list.SelectedIndex >= 0 && list.SelectedIndex < groups.Count ? groups[list.SelectedIndex] : null;

    void Reload(string? select)
    {
        foreach (var v in visuals.Values)
        {
            v.image.Dispose();
            foreach (var m in v.minis) m.Dispose();
        }
        visuals.Clear();

        groups = Group.LoadAll().ToList();
        int mini = (int)(20 * S);
        foreach (var g in groups)
        {
            Bitmap img;
            try { img = File.Exists(g.ImageFile) ? Gfx.LoadBitmap(g.ImageFile) : Gfx.DefaultGroupImage(); }
            catch { img = Gfx.DefaultGroupImage(); }
            var minis = g.Items.Take(12).Select(i => IconCache.Get(g, i, mini)).ToArray();
            visuals[g.Name] = (img, minis);
        }

        list.BeginUpdate();
        list.Items.Clear();
        foreach (var g in groups) list.Items.Add(g.Name);
        list.EndUpdate();

        int idx = select == null ? -1 : groups.FindIndex(g => g.Name.Equals(select, StringComparison.OrdinalIgnoreCase));
        list.SelectedIndex = idx >= 0 ? idx : (groups.Count > 0 ? 0 : -1);
        lblEmpty.Visible = groups.Count == 0;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bool has = Selected != null;
        btnEdit.Enabled = btnShow.Enabled = btnDelete.Enabled = has;
    }

    void DrawGroup(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= groups.Count) return;
        var g = e.Graphics;
        Gfx.HighQuality(g);
        var grp = groups[e.Index];
        var r = e.Bounds;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        float s = S;

        using (var bg = new SolidBrush(sel ? Theme.Selected : Theme.Surface))
            g.FillRectangle(bg, r);
        if (sel)
        {
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, r.X, r.Y + 10 * s, 3 * s, r.Height - 20 * s);
        }

        // Group icon on a tile in the group's own colour (a preview of the popup background).
        var tile = new RectangleF(r.X + 14 * s, r.Y + 9 * s, 48 * s, 48 * s);
        using (var path = Gfx.RoundedRect(tile, 7 * s))
        using (var tb = new SolidBrush(grp.BackColor))
            g.FillPath(tb, path);
        var (img, minis) = visuals[grp.Name];
        g.DrawImage(img, RectangleF.Inflate(tile, -8 * s, -8 * s));

        int tx = (int)(tile.Right + 14 * s);
        using (var bold = new Font("Segoe UI Semibold", 10.5f))
            TextRenderer.DrawText(g, grp.DisplayName, bold, new Point(tx, r.Y + (int)(8 * s)), Theme.Text, TextFormatFlags.NoPrefix);

        int mx = tx, my = r.Y + (int)(34 * s), mini = (int)(20 * s);
        foreach (var m in minis)
        {
            if (mx + mini > r.Right - 8 * s) break;
            g.DrawImage(m, mx, my, mini, mini);
            mx += mini + (int)(6 * s);
        }
        if (grp.Items.Count > minis.Length)
            TextRenderer.DrawText(g, $"+{grp.Items.Count - minis.Length}", Font, new Point(mx, my), Theme.Dim);
    }

    void NewGroup()
    {
        using var f = new GroupEditorForm(null);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Reload(f.SavedName);
        var answer = MessageBox.Show(this,
            "Group saved.\n\nShow its shortcut now so you can pin it to the taskbar?\n(Right-click it → Pin to taskbar)",
            Paths.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes) ShowShortcut();
    }

    void EditSelected()
    {
        var g = Selected;
        if (g == null) return;
        using var f = new GroupEditorForm(g);
        var result = f.ShowDialog(this);
        if (result == DialogResult.OK || result == DialogResult.Abort) Reload(f.SavedName);
    }

    void ShowShortcut()
    {
        var g = Selected;
        if (g == null) return;
        try
        {
            if (!File.Exists(g.ShortcutFile)) GroupStore.WriteShortcut(g);
            Launcher.ShowInExplorer(g.ShortcutFile);
        }
        catch (Exception ex)
        {
            Paths.LogError(ex, "Show shortcut");
            MessageBox.Show(this, ex.Message, Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Groups are stored next to the exe, so the folder must be writable (not Program Files).</summary>
    void CheckWritable()
    {
        try
        {
            Directory.CreateDirectory(Paths.ConfigDir);
            string probe = Path.Combine(Paths.ConfigDir, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            MessageBox.Show(this,
                $"{Paths.AppName} keeps your groups next to the program, but it isn't allowed to write to:\n\n{Paths.AppDir}\n\n" +
                "Move the whole folder somewhere you own, for example\n" +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TaskbarGroupsPlus") +
                "\n\n(Program Files doesn't work without running as administrator.)",
                Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    async Task CheckForUpdates(bool manual)
    {
        lnkCheck.Enabled = false;
        try
        {
            pendingUpdate = await Updater.CheckAsync();
            bool skipped = pendingUpdate != null && pendingUpdate.Version.ToString(3) == Settings.SkippedVersion;
            if (skipped && !manual) pendingUpdate = null;
            if (pendingUpdate != null)
            {
                btnUpdate.Text = $"Update to {pendingUpdate.Version.ToString(3)}";
                btnUpdate.Visible = true;
            }
            else if (manual)
                MessageBox.Show(this, $"You have the latest version ({Paths.Version.ToString(3)}).", Paths.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (manual)
                MessageBox.Show(this, "Couldn't reach GitHub to check for updates:\n\n" + ex.Message, Paths.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) lnkCheck.Enabled = true; }
    }

    async Task InstallUpdate()
    {
        var r = pendingUpdate;
        if (r == null) return;
        string ver = r.Version.ToString(3);
        string notes = r.Notes.Trim();
        if (notes.Length > 700) notes = notes[..700] + "…";

        var install = new TaskDialogCommandLinkButton("Install now",
            "Your groups and taskbar pins are kept. The window closes and reopens when it's done.");
        var skip = new TaskDialogCommandLinkButton("Skip this version",
            "Hide this update. You'll only hear about a newer one.");
        var later = new TaskDialogCommandLinkButton("Not now", "Ask again next time.");
        var page = new TaskDialogPage
        {
            Caption = Paths.AppName,
            Heading = $"Version {ver} is available (you have {Paths.Version.ToString(3)})",
            Text = notes,
            Icon = TaskDialogIcon.Information,
            Buttons = { install, skip, later },
            DefaultButton = install,
            AllowCancel = true,
        };
        var choice = TaskDialog.ShowDialog(this, page);
        if (choice == skip)
        {
            Settings.SkippedVersion = ver;
            Settings.Save();
            btnUpdate.Visible = false;
            pendingUpdate = null;
            return;
        }
        if (choice != install) return;

        btnUpdate.Enabled = false;
        btnUpdate.Text = "Downloading…";
        UseWaitCursor = true;
        try
        {
            await Updater.DownloadAndStartAsync(r);
            Close();
        }
        catch (Exception ex)
        {
            Paths.LogError(ex, "Update");
            UseWaitCursor = false;
            btnUpdate.Enabled = true;
            btnUpdate.Text = $"Update to {r.Version.ToString(3)}";
            MessageBox.Show(this, "The update couldn't be downloaded:\n\n" + ex.Message +
                (r.PageUrl.Length > 0 ? "\n\nYou can get it manually from:\n" + r.PageUrl : ""),
                Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void DeleteSelected()
    {
        var g = Selected;
        if (g == null) return;
        if (!GroupEditorForm.ConfirmDelete(this, g)) return;
        try { GroupStore.Delete(g); }
        catch (Exception ex)
        {
            Paths.LogError(ex, "Delete group");
            MessageBox.Show(this, "Couldn't delete the group:\n\n" + ex.Message, Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Reload(null);
    }
}
