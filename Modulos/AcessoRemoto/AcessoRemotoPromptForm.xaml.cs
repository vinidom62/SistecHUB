using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SistecHub.Modulos.AcessoRemoto;

/// <summary>
/// Janela WPF de consentimento para aceitar ou recusar uma solicitação de acesso remoto.
/// Exibe nome completo e foto de perfil (ou iniciais) do técnico.
/// </summary>
public sealed partial class AcessoRemotoPromptForm : Window
{
    static AcessoRemotoPromptForm? _activeInstance;
    static string? _pendingTechnicianName;
    static string? _pendingTechnicianAvatar;
    static int _requestGeneration;
    static int _approvedGeneration;
    static readonly object SyncLock = new();

    readonly DispatcherTimer _countdownTimer;
    readonly int _totalSeconds;
    int _remainingSeconds;

    public static async Task<bool> RequestConsentAsync(
        string technicianName,
        string? avatarBase64OrPath = null,
        int timeoutSeconds = 45)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        int requestGen;
        lock (SyncLock)
        {
            requestGen = ++_requestGeneration;
            _pendingTechnicianName = !string.IsNullOrWhiteSpace(technicianName) ? technicianName : "Técnico de Suporte";
            _pendingTechnicianAvatar = !string.IsNullOrWhiteSpace(avatarBase64OrPath) ? avatarBase64OrPath : null;
        }

