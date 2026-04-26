using System.Drawing.Drawing2D;

namespace SistecHub.UI;

/// <summary>Painel com cartão branco, cantos arredondados, bordo suave e sombra ligeira (estilo dashboard).</summary>
internal sealed class ElevatedCardPanel : Panel
{
    public const int CornerRadius = 14;

    internal static readonly Color CardFill = Color.White;
    internal static readonly Color CardBorderColor = Color.FromArgb(226, 232, 240);
    internal static readonly Color ShadowColor = Color.FromArgb(18, 15, 23, 42);

    public ElevatedCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.Transparent;
        Padding = new Padding(10, 10, 14, 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var inner = DisplayRectangle;
        if (inner.Width <= 1 || inner.Height <= 1)
            return;

        using var path = ShellTheme.CreateRoundedRectanglePath(inner.Width, inner.Height, CornerRadius);

        g.TranslateTransform(inner.X + 2.5f, inner.Y + 3.5f);
        using (var shadowBrush = new SolidBrush(ShadowColor))
            g.FillPath(shadowBrush, path);
        g.ResetTransform();

        g.TranslateTransform(inner.X, inner.Y);
        using (var fill = new SolidBrush(CardFill))
            g.FillPath(fill, path);
        using (var border = new Pen(CardBorderColor, 1f))
            g.DrawPath(border, path);
        g.ResetTransform();
    }
}
