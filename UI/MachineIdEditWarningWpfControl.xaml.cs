using System;
using System.Windows;

namespace SistecHub.UI;

public sealed partial class MachineIdEditWarningWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? ContinueRequested;

    public MachineIdEditWarningWpfControl()
    {
        InitializeComponent();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
        => ContinueRequested?.Invoke(this, EventArgs.Empty);
}

