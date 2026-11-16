using System.Diagnostics;
using System.Windows.Forms.Integration;
using SistecHub.Core;

namespace SistecHub.UI;

/// <summary>
/// Janela integrada de progresso de atualização (Verificação -> Download 0-100% -> Aviso de 10s -> Reinício).
/// Sem opção de adiar quando a atualização está pronta.
/// </summary>
internal sealed class UpdateProgressForm : Form
{
    enum DialogStage
    {
        Checking,
        Downloading,
        ReadyCountdown,
        UpToDate,
        Error,
    }

    static readonly TimeSpan InitialCheckTimeout = TimeSpan.FromMinutes(3);
    static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    const int DefaultCountdownSeconds = 10;

    readonly UpdateProgressWpfControl _wpfControl;
    readonly System.Windows.Forms.Timer _pollTimer;
    readonly System.Windows.Forms.Timer _countdownTimer;
    readonly bool _includePrerelease;
    readonly int _configuredCountdownSeconds;

    DialogStage _stage;
    DateTimeOffset _checkStartedUtc;
    DateTime _deadlineUtc;
    int _lastProgressPercent = -1;
    string _targetVersion = "";
    int _countdownRemaining = DefaultCountdownSeconds;
    bool _allowClose;

    public bool RestartConfirmed { get; private set; }

    public string TargetVersion => _targetVersion;

    /// <summary>Construtor para verificação manual (Etapa 1 -> Etapa 2 -> Etapa 3/5/6).</summary>
    public UpdateProgressForm(bool includePrerelease)
        : this(initialReadyVersion: null, includePrerelease: includePrerelease, countdownSeconds: DefaultCountdownSeconds)
    {
    }

    /// <summary>Construtor para aviso imediato de atualização já transferida (Etapa 3 — 10s de leitura).</summary>
    public UpdateProgressForm(string readyVersion, int countdownSeconds = DefaultCountdownSeconds)
        : this(initialReadyVersion: readyVersion, includePrerelease: false, countdownSeconds: countdownSeconds)
    {
    }

    UpdateProgressForm(string? initialReadyVersion, bool includePrerelease, int countdownSeconds)
    {
        _includePrerelease = includePrerelease;
        _configuredCountdownSeconds = Math.Max(1, countdownSeconds);
        _countdownRemaining = _configuredCountdownSeconds;

        Text = "SistecHub — Atualização do Sistema";
        ClientSize = new Size(520, 265);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = true;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        TopMost = true;

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        _wpfControl = new UpdateProgressWpfControl();
        _wpfControl.RestartNowClicked += (_, _) => CompleteWithRestart();
        _wpfControl.RetryClicked += (_, _) => BeginCheckCycle();
        _wpfControl.ViewLogsClicked += (_, _) => OpenLogsFile();
        _wpfControl.CloseClicked += (_, _) => CloseWithoutRestart();

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfControl,
        };

        Controls.Add(host);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _pollTimer.Tick += OnPollTimerTick;

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += OnCountdownTimerTick;

        FormClosing += OnFormClosing;

