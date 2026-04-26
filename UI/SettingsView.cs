using SistecHub.Core;

namespace SistecHub.UI;

internal sealed class SettingsView : UserControl
{
    readonly TextBox _entityIdTextBox;
    readonly TextBox _glpiAppTokenTextBox;
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

        stack.Controls.Add(MakeFieldLabel("Id da entidade (client id)"));
        _entityIdTextBox = MakeWideTextBox();
        stack.Controls.Add(_entityIdTextBox);

        stack.Controls.Add(MakeSectionGap());
        stack.Controls.Add(MakeSectionTitle("Integração GLPI"));
        stack.Controls.Add(MakeMutedLine(
            "A URL base é fixa (sem /apirest.php). Os tokens ficam guardados apenas neste computador."));

        stack.Controls.Add(MakeFieldLabel("URL base GLPI"));
        stack.Controls.Add(MakeFixedValueLine(AppUserSettings.DefaultGlpiBaseUrl));

        stack.Controls.Add(MakeFieldLabel("App token"));
        _glpiAppTokenTextBox = MakeWideTextBox();
        _glpiAppTokenTextBox.UseSystemPasswordChar = true;
        stack.Controls.Add(_glpiAppTokenTextBox);

        stack.Controls.Add(MakeFieldLabel("User token"));
        _glpiUserTokenTextBox = MakeWideTextBox();
        _glpiUserTokenTextBox.UseSystemPasswordChar = true;
        stack.Controls.Add(_glpiUserTokenTextBox);

        stack.Controls.Add(MakeSectionGap());
        stack.Controls.Add(MakeSectionTitle("Inteligência artificial (Groq)"));
        stack.Controls.Add(MakeMutedLine(
            "A variável de ambiente GROQ_API_KEY tem prioridade sobre a chave guardada aqui. " +
            "Modelo, temperatura e URL da API são fixos."));

        stack.Controls.Add(MakeFieldLabel("Chave API Groq"));
        _groqApiKeyTextBox = MakeWideTextBox();
        _groqApiKeyTextBox.UseSystemPasswordChar = true;
        stack.Controls.Add(_groqApiKeyTextBox);

        stack.Controls.Add(MakeFieldLabel("Modelo"));
        stack.Controls.Add(MakeFixedValueLine(AppUserSettings.DefaultGroqModel));

        stack.Controls.Add(MakeFieldLabel("Temperatura"));
        stack.Controls.Add(MakeFixedValueLine(AppUserSettings.DefaultGroqTemperature.ToString("0.00")));

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
        _saveButton.Click += OnSaveClicked;

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

    static Label MakeSectionTitle(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 4),
        };

    static Label MakeMutedLine(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
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

    static Label MakeFixedValueLine(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 0),
        };

    void OnViewLoad(object? sender, EventArgs e)
    {
        Load -= OnViewLoad;
        var s = AppSettingsStore.Load();
        _entityIdTextBox.Text = s.EntityId ?? "";
        _glpiAppTokenTextBox.Text = s.GlpiAppToken ?? "";
        _glpiUserTokenTextBox.Text = s.GlpiUserToken ?? "";
        _groqApiKeyTextBox.Text = s.GroqApiKey ?? "";
    }

    void OnSaveClicked(object? sender, EventArgs e)
    {
        try
        {
            var merged = AppSettingsStore.Load();
            merged.EntityId = _entityIdTextBox.Text.Trim();
            merged.GlpiBaseUrl = AppUserSettings.DefaultGlpiBaseUrl;
            merged.GlpiAppToken = _glpiAppTokenTextBox.Text.Trim();
            merged.GlpiUserToken = _glpiUserTokenTextBox.Text.Trim();
            merged.GroqApiKey = _groqApiKeyTextBox.Text.Trim();
            merged.GroqModel = AppUserSettings.DefaultGroqModel;
            merged.GroqTemperature = AppUserSettings.DefaultGroqTemperature;
            AppSettingsStore.Save(merged);

            _feedbackLabel.Text = "Configurações salvas com sucesso.";
            _feedbackLabel.ForeColor = Color.FromArgb(22, 163, 74);
            _feedbackLabel.Visible = true;
        }
        catch (Exception ex)
        {
            _feedbackLabel.Text = "Não foi possível salvar: " + ex.Message;
            _feedbackLabel.ForeColor = Color.FromArgb(220, 38, 38);
            _feedbackLabel.Visible = true;
        }
    }

    internal string EntityId
    {
        get => _entityIdTextBox.Text;
        set => _entityIdTextBox.Text = value;
    }
}
