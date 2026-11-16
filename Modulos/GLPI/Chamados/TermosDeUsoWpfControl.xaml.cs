using System;
using System.Windows;

namespace SistecHub.Modulos.GLPI.Chamados;

public sealed partial class TermosDeUsoWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? CloseRequested;

    public TermosDeUsoWpfControl(string bodyText)
    {
        InitializeComponent();
        TxtTermsBody.Text = bodyText ?? "";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);
}