        Shown += (_, _) =>
        {
            Activate();
            BringToFront();

            if (!string.IsNullOrWhiteSpace(initialReadyVersion))
            {
                EnterReadyCountdownStage(initialReadyVersion);
            }
            else
            {
                BeginCheckCycle();
            }
        };
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            // Não permite fechar/adiar pelo teclado (Alt+F4) durante verificação, download ou contagem regressiva
            if (_stage is DialogStage.Checking or DialogStage.Downloading or DialogStage.ReadyCountdown)
            {
                e.Cancel = true;
                return;
            }
        }

        _pollTimer.Stop();
        _countdownTimer.Stop();
    }

    void BeginCheckCycle()
    {
        _countdownTimer.Stop();
        _stage = DialogStage.Checking;
        _lastProgressPercent = -1;
        _checkStartedUtc = DateTimeOffset.UtcNow;
        _deadlineUtc = DateTime.UtcNow + InitialCheckTimeout;

        _wpfControl.ShowCheckingStage(_includePrerelease);

        if (_includePrerelease)
            UpdateServiceCoordinator.RequestImmediateBetaCheck(resetStatus: true);
        else
            UpdateServiceCoordinator.RequestImmediateCheck(resetStatus: true);

        _pollTimer.Start();
    }

    void OnPollTimerTick(object? sender, EventArgs e)
    {
        var status = UpdateServiceCoordinator.TryReadStatus();

        // Verifica se uma nova versão já ficou pronta para aplicar
        if (AppUpdateService.IsUpdateReadyToApply())
        {
            var pendingVer = VelopackUpdateEngine.PendingRestart?.Version.ToString()
                ?? status?.AvailableVersion
                ?? "";

            // Se estávamos em verificação manual e não havia PendingRestart local, garante que o status PendingAppClose é recente
            var isFreshOrLocal = VelopackUpdateEngine.PendingRestart is not null
                || (status is not null && status.LastUpdateUtc >= _checkStartedUtc.AddSeconds(-2));

            if (isFreshOrLocal && !string.IsNullOrWhiteSpace(pendingVer))
            {
                _pollTimer.Stop();
                EnterReadyCountdownStage(pendingVer);
                return;
            }
        }

        if (status is not null)
        {
            var isStatusFromCurrentCheck = status.LastUpdateUtc >= _checkStartedUtc.AddSeconds(-2);
            var isActiveDownload = status.Phase == UpdateServicePhase.Downloading
                && (isStatusFromCurrentCheck || status.LastUpdateUtc >= _checkStartedUtc.AddSeconds(-30));

            if (isActiveDownload)
            {
                var dlVer = status.AvailableVersion ?? "nova versão";
                if (_stage != DialogStage.Downloading)
                {
                    _stage = DialogStage.Downloading;
                    _deadlineUtc = DateTime.UtcNow + DownloadTimeout;
                    UpdateActivityLog.Info("Update", $"UI entrou na fase de download da versão {dlVer}.");
                }
                else if (status.ProgressPercent != _lastProgressPercent)
                {
                    // Enquanto houver evolução do download, mantém o timeout dinâmico de até 15 minutos ativo
                    _deadlineUtc = DateTime.UtcNow + DownloadTimeout;
                }

                _lastProgressPercent = status.ProgressPercent;
                _wpfControl.ShowDownloadingStage(dlVer, status.ProgressPercent);
                return;
            }

            if (isStatusFromCurrentCheck && status.Phase == UpdateServicePhase.UpToDate)
            {
                _pollTimer.Stop();
                _stage = DialogStage.UpToDate;
                _wpfControl.ShowUpToDateStage(AppUpdateService.DisplayVersion, _includePrerelease);
                return;
            }

            if (isStatusFromCurrentCheck && status.Phase == UpdateServicePhase.Completed)
            {
                _pollTimer.Stop();
                _stage = DialogStage.UpToDate;
                _wpfControl.ShowUpToDateStage(AppUpdateService.DisplayVersion, _includePrerelease);
                return;
            }

            if (isStatusFromCurrentCheck && status.Phase == UpdateServicePhase.Error)
            {
                _pollTimer.Stop();
                _stage = DialogStage.Error;
                _wpfControl.ShowErrorStage(BuildFriendlyErrorMessage(status.Message));
                return;
            }
        }

        if (DateTime.UtcNow >= _deadlineUtc)
        {
            _pollTimer.Stop();
            var wasDownloading = _stage == DialogStage.Downloading;
            _stage = DialogStage.Error;
            var timeoutMsg = wasDownloading
                ? "O tempo limite de transferência da atualização esgotou. Verifique sua conexão com a internet e tente novamente."
                : "O serviço de atualização não respondeu a tempo. Verifique se o «SistecHub Service» está em execução e tente novamente.";
            _wpfControl.ShowErrorStage(timeoutMsg);
        }
    }

    void EnterReadyCountdownStage(string version)
    {
        _pollTimer.Stop();
        _stage = DialogStage.ReadyCountdown;
        _targetVersion = version;
        _countdownRemaining = _configuredCountdownSeconds;

        _wpfControl.ShowReadyCountdownStage(_targetVersion, _countdownRemaining, _configuredCountdownSeconds);
        _countdownTimer.Start();
    }

    void OnCountdownTimerTick(object? sender, EventArgs e)
    {
        _countdownRemaining--;
        if (_countdownRemaining <= 0)
        {
            CompleteWithRestart();
            return;
        }

        _wpfControl.ShowReadyCountdownStage(_targetVersion, _countdownRemaining, _configuredCountdownSeconds);
    }

    void CompleteWithRestart()
    {
        _pollTimer.Stop();
        _countdownTimer.Stop();
        RestartConfirmed = true;
        _allowClose = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    void CloseWithoutRestart()
    {
        _pollTimer.Stop();
        _countdownTimer.Stop();
        RestartConfirmed = false;
        _allowClose = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    static void OpenLogsFile()
    {
        try
        {
            var logPath = UpdateActivityLog.LogFilePath;
            if (File.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{logPath}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                MessageBox.Show(
                    "O arquivo de log ainda não possui registros.",
                    "Logs de Atualização",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não foi possível abrir o arquivo de log: " + ex.Message,
                "Logs de Atualização",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    static string BuildFriendlyErrorMessage(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return "Não foi possível comunicar com o servidor de atualizações. Verifique sua conexão com a internet e tente novamente.";

        if (rawMessage.Contains("404", StringComparison.OrdinalIgnoreCase))
            return "Nenhum pacote de lançamento foi encontrado no servidor no momento.";

        if (rawMessage.Contains("403", StringComparison.OrdinalIgnoreCase)
            || rawMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return "O servidor de atualizações (GitHub) atingiu o limite temporário de requisições. Aguarde alguns minutos e tente novamente.";

        if (rawMessage.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || rawMessage.Contains("NameResolution", StringComparison.OrdinalIgnoreCase)
            || rawMessage.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || rawMessage.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || rawMessage.Contains("host", StringComparison.OrdinalIgnoreCase))
        {
            return "Sem conexão com o servidor de atualizações. Verifique sua internet ou firewall e tente novamente.\n(" + rawMessage + ")";
        }

        return rawMessage;
    }
}
