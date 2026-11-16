using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.Inventario;

namespace SistecHub.UI;

internal sealed class InitialSetupForm : Form
{
    enum SetupStep
    {
        GlpiUserToken = 1,
        EntityId = 2,
    }

    readonly InitialSetupWpfControl _wpfControl;
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

        _wpfControl = new InitialSetupWpfControl();
        _wpfControl.ContinueRequested += async (_, _) => await OnContinueClickedAsync().ConfigureAwait(true);

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfControl,
        };

        Controls.Add(host);

        FormClosing += OnFormClosing;
        Load += OnFormLoad;
    }

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
        switch (_step)
        {
            case SetupStep.GlpiUserToken:
                _wpfControl.SetStepGlpiToken(_draftSettings.GlpiUserToken ?? "");
                break;

            case SetupStep.EntityId:
                _wpfControl.SetStepEntity(null, 0, loading: true);
                await LoadEntityComboAsync().ConfigureAwait(true);
                break;
        }
    }

    async Task LoadEntityComboAsync()
    {
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

            _wpfControl.SetStepEntity(entities, selectedId, loading: false);
        }
        catch
        {
            _wpfControl.SetStepEntity(null, 0, loading: false);
            throw;
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
        _wpfControl.SetContinueEnabled(false);
        UseWaitCursor = true;
        try
        {
            switch (_step)
            {
                case SetupStep.GlpiUserToken:
                    await CompleteGlpiUserTokenStepAsync(_wpfControl.Token.Trim()).ConfigureAwait(true);
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
            _wpfControl.SetContinueEnabled(true);
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
        var entityId = _wpfControl.GetSelectedEntityId();
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
}
