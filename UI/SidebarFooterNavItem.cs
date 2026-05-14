using System.Drawing.Drawing2D;

namespace SistecHub.UI;

/// <summary>
/// Item do rodapé da barra lateral: ícone MDL2 + texto, com realce arredondado (hover / selecionado).
/// </summary>
internal sealed class SidebarFooterNavItem : Panel
{
    const int CornerRadius = 10;

    readonly string _glyph;
    readonly string _caption;
    readonly Font _iconFont;
    readonly Font _captionFont;

    bool _hover;
    bool _selected;

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

    public SidebarFooterNavItem(string mdl2Glyph, string caption)
    {
        _glyph = mdl2Glyph;
        _caption = caption;

        Height = 44;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 0, 4);
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
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var card = ClientRectangle;
        card.Inflate(-1, -1);
        if (card.Width > 0 && card.Height > 0)
        {
            g.TranslateTransform(card.X, card.Y);
            using var path = ShellTheme.CreateRoundedRectanglePath(card.Width, card.Height, CornerRadius);

            if (_selected)
            {
                using var fill = new SolidBrush(Color.FromArgb(30, 41, 59));
                g.FillPath(fill, path);
                using var border = new Pen(Color.FromArgb(82, 98, 122));
                g.DrawPath(border, path);
            }
            else if (_hover)
            {
                using var fill = new SolidBrush(ShellTheme.MenuBtnHover);
                g.FillPath(fill, path);
            }

            g.ResetTransform();
        }

        using var fg = new SolidBrush(Color.FromArgb(226, 232, 240));
        g.DrawString(_glyph, _iconFont, fg, 12, 10);
        g.DrawString(_caption, _captionFont, fg, 40, 12);
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
