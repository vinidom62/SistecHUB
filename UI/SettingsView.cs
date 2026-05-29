using SistecHub.Core;

namespace SistecHub.UI;

internal sealed class SettingsView : UserControl
{
    readonly TextBox _entityIdTextBox;
    readonly TextBox _glpiUserTokenTextBox;
    readonly TextBox _groqApiKeyTextBox;
    readonly Button _saveButton;
    readonly Label _feedbackLabel;

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

        var checkUpdatesButton = new Button
        {
            Text = "Verificar atualizações",
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
        checkUpdatesButton.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
        checkUpdatesButton.FlatAppearance.BorderSize = 1;
        checkUpdatesButton.Click += async (_, _) =>
        {
            checkUpdatesButton.Enabled = false;
            try
            {
                await AppUpdateService.CheckAndPromptAsync(FindForm(), silentIfUpToDate: false)
                    .ConfigureAwait(true);
            }
            finally
            {
                checkUpdatesButton.Enabled = true;
            }
        };
        stack.Controls.Add(checkUpdatesButton);

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

    void OnViewLoad(object? sender, EventArgs e)
    {
        Load -= OnViewLoad;
        var s = AppSettingsStore.Load();
        _entityIdTextBox.Text = s.EntityId ?? "";
        _glpiUserTokenTextBox.Text = s.GlpiUserToken ?? "";
        _groqApiKeyTextBox.Text = s.GroqApiKey ?? "";
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
            var merged = AppSettingsStore.Load();
            merged.EntityId = _entityIdTextBox.Text.Trim();
            merged.GlpiUserToken = _glpiUserTokenTextBox.Text.Trim();
            merged.GroqApiKey = _groqApiKeyTextBox.Text.Trim();

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
