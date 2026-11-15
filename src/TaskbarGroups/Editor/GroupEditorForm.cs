using System.Drawing;
using TaskbarGroups.Icons;
using TaskbarGroups.Model;
using TaskbarGroups.Popup;
using TaskbarGroups.Shell;

namespace TaskbarGroups.Editor;

/// <summary>Create or edit one group.</summary>
sealed class GroupEditorForm : Form
{
    const string DragFormat = "TaskbarGroups.ItemIndex";

    readonly Group work;
    readonly string? originalName;
    Bitmap groupImage;
    Bitmap? newImage;
    bool dirty, loadingDetails;
    public string SavedName { get; private set; } = "";

    // header
    readonly Panel iconTile = new() { Cursor = Cursors.Hand };
    readonly TextBox txtName = Theme.TextBox();
    readonly ContextMenuStrip iconMenu = new() { Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
    // programs
    readonly DarkList list = new() { AllowDrop = true };
    readonly Button btnAdd = Theme.Button("Add program…");
    readonly Button btnAddFolder = Theme.Button("Add folder…");
    readonly Button btnRemove = Theme.Button("Remove");
    readonly Button btnUp = Theme.Button("Move up");
    readonly Button btnDown = Theme.Button("Move down");
    // selected program
    readonly Label lblTarget = Theme.Label("", dim: true);
    readonly TextBox txtItemName = Theme.TextBox();
    readonly TextBox txtArgs = Theme.TextBox();
    readonly TextBox txtDir = Theme.TextBox();
    readonly Button btnDir = Theme.Button("…");
    readonly CheckBox chkAdmin = Theme.CheckBox("Run as administrator");
    // look
    readonly NumericUpDown numCols = new() { Minimum = 1, Maximum = 20, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
    readonly ComboBox cmbSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Field, ForeColor = Theme.Text };
    readonly ComboBox cmbColor = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Field, ForeColor = Theme.Text };
    readonly NumericUpDown numTrans = new() { Minimum = 0, Maximum = 90, Increment = 5, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
    readonly CheckBox chkLabels = Theme.CheckBox("Show names under icons");
    readonly CheckBox chkOpenAll = Theme.CheckBox("Allow \"open all\" (Ctrl+Enter)");
    readonly Button btnPreview = Theme.Button("Preview");
    // bottom
    readonly Label lblError = Theme.Label("");
    readonly Button btnDelete = Theme.Button("Delete group");
    readonly Button btnCancel = Theme.Button("Cancel");
    readonly Button btnSave = Theme.Button("Save", accent: true);

    readonly Dictionary<ShortcutItem, Bitmap> listIcons = new(ReferenceEqualityComparer.Instance);
    int dragFrom = -1;
    Point dragStart;
    string customColor;

    public GroupEditorForm(Group? existing)
    {
        originalName = existing?.Name;
        work = existing?.Clone() ?? new Group();
        customColor = work.ColorString;
        groupImage = LoadGroupImage(existing);

        SuspendLayout();
        Theme.Prepare(this, 680, 652);
        Text = existing == null ? "New group" : "Edit group — " + existing.DisplayName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        CancelButton = btnCancel;

        BuildHeader();
        BuildPrograms();
        BuildDetails();
        BuildLook();
        BuildBottom();

        ResumeLayout(false);
        PerformLayout();

        LoadValues();
        dirty = false;
    }

    float S => DeviceDpi / 96f;

    // ------------------------------------------------------------------ layout

    void BuildHeader()
    {
        iconTile.Location = new Point(20, 20);
        iconTile.Size = new Size(72, 72);
        iconTile.Paint += PaintIconTile;
        iconTile.Click += (_, _) => iconMenu.Show(iconTile, new Point(0, iconTile.Height));
        iconTile.AllowDrop = true;
        iconTile.DragEnter += (_, e) => e.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        iconTile.DragDrop += (_, e) =>
        {
            if (e.Data!.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f) SetIconFromFile(f[0]);
        };
        new ToolTip().SetToolTip(iconTile, "Click to change the group icon (or drop an image/program here)");

        iconMenu.Font = new Font("Segoe UI", 9.75f);
        AddMenu(iconMenu, "Choose an image or .ico file…", PickIconImage);
        AddMenu(iconMenu, "Use a program's icon…", PickIconProgram);
        AddMenu(iconMenu, "Use the first program's icon", UseFirstProgramIcon);
        AddMenu(iconMenu, "Plain \"+\" (default)", () => SetImage(Gfx.DefaultGroupImage()));

        var lbl = Theme.Label("Group name", dim: true);
        lbl.Location = new Point(108, 24);
        txtName.Location = new Point(108, 46);
        txtName.Size = new Size(332, 27);
        txtName.MaxLength = 60;
        txtName.TextChanged += (_, _) => { dirty = true; lblError.Text = ""; };

        var hint = Theme.Label("Click the icon to change it.", dim: true);
        hint.Location = new Point(108, 76);
        Controls.AddRange([iconTile, lbl, txtName, hint]);
    }

    void BuildPrograms()
    {
        var hdr = Theme.Label("Programs", header: true);
        hdr.Location = new Point(18, 108);

        list.Location = new Point(20, 134);
        list.Size = new Size(420, 236);
        list.Placeholder = "No programs yet.\n\nClick \"Add program…\" or drag programs here\nfrom Explorer, the desktop or the Start menu.";
        list.DrawItem += DrawProgram;
        list.SelectedIndexChanged += (_, _) => ShowDetails();
        list.MouseDown += List_MouseDown;
        list.MouseMove += List_MouseMove;
        list.DragEnter += List_DragOver;
        list.DragOver += List_DragOver;
        list.DragDrop += List_DragDrop;
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Up) { MoveSelected(-1); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Down) { MoveSelected(1); e.Handled = true; }
        };

