using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.UI;

namespace SistecHub.Modulos.Inventario;

public sealed class InventarioView : UserControl
{
    readonly InventarioWpfControl _wpfView;
    string? _currentOsKey;
    bool _isKeyRevealed;

    public InventarioView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        _wpfView = new InventarioWpfControl();
        _wpfView.RefreshClicked += async (_, _) => await OnRefreshClickedAsync().ConfigureAwait(true);
        _wpfView.KeyRevealOrHideClicked += (_, _) => OnKeyRevealOrHideClicked();
        _wpfView.CopyKeyClicked += OnCopyKeyClicked;

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfView,
        };

        Controls.Add(host);

        InventarioSnapshotCoordinator.SnapshotUpdated += OnInventarioSnapshotUpdated;
        HandleDestroyed += OnInventarioHandleDestroyed;
        Load += OnInventarioLoad;
    }

    void OnInventarioHandleDestroyed(object? sender, EventArgs e)
    {
        HandleDestroyed -= OnInventarioHandleDestroyed;
        InventarioSnapshotCoordinator.SnapshotUpdated -= OnInventarioSnapshotUpdated;
    }

    void OnInventarioSnapshotUpdated(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke(() =>
        {
            if (IsDisposed)
                return;
            if (InventarioSnapshotCoordinator.TryGetLatest() is { } snap)
                ApplySnapshotToUi(snap);
            else
            {
                _wpfView.ApplyErrorState();
                UpdateLastCollectedLabel();
                _currentOsKey = null;
                _isKeyRevealed = false;
                UpdateKeyDisplay();
            }
        });
    }

    void OnInventarioLoad(object? sender, EventArgs e)
    {
        Load -= OnInventarioLoad;
        if (InventarioSnapshotCoordinator.TryGetLatest() is { } snap)
            ApplySnapshotToUi(snap);
        else
            UpdateLastCollectedLabel();
    }

    void OnKeyRevealOrHideClicked()
    {
        if (string.IsNullOrWhiteSpace(_currentOsKey))
            return;

        if (_isKeyRevealed)
        {
            _isKeyRevealed = false;
            UpdateKeyDisplay();
            return;
        }

        using var prompt = new SettingsPasswordForm(
            "Exibir Chave de Ativação",
            "Senha de administrador",
            "Digite a senha de administrador para visualizar a chave do Windows.");

        if (prompt.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _isKeyRevealed = true;
            UpdateKeyDisplay();
        }
    }

    void OnCopyKeyClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentOsKey))
            return;

        if (!_isKeyRevealed)
        {
            using var prompt = new SettingsPasswordForm(
                "Copiar Chave de Ativação",
                "Senha de administrador",
                "Digite a senha de administrador para copiar a chave do Windows.");

            if (prompt.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            _isKeyRevealed = true;
            UpdateKeyDisplay();
        }

        try
        {
            Clipboard.SetText(_currentOsKey);
            _wpfView.SetCopyKeyTooltip("Chave copiada!");
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(() => _wpfView.SetCopyKeyTooltip("Copiar chave de ativação"));
            });
        }
        catch
        {
            // falha de acesso à área de transferência
        }
    }

    void UpdateKeyDisplay()
    {
        _wpfView.UpdateKeyDisplay(_currentOsKey, _isKeyRevealed);
    }

    void ApplySnapshotToUi(in InventarioHardwareSnapshot snapshot)
    {
        _wpfView.ApplySnapshot(snapshot);
        UpdateLastCollectedLabel();
        _currentOsKey = snapshot.SistemaOperacional.ChaveAtivacao;
        UpdateKeyDisplay();
    }

    void UpdateLastCollectedLabel()
    {
        if (InventarioSnapshotCoordinator.LastCollectedAt is { } stamp)
        {
            var local = stamp.ToLocalTime();
            _wpfView.SetLastUpdateText($"Última atualização: {local:HH:mm:ss}");
        }
        else
        {
            _wpfView.SetLastUpdateText("");
        }
    }

    async Task OnRefreshClickedAsync()
    {
        if (!_wpfView.IsRefreshEnabled)
            return;

        _wpfView.SetRefreshState(enabled: false, tooltip: "A atualizar…");
        _wpfView.SetLastUpdateText("A atualizar inventário…");

        try
        {
            var previousStamp = InventarioSnapshotCoordinator.LastCollectedAt;
            InventarioSnapshotCoordinator.RequestRefreshNow();

            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(400).ConfigureAwait(true);
                if (IsDisposed)
                    return;

                if (InventarioSnapshotCoordinator.PollFromService()
                    || InventarioSnapshotCoordinator.LastCollectedAt != previousStamp)
                {
                    break;
                }
            }

            if (InventarioSnapshotCoordinator.TryGetLatest() is { } snap)
                ApplySnapshotToUi(snap);
        }
        catch (Exception ex)
        {
            AppDebugLog.LogException("Inventario", ex, "Falha ao atualizar inventário");
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
            {
                UpdateLastCollectedLabel();
                await Task.Delay(3000).ConfigureAwait(true);
                if (!IsDisposed)
                {
                    _wpfView.SetRefreshState(enabled: true, tooltip: "Atualizar inventário");
                }
            }
        }
    }
}
