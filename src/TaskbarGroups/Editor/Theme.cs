using System.Drawing;

namespace TaskbarGroups.Editor;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(32, 32, 32);
    public static readonly Color Surface = Color.FromArgb(43, 43, 43);
    public static readonly Color Field = Color.FromArgb(50, 50, 50);
    public static readonly Color Hover = Color.FromArgb(58, 58, 58);
    public static readonly Color Selected = Color.FromArgb(64, 64, 64);
    public static readonly Color Border = Color.FromArgb(75, 75, 75);
    public static readonly Color Text = Color.FromArgb(240, 240, 240);
    public static readonly Color Dim = Color.FromArgb(165, 165, 165);
    public static readonly Color Accent = Color.FromArgb(76, 194, 255);
    public static readonly Color Error = Color.FromArgb(255, 120, 110);

    public static Button Button(string text, bool accent = false)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent ? Accent : Surface,
            ForeColor = accent ? Color.Black : Text,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = accent ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(110, 207, 255) : Hover;
        b.FlatAppearance.MouseDownBackColor = accent ? Color.FromArgb(60, 170, 230) : Selected;
        return b;
    }

    public static Label Label(string text, bool dim = false, bool header = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = dim ? Dim : Text,
        BackColor = Color.Transparent,
        Font = header ? new Font("Segoe UI Semibold", 10.5f) : null,
    };

    public static TextBox TextBox() => new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Field,
        ForeColor = Text,
    };

    public static CheckBox CheckBox(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        BackColor = Color.Transparent,
    };

    public static void Prepare(Form f, int width, int height)
    {
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.Font = new Font("Segoe UI", 9.75f);
        f.BackColor = Bg;
        f.ForeColor = Text;
        f.ClientSize = new Size(width, height);
        try { f.Icon = Icon.ExtractAssociatedIcon(Paths.ExePath); } catch { }
    }

    public static void DarkTitle(Form f) => Shell.Native.SetDarkTitleBar(f.Handle);
}

/// <summary>Owner-drawn list without flicker.</summary>
class DarkList : ListBox
{
    public DarkList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Text shown when the list is empty.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get; set; } = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (Items.Count == 0 && Placeholder.Length > 0)
        {
            TextRenderer.DrawText(e.Graphics, Placeholder, Font, ClientRectangle, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        for (int i = 0; i < Items.Count; i++)
        {
            var r = GetItemRectangle(i);
            if (!r.IntersectsWith(e.ClipRectangle)) continue;
            var state = SelectedIndex == i ? DrawItemState.Selected : DrawItemState.None;
            OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, r, i, state));
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Let OnPaint do all drawing (UserPaint) and skip background erase.
        if (m.Msg == 0x0014 /* WM_ERASEBKGND */) { m.Result = 1; return; }
        base.WndProc(ref m);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }
}
