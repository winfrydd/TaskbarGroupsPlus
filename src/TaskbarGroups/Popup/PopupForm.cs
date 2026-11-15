using System.Diagnostics;
using System.Drawing;
using TaskbarGroups.Icons;
using TaskbarGroups.Model;
using TaskbarGroups.Shell;

namespace TaskbarGroups.Popup;

/// <summary>
/// The window that opens when a group's taskbar button is clicked.
/// Fully owner-drawn (no child controls) so it appears as fast as possible.
/// Mouse: click opens; Ctrl+click or middle-click opens and keeps the popup open; right-click for more.
/// Keys: 1-9/0 open by position, arrows + Enter, Ctrl+Enter opens all, Esc closes.
/// </summary>
sealed class PopupForm : Form
{
    readonly Group group;
    readonly bool preview;
    readonly float s;                // DPI scale of the target monitor
    readonly int iconPx, cellW, cellH, margin, cols, rows;
    readonly Bitmap[] icons;
    readonly Rectangle[] cells;
    readonly Color back, hoverBack, pressBack, textColor;
    readonly Font? labelFont;
    readonly ToolTip tip = new() { UseAnimation = false, UseFading = false };
    readonly System.Windows.Forms.Timer tipTimer = new() { Interval = 450 };
    int hover = -1, keySel = -1, pressed = -1;
    bool menuOpen, closing;