        void RunOnUi()
        {
            AcessoRemotoPromptForm? form = null;
            try
            {
                CloseIfOpen(clearPending: false);

                string initialName;
                string? initialAvatar;
                lock (SyncLock)
                {
                    if (requestGen != _requestGeneration)
                    {
                        tcs.TrySetResult(_approvedGeneration >= requestGen);
                        return;
                    }
                    initialName = !string.IsNullOrWhiteSpace(_pendingTechnicianName) ? _pendingTechnicianName! : "Técnico de Suporte";
                    initialAvatar = _pendingTechnicianAvatar;
                }

                form = new AcessoRemotoPromptForm(initialName, initialAvatar, timeoutSeconds);
                lock (SyncLock)
                {
                    if (requestGen != _requestGeneration)
                    {
                        bool wasApproved = _approvedGeneration >= requestGen;
                        try { form.Close(); } catch { }
                        tcs.TrySetResult(wasApproved);
                        return;
                    }
                    _activeInstance = form;
                    if (!string.IsNullOrWhiteSpace(_pendingTechnicianName) || !string.IsNullOrWhiteSpace(_pendingTechnicianAvatar))
                    {
                        form.SetTechnicianData(_pendingTechnicianName ?? initialName, _pendingTechnicianAvatar ?? initialAvatar);
                    }
                }

                var result = form.ShowDialog();
                tcs.TrySetResult(result == true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                lock (SyncLock)
                {
                    if (form is not null && ReferenceEquals(_activeInstance, form))
                        _activeInstance = null;
                    if (requestGen == _requestGeneration)
                    {
                        _pendingTechnicianName = null;
                        _pendingTechnicianAvatar = null;
                    }
                }
            }
        }

        System.Windows.Forms.Form? targetForm = null;
        for (int i = 0; i < System.Windows.Forms.Application.OpenForms.Count; i++)
        {
            var f = System.Windows.Forms.Application.OpenForms[i];
            if (f is not null && !f.IsDisposed && f.IsHandleCreated)
            {
                targetForm = f;
                break;
            }
        }

        if (targetForm is not null)
        {
            targetForm.BeginInvoke(new Action(RunOnUi));
        }
        else
        {
            var thread = new Thread(RunOnUi) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    public static void UpdateTechnicianInfo(string name, string? avatarBase64OrPath)
    {
        AcessoRemotoPromptForm? form;
        string resolvedName;
        string? resolvedAvatar;
        lock (SyncLock)
        {
            _pendingTechnicianName = !string.IsNullOrWhiteSpace(name) ? name : "Técnico de Suporte";
            _pendingTechnicianAvatar = !string.IsNullOrWhiteSpace(avatarBase64OrPath) ? avatarBase64OrPath : null;

            resolvedName = _pendingTechnicianName;
            resolvedAvatar = _pendingTechnicianAvatar;
            form = _activeInstance;
        }

        form?.Dispatcher.BeginInvoke(new Action(() => form.SetTechnicianData(resolvedName, resolvedAvatar)));
    }

    public static void ApproveIfOpen()
    {
        AcessoRemotoPromptForm? form;
        lock (SyncLock)
        {
            _approvedGeneration = ++_requestGeneration;
            form = _activeInstance;
            _activeInstance = null;
            _pendingTechnicianName = null;
            _pendingTechnicianAvatar = null;
        }

        if (form is null)
            return;

        try
        {
            form.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { form.Finish(true); } catch { }
            }));
        }
        catch
        {
            // Ignorado
        }
    }

    public static void CloseIfOpen(bool clearPending = true)
    {
        AcessoRemotoPromptForm? form;
        lock (SyncLock)
        {
            form = _activeInstance;
            _activeInstance = null;
            if (clearPending)
            {
                _requestGeneration++;
                _pendingTechnicianName = null;
                _pendingTechnicianAvatar = null;
            }
        }

        if (form is null)
            return;

        try
        {
            form.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { form.DialogResult = false; } catch { }
                try { form.Close(); } catch { }
            }));
        }
        catch
        {
            // Ignorado
        }
    }

    public AcessoRemotoPromptForm(string technicianName, string? avatarBase64OrPath, int timeoutSeconds)
    {
        InitializeComponent();

        _totalSeconds = Math.Clamp(timeoutSeconds, 10, 180);
        _remainingSeconds = _totalSeconds;
        LblTimer.Text = $"Recusa automática em {_remainingSeconds}s";

        SetTechnicianData(technicianName, avatarBase64OrPath);

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _countdownTimer.Stop();
                Finish(false);
                return;
            }

            LblTimer.Text = $"Recusa automática em {_remainingSeconds}s";
        };

        Loaded += (_, _) =>
        {
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }

            BarFill.Width = ((FrameworkElement)BarFill.Parent).ActualWidth;
            BarScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(_totalSeconds))));

            _countdownTimer.Start();
            Activate();
        };

        Closed += (_, _) => _countdownTimer.Stop();
    }

    void SetTechnicianData(string name, string? avatarBase64OrPath)
    {
        var display = string.IsNullOrWhiteSpace(name) ? "Técnico de Suporte" : name.Trim();
        LblName.Text = display;
        AvatarInitials.Text = ExtractInitials(display);

        var image = LoadAvatar(avatarBase64OrPath);
        if (image is not null)
        {
            AvatarImage.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            AvatarImage.Visibility = Visibility.Visible;
            AvatarInitials.Visibility = Visibility.Collapsed;
        }
        else
        {
            AvatarImage.Visibility = Visibility.Collapsed;
            AvatarInitials.Visibility = Visibility.Visible;
        }
    }

    static BitmapImage? LoadAvatar(string? avatarBase64OrPath)
    {
        if (string.IsNullOrWhiteSpace(avatarBase64OrPath))
            return null;

        try
        {
            var raw = avatarBase64OrPath.Trim();
            byte[] bytes;

            if (File.Exists(raw))
            {
                bytes = File.ReadAllBytes(raw);
            }
            else
            {
                int comma = raw.IndexOf(',', StringComparison.Ordinal);
                if (comma >= 0 && raw.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                    raw = raw[(comma + 1)..];
                bytes = Convert.FromBase64String(raw);
            }

            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    static string ExtractInitials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "TS";
        if (parts.Length == 1)
            return parts[0].Length <= 2 ? parts[0].ToUpperInvariant() : parts[0][..2].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    void Finish(bool accepted)
    {
        _countdownTimer.Stop();
        try { DialogResult = accepted; } catch { }
        Close();
    }

    void BtnAccept_Click(object sender, RoutedEventArgs e) => Finish(true);

    void BtnReject_Click(object sender, RoutedEventArgs e) => Finish(false);

    void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Finish(false);
    }

    void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }
}
