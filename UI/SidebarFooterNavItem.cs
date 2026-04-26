using System.Drawing.Drawing2D;

namespace SistecHub.UI;

/// <summary>
/// Item do rodapé da barra lateral: ícone MDL2 + texto, com realce arredondado (hover / selecionado).
/// </summary>
internal sealed class SidebarFooterNavItem : Panel
{
    readonly string _glyph;
    readonly string _caption;
    readonly Font _iconFont;
    readonly Font _captionFont;

    bool _hover;
    bool _selected;

    public string PageId { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            Invalidate();
        }
    }

    public SidebarFooterNavItem(string pageId, string mdl2Glyph, string caption)
    {
        PageId = pageId;
        _glyph = mdl2Glyph;
        _caption = caption;

        Height = 42;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 0, 6);
        BackColor = Color.Transparent;

        _iconFont = new Font("Segoe MDL2 Assets", 14F, FontStyle.Regular, GraphicsUnit.Point);
        _captionFont = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        SetStyle(
            ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var inner = ClientRectangle;
        inner.Inflate(-2, -2);

        if (inner.Width > 0 && inner.Height > 0)
        {
            using var path = CreateRoundedRectPath(inner, 8);

            if (_selected)
            {
                using var fill = new SolidBrush(Color.FromArgb(30, 41, 59));
                g.FillPath(fill, path);
                using var border = new Pen(Color.FromArgb(82, 98, 122));
                g.DrawPath(border, path);
            }
            else if (_hover)
            {
                using var fill = new SolidBrush(Color.FromArgb(38, 52, 74));
                g.FillPath(fill, path);
            }
        }

        using var fg = new SolidBrush(Color.FromArgb(226, 232, 240));
        g.DrawString(_glyph, _iconFont, fg, 12, 9);
        g.DrawString(_caption, _captionFont, fg, 40, 11);
    }

    static GraphicsPath CreateRoundedRectPath(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
            _captionFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
