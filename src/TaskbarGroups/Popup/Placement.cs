using System.Drawing;
using TaskbarGroups.Shell;

namespace TaskbarGroups.Popup;

/// <summary>Works out where the popup goes: next to the taskbar button that was clicked, on that monitor.</summary>
static class Placement
{
    enum Edge { Bottom, Top, Left, Right }

    public static Rectangle Compute(Point cursor, Size size, int gap, bool centerOnCursor)
    {
        var screen = Screen.FromPoint(cursor);
        Rectangle b = screen.Bounds, wa = screen.WorkingArea;
        int w = size.Width, h = size.Height;

        if (centerOnCursor)
            return Clamp(new Rectangle(cursor.X - w / 2, cursor.Y - h / 2, w, h), wa, gap);

        // Find this monitor's taskbar from the gap between its bounds and working area.
        Edge edge;
        Rectangle bar;
        if (wa.Bottom < b.Bottom) { edge = Edge.Bottom; bar = Rectangle.FromLTRB(b.Left, wa.Bottom, b.Right, b.Bottom); }
        else if (wa.Top > b.Top) { edge = Edge.Top; bar = Rectangle.FromLTRB(b.Left, b.Top, b.Right, wa.Top); }
        else if (wa.Left > b.Left) { edge = Edge.Left; bar = Rectangle.FromLTRB(b.Left, b.Top, wa.Left, b.Bottom); }
        else if (wa.Right < b.Right) { edge = Edge.Right; bar = Rectangle.FromLTRB(wa.Right, b.Top, b.Right, b.Bottom); }
        else
        {
            // Auto-hide taskbar (or none on this monitor): ask the shell where it is.
            (edge, bar) = AutoHideBar(b);
            // Keep the popup off the area the taskbar pops up over.
            wa = edge switch
            {
                Edge.Bottom => Rectangle.FromLTRB(b.Left, b.Top, b.Right, bar.Top),
                Edge.Top => Rectangle.FromLTRB(b.Left, bar.Bottom, b.Right, b.Bottom),
                Edge.Left => Rectangle.FromLTRB(bar.Right, b.Top, b.Right, b.Bottom),
                _ => Rectangle.FromLTRB(b.Left, b.Top, bar.Left, b.Bottom),
            };
        }

        Rectangle r;
        if (bar.Contains(cursor))
        {
            r = edge switch
            {
                Edge.Bottom => new Rectangle(cursor.X - w / 2, bar.Top - gap - h, w, h),
                Edge.Top => new Rectangle(cursor.X - w / 2, bar.Bottom + gap, w, h),
                Edge.Left => new Rectangle(bar.Right + gap, cursor.Y - h / 2, w, h),
                _ => new Rectangle(bar.Left - gap - w, cursor.Y - h / 2, w, h),
            };
        }
        else
        {
            // Launched some other way (desktop shortcut, keyboard): open just above the pointer.
            r = new Rectangle(cursor.X - w / 2, cursor.Y - gap - h, w, h);
            if (r.Top < wa.Top + gap) r.Y = cursor.Y + gap;
        }
        return Clamp(r, wa, gap);
    }

    static (Edge, Rectangle) AutoHideBar(Rectangle screenBounds)
    {
        var abd = new Native.APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>() };
        Native.SHAppBarMessage(Native.ABM_GETTASKBARPOS, ref abd);
        var rc = abd.rc.ToRectangle();
        var edge = abd.uEdge switch
        {
            Native.ABE_TOP => Edge.Top,
            Native.ABE_LEFT => Edge.Left,
            Native.ABE_RIGHT => Edge.Right,
            _ => Edge.Bottom,
        };
        if (rc.Width > 0 && rc.Height > 0 && screenBounds.IntersectsWith(rc))
        {
            // rc may be the hidden position (a sliver); rebuild it at full thickness on this screen.
            int thick = Math.Max(Math.Min(rc.Width, rc.Height), 48);
            return edge switch
            {
                Edge.Top => (edge, new Rectangle(screenBounds.Left, screenBounds.Top, screenBounds.Width, thick)),
                Edge.Left => (edge, new Rectangle(screenBounds.Left, screenBounds.Top, thick, screenBounds.Height)),
                Edge.Right => (edge, new Rectangle(screenBounds.Right - thick, screenBounds.Top, thick, screenBounds.Height)),
                _ => (edge, new Rectangle(screenBounds.Left, screenBounds.Bottom - thick, screenBounds.Width, thick)),
            };
        }
        // No taskbar on this monitor: treat the bottom 48px as the "bar" zone.
        return (Edge.Bottom, new Rectangle(screenBounds.Left, screenBounds.Bottom - 48, screenBounds.Width, 48));
    }

    static Rectangle Clamp(Rectangle r, Rectangle area, int gap)
    {
        int x = Math.Max(area.Left + gap, Math.Min(r.X, area.Right - gap - r.Width));
        int y = Math.Max(area.Top + gap, Math.Min(r.Y, area.Bottom - gap - r.Height));
        return new Rectangle(x, y, r.Width, r.Height);
    }
}
