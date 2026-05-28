using System.Drawing.Drawing2D;

namespace SistecHub.UI;

internal static class ShellTheme
{
    internal static readonly Color SidebarBg = Color.FromArgb(15, 23, 42);
    internal static readonly Color SidebarHeaderBg = Color.FromArgb(12, 18, 32);
    internal static readonly Color SidebarDivider = Color.FromArgb(51, 65, 85);
    internal static readonly Color MenuBtnIdle = Color.FromArgb(30, 41, 59);
    internal static readonly Color MenuBtnHover = Color.FromArgb(51, 65, 85);
    internal static readonly Color MenuBtnPress = Color.FromArgb(79, 70, 229);
    internal static readonly Color MainBg = Color.FromArgb(248, 250, 252);
    internal static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
    internal static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
    internal static readonly Color Accent = Color.FromArgb(99, 102, 241);

    internal static Button CreateSidebarMenuButton(string text)
    {
        var btn = new Button
        {
            Text = "  " + text,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(226, 232, 240),
            BackColor = MenuBtnIdle,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 8),
            Cursor = Cursors.Hand,
            UseCompatibleTextRendering = false,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = MenuBtnHover;
        btn.FlatAppearance.MouseDownBackColor = MenuBtnPress;
        btn.SizeChanged += (_, _) => ApplyRoundedRegion(btn, 10);
        ApplyRoundedRegion(btn, 10);
        return btn;
    }

    internal static GraphicsPath CreateRoundedRectanglePath(int width, int height, int radius)
    {
        var path = new GraphicsPath();
        if (width <= 0 || height <= 0)
            return path;

        int d = Math.Min(radius * 2, Math.Min(width, height));
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(width - d, 0, d, d, 270, 90);
        path.AddArc(width - d, height - d, d, d, 0, 90);
        path.AddArc(0, height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0)
            return;

        using var path = CreateRoundedRectanglePath(control.Width, control.Height, radius);
        control.Region?.Dispose();
        control.Region = new Region(path);
    }
}
