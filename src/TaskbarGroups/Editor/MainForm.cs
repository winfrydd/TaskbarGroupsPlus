using System.Drawing;
using TaskbarGroups.Icons;
using TaskbarGroups.Model;

namespace TaskbarGroups.Editor;

/// <summary>The window you get when opening TaskbarGroups.exe directly: your list of groups.</summary>
sealed class MainForm : Form
{
    public const string WindowTitle = "Taskbar Groups";

    readonly DarkList list = new();
    readonly Button btnNew = Theme.Button("New group", accent: true);
    readonly Button btnEdit = Theme.Button("Edit");
    readonly Button btnShow = Theme.Button("Show shortcut");
    readonly Button btnDelete = Theme.Button("Delete");
    readonly Label lblEmpty = Theme.Label("No groups yet.\nClick \"New group\" to make your first one.", dim: true);
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
        MinimumSize = new Size(520, 380);
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

        Controls.AddRange([title, sub, lblEmpty, list, btnNew, btnEdit, btnShow, btnDelete, hint]);
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
            "Taskbar Groups", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
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
            MessageBox.Show(this, ex.Message, "Taskbar Groups", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show(this, "Couldn't delete the group:\n\n" + ex.Message, "Taskbar Groups", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Reload(null);
    }
}
