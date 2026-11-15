using System.Drawing;

namespace TaskbarGroups.Popup;

/// <summary>Dark Windows-11-ish look for right-click menus.</summary>
sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Color.FromArgb(235, 235, 235) : Color.FromArgb(120, 120, 120);
        base.OnRenderItemText(e);
    }

    sealed class Colors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(43, 43, 43);
        static readonly Color Hi = Color.FromArgb(61, 61, 61);
        static readonly Color Border = Color.FromArgb(70, 70, 70);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hi;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Bg;
    }
}
