namespace SistecHub.UI;

/// <summary>Painel com double buffering para animações sem flicker.</summary>
internal sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
    }
}
