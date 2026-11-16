using System.IO;
using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.Inventario;

namespace SistecHub.UI;

internal sealed class SettingsView : UserControl
{
    readonly SettingsWpfControl _wpfView;
    int _persistedEntityId;
    bool _machineIdUnlocked;

    public SettingsView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        _wpfView = new SettingsWpfControl();
        _wpfView.InitializeHeaderInfo(
            AppUpdateService.DisplayVersion,
            AppUpdateService.GetUpdateStatusText(),
            AppUpdateService.IsUpdateSupported);

        _wpfView.CheckUpdateClicked += async (_, _) => await OnCheckUpdateClickedAsync(includePrerelease: false);
        _wpfView.CheckBetaUpdateClicked += async (_, _) => await OnCheckUpdateClickedAsync(includePrerelease: true);
        _wpfView.ExportJsonClicked += (_, _) => _ = ExportRelatorioJsonAsync();
        _wpfView.EnviarInventarioClicked += (_, _) => _ = EnviarInventarioServidorAsync();
        _wpfView.MachineIdLockClicked += (_, _) => OnMachineIdLockClicked();
        _wpfView.BloqueioRemotoClicked += (_, _) => OnBloqueioRemotoClicked();
        _wpfView.SaveClicked += async (_, _) => await OnSaveClickedAsync();

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfView,
        };

        Controls.Add(host);

        Load += OnViewLoad;
        VisibleChanged += OnVisibleChanged;
        HandleDestroyed += OnHandleDestroyed;
    }

    void OnHandleDestroyed(object? sender, EventArgs e)
    {
        HandleDestroyed -= OnHandleDestroyed;
        VisibleChanged -= OnVisibleChanged;
    }

    void OnVisibleChanged(object? sender, EventArgs e)
    {
        if (!Visible)
            LockMachineIdField();
        else
            RefreshUpdateStatusLabel();
    }

    void OnMachineIdLockClicked()
    {
        if (_machineIdUnlocked)
        {
            LockMachineIdField();
            return;
        }

        using var warning = new MachineIdEditWarningForm();
        if (warning.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        UnlockMachineIdField();
    }

    void UnlockMachineIdField()
    {
        _machineIdUnlocked = true;
        _wpfView.SetMachineIdUnlockedState(unlocked: true);
    }

    void LockMachineIdField()
    {
        _machineIdUnlocked = false;
        _wpfView.SetMachineIdUnlockedState(unlocked: false);
    }

    void OnBloqueioRemotoClicked()
    {
        var currentlyBlocked = _wpfView.RemotoBloqueado;
        var host = FindForm();

        if (!currentlyBlocked)
        {
            var res = MessageBox.Show(
                (IWin32Window?)host ?? this,
                "Deseja realmente bloquear o acesso remoto nesta máquina?\n\nO sistema de acesso remoto do SistecHUB será totalmente desativado e nenhuma conexão de suporte poderá ser realizada até o desbloqueio.",
                "Bloquear acesso remoto",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (res != DialogResult.Yes)
                return;

            var settings = AppSettingsStore.Load();
            settings.RemotoBloqueado = true;
            AppSettingsStore.Save(settings);

            _wpfView.SetRemotoBloqueado(true);
            Modulos.AcessoRemoto.AcessoRemotoAgentService.Stop();
            _wpfView.ShowFeedback("Acesso remoto bloqueado com sucesso nesta máquina.");
        }
        else
        {
            var res = MessageBox.Show(
                (IWin32Window?)host ?? this,
                "Deseja desbloquear e reativar o sistema de acesso remoto nesta máquina?",
                "Desbloquear acesso remoto",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res != DialogResult.Yes)
                return;

            var settings = AppSettingsStore.Load();
            settings.RemotoBloqueado = false;
            AppSettingsStore.Save(settings);

            _wpfView.SetRemotoBloqueado(false);
            Modulos.AcessoRemoto.AcessoRemotoAgentService.Start();
            _wpfView.ShowFeedback("Acesso remoto desbloqueado e reativado com sucesso.");
        }
    }

    void RefreshUpdateStatusLabel() =>
        _wpfView.SetUpdateStatusText(AppUpdateService.GetUpdateStatusText());

    async Task OnCheckUpdateClickedAsync(bool includePrerelease)
    {
        _wpfView.SetUpdateButtonsEnabled(false);
        var host = FindForm();
        try
        {
            RefreshUpdateStatusLabel();
            if (includePrerelease)
                await AppUpdateService.CheckForBetaUpdatesManuallyAsync(host).ConfigureAwait(true);
            else
                await AppUpdateService.CheckForUpdatesManuallyAsync(host).ConfigureAwait(true);
        }
        finally
        {
            if (!IsDisposed)
            {
                RefreshUpdateStatusLabel();
                var supported = AppUpdateService.IsUpdateSupported;
                _wpfView.SetUpdateButtonsEnabled(supported);
            }
        }
    }

    async void OnViewLoad(object? sender, EventArgs e)
    {
        Load -= OnViewLoad;
        RefreshUpdateStatusLabel();
        LockMachineIdField();
        var settings = AppSettingsStore.Load();
        _wpfView.GlpiUserToken = settings.GlpiUserToken ?? "";
        _wpfView.GlpiMachineId = settings.GlpiMachineId ?? "";
        _wpfView.RemotoServerUrl = string.IsNullOrWhiteSpace(settings.RemotoServerUrl)
            ? AppSettingsStore.DefaultRemotoServerUrl
            : settings.RemotoServerUrl;
        _wpfView.IniciarMinimizado = settings.IniciarMinimizado;
        _wpfView.SetRemotoBloqueado(settings.RemotoBloqueado);
        _persistedEntityId = ParseEntityId(settings.EntityId);
        ApplySavedEntityPlaceholder();
        await LoadEntityComboAsync(settings).ConfigureAwait(true);
    }

    static int ParseEntityId(string? entityId) =>
        int.TryParse(entityId?.Trim(), out var parsed) && parsed >= 1 ? parsed : 0;

    static GlpiEntityInfo MakeSavedEntityPlaceholder(int entityId)
    {
        var label = $"Entidade #{entityId} (salva)";
        return new()
        {
            Id = entityId,
            Name = $"Entidade #{entityId}",
            CompleteName = label,
            PickerLabel = label,
        };
    }

    void ApplySavedEntityPlaceholder()
    {
        if (_persistedEntityId < 1)
        {
            _wpfView.ShowNoEntityConfigured();
            return;
        }

        _wpfView.SetEntityComboEnabled(true);
        _wpfView.BindEntities([MakeSavedEntityPlaceholder(_persistedEntityId)], _persistedEntityId);
    }

    async Task LoadEntityComboAsync(AppUserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            return;

        _wpfView.SetEntityComboEnabled(false);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var entities = await GlpiApiClient.GetEntitiesAsync(settings, cts.Token).ConfigureAwait(true);

            if (entities.Count == 0)
                return;

            var list = entities.ToList();
            if (_persistedEntityId >= 1 && list.All(e => e.Id != _persistedEntityId))
                list.Insert(0, MakeSavedEntityPlaceholder(_persistedEntityId));

            var selectedId = _wpfView.GetSelectedEntityId();
            if (selectedId < 1)
                selectedId = _persistedEntityId;

            _wpfView.BindEntities(list, selectedId);

            AppDebugLog.Info(
                "Entidades",
                $"API={list.Count}; ComboBox={_wpfView.EntityComboItemCount}; " +
                $"opus={list.Count(e => e.PickerLabel.Contains("opus", StringComparison.OrdinalIgnoreCase))}; " +
                $"passarinho={list.Count(e => e.PickerLabel.Contains("passarinho", StringComparison.OrdinalIgnoreCase))}; " +
                $"donna={list.Count(e => e.PickerLabel.Contains("donna", StringComparison.OrdinalIgnoreCase))}");
        }
        catch (Exception ex)
        {
            AppDebugLog.LogException("Entidades", ex, "Falha ao carregar lista de entidades");
        }
        finally
        {
            _wpfView.SetEntityComboEnabled(true);
        }
    }

    int ResolveEntityIdForSave()
    {
        var entityId = _wpfView.GetSelectedEntityId();
        if (entityId >= 1)
            return entityId;

        if (_persistedEntityId >= 1)
            return _persistedEntityId;

        return ParseEntityId(AppSettingsStore.Load().EntityId);
    }

    async Task ExportRelatorioJsonAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Guardar relatório de inventário",
            Filter = "JSON (*.json)|*.json|Todos os ficheiros (*.*)|*.*",
            DefaultExt = "json",
            FileName = $"inventario-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            InventarioServiceCoordinator.RequestRefresh();
            string? json = null;
            for (var i = 0; i < 30; i++)
            {
                json = InventarioServiceCoordinator.TryReadReportJson();
                if (!string.IsNullOrWhiteSpace(json))
                    break;
                await Task.Delay(1000).ConfigureAwait(true);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    "O serviço ainda não disponibilizou o inventário. Verifique se o SistecHub Service está em execução.");
            }

            await File.WriteAllTextAsync(dlg.FileName, json).ConfigureAwait(true);

            MessageBox.Show(
                this,
                $"Relatório guardado em:\n{dlg.FileName}",
                "Inventário",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UserFacingErrorHelper.ShowErrorFromException(this, ex);
        }
    }

    async Task EnviarInventarioServidorAsync()
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        _wpfView.SetEnviarInventarioEnabled(false);
        try
        {
            var before = InventarioServiceCoordinator.TryReadStatus()?.LastUploadUtc;
            InventarioServiceCoordinator.RequestUpload();

            InventarioServiceStatus? status = null;
            for (var i = 0; i < 90; i++)
            {
                await Task.Delay(1000).ConfigureAwait(true);
                if (IsDisposed)
                    return;

                status = InventarioServiceCoordinator.TryReadStatus();
                if (status is null)
                    continue;

                if (status.Phase == InventarioServicePhase.Error)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(status.Message)
                            ? "Falha ao enviar inventário pelo serviço."
                            : status.Message);
                }

                if (status.LastUploadUtc is { } uploaded
                    && (before is null || uploaded > before))
                {
                    MessageBox.Show(
                        this,
                        "Inventário enviado com sucesso pelo serviço.",
                        "Inventário",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
            }

            throw new InvalidOperationException(
                "Tempo esgotado à espera do serviço. Verifique se o SistecHub Service está em execução.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                UserFacingErrorHelper.ShowErrorFromException(this, ex);
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
                _wpfView.SetEnviarInventarioEnabled(true);
        }
    }

    async Task OnSaveClickedAsync()
    {
        _wpfView.SetSaveEnabled(false);
        _wpfView.HideFeedback();
        var host = FindForm();
        if (host is not null)
            host.UseWaitCursor = true;
        try
        {
            var entityId = ResolveEntityIdForSave();
            if (entityId < 1)
            {
                throw new InvalidOperationException("Selecione uma entidade.");
            }

            var merged = AppSettingsStore.Load();
            merged.EntityId = entityId.ToString();
            merged.GlpiUserToken = _wpfView.GlpiUserToken;
            merged.GlpiMachineId = _wpfView.GlpiMachineId;
            merged.RemotoServerUrl = string.IsNullOrWhiteSpace(_wpfView.RemotoServerUrl)
                ? AppSettingsStore.DefaultRemotoServerUrl
                : _wpfView.RemotoServerUrl;
            merged.IniciarMinimizado = _wpfView.IniciarMinimizado;
            merged.RemotoBloqueado = _wpfView.RemotoBloqueado;
            _persistedEntityId = entityId;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await global::SistecHub.AppConfigurationValidation.ValidateAllAsync(merged, cts.Token)
                .ConfigureAwait(true);

            AppSettingsStore.Save(merged);
            _wpfView.ShowFeedback("Configurações salvas. A reiniciar a aplicação...");
            host?.Refresh();
            Application.Restart();
        }
        catch (Exception ex)
        {
            global::SistecHub.UserFacingErrorHelper.ShowErrorFromException(
                host, ex, global::SistecHub.UserFacingErrorHelper.ValidationErrorTitle);
        }
        finally
        {
            _wpfView.SetSaveEnabled(true);
            if (host is not null)
                host.UseWaitCursor = false;
        }
    }
}
