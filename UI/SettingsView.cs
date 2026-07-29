using System.IO;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.Inventario;

namespace SistecHub.UI;

internal sealed class SettingsView : UserControl
{
    const string LockGlyph = "\uE72E";
    const string UnlockGlyph = "\uE785";

    readonly ComboBox _entityComboBox;
    readonly TextBox _glpiUserTokenTextBox;
    readonly TextBox _glpiMachineIdTextBox;
    readonly Label _machineIdLockButton;
    readonly Label _updateStatusLabel;
    readonly Button _checkUpdateButton;
    readonly Button _checkBetaUpdateButton;
    readonly Button _enviarInventarioBtn;
    readonly Button _saveButton;
    readonly Label _feedbackLabel;

    int _persistedEntityId;
    bool _machineIdUnlocked;

    public SettingsView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(40, 36, 40, 36);

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
        };

        var title = new Label
        {
            Text = "Configurações",
            AutoSize = true,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 24),
        };

        stack.Controls.Add(title);

        var versionLabel = new Label
        {
            Text = $"Versão: {AppUpdateService.DisplayVersion}",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };
        stack.Controls.Add(versionLabel);

        _updateStatusLabel = new Label
        {
            Text = AppUpdateService.GetUpdateStatusText(),
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };
        stack.Controls.Add(_updateStatusLabel);

        if (!AppUpdateService.IsUpdateSupported)
        {
            stack.Controls.Add(new Label
            {
                Text = "Atualizações indisponíveis — use a instalação MSI.",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = ShellTheme.TextMuted,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 16),
            });
        }

        var updateBtnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };

        _checkUpdateButton = new Button
        {
            Text = "Verificar atualizações",
            AutoSize = true,
            Height = 36,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 8, 8),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = ShellTheme.Accent,
            Cursor = Cursors.Hand,
            Enabled = AppUpdateService.IsUpdateSupported,
        };
        _checkUpdateButton.FlatAppearance.BorderSize = 0;
        _checkUpdateButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);
        _checkUpdateButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 56, 202);
        _checkUpdateButton.Click += async (_, _) => await OnCheckUpdateClickedAsync(includePrerelease: false);

        _checkBetaUpdateButton = new Button
        {
            Text = "Verificar atualização Beta",
            AutoSize = true,
            Height = 36,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 0, 8),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.Accent,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            Enabled = AppUpdateService.IsUpdateSupported,
        };
        _checkBetaUpdateButton.FlatAppearance.BorderSize = 1;
        _checkBetaUpdateButton.FlatAppearance.BorderColor = ShellTheme.Accent;
        _checkBetaUpdateButton.Click += async (_, _) => await OnCheckUpdateClickedAsync(includePrerelease: true);

        updateBtnRow.Controls.Add(_checkUpdateButton);
        updateBtnRow.Controls.Add(_checkBetaUpdateButton);
        stack.Controls.Add(updateBtnRow);

        var debugModeButton = new Button
        {
            Text = "Modo Debug",
            AutoSize = true,
            Height = 32,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 0, 16),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
        };
        debugModeButton.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
        debugModeButton.FlatAppearance.BorderSize = 1;
        debugModeButton.Click += (_, _) => DebugConsoleWindow.ShowOrActivate(FindForm());
        stack.Controls.Add(debugModeButton);

        var inventoryBtnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 24),
        };

        var exportBtn = new Button
        {
            Text = "Exportar relatório JSON",
            AutoSize = true,
            Height = 36,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = ShellTheme.Accent,
            Cursor = Cursors.Hand,
        };
        exportBtn.FlatAppearance.BorderSize = 0;
        exportBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);
        exportBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 56, 202);
        exportBtn.Click += (_, _) => _ = ExportRelatorioJsonAsync();

        _enviarInventarioBtn = new Button
        {
            Text = "Enviar inventário ao servidor",
            AutoSize = true,
            Height = 36,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.Accent,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
        };
        _enviarInventarioBtn.FlatAppearance.BorderSize = 1;
        _enviarInventarioBtn.FlatAppearance.BorderColor = ShellTheme.Accent;
        _enviarInventarioBtn.Click += (_, _) => _ = EnviarInventarioServidorAsync();

        inventoryBtnRow.Controls.Add(exportBtn);
        inventoryBtnRow.Controls.Add(_enviarInventarioBtn);
        stack.Controls.Add(inventoryBtnRow);

        stack.Controls.Add(MakeFieldLabel("User token"));
        _glpiUserTokenTextBox = MakeWideTextBox();
        _glpiUserTokenTextBox.UseSystemPasswordChar = true;
        stack.Controls.Add(_glpiUserTokenTextBox);

        stack.Controls.Add(MakeSectionGap());

        stack.Controls.Add(MakeFieldLabel("Entidade"));
        var entityHost = new Panel
        {
            Width = 400,
            Height = 28,
            Margin = new Padding(0),
            AutoScroll = false,
        };
        _entityComboBox = EntityComboBoxHelper.Create(400);
        _entityComboBox.Dock = DockStyle.Fill;
        entityHost.Controls.Add(_entityComboBox);
        stack.Controls.Add(entityHost);

        stack.Controls.Add(MakeSectionGap());

        var machineIdLabelRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 6),
        };
        machineIdLabelRow.Controls.Add(new Label
        {
            Text = "ID da máquina (plugin Inventário)",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 8, 0),
        });

        _machineIdLockButton = new Label
        {
            Text = LockGlyph,
            AutoSize = true,
            Font = new Font("Segoe MDL2 Assets", 14F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.Accent,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 0),
        };
        _machineIdLockButton.Click += (_, _) => OnMachineIdLockClicked();
        machineIdLabelRow.Controls.Add(_machineIdLockButton);
        stack.Controls.Add(machineIdLabelRow);

        _glpiMachineIdTextBox = MakeWideTextBox();
        _glpiMachineIdTextBox.ReadOnly = true;
        _glpiMachineIdTextBox.BackColor = Color.FromArgb(241, 245, 249);
        stack.Controls.Add(_glpiMachineIdTextBox);

        _saveButton = new Button
        {
            Text = "Salvar configurações",
            AutoSize = true,
            Height = 36,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 20, 0, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = ShellTheme.Accent,
            Cursor = Cursors.Hand,
        };
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);
        _saveButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 56, 202);
        _saveButton.Click += async (_, _) => await OnSaveClickedAsync();

        _feedbackLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(22, 163, 74),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0),
            Visible = false,
        };

        stack.Controls.Add(_saveButton);
        stack.Controls.Add(_feedbackLabel);

        Controls.Add(stack);

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
        _glpiMachineIdTextBox.ReadOnly = false;
        _glpiMachineIdTextBox.BackColor = SystemColors.Window;
        _machineIdLockButton.Text = UnlockGlyph;
        _machineIdLockButton.ForeColor = Color.FromArgb(185, 28, 28);
    }

    void LockMachineIdField()
    {
        _machineIdUnlocked = false;
        _glpiMachineIdTextBox.ReadOnly = true;
        _glpiMachineIdTextBox.BackColor = Color.FromArgb(241, 245, 249);
        _machineIdLockButton.Text = LockGlyph;
        _machineIdLockButton.ForeColor = ShellTheme.Accent;
    }

    void RefreshUpdateStatusLabel() =>
        _updateStatusLabel.Text = AppUpdateService.GetUpdateStatusText();

    async Task OnCheckUpdateClickedAsync(bool includePrerelease)
    {
        _checkUpdateButton.Enabled = false;
        _checkBetaUpdateButton.Enabled = false;
        var host = FindForm();
        if (host is not null)
            host.UseWaitCursor = true;
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
            RefreshUpdateStatusLabel();
            var supported = AppUpdateService.IsUpdateSupported;
            _checkUpdateButton.Enabled = supported;
            _checkBetaUpdateButton.Enabled = supported;
            if (host is not null)
                host.UseWaitCursor = false;
        }
    }

    static Label MakeFieldLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 6),
        };

    static Panel MakeSectionGap() =>
        new()
        {
            Height = 8,
            Width = 1,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent,
        };

    static TextBox MakeWideTextBox() =>
        new()
        {
            Width = 400,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.FixedSingle,
        };

    async void OnViewLoad(object? sender, EventArgs e)
    {
        Load -= OnViewLoad;
        RefreshUpdateStatusLabel();
        LockMachineIdField();
        var settings = AppSettingsStore.Load();
        _glpiUserTokenTextBox.Text = settings.GlpiUserToken ?? "";
        _glpiMachineIdTextBox.Text = settings.GlpiMachineId ?? "";
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
            _entityComboBox.Enabled = false;
            _entityComboBox.DataSource = null;
            _entityComboBox.Items.Clear();
            _entityComboBox.Items.Add("Nenhuma entidade configurada");
            _entityComboBox.SelectedIndex = 0;
            return;
        }

        _entityComboBox.Enabled = true;
        EntityComboBoxHelper.Bind(
            _entityComboBox,
            [MakeSavedEntityPlaceholder(_persistedEntityId)],
            _persistedEntityId);
    }

    async Task LoadEntityComboAsync(AppUserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            return;

        _entityComboBox.Enabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var entities = await GlpiApiClient.GetEntitiesAsync(settings, cts.Token).ConfigureAwait(true);

            if (entities.Count == 0)
                return;

            var list = entities.ToList();
            if (_persistedEntityId >= 1 && list.All(e => e.Id != _persistedEntityId))
                list.Insert(0, MakeSavedEntityPlaceholder(_persistedEntityId));

            var selectedId = EntityComboBoxHelper.GetSelectedEntityId(_entityComboBox);
            if (selectedId < 1)
                selectedId = _persistedEntityId;

            EntityComboBoxHelper.Bind(_entityComboBox, list, selectedId);

            AppDebugLog.Info(
                "Entidades",
                $"API={list.Count}; ComboBox={_entityComboBox.Items.Count}; " +
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
            _entityComboBox.Enabled = true;
        }
    }

    int ResolveEntityIdForSave()
    {
        var entityId = EntityComboBoxHelper.GetSelectedEntityId(_entityComboBox);
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

        _enviarInventarioBtn.Enabled = false;
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
            if (IsHandleCreated && !_enviarInventarioBtn.IsDisposed)
                _enviarInventarioBtn.Enabled = true;
        }
    }

    async Task OnSaveClickedAsync()
    {
        _saveButton.Enabled = false;
        _feedbackLabel.Visible = false;
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
            merged.GlpiUserToken = _glpiUserTokenTextBox.Text.Trim();
            merged.GlpiMachineId = _glpiMachineIdTextBox.Text.Trim();
            _persistedEntityId = entityId;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await global::SistecHub.AppConfigurationValidation.ValidateAllAsync(merged, cts.Token)
                .ConfigureAwait(true);

            AppSettingsStore.Save(merged);
            _feedbackLabel.Text = "Configurações salvas. A reiniciar a aplicação...";
            _feedbackLabel.ForeColor = Color.FromArgb(22, 163, 74);
            _feedbackLabel.Visible = true;
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
            _saveButton.Enabled = true;
            if (host is not null)
                host.UseWaitCursor = false;
        }
    }
}
