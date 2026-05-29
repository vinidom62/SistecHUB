using System.Drawing.Drawing2D;

namespace SistecHub.UI;

/// <summary>
/// Indicador estático no rodapé da barra lateral: ícone MDL2 + nome da entidade (com reticências se necessário).
/// </summary>
internal sealed class SidebarFooterEntityBadge : Panel
{
    const int CornerRadius = 10;

    readonly string _glyph;
    readonly Font _iconFont;
    readonly Font _captionFont;

    string _displayText = "";

    public string DisplayText
    {
        get => _displayText;
        set
        {
            var next = value ?? "";
            if (string.Equals(_displayText, next, StringComparison.Ordinal))
                return;

            _displayText = next;
            Invalidate();
        }
    }

    public SidebarFooterEntityBadge(string mdl2Glyph)
    {
        _glyph = mdl2Glyph;

        Height = 44;
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
            using var fill = new SolidBrush(Color.FromArgb(30, 41, 59));
            g.FillPath(fill, path);
            g.ResetTransform();
        }

        var fg = Color.FromArgb(226, 232, 240);
        using var fgBrush = new SolidBrush(fg);
        g.DrawString(_glyph, _iconFont, fgBrush, 12, 10);

        if (!string.IsNullOrEmpty(_displayText))
        {
            var textRect = new Rectangle(40, 0, Math.Max(0, Width - 52), Height);
            TextRenderer.DrawText(
                g,
                _displayText,
                _captionFont,
                textRect,
                fg,
                TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
        }
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
