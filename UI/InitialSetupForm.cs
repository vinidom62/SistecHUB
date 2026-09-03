using SistecHub.Core;
using SistecHub.Modulos.GLPI;

namespace SistecHub.UI;

internal sealed class InitialSetupForm : Form
{
    enum SetupStep
    {
        GlpiUserToken = 1,
        EntityId = 2,
    }

    readonly Label _subtitle;
    readonly Label _stepIndicator;
    readonly Label _fieldLabel;
    readonly TextBox _inputTextBox;
    readonly ComboBox _entityComboBox;
    readonly Panel _entityHost;
    readonly Button _continueButton;

    SetupStep _step;
    AppUserSettings _draftSettings = new();
    bool _setupCompleted;

    public InitialSetupForm()
    {
        Text = "SistecHub";
        ClientSize = new Size(560, 320);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon != null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(40, 36, 40, 36),
        };

        var title = new Label
        {
            Text = "Configuração inicial",
            AutoSize = true,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
        };

        _stepIndicator = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 4),
        };

        _subtitle = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 24),
        };

        _fieldLabel = MakeFieldLabel("");
        _inputTextBox = MakeWideTextBox();
        _entityComboBox = EntityComboBoxHelper.Create(460);
        _entityComboBox.Visible = false;

        var entityHost = new Panel
        {
            Width = 460,
            Height = 28,
            Margin = new Padding(0),
            AutoScroll = false,
            Visible = false,
        };
        _entityHost = entityHost;
        _entityComboBox.Dock = DockStyle.Fill;
        entityHost.Controls.Add(_entityComboBox);

        _continueButton = new Button
        {
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
        _continueButton.FlatAppearance.BorderSize = 0;
        _continueButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);
        _continueButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 56, 202);
        _continueButton.Click += async (_, _) => await OnContinueClickedAsync();

        stack.Controls.Add(title);
        stack.Controls.Add(_stepIndicator);
        stack.Controls.Add(_subtitle);
        stack.Controls.Add(_fieldLabel);
        stack.Controls.Add(_inputTextBox);
        stack.Controls.Add(_entityHost);
        stack.Controls.Add(_continueButton);

        Controls.Add(stack);

        FormClosing += OnFormClosing;
        Load += OnFormLoad;
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

    static TextBox MakeWideTextBox() =>
        new()
        {
            Width = 460,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.FixedSingle,
        };

    async void OnFormLoad(object? sender, EventArgs e)
    {
        Load -= OnFormLoad;
        _draftSettings = AppSettingsStore.Load();

        if (NeedsGroqSync(_draftSettings))
        {
            try
            {
                await FinishSetupAsync().ConfigureAwait(true);
                return;
            }
            catch (Exception ex)
            {
                global::SistecHub.UserFacingErrorHelper.ShowErrorFromException(
                    this, ex, global::SistecHub.UserFacingErrorHelper.ValidationErrorTitle);
            }
        }

        _step = ResolveStartingStep(_draftSettings);
        await ApplyStepToUiAsync().ConfigureAwait(true);
    }

    static SetupStep ResolveStartingStep(AppUserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            return SetupStep.GlpiUserToken;

        return SetupStep.EntityId;
    }

    static bool NeedsGroqSync(AppUserSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")))
            return false;

        if (!string.IsNullOrWhiteSpace(settings.GroqApiKey))
            return false;

        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            return false;

        return int.TryParse(settings.EntityId?.Trim(), out var entityId) && entityId >= 1;
    }

    async Task ApplyStepToUiAsync()
    {
        _stepIndicator.Text = $"Passo {(int)_step} de 2";
        _inputTextBox.Visible = _step != SetupStep.EntityId;
        _entityHost.Visible = _step == SetupStep.EntityId;
        _entityComboBox.Visible = true;

        switch (_step)
        {
            case SetupStep.GlpiUserToken:
                _subtitle.Text = "Indique o user token do GLPI para continuar.";
                _fieldLabel.Text = "User token";
                _inputTextBox.Text = _draftSettings.GlpiUserToken ?? "";
                _inputTextBox.UseSystemPasswordChar = true;
                _continueButton.Text = "Continuar";
                _inputTextBox.SelectAll();
                _inputTextBox.Focus();
                break;

            case SetupStep.EntityId:
                _subtitle.Text = "Selecione a entidade no GLPI.";
                _fieldLabel.Text = "Entidade";
                _continueButton.Text = "Salvar e continuar";
                await LoadEntityComboAsync().ConfigureAwait(true);
                _entityComboBox.Focus();
                break;
        }
    }

    async Task LoadEntityComboAsync()
    {
        _continueButton.Enabled = false;
        _entityComboBox.Enabled = false;
        _entityComboBox.DataSource = null;
        _entityComboBox.Items.Clear();
        _entityComboBox.Items.Add("A carregar entidades...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var entities = await GlpiApiClient.GetEntitiesAsync(_draftSettings, cts.Token).ConfigureAwait(true);

            if (entities.Count == 0)
            {
                throw new InvalidOperationException("Não foram encontradas entidades acessíveis no GLPI.");
            }

            var selectedId = 0;
            if (int.TryParse(_draftSettings.EntityId?.Trim(), out var savedId))
                selectedId = savedId;

            EntityComboBoxHelper.Bind(_entityComboBox, entities, selectedId);
        }
        finally
        {
            _entityComboBox.Enabled = true;
            _continueButton.Enabled = true;
        }
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_setupCompleted || e.CloseReason != CloseReason.UserClosing)
            return;

        DialogResult = DialogResult.Cancel;
    }

    async Task OnContinueClickedAsync()
    {
        _continueButton.Enabled = false;
        UseWaitCursor = true;
        try
        {
            switch (_step)
            {
                case SetupStep.GlpiUserToken:
                    await CompleteGlpiUserTokenStepAsync(_inputTextBox.Text.Trim()).ConfigureAwait(true);
                    break;

                case SetupStep.EntityId:
                    await CompleteEntityIdStepAsync().ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            global::SistecHub.UserFacingErrorHelper.ShowErrorFromException(
                this, ex, global::SistecHub.UserFacingErrorHelper.ValidationErrorTitle);
        }
        finally
        {
            _continueButton.Enabled = true;
            UseWaitCursor = false;
        }
    }

    async Task CompleteGlpiUserTokenStepAsync(string userToken)
    {
        if (string.IsNullOrWhiteSpace(userToken))
        {
            throw new InvalidOperationException("Indique o user token do GLPI.");
        }

        _draftSettings.GlpiUserToken = userToken;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await global::SistecHub.AppConfigurationValidation.ValidateGlpiUserTokenAsync(_draftSettings, cts.Token)
            .ConfigureAwait(true);

        AppSettingsStore.Save(_draftSettings);
        _step = SetupStep.EntityId;
        await ApplyStepToUiAsync().ConfigureAwait(true);
    }

    async Task CompleteEntityIdStepAsync()
    {
        var entityId = ResolveSelectedEntityId();
        if (entityId < 1)
        {
            throw new InvalidOperationException("Selecione uma entidade.");
        }

        _draftSettings.EntityId = entityId.ToString();
        await FinishSetupAsync().ConfigureAwait(true);
    }

    async Task FinishSetupAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await global::SistecHub.AppConfigurationValidation.ValidateAllAsync(_draftSettings, cts.Token)
            .ConfigureAwait(true);

        AppSettingsStore.Save(_draftSettings);
        _setupCompleted = true;

        InventarioServiceCoordinator.RequestRefresh();
        InventarioServiceCoordinator.RequestUpload();

        DialogResult = DialogResult.OK;
        Close();
    }

    int ResolveSelectedEntityId() => EntityComboBoxHelper.GetSelectedEntityId(_entityComboBox);
}
