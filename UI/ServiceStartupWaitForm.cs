using System.ServiceProcess;
using System.Windows.Forms.Integration;
using SistecHub.Core;

namespace SistecHub.UI;

/// <summary>Aguarda o serviço durante ou após uma actualização, com novas tentativas automáticas (WPF).</summary>
internal sealed class ServiceStartupWaitForm : Form
{
    const int WaitPhaseSeconds = 15;
    const int MaxCycles = 4;

    readonly ServiceStartupWaitWpfControl _wpfControl;
    readonly System.Windows.Forms.Timer _timer;

    int _secondsInPhase;
    int _cycle;
    bool _inCountdownPhase;

    public ServiceStartupWaitForm()
    {
        Text = "SistecHub — atualização";
        ClientSize = new Size(480, 200);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        TopMost = true;

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        _wpfControl = new ServiceStartupWaitWpfControl();

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfControl,
        };

        Controls.Add(host);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTimerTick;

        Shown += (_, _) =>
        {
            TryStartService();
            UpdateLabels();
            _timer.Start();
        };
    }

    void OnTimerTick(object? sender, EventArgs e)
    {
        if (WindowsServiceGuard.IsRunning())
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _secondsInPhase++;

        if (!_inCountdownPhase)
        {
            if (_secondsInPhase % 2 == 0)
                TryStartService();

            if (_secondsInPhase >= WaitPhaseSeconds)
            {
                _inCountdownPhase = true;
                _secondsInPhase = 0;
            }
        }
        else
        {
            if (_secondsInPhase >= 10)
            {
                _cycle++;
                if (_cycle >= MaxCycles)
                {
                    _timer.Stop();
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }

                _inCountdownPhase = false;
                _secondsInPhase = 0;
                TryStartService();
            }
        }

        UpdateLabels();
    }

    void UpdateLabels()
    {
        if (!_inCountdownPhase)
        {
            var remaining = WaitPhaseSeconds - _secondsInPhase;
            _wpfControl.SetMessage("A aplicar atualização, aguarde o serviço reiniciar...");
            _wpfControl.SetCountdown($"Tempo restante nesta tentativa: {remaining}s (tentativa {_cycle + 1}/{MaxCycles})");
        }
        else
        {
            var remaining = 10 - _secondsInPhase;
            _wpfControl.SetMessage("O serviço ainda não respondeu. Nova tentativa em breve...");
            _wpfControl.SetCountdown($"Tentando novamente em {remaining}s...");
        }
    }

    static void TryStartService()
    {
        try
        {
            using var sc = new ServiceController("SistecHubService");
            if (sc.Status == ServiceControllerStatus.Stopped)
                sc.Start();
        }
        catch
        {
            // O serviço pode estar no meio de um update ou a arrancar
        }
    }
}
