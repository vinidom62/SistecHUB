using SistecHub.Core;
using SistecHub.Modulos.GLPI;

namespace SistecHub.UI;

internal sealed class SettingsView : UserControl
{
    readonly ComboBox _entityComboBox;
    readonly TextBox _glpiUserTokenTextBox;
    readonly Label _updateStatusLabel;
    readonly Button _installUpdateButton;
    readonly Button _saveButton;
    readonly Label _feedbackLabel;

    int _persistedEntityId;

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
            Margin = new Padding(0, 0, 0, 10),
        };
        stack.Controls.Add(_updateStatusLabel);

        _installUpdateButton = new Button
        {
            Text = "Verificar actualização",
            AutoSize = true,
            Height = 36,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 0, 24),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = ShellTheme.Accent,
            Cursor = Cursors.Hand,
            Enabled = AppUpdateService.IsUpdateSupported,
        };
        _installUpdateButton.FlatAppearance.BorderSize = 0;
        _installUpdateButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);
        _installUpdateButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 56, 202);
        _installUpdateButton.Click += async (_, _) => await OnInstallUpdateClickedAsync();
        stack.Controls.Add(_installUpdateButton);

        if (!AppUpdateService.IsUpdateSupported)
        {
            stack.Controls.Add(new Label
            {
                Text = "Actualizações indisponíveis — use a instalação MSI.",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = ShellTheme.TextMuted,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 16),
            });
        }

        var debugModeButton = new Button
        {
            Text = "Modo Debug",
            AutoSize = true,
            Height = 32,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 0, 24),
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

        var viewUpdateLogButton = new Button
        {
            Text = "Ver log de actualização",
            AutoSize = true,
            Height = 32,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 0, 24),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
        };
        viewUpdateLogButton.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
        viewUpdateLogButton.FlatAppearance.BorderSize = 1;
        viewUpdateLogButton.Click += (_, _) => ShowUpdateLog();
        stack.Controls.Add(viewUpdateLogButton);

        stack.Controls.Add(MakeFieldLabel("User token"));
        _glpiUserTokenTextBox = MakeWideTextBox();
        _glpiUserTokenTextBox.UseSystemPasswordChar = true;
        stack.Controls.Add(_glpiUserTokenTextBox);

        stack.Controls.Add(MakeSectionGap());

        stack.Controls.Add(MakeFieldLabel("Entidade"));
        _entityComboBox = MakeEntityComboBox();
        stack.Controls.Add(_entityComboBox);

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
    }

    async Task OnInstallUpdateClickedAsync()
    {
        _installUpdateButton.Enabled = false;
        var host = FindForm();
        if (host is not null)
            host.UseWaitCursor = true;
        try
        {
            RefreshUpdateStatusLabel();
            await AppUpdateService.CheckForUpdatesManuallyAsync(host).ConfigureAwait(true);
        }
        finally
        {
            RefreshUpdateStatusLabel();
            _installUpdateButton.Enabled = AppUpdateService.IsUpdateSupported;
            if (host is not null)
                host.UseWaitCursor = false;
        }
    }

    void RefreshUpdateStatusLabel() =>
        _updateStatusLabel.Text = AppUpdateService.GetUpdateStatusText();

    static void ShowUpdateLog()
    {
        var tail = UpdateActivityLog.ReadTail(50);
        MessageBox.Show(
            tail,
            "Log de actualização (update.log)",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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

    static ComboBox MakeEntityComboBox() =>
        new()
        {
            Width = 400,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
        };

    async void OnViewLoad(object? sender, EventArgs e)
    {
        Load -= OnViewLoad;
        RefreshUpdateStatusLabel();
        var settings = AppSettingsStore.Load();
        _glpiUserTokenTextBox.Text = settings.GlpiUserToken ?? "";
        _persistedEntityId = ParseEntityId(settings.EntityId);
        ApplySavedEntityPlaceholder();
        await LoadEntityComboAsync(settings).ConfigureAwait(true);
    }

    static int ParseEntityId(string? entityId) =>
        int.TryParse(entityId?.Trim(), out var parsed) && parsed >= 1 ? parsed : 0;

    static GlpiEntityInfo MakeSavedEntityPlaceholder(int entityId) =>
        new()
        {
            Id = entityId,
            Name = $"Entidade #{entityId}",
            CompleteName = $"Entidade #{entityId} (salva)",
        };

    void ApplySavedEntityPlaceholder()
    {
        if (_persistedEntityId < 1)
        {
            _entityComboBox.DataSource = null;
            _entityComboBox.Items.Clear();
            _entityComboBox.Items.Add("Nenhuma entidade configurada");
            _entityComboBox.SelectedIndex = 0;
            return;
        }

        BindEntityCombo([MakeSavedEntityPlaceholder(_persistedEntityId)], _persistedEntityId);
    }

    void BindEntityCombo(IReadOnlyList<GlpiEntityInfo> entities, int selectedEntityId)
    {
        _entityComboBox.DataSource = entities.ToList();
        _entityComboBox.DisplayMember = nameof(GlpiEntityInfo.DisplayName);
        _entityComboBox.ValueMember = nameof(GlpiEntityInfo.Id);

        if (selectedEntityId >= 1)
            _entityComboBox.SelectedValue = selectedEntityId;

        if (_entityComboBox.SelectedIndex < 0 && _entityComboBox.Items.Count > 0)
            _entityComboBox.SelectedIndex = 0;
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

            var selectedId = ResolveSelectedEntityId(_entityComboBox);
            if (selectedId < 1)
                selectedId = _persistedEntityId;

            BindEntityCombo(list, selectedId);
        }
        catch
        {
            // Mantém a entidade salva visível quando o token ainda não permite listar entidades.
        }
        finally
        {
            _entityComboBox.Enabled = true;
        }
    }

    static int ResolveSelectedEntityId(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is GlpiEntityInfo entity)
            return entity.Id;

        if (comboBox.SelectedValue is int id)
            return id;

        if (comboBox.SelectedValue is string idText && int.TryParse(idText, out var parsed))
            return parsed;

        return 0;
    }

    int ResolveEntityIdForSave()
    {
        var entityId = ResolveSelectedEntityId(_entityComboBox);
        if (entityId >= 1)
            return entityId;

        if (_persistedEntityId >= 1)
            return _persistedEntityId;

        return ParseEntityId(AppSettingsStore.Load().EntityId);
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