        int y = 134;
        foreach (var b in new[] { btnAdd, btnAddFolder, btnRemove, btnUp, btnDown })
        {
            b.Location = new Point(460, y);
            b.Size = new Size(200, 32);
            y += 38;
        }
        btnAdd.Click += (_, _) => AddPrograms();
        btnAddFolder.Click += (_, _) => AddFolder();
        btnRemove.Click += (_, _) => RemoveSelected();
        btnUp.Click += (_, _) => MoveSelected(-1);
        btnDown.Click += (_, _) => MoveSelected(1);

        var tip = Theme.Label("Drag programs here from Explorer, the desktop or the Start menu. Drag to reorder. " +
                              "In the popup, keys 1–9 open them in this order.", dim: true);
        tip.AutoSize = false;
        tip.Location = new Point(20, 374);
        tip.Size = new Size(420, 38);
        tip.Font = new Font("Segoe UI", 8.75f);

        Controls.AddRange([hdr, list, btnAdd, btnAddFolder, btnRemove, btnUp, btnDown, tip]);
    }

    void BuildDetails()
    {
        var hdr = Theme.Label("Selected program", header: true);
        hdr.Location = new Point(18, 418);
        lblTarget.AutoSize = false;
        lblTarget.AutoEllipsis = true;
        lblTarget.Location = new Point(160, 421);
        lblTarget.Size = new Size(280, 20);

        int y = 446;
        foreach (var (text, box) in new[] { ("Name", txtItemName), ("Arguments", txtArgs), ("Start in", txtDir) })
        {
            var l = Theme.Label(text, dim: true);
            l.Location = new Point(20, y + 4);
            box.Location = new Point(108, y);
            box.Size = new Size(332, 27);
            Controls.AddRange([l, box]);
            y += 34;
        }
        txtDir.Width = 296;
        btnDir.Location = new Point(410, 513);
        btnDir.Size = new Size(30, 27);
        chkAdmin.Location = new Point(108, 550);

        txtItemName.TextChanged += (_, _) => EditItem(i => i.Name = txtItemName.Text);
        txtArgs.TextChanged += (_, _) => EditItem(i => i.Arguments = txtArgs.Text);
        txtDir.TextChanged += (_, _) => EditItem(i => i.WorkingDirectory = txtDir.Text);
        chkAdmin.CheckedChanged += (_, _) => EditItem(i => i.RunAsAdmin = chkAdmin.Checked);
        btnDir.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { InitialDirectory = Directory.Exists(txtDir.Text) ? txtDir.Text : "" };
            if (d.ShowDialog(this) == DialogResult.OK) txtDir.Text = d.SelectedPath;
        };
        new ToolTip().SetToolTip(txtArgs, "Extra command-line arguments passed to the program");
        new ToolTip().SetToolTip(txtDir, "Working folder for .exe files (shortcuts use their own)");

        Controls.AddRange([hdr, lblTarget, btnDir, chkAdmin]);
    }

    void BuildLook()
    {
        var hdr = Theme.Label("Look", header: true);
        hdr.Location = new Point(458, 334);
        Controls.Add(hdr);

        int y = 362;
        foreach (var (text, ctl, w) in new (string, Control, int)[]
                 { ("Columns", numCols, 70), ("Icon size", cmbSize, 104), ("Background", cmbColor, 104), ("Transparency %", numTrans, 70) })
        {
            var l = Theme.Label(text, dim: true);
            l.Location = new Point(460, y + 3);
            ctl.Location = new Point(660 - w, y);
            ctl.Width = w;
            Controls.AddRange([l, ctl]);
            y += 34;
        }
        cmbSize.Items.AddRange(["Small", "Medium", "Large"]);
        cmbColor.Items.AddRange(["Dark", "Light", "Custom…"]);
        chkLabels.Location = new Point(460, 500);
        chkOpenAll.Location = new Point(460, 528);
        btnPreview.Location = new Point(460, 562);
        btnPreview.Size = new Size(200, 32);

        numCols.ValueChanged += (_, _) => { work.Columns = (int)numCols.Value; dirty = true; };
        numTrans.ValueChanged += (_, _) => { work.Transparency = (double)numTrans.Value; dirty = true; };
        cmbSize.SelectedIndexChanged += (_, _) => { work.IconSize = Group.IconSizes[Math.Max(0, cmbSize.SelectedIndex)]; dirty = true; };
        cmbColor.SelectionChangeCommitted += (_, _) => ColorChosen();
        chkLabels.CheckedChanged += (_, _) => { work.ShowLabels = chkLabels.Checked; dirty = true; };
        chkOpenAll.CheckedChanged += (_, _) => { work.AllowOpenAll = chkOpenAll.Checked; dirty = true; };
        btnPreview.Click += (_, _) => Preview();

        Controls.AddRange([chkLabels, chkOpenAll, btnPreview]);
    }

    void BuildBottom()
    {
        btnDelete.Location = new Point(20, 606);
        btnDelete.Size = new Size(120, 32);
        btnDelete.Visible = originalName != null;
        btnDelete.ForeColor = Theme.Error;
        lblError.AutoSize = false;
        lblError.Location = new Point(150, 612);
        lblError.Size = new Size(300, 24);
        lblError.ForeColor = Theme.Error;
        btnCancel.Location = new Point(460, 606);
        btnCancel.Size = new Size(96, 32);
        btnSave.Location = new Point(564, 606);
        btnSave.Size = new Size(96, 32);

        btnDelete.Click += (_, _) => DeleteGroup();
        btnCancel.Click += (_, _) => Close();
        btnSave.Click += (_, _) => Save();
        Controls.AddRange([btnDelete, lblError, btnCancel, btnSave]);
    }

    static void AddMenu(ContextMenuStrip m, string text, Action a)
    {
        var it = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
        it.Click += (_, _) => a();
        m.Items.Add(it);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitle(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        list.ItemHeight = (int)(44 * S);
        if (originalName == null) txtName.Focus();
    }

    // ------------------------------------------------------------------ values

    void LoadValues()
    {
        txtName.Text = originalName == null ? "" : work.DisplayName;
        numCols.Value = Math.Clamp(work.Columns, 1, 20);
        numTrans.Value = (decimal)Math.Clamp(Math.Round(work.Transparency / 5) * 5, 0, 90);
        cmbSize.SelectedIndex = Math.Max(0, Array.IndexOf(Group.IconSizes, work.IconSize));
        cmbColor.SelectedIndex = work.ColorString.Equals(Group.DarkColor, StringComparison.OrdinalIgnoreCase) ? 0
            : work.ColorString.Equals(Group.LightColor, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        chkLabels.Checked = work.ShowLabels;
        chkOpenAll.Checked = work.AllowOpenAll;
        RefreshList(work.Items.Count > 0 ? 0 : -1);
    }

    static Bitmap LoadGroupImage(Group? g)
    {
        try { if (g != null && File.Exists(g.ImageFile)) return Gfx.LoadBitmap(g.ImageFile); }
        catch { }
        return Gfx.DefaultGroupImage();
    }

    void ColorChosen()
    {
        switch (cmbColor.SelectedIndex)
        {
            case 0: work.ColorString = Group.DarkColor; break;
            case 1: work.ColorString = Group.LightColor; break;
            default:
                using (var d = new ColorDialog { Color = work.BackColor, FullOpen = true })
                {
                    if (d.ShowDialog(this) == DialogResult.OK)
                        work.ColorString = customColor = ColorTranslator.ToHtml(Color.FromArgb(d.Color.R, d.Color.G, d.Color.B));
                    else
                        cmbColor.SelectedIndex = work.ColorString == Group.DarkColor ? 0 : work.ColorString == Group.LightColor ? 1 : 2;
                }
                break;
        }
        dirty = true;
        iconTile.Invalidate();
    }

    // ------------------------------------------------------------------ group icon

    void PaintIconTile(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.HighQuality(g);
        g.Clear(Theme.Bg);
        var r = new RectangleF(0.5f, 0.5f, iconTile.Width - 1.5f, iconTile.Height - 1.5f);
        using var path = Gfx.RoundedRect(r, 9 * S);
        using var b = new SolidBrush(work.BackColor);
        using var pen = new Pen(Theme.Border);
        g.FillPath(b, path);
        g.DrawPath(pen, path);
        g.DrawImage(groupImage, RectangleF.Inflate(r, -12 * S, -12 * S));
    }

    void SetImage(Bitmap img)
    {
        newImage?.Dispose();
        newImage = img;
        groupImage = img;
        dirty = true;
        iconTile.Invalidate();
    }

    void PickIconImage()
    {
        using var d = new OpenFileDialog
        {
            Title = "Choose a group icon",
            Filter = "Images and icons|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (d.ShowDialog(this) == DialogResult.OK) SetIconFromFile(d.FileName);
    }

    void PickIconProgram()
    {
        using var d = new OpenFileDialog
        {
            Title = "Use the icon of…",
            Filter = "Programs and shortcuts|*.exe;*.lnk;*.dll|All files|*.*",
            DereferenceLinks = false,
        };
        if (d.ShowDialog(this) == DialogResult.OK) SetIconFromFile(d.FileName);
    }

    void UseFirstProgramIcon()
    {
        if (work.Items.Count == 0) { lblError.Text = "Add a program first."; return; }
        if (ShellInfo.GetIcon(work.Items[0].ShellPath, 256) is { } bmp) SetImage(bmp);
    }

    void SetIconFromFile(string file)
    {
        try
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            Bitmap? bmp = ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => Gfx.LoadBitmap(file),
                ".ico" => new Icon(file, 256, 256).ToBitmap(),
                _ => ShellInfo.GetIcon(file, 256),
            };
            if (bmp != null) SetImage(bmp);
            else lblError.Text = "Couldn't read an icon from that file.";
        }
        catch (Exception ex)
        {
            Paths.LogError(ex, "Loading icon " + file);
            lblError.Text = "Couldn't read an icon from that file.";
        }
    }

    // ------------------------------------------------------------------ program list

    ShortcutItem? SelectedItem => list.SelectedIndex >= 0 && list.SelectedIndex < work.Items.Count ? work.Items[list.SelectedIndex] : null;

    void RefreshList(int select)
    {
        list.BeginUpdate();
        list.Items.Clear();
        for (int i = 0; i < work.Items.Count; i++) list.Items.Add(i);
        list.EndUpdate();
        list.SelectedIndex = Math.Min(select, work.Items.Count - 1);
        if (list.SelectedIndex < 0) ShowDetails();
    }

    Bitmap ListIcon(ShortcutItem item)
    {
        if (!listIcons.TryGetValue(item, out var bmp))
            listIcons[item] = bmp = IconCache.Extract(item, (int)(28 * S));
        return bmp;
    }

    void DrawProgram(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= work.Items.Count) return;
        var g = e.Graphics;
        var item = work.Items[e.Index];
        var r = e.Bounds;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        float s = S;

        using (var bg = new SolidBrush(sel ? Theme.Selected : Theme.Surface)) g.FillRectangle(bg, r);
        if (sel)
        {
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, r.X, r.Y + 8 * s, 3 * s, r.Height - 16 * s);
        }

        // position number = the key that opens it in the popup
        string num = e.Index < 9 ? (e.Index + 1).ToString() : e.Index == 9 ? "0" : "";
        TextRenderer.DrawText(g, num, Font, new Rectangle(r.X + (int)(6 * s), r.Y, (int)(18 * s), r.Height), Theme.Dim,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        int ip = (int)(28 * s);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(ListIcon(item), r.X + (int)(28 * s), r.Y + (r.Height - ip) / 2, ip, ip);

        int tx = r.X + (int)(66 * s);
        var nameRect = new Rectangle(tx, r.Y + (int)(4 * s), r.Right - tx - (int)(8 * s), r.Height / 2);
        TextRenderer.DrawText(g, item.DisplayName, Font, nameRect, item.Exists ? Theme.Text : Theme.Error,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Bottom);
        string sub = item.IsWindowsApp ? "Store app" : item.Exists ? item.FilePath : "Missing: " + item.FilePath;
        using var small = new Font(Font.FontFamily, Font.Size * 0.85f);
        var subRect = new Rectangle(tx, r.Y + r.Height / 2 + (int)(1 * s), nameRect.Width, r.Height / 2 - (int)(4 * s));
        TextRenderer.DrawText(g, sub, small, subRect, Theme.Dim,
            TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
    }

    void ShowDetails()
    {
        var item = SelectedItem;
        loadingDetails = true;
        foreach (Control c in new Control[] { txtItemName, txtArgs, txtDir, btnDir, chkAdmin, btnRemove })
            c.Enabled = item != null;
        btnUp.Enabled = item != null && list.SelectedIndex > 0;
        btnDown.Enabled = item != null && list.SelectedIndex < work.Items.Count - 1;

        txtItemName.Text = item?.DisplayName ?? "";
        txtArgs.Text = item?.Arguments ?? "";
        txtDir.Text = item?.WorkingDirectory ?? "";
        chkAdmin.Checked = item?.RunAsAdmin ?? false;
        lblTarget.Text = item == null ? "" : item.IsWindowsApp ? item.FilePath : item.FilePath;

        bool isPlainFile = item != null && !item.IsWindowsApp && !item.IsFolder &&
                           Path.GetExtension(item.FilePath).ToLowerInvariant() is not ".lnk" and not ".url";
        txtDir.Enabled = btnDir.Enabled = isPlainFile;
        chkAdmin.Enabled = item != null && !item.IsWindowsApp && !item.IsFolder;
        loadingDetails = false;
    }

    void EditItem(Action<ShortcutItem> change)
    {
        if (loadingDetails || SelectedItem is not { } item) return;
        change(item);
        dirty = true;
        list.Invalidate(list.GetItemRectangle(list.SelectedIndex));
    }

    void AddPrograms()
    {
        using var d = new OpenFileDialog
        {
            Title = "Add programs",
            Multiselect = true,
            DereferenceLinks = false,
            Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd;*.ps1;*.msc;*.appref-ms|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };
        if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames.Select(f => (f, false)));
    }

    void AddFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Add a folder to the group", UseDescriptionForTitle = true };
        if (d.ShowDialog(this) == DialogResult.OK) AddPaths([(d.SelectedPath, false)]);
    }

    void AddPaths(IEnumerable<(string path, bool isApp)> paths, int insertAt = -1)
    {
        int at = insertAt < 0 ? work.Items.Count : insertAt;
        int first = at;
        foreach (var (path, isApp) in paths)
        {
            var item = new ShortcutItem { IsWindowsApp = isApp, FilePath = isApp ? path : Path.GetFullPath(path) };
            if (!isApp)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                item.WorkingDirectory = Directory.Exists(path) ? path
                    : ext == ".lnk" ? Path.GetDirectoryName(ShellInfo.GetLinkTarget(path) ?? path) ?? ""
                    : Path.GetDirectoryName(path) ?? "";
            }
            item.Name = ShortcutItem.DefaultName(item.FilePath, isApp);
            work.Items.Insert(at++, item);
        }
        if (at == first) return;
        dirty = true;
        lblError.Text = "";
        RefreshList(first);
    }

    void RemoveSelected()
    {
        int i = list.SelectedIndex;
        if (i < 0) return;
        work.Items.RemoveAt(i);
        dirty = true;
        RefreshList(Math.Min(i, work.Items.Count - 1));
    }

    void MoveSelected(int delta) => MoveItem(list.SelectedIndex, list.SelectedIndex + delta);

    void MoveItem(int from, int to)
    {
        if (from < 0 || from >= work.Items.Count) return;
        to = Math.Clamp(to, 0, work.Items.Count - 1);
        if (from == to) return;
        var item = work.Items[from];
        work.Items.RemoveAt(from);
        work.Items.Insert(to, item);
        dirty = true;
        RefreshList(to);
    }

    void List_MouseDown(object? sender, MouseEventArgs e)
    {
        dragFrom = e.Button == MouseButtons.Left ? list.IndexFromPoint(e.Location) : -1;
        dragStart = e.Location;
    }

    void List_MouseMove(object? sender, MouseEventArgs e)
    {
        if (dragFrom < 0 || e.Button != MouseButtons.Left) return;
        var ds = SystemInformation.DragSize;
        if (Math.Abs(e.X - dragStart.X) < ds.Width && Math.Abs(e.Y - dragStart.Y) < ds.Height) return;
        int from = dragFrom;
        dragFrom = -1;
        var data = new DataObject();
        data.SetData(DragFormat, from);
        list.DoDragDrop(data, DragDropEffects.Move);
    }

    void List_DragOver(object? sender, DragEventArgs e)
    {
        var d = e.Data!;
        if (d.GetDataPresent(DragFormat)) e.Effect = DragDropEffects.Move;
        else if (d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent("Shell IDList Array")) e.Effect = DragDropEffects.Copy | DragDropEffects.Link;
        else e.Effect = DragDropEffects.None;
    }

    void List_DragDrop(object? sender, DragEventArgs e)
    {
        var d = e.Data!;
        var pt = list.PointToClient(new Point(e.X, e.Y));
        int target = list.IndexFromPoint(pt);

        if (d.GetDataPresent(DragFormat))
        {
            int from = (int)d.GetData(DragFormat)!;
            MoveItem(from, target < 0 ? work.Items.Count - 1 : target);
            return;
        }
        var items = ShellInfo.GetDroppedItems(d);
        if (items.Count > 0) AddPaths(items, target);
    }

    // ------------------------------------------------------------------ actions

    void Preview()
    {
        if (work.Items.Count == 0) { lblError.Text = "Add a program first."; return; }
        var copy = work.Clone();
        if (string.IsNullOrWhiteSpace(copy.Name)) copy.Name = Group.ToFolderName(txtName.Text.Length > 0 ? txtName.Text : "Preview");
        new PopupForm(copy, Cursor.Position, preview: true).Show();
    }

    void Save()
    {
        var err = GroupStore.ValidateName(txtName.Text, originalName);
        if (err != null) { lblError.Text = err; txtName.Focus(); return; }
        if (work.Items.Count == 0) { lblError.Text = "Add at least one program."; return; }

        var newFolder = Group.ToFolderName(txtName.Text);
        if (originalName != null && newFolder != originalName)
        {
            var ok = MessageBox.Show(this,
                "Renaming a group breaks its current taskbar pin.\n\nAfter saving, unpin the old button and pin the new shortcut " +
                "(Show shortcut → right-click → Pin to taskbar).\n\nRename anyway?",
                "Rename group", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ok != DialogResult.OK) return;
        }

        work.Name = newFolder;
        UseWaitCursor = true;
        try
        {
            GroupStore.Save(work, originalName, newImage);
            SavedName = work.Name;
            dirty = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Paths.LogError(ex, "Saving group");
            lblError.Text = "Couldn't save: " + ex.Message;
        }
        finally { UseWaitCursor = false; }
    }

    public static bool ConfirmDelete(IWin32Window owner, Group g) =>
        MessageBox.Show(owner,
            $"Delete the group \"{g.DisplayName}\"?\n\nIts files go to the Recycle Bin. If it's pinned to the taskbar, unpin it too.",
            "Delete group", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.OK;

    void DeleteGroup()
    {
        if (originalName == null || Group.Load(originalName) is not { } g) return;
        if (!ConfirmDelete(this, g)) return;
        try
        {
            GroupStore.Delete(g);
            SavedName = "";
            dirty = false;
            DialogResult = DialogResult.Abort;
            Close();
        }
        catch (Exception ex)
        {
            Paths.LogError(ex, "Deleting group");
            lblError.Text = "Couldn't delete: " + ex.Message;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (dirty && DialogResult != DialogResult.OK && DialogResult != DialogResult.Abort)
        {
            var r = MessageBox.Show(this, "Discard your changes to this group?", Paths.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) { e.Cancel = true; DialogResult = DialogResult.None; return; }
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var b in listIcons.Values) b.Dispose();
            groupImage.Dispose();
            iconMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
