namespace SistecHub.UI;

/// <summary>Painel de cartão com fundo claro e borda suave para métricas da UI.</summary>
internal sealed class ElevatedCardPanel : Panel
{
    public ElevatedCardPanel()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
            true);
        DoubleBuffered = true;
        BackColor = Color.White;
        Padding = new Padding(12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;

        bounds.Width -= 1;
        bounds.Height -= 1;
        using var border = new Pen(Color.FromArgb(226, 232, 240));
        e.Graphics.DrawRectangle(border, bounds);
    }
}
