using System.Drawing;
using System.Text.RegularExpressions;

namespace TaskbarGroups.Editor;

/// <summary>Small dark question window: a heading, a few short lines, and buttons (last one is the default).</summary>
sealed partial class DarkPrompt : Form
{
    public int Choice { get; private set; } = -1;

    internal DarkPrompt(string heading, string body, string[] buttons)
    {
        SuspendLayout();
        Theme.Prepare(this, 420, 10);
        Text = Paths.AppName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var h = Theme.Label(heading);
        h.Font = new Font("Segoe UI Semibold", 12f);
        h.Location = new Point(22, 18);
        Controls.Add(h);
        int y = 54;

        if (body.Length > 0)
        {
            var b = Theme.Label(body, dim: true);
            b.MaximumSize = new Size(376, 0);
            b.Location = new Point(23, y);
            Controls.Add(b);
            y += b.PreferredHeight + 18;
        }
        else y += 6;

        int x = 398;
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            bool primary = i == buttons.Length - 1;
            var btn = Theme.Button(buttons[i], accent: primary);
            int w = Math.Max(92, TextRenderer.MeasureText(buttons[i], Font).Width + 28);
            x -= w;
            btn.SetBounds(x, y, w, 32);
            int idx = i;
            btn.Click += (_, _) => { Choice = idx; Close(); };
            Controls.Add(btn);
            if (primary) AcceptButton = btn;
            x -= 8;
        }
        ClientSize = new Size(420, y + 32 + 18);
        ResumeLayout(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitle(this);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Choice = -1; Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    /// <summary>Returns the index of the clicked button, or -1 if closed.</summary>
    public static int Show(IWin32Window owner, string heading, string body, params string[] buttons)
    {
        using var f = new DarkPrompt(heading, body, buttons);
        f.ShowDialog(owner);
        return f.Choice;
    }

    /// <summary>Up to <paramref name="max"/> bullet points from release notes, as plain text.</summary>
    public static string Highlights(string markdown, int max = 3)
    {
        var lines = markdown.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => "•  " + Plain(l[2..]))
            .Take(max);
        return string.Join("\n", lines);
    }

    static string Plain(string s)
    {
        s = LinkRx().Replace(s, "$1");          // [text](url) → text
        return s.Replace("**", "").Replace("__", "").Replace("`", "").Trim();
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex LinkRx();
}
