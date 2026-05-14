using SistecHub.Core;

namespace SistecHub.UI;

internal sealed class InitialSetupForm : Form
{
    readonly TextBox _entityIdTextBox;
    readonly TextBox _glpiUserTokenTextBox;
    readonly TextBox _groqApiKeyTextBox;
    readonly Button _continueButton;
    readonly Label _feedbackLabel;

    bool _setupCompleted;

    public InitialSetupForm()
    {
        Text = "SistecHub";
        ClientSize = new Size(560, 520);
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

        var subtitle = new Label
        {
            Text = "Antes de usar o SistecHub, indique a entidade, o user token do GLPI e a chave da API Groq.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 24),
        };

        stack.Controls.Add(title);
        stack.Controls.Add(subtitle);

        stack.Controls.Add(MakeFieldLabel("Id da entidade (client id)"));
        _entityIdTextBox = MakeWideTextBox();
        stack.Controls.Add(_entityIdTextBox);

        stack.Controls.Add(MakeSectionGap());

        stack.Controls.Add(MakeFieldLabel("User token"));
        _glpiUserTokenTextBox = MakeWideTextBox();
        _glpiUserTokenTextBox.UseSystemPasswordChar = true;
        stack.Controls.Add(_glpiUserTokenTextBox);

        stack.Controls.Add(MakeSectionGap());

        stack.Controls.Add(MakeFieldLabel("Chave API Groq"));
        _groqApiKeyTextBox = MakeWideTextBox();
        _groqApiKeyTextBox.UseSystemPasswordChar = true;
        stack.Controls.Add(_groqApiKeyTextBox);

        _continueButton = new Button
        {
            Text = "Salvar e continuar",
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

        stack.Controls.Add(_continueButton);
        stack.Controls.Add(_feedbackLabel);

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
            Width = 460,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.FixedSingle,
        };

    void OnFormLoad(object? sender, EventArgs e)
    {
        Load -= OnFormLoad;
        var settings = AppSettingsStore.Load();
        _entityIdTextBox.Text = settings.EntityId ?? "";
        _glpiUserTokenTextBox.Text = settings.GlpiUserToken ?? "";
        _groqApiKeyTextBox.Text = settings.GroqApiKey ?? "";
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
        _feedbackLabel.Visible = false;
        UseWaitCursor = true;
        try
        {
            var merged = AppSettingsStore.Load();
            merged.EntityId = _entityIdTextBox.Text.Trim();
            merged.GlpiUserToken = _glpiUserTokenTextBox.Text.Trim();
            merged.GroqApiKey = _groqApiKeyTextBox.Text.Trim();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await global::SistecHub.AppConfigurationValidation.ValidateAllAsync(merged, cts.Token)
                .ConfigureAwait(true);

            AppSettingsStore.Save(merged);
            _setupCompleted = true;
            DialogResult = DialogResult.OK;
            Close();
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
}