    public PopupForm(Group group, Point cursor, bool preview = false)
    {
        this.group = group;
        this.preview = preview;

        int dpi = Native.DpiAt(cursor);
        s = dpi / 96f;

        iconPx = (int)Math.Round(group.IconSize * s);
        margin = (int)Math.Round(6 * s);
        if (group.ShowLabels)
        {
            labelFont = new Font("Segoe UI", 12f * s, GraphicsUnit.Pixel);
            cellW = Math.Max(iconPx + (int)(28 * s), (int)(78 * s));
            cellH = iconPx + (int)(16 * s) + labelFont.Height + (int)(4 * s);
        }
        else
        {
            cellW = cellH = iconPx + (int)Math.Round(20 * s);
        }

        int n = Math.Max(1, group.Items.Count);
        cols = Math.Clamp(group.Columns, 1, n);
        rows = (n + cols - 1) / cols;

        Timing.Mark("ctor");
        // Preview (unsaved group) never touches the disk cache.
        icons = group.Items.Select(i => preview ? IconCache.Extract(i, iconPx) : IconCache.Get(group, i, iconPx)).ToArray();
        Timing.Mark("icons");
        cells = new Rectangle[group.Items.Count];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new Rectangle(margin + i % cols * cellW, margin + i / cols * cellH, cellW, cellH);

        back = group.BackColor;
        bool light = Gfx.IsLight(back);
        hoverBack = Gfx.Blend(back, light ? Color.Black : Color.White, light ? 0.10 : 0.13);
        pressBack = Gfx.Blend(back, light ? Color.Black : Color.White, light ? 0.18 : 0.22);
        textColor = light ? Color.FromArgb(28, 28, 28) : Color.FromArgb(235, 235, 235);

        // Window setup. Bounds are set before the handle exists so the window is created directly on
        // the right monitor at the right DPI (no rescale flash).
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = !preview;   // groups under the pinned button (same AppUserModelID)
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = back;
        Text = group.DisplayName;
        if (group.Transparency > 0) Opacity = 1 - group.Transparency / 100.0;

        var size = new Size(margin * 2 + cols * cellW, margin * 2 + rows * cellH);
        Bounds = Placement.Compute(cursor, size, (int)Math.Round(12 * s), preview);

        try { if (File.Exists(group.IconFile)) Icon = new Icon(group.IconFile); } catch { }

        tipTimer.Tick += (_, _) => ShowTip();
        Timing.Mark("ctorDone");
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        var border = Gfx.Blend(back, Gfx.IsLight(back) ? Color.Black : Color.White, 0.18);
        int colorRef = border.R | border.G << 8 | border.B << 16;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref colorRef, 4);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Native.SetForegroundWindow(Handle);
        Timing.Mark("shown");
        Timing.Flush();
    }

    // ---------------- painting ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        float radius = 6 * s;

        for (int i = 0; i < cells.Length; i++)
        {
            var c = cells[i];
            if (i == pressed || i == hover || i == keySel)
            {
                var r = RectangleF.Inflate(c, -2 * s, -2 * s);
                using var path = Gfx.RoundedRect(r, radius);
                using var b = new SolidBrush(i == pressed ? pressBack : hoverBack);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillPath(b, path);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            }

            int iy = labelFont != null ? c.Y + (int)(9 * s) : c.Y + (c.Height - iconPx) / 2;
            var icon = icons[i];
            var dest = new Rectangle(c.X + (c.Width - iconPx) / 2, iy, iconPx, iconPx);
            if (icon.Width == iconPx && icon.Height == iconPx) g.DrawImageUnscaled(icon, dest.Location);
            else g.DrawImage(icon, dest);

            if (labelFont != null)
            {
                var tr = new Rectangle(c.X + (int)(3 * s), dest.Bottom + (int)(4 * s), c.Width - (int)(6 * s), labelFont.Height);
                TextRenderer.DrawText(g, group.Items[i].DisplayName, labelFont, tr, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }

    void InvalidateCell(int i)
    {
        if (i >= 0 && i < cells.Length) Invalidate(cells[i]);
    }

    // ---------------- mouse ----------------

    int HitTest(Point p)
    {
        for (int i = 0; i < cells.Length; i++)
            if (cells[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h == hover) return;
        InvalidateCell(hover);
        hover = h;
        InvalidateCell(hover);
        if (keySel != -1) { InvalidateCell(keySel); keySel = -1; }

        tip.Hide(this);
        tipTimer.Stop();
        if (hover >= 0 && !group.ShowLabels) tipTimer.Start();
    }

    void ShowTip()
    {
        tipTimer.Stop();
        if (hover < 0 || menuOpen) return;
        var c = cells[hover];
        var item = group.Items[hover];
        var text = item.Exists ? item.DisplayName : item.DisplayName + " (missing)";
        tip.Show(text, this, c.X, c.Bottom + (int)(2 * s), 3000);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        tipTimer.Stop();
        tip.Hide(this);
        InvalidateCell(hover);
        hover = -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Middle)
        {
            pressed = HitTest(e.Location);
            InvalidateCell(pressed);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int i = HitTest(e.Location);
        int wasPressed = pressed;
        pressed = -1;
        InvalidateCell(wasPressed);

        switch (e.Button)
        {
            case MouseButtons.Left when i >= 0 && i == wasPressed:
                Open(i, keepOpen: (ModifierKeys & Keys.Control) != 0);
                break;
            case MouseButtons.Middle when i >= 0 && i == wasPressed:
                Open(i, keepOpen: true);
                break;
            case MouseButtons.Right:
                ShowMenu(i, e.Location);
                break;
        }
    }

    // ---------------- keyboard ----------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Control) != 0;

        if (key == Keys.Escape) { CloseNow(); return true; }

        int num = key switch
        {
            >= Keys.D1 and <= Keys.D9 => key - Keys.D1,
            >= Keys.NumPad1 and <= Keys.NumPad9 => key - Keys.NumPad1,
            Keys.D0 or Keys.NumPad0 => 9,
            _ => -1,
        };
        if (num >= 0)
        {
            if (num < cells.Length) Open(num, keepOpen: ctrl);
            return true;
        }

        if (key == Keys.Enter)
        {
            if (ctrl) OpenAll();
            else if (keySel >= 0) Open(keySel, keepOpen: false);
            else if (hover >= 0) Open(hover, keepOpen: false);
            return true;
        }

        int dx = key switch { Keys.Left => -1, Keys.Right => 1, _ => 0 };
        int dy = key switch { Keys.Up => -1, Keys.Down => 1, _ => 0 };
        if ((dx != 0 || dy != 0) && cells.Length > 0)
        {
            int cur = keySel >= 0 ? keySel : hover >= 0 ? hover : -1;
            int next;
            if (cur < 0) next = 0;
            else
            {
                next = cur + dx + dy * cols;
                if (next < 0 || next >= cells.Length) next = cur;
            }
            InvalidateCell(keySel);
            InvalidateCell(hover);
            hover = -1;
            keySel = next;
            InvalidateCell(keySel);
            return true;
        }

        if (key == Keys.Tab && cells.Length > 0)
        {
            InvalidateCell(keySel);
            int step = (keyData & Keys.Shift) != 0 ? -1 : 1;
            keySel = ((keySel < 0 ? (step > 0 ? -1 : 0) : keySel) + step + cells.Length) % cells.Length;
            InvalidateCell(keySel);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------- actions ----------------

    void Open(int i, bool keepOpen, bool asAdmin = false)
    {
        if (i < 0 || i >= group.Items.Count) return;
        bool ok = Launcher.Launch(group.Items[i], asAdmin);
        if (ok && !keepOpen) CloseNow();
    }

    void OpenAll()
    {
        if (!group.AllowOpenAll && !preview) return;
        foreach (var item in group.Items)
            if (item.Exists) Launcher.Launch(item);
        CloseNow();
    }

    void ShowMenu(int i, Point at)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = false };
        menu.Font = new Font("Segoe UI", 9f);
        if (i >= 0)
        {
            var item = group.Items[i];
            Add(menu, "Open", () => Open(i, false), bold: true);
            Add(menu, "Open and keep this open", () => Open(i, true));
            if (!item.IsWindowsApp && !item.IsFolder)
                Add(menu, "Run as administrator", () => Open(i, false, asAdmin: true));
            Add(menu, item.IsWindowsApp ? "Show all apps" : "Open file location", () => { Launcher.OpenLocation(item); CloseNow(); });
            menu.Items.Add(new ToolStripSeparator());
        }
        if (group.Items.Count > 1 && group.AllowOpenAll)
            Add(menu, $"Open all {group.Items.Count}", OpenAll);
        if (!preview)
            Add(menu, "Edit group…", EditGroup);

        menuOpen = true;
        menu.Closed += (_, _) =>
        {
            menuOpen = false;
            BeginInvoke(() => { if (ActiveForm != this) CloseNow(); });
        };
        menu.Show(this, at);

        static void Add(ContextMenuStrip m, string text, Action a, bool bold = false)
        {
            var it = new ToolStripMenuItem(text) { ForeColor = Color.FromArgb(235, 235, 235) };
            if (bold) it.Font = new Font(m.Font, FontStyle.Bold);
            it.Click += (_, _) => a();
            m.Items.Add(it);
        }
    }

    void EditGroup()
    {
        try { Process.Start(new ProcessStartInfo(Paths.ExePath, $"--edit \"{group.Name}\"") { UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) { Paths.LogError(ex, "Opening editor"); }
        CloseNow();
    }

    void CloseNow()
    {
        if (closing) return;
        closing = true;
        Close();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!menuOpen && !Program.TestMode) CloseNow();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var b in icons) b.Dispose();
            labelFont?.Dispose();
            tip.Dispose();
            tipTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
