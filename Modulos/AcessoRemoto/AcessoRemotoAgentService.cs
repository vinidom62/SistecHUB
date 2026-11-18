using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SistecHub.Core;

namespace SistecHub.Modulos.AcessoRemoto;

internal enum AcessoRemotoConnectionPhase
{
    Stopped,
    Connecting,
    ConnectedWaiting,
    SessionActive,
    Reconnecting,
    Error,
}

internal sealed record AcessoRemotoChatEntry(
    string Sender,
    string Text,
    string Time,
    bool IsFromTechnician,
    bool IsSystemEvent = false);

internal sealed record AcessoRemotoStateSnapshot(
    AcessoRemotoConnectionPhase Phase,
    string ServerUrl,
    string AssignedId,
    string FormattedId,
    string Password,
    bool IsSessionActive,
    bool IsInputBlocked,
    int SelectedMonitorIndex,
    int Fps,
    int Quality,
    string StatusText,
    string? LastError,
    IReadOnlyList<AcessoRemotoMonitorInfo> Monitors,
    string? ConnectedTechnicianName = null,
    string? ConnectedTechnicianAvatar = null,
    DateTimeOffset? SessionStartTime = null,
    string UnattendedPassword = "",
    bool HasUnattendedPassword = false);

internal static class AcessoRemotoAgentService
{
    static readonly object SyncRoot = new();
    static readonly List<AcessoRemotoChatEntry> ChatHistory = new();

    static CancellationTokenSource? _serviceCts;
    static CancellationTokenSource? _connectionCycleCts;
    static Task? _workerTask;
    static SafeWebSocketConnection? _activeConnection;
    static SafeWebSocketConnection? _activeFileConnection;
    static string? _activeSessionToken;

    static AcessoRemotoConnectionPhase _phase = AcessoRemotoConnectionPhase.Stopped;
    static string _serverUrl = AppSettingsStore.DefaultRemotoServerUrl;
    static string _assignedId = "";
    static string _requestedId = "";
    static string _password = "";
    static string _unattendedPassword = "";
    static bool _isSessionActive;
    static bool _isInputBlocked;
    static string? _connectedTechnicianName;
    static string? _connectedTechnicianAvatar;
    static DateTimeOffset? _sessionStartTime;
    static int _selectedMonitorIndex;
    static int _fps = 25;
    static int _quality = 70;
    static string _statusText = "Parado";
    static string? _lastError;
    static List<AcessoRemotoMonitorInfo> _monitors = new();
    static List<AcessoRemotoClipboardFileItem> _hostAdvertisedFiles = new();
    static readonly ConcurrentDictionary<string, RemoteClipboardPullStream> ActiveClipboardPullStreams = new();
    static Thread? _clipboardMonitorThread;
    static ApplicationContext? _clipboardAppContext;

    public static event EventHandler? StateChanged;
    public static event EventHandler<AcessoRemotoChatEntry>? ChatMessageAdded;
    public static event EventHandler? SessionStarted;
    public static event EventHandler? SessionEnded;

    private static RemoteCursorState? _lastSentCursorState;
    private static AcessoRemotoScreenCapturer? _currentCapturer;
    private static long _lastFileActivityTick;

    static AcessoRemotoAgentService()
    {
        AcessoRemotoClientPriorityMonitor.PriorityChanged += OnClientPriorityChanged;
        AcessoRemotoClientPriorityMonitor.ClientPhysicalActivityOccurred += OnClientPhysicalActivityOccurred;
    }

    private static void OnClientPhysicalActivityOccurred(int x, int y)
    {
        TriggerImmediateCursorSend();
    }

    private static int _cursorDelayedCheckScheduled;

    public static void NotifyRemoteCursorPositionChanged(int ax, int ay, int aw, int ah)
    {
        TriggerImmediateCursorSend(ax, ay, aw, ah);

        if (Interlocked.Exchange(ref _cursorDelayedCheckScheduled, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Delay curto de 20ms: tempo para o message loop da janela alvo processar WM_SETCURSOR
                    await Task.Delay(20).ConfigureAwait(false);
                    TriggerImmediateCursorSend(ax, ay, aw, ah);

                    // Rechecagem aos 55ms: para navegadores pesados ou aplicativos com debounce
                    await Task.Delay(35).ConfigureAwait(false);
                    TriggerImmediateCursorSend(ax, ay, aw, ah);
                }
                catch
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _cursorDelayedCheckScheduled, 0);
                }
            });
        }
    }

    public static void SendCursorPacket(byte[] packet)
    {
        SafeWebSocketConnection? conn;
        lock (SyncRoot)
        {
            conn = _activeConnection;
        }

        if (conn == null || !conn.IsOpen || packet == null || packet.Length < 11)
            return;

        if (AcessoRemotoCursorHelper.TryParsePacket(packet, out var state))
        {
            if (AcessoRemotoCursorHelper.HasMeaningfulChange(_lastSentCursorState, state))
            {
                _lastSentCursorState = state;
                conn.EnqueueLatestCursorPacket(packet);
            }
        }
        else
        {
            conn.EnqueueLatestCursorPacket(packet);
        }
    }

    private static void TriggerImmediateCursorSend(int ax = 0, int ay = 0, int aw = 0, int ah = 0, bool? clientActiveOverride = null)
    {
        SafeWebSocketConnection? conn;
        lock (SyncRoot)
        {
            conn = _activeConnection;
        }

        if (conn == null || !conn.IsOpen)
            return;

        if (aw <= 0 || ah <= 0)
        {
            var capturer = _currentCapturer;
            if (capturer != null)
            {
                (ax, ay, aw, ah) = capturer.GetActiveBounds();
            }
            else
            {
                (ax, ay, aw, ah) = AcessoRemotoScreenCapturer.GetVirtualDesktopBounds();
            }
        }

        bool isClientActive = clientActiveOverride ?? AcessoRemotoClientPriorityMonitor.IsClientActive;
        if (AcessoRemotoCursorHelper.TryGetCurrentState(ax, ay, aw, ah, isClientActive, out var state))
        {
            if (AcessoRemotoCursorHelper.HasMeaningfulChange(_lastSentCursorState, state))
            {
                _lastSentCursorState = state;
                var packet = AcessoRemotoCursorHelper.CreatePacket(state);
                conn.EnqueueLatestCursorPacket(packet);
            }
        }
    }

    static void OnClientPriorityChanged(bool isBlocked)
    {
        SafeWebSocketConnection? conn;
        lock (SyncRoot)
        {
            conn = _activeConnection;
        }

        if (conn != null && conn.IsOpen)
        {
            _ = conn.WriteJsonAsync(new AcessoRemotoMessage
            {
                Type = AcessoRemotoProtocol.TypeTechnicianInputBlocked,
                Blocked = isBlocked,
                Reason = isBlocked ? "client_activity" : null
            }, CancellationToken.None);
        }

        TriggerImmediateCursorSend(clientActiveOverride: isBlocked);
    }

    public static void Start()
    {
        var settings = AppSettingsStore.Load();
        if (settings.RemotoBloqueado)
        {
            Stop();
            lock (SyncRoot)
            {
                _phase = AcessoRemotoConnectionPhase.Stopped;
                _statusText = "Acesso remoto bloqueado nesta máquina pelas configurações.";
            }
            RaiseStateChanged();
            return;
        }

        lock (SyncRoot)
        {
            if (_workerTask is { IsCompleted: false })
                return;

            _serverUrl = string.IsNullOrWhiteSpace(settings.RemotoServerUrl)
                ? AppSettingsStore.DefaultRemotoServerUrl
                : settings.RemotoServerUrl.Trim();
            _assignedId = AppSettingsStore.EnsureRemotoId(settings);
            _requestedId = _assignedId;
            _unattendedPassword = settings.RemotoUnattendedPassword ?? "";
            _password = _unattendedPassword;

            _serviceCts?.Cancel();
            _serviceCts?.Dispose();
            _serviceCts = new CancellationTokenSource();
            var token = _serviceCts.Token;

            _workerTask = Task.Run(() => RunAgentLoopAsync(token), token);
            _ = Task.Run(SyncWithGlpiAsync);
        }
    }

    public static async Task SyncWithGlpiAsync()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            var mid = 0;
            if (!int.TryParse(settings.GlpiMachineId, out mid) || mid <= 0)
            {
                try
                {
                    var regId = await SistecHub.Modulos.Inventario.InventarioMachineRegistration.EnsureRegisteredAsync().ConfigureAwait(false);
                    if (regId.HasValue && regId.Value > 0)
                    {
                        mid = regId.Value;
                        settings = AppSettingsStore.Load();
                    }
                }
                catch
                {
                    // Ignora se não for possível obter registro no momento
                }
            }

            if (mid > 0)
            {
                var glpiInfo = await SistecHub.Modulos.GLPI.GlpiApiClient.GetMachineRemoteAccessAsync(settings, mid).ConfigureAwait(false);
                if (glpiInfo is not null)
                {
                    var changed = false;
                    if (!string.IsNullOrWhiteSpace(glpiInfo.RemotoId) && glpiInfo.RemotoId != settings.RemotoId)
                    {
                        settings.RemotoId = glpiInfo.RemotoId;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(glpiInfo.HostToken) && glpiInfo.HostToken != settings.RemotoHostToken)
                    {
                        settings.RemotoHostToken = glpiInfo.HostToken;
                        changed = true;
                    }
                    var effectivePwd = glpiInfo.UnattendedEnabled ? glpiInfo.UnattendedPassword : "";
                    if (effectivePwd != settings.RemotoUnattendedPassword)
                    {
                        settings.RemotoUnattendedPassword = effectivePwd;
                        changed = true;
                    }
                    if (changed)
                    {
                        AppSettingsStore.Save(settings);
                        lock (SyncRoot)
                        {
                            _assignedId = settings.RemotoId;
                            _requestedId = settings.RemotoId;
                            _unattendedPassword = settings.RemotoUnattendedPassword;
                            _password = _unattendedPassword;
                        }
                        try
                        {
                            _connectionCycleCts?.Cancel();
                        }
                        catch { }
                        RaiseStateChanged();
                    }
                }
            }
        }
        catch
        {
            // Se offline, mantém credenciais em cache local
        }
    }

    static async Task TryRefreshPasswordFromGlpiWithoutReconnectAsync()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            if (!int.TryParse(settings.GlpiMachineId, out var mid) || mid <= 0)
                return;

            var fetchTask = SistecHub.Modulos.GLPI.GlpiApiClient.GetMachineRemoteAccessAsync(settings, mid);
            var completed = await Task.WhenAny(fetchTask, Task.Delay(2000)).ConfigureAwait(false);
            if (completed != fetchTask)
                return;

            var glpiInfo = await fetchTask.ConfigureAwait(false);
            if (glpiInfo is not null)
            {
                var effectivePwd = glpiInfo.UnattendedEnabled ? (glpiInfo.UnattendedPassword ?? "") : "";
                if (effectivePwd != settings.RemotoUnattendedPassword)
                {
                    settings.RemotoUnattendedPassword = effectivePwd;
                    AppSettingsStore.Save(settings);
                }
                lock (SyncRoot)
                {
                    _unattendedPassword = effectivePwd;
                    _password = effectivePwd;
                }
                RaiseStateChanged();
            }
        }
        catch
        {
            // Ignora falhas de rede durante validação em tempo real
        }
    }

    public static void Stop()
    {
        SafeWebSocketConnection? conn;
        SafeWebSocketConnection? fileConn;
        CancellationTokenSource? cts;
        CancellationTokenSource? cycleCts;
        lock (SyncRoot)
        {
            conn = _activeConnection;
            fileConn = _activeFileConnection;
            cts = _serviceCts;
            cycleCts = _connectionCycleCts;
            _serviceCts = null;
            _connectionCycleCts = null;
            _activeConnection = null;
            _activeFileConnection = null;
            _activeSessionToken = null;
            _isSessionActive = false;
            _isInputBlocked = false;
            _connectedTechnicianName = null;
            _connectedTechnicianAvatar = null;
            _sessionStartTime = null;
            _phase = AcessoRemotoConnectionPhase.Stopped;
            _statusText = AppSettingsStore.Load().RemotoBloqueado
                ? "Acesso remoto bloqueado nesta máquina pelas configurações."
                : "Serviço de acesso remoto parado.";
        }

        AcessoRemotoPromptForm.CloseIfOpen();
        AcessoRemotoChatForm.CloseIfOpen();
        AcessoRemotoInputHandler.BlockUserInput(false);
        AcessoRemotoFileSystem.CloseAllActiveUploads();

        if (fileConn is not null)
        {
            _ = fileConn.CloseQuietlyAsync();
        }

        if (conn is not null)
        {
            _ = conn.CloseQuietlyAsync();
        }

        try { cycleCts?.Cancel(); } catch { }
        try { cts?.Cancel(); } catch { }
        RaiseStateChanged();
        RaiseSessionEnded();
    }

    public static void Reconnect()
    {
        var settings = AppSettingsStore.Load();
        if (settings.RemotoBloqueado)
        {
            Stop();
            return;
        }

        lock (SyncRoot)
        {
            _serverUrl = string.IsNullOrWhiteSpace(settings.RemotoServerUrl)
                ? AppSettingsStore.DefaultRemotoServerUrl
                : settings.RemotoServerUrl.Trim();

            if (_workerTask is null || _workerTask.IsCompleted)
            {
                Start();
                return;
            }

            try
            {
                _connectionCycleCts?.Cancel();
            }
            catch
            {
                // Ignorado
            }
        }

        _ = Task.Run(SyncWithGlpiAsync);
    }

    public static void SetUnattendedPassword(string password)
    {
        var trimmed = password?.Trim() ?? "";
        lock (SyncRoot)
        {
            _unattendedPassword = trimmed;
            _password = trimmed;
        }

        var settings = AppSettingsStore.Load();
        settings.RemotoUnattendedPassword = trimmed;
        AppSettingsStore.Save(settings);

        AppendSystemChat(string.IsNullOrEmpty(trimmed)
            ? "Senha de acesso não supervisionado removida."
            : "Senha de acesso não supervisionado configurada.");
        Reconnect();
        RaiseStateChanged();
    }

    public static void RegeneratePasswordAndReconnect()
    {
        var pin = GeneratePin();
        SetUnattendedPassword(pin);
    }

    public static void SetCustomPasswordAndReconnect(string newPassword)
    {
        SetUnattendedPassword(newPassword);
    }

    public static void DisconnectActiveSession()
    {
        SafeWebSocketConnection? conn;
        lock (SyncRoot)
        {
            conn = _activeConnection;
            _connectedTechnicianName = null;
            _connectedTechnicianAvatar = null;
            _sessionStartTime = null;
        }

        AcessoRemotoPromptForm.CloseIfOpen();
        AcessoRemotoInputHandler.BlockUserInput(false);
        AppendSystemChat("Sessão encerrada pelo utilizador local.");

        if (conn is not null && conn.IsOpen)
        {
            try
            {
                _ = conn.WriteJsonAsync(new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeSessionEnded,
                    Message = "A sessão remota foi finalizada pelo cliente."
                }, CancellationToken.None);
            }
            catch { }
        }

        Reconnect();
    }

    public static AcessoRemotoStateSnapshot GetSnapshot()
    {
        lock (SyncRoot)
        {
            var hasUnattended = !string.IsNullOrEmpty(_unattendedPassword);
            return new AcessoRemotoStateSnapshot(
                _phase,
                _serverUrl,
                _assignedId,
                FormatId(_assignedId),
                _password,
                _isSessionActive,
                _isInputBlocked,
                _selectedMonitorIndex,
                _fps,
                _quality,
                _statusText,
                _lastError,
                new List<AcessoRemotoMonitorInfo>(_monitors),
                _connectedTechnicianName,
                _connectedTechnicianAvatar,
                _sessionStartTime,
                _unattendedPassword,
                hasUnattended);
        }
    }

    public static IReadOnlyList<AcessoRemotoChatEntry> GetChatHistory()
    {
        lock (SyncRoot)
        {
            return new List<AcessoRemotoChatEntry>(ChatHistory);
        }
    }

    public static async Task<bool> SendChatMessageAsync(string text)
    {
        var trimmed = text?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed))
            return false;

        SafeWebSocketConnection? conn;
        lock (SyncRoot)
        {
            conn = _activeConnection;
        }

        if (conn is null || !conn.IsOpen)
            return false;

        var timeStr = DateTime.Now.ToString("HH:mm");
        var senderName = string.IsNullOrWhiteSpace(Environment.UserName) ? "Cliente" : Environment.UserName;

        var msg = new AcessoRemotoMessage
        {
            Type = AcessoRemotoProtocol.TypeChatMessage,
            Sender = senderName,
            Text = trimmed,
            Time = timeStr,
        };

        try
        {
            await conn.WriteJsonAsync(msg, CancellationToken.None).ConfigureAwait(false);
            AddChatEntry(new AcessoRemotoChatEntry(
                Sender: "Você",
                Text: trimmed,
                Time: timeStr,
                IsFromTechnician: false,
                IsSystemEvent: false));
            return true;
        }
        catch (Exception ex)
        {
            AppDebugLog.LogException("AcessoRemoto", ex, "Falha ao enviar mensagem de chat");
            return false;
        }
    }

    public static string FormatId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "--- --- ---";
        var clean = id.Trim().Replace(" ", "").Replace("-", "");
        if (clean.Length == 6)
            return $"{clean[..3]} {clean[3..]}";
        if (clean.Length == 8)
            return $"{clean[..4]} {clean[4..]}";
        if (clean.Length == 9)
            return $"{clean[..3]} {clean[3..6]} {clean[6..]}";
        if (clean.Length == 10)
            return $"{clean[..3]} {clean[3..6]} {clean[6..]}";
        return clean;
    }

    static string GeneratePin()
    {
        int n = RandomNumberGenerator.GetInt32(1000, 10000);
        return n.ToString("D4");
    }

    static void UpdateState(
        AcessoRemotoConnectionPhase phase,
        string statusText,
        string? lastError = null,
        bool? sessionActive = null,
        bool? inputBlocked = null)
    {
        lock (SyncRoot)
        {
            _phase = phase;
            _statusText = statusText;
            _lastError = lastError;
            if (sessionActive.HasValue)
            {
                _isSessionActive = sessionActive.Value;
                if (!sessionActive.Value)
                {
                    _connectedTechnicianName = null;
                    _connectedTechnicianAvatar = null;
                    _sessionStartTime = null;
                }
            }
            if (inputBlocked.HasValue)
                _isInputBlocked = inputBlocked.Value;
        }

        RaiseStateChanged();
    }

    static bool _sessionStartedNotified;

    static void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            // Ignora exceções de assinantes de UI
        }
    }

    static void RaiseSessionStarted()
    {
        lock (SyncRoot)
        {
            if (_sessionStartedNotified)
                return;
            _sessionStartedNotified = true;
        }

        AcessoRemotoClientPriorityMonitor.Start();

        try
        {
            SessionStarted?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppDebugLog.LogException("AcessoRemoto", ex, "Falha ao disparar SessionStarted");
        }
    }

    static void RaiseSessionEnded()
    {
        lock (SyncRoot)
        {
            if (!_sessionStartedNotified)
                return;
            _sessionStartedNotified = false;
        }

        AcessoRemotoClientPriorityMonitor.Stop();

        try
        {
            SessionEnded?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppDebugLog.LogException("AcessoRemoto", ex, "Falha ao disparar SessionEnded");
        }
    }

    static void AppendSystemChat(string text)
    {
        AddChatEntry(new AcessoRemotoChatEntry(
            Sender: "Sistema",
            Text: text,
            Time: DateTime.Now.ToString("HH:mm"),
            IsFromTechnician: false,
            IsSystemEvent: true));
    }

    static void AddChatEntry(AcessoRemotoChatEntry entry)
    {
        if (AcessoRemotoClipboard.IsAutomatedFileTransferChatMessage(entry.Text))
            return;

        lock (SyncRoot)
        {
            ChatHistory.Add(entry);
            if (ChatHistory.Count > 200)
                ChatHistory.RemoveAt(0);
        }

        try
        {
            ChatMessageAdded?.Invoke(null, entry);
        }
        catch
        {
            // Ignora exceções de assinantes de UI
        }
    }

    static void EnsureClipboardListenerStarted()
    {
        lock (SyncRoot)
        {
            if (_clipboardMonitorThread is { IsAlive: true })
                return;

            _clipboardMonitorThread = new Thread(() =>
            {
                ClipboardNotificationWindow? listener = null;
                try
                {
                    listener = new ClipboardNotificationWindow(OnLocalHostClipboardChanged);
                    _clipboardAppContext = new ApplicationContext();
                    Application.Run(_clipboardAppContext);
                }
                catch
                {
                    // Ignora falhas na thread de clipboard
                }
                finally
                {
                    listener?.Dispose();
                }
            })
            {
                IsBackground = true,
                Name = "SistecHubHostClipboardMonitor"
            };
            _clipboardMonitorThread.SetApartmentState(ApartmentState.STA);
            _clipboardMonitorThread.Start();
        }
    }

    static void OnLocalHostClipboardChanged()
    {
        SafeWebSocketConnection? conn;
        bool active;
        lock (SyncRoot)
        {
            conn = _activeConnection;
            active = _isSessionActive;
        }

        if (!active || conn is null || !conn.IsOpen)
            return;

        var (text, files) = AcessoRemotoClipboard.ReadLocalClipboard();
        if (files is { Count: > 0 })
        {
            var ads = AcessoRemotoClipboard.BuildClipboardFilesAdvertisement(files);
            if (ads.Count > 0)
            {
                lock (SyncRoot)
                {
                    _hostAdvertisedFiles = ads;
                }

                var wireAds = ads.Select(a => new AcessoRemotoClipboardFileItem
                {
                    Index = a.Index,
                    Name = a.Name,
                    RelativePath = a.RelativePath,
                    Size = a.Size,
                    IsDir = a.IsDir,
                    LastWriteUtcTicks = a.LastWriteUtcTicks
                }).ToList();

                _ = WriteFileOrFallbackJsonAsync(conn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeClipboardFilesAdvertisement,
                    ClipboardFiles = wireAds
                }, CancellationToken.None);
            }
        }
        else if (!string.IsNullOrEmpty(text))
        {
            _ = conn.WriteJsonAsync(new AcessoRemotoMessage
            {
                Type = AcessoRemotoProtocol.TypeClipboardText,
                Text = text
            }, CancellationToken.None);
        }
    }

    static async Task<bool> WriteFileOrFallbackJsonAsync(
        SafeWebSocketConnection? fallbackMainConn,
        AcessoRemotoMessage message,
        CancellationToken ct)
    {
        SafeWebSocketConnection? fileConn;
        SafeWebSocketConnection? mainConn;
        lock (SyncRoot)
        {
            fileConn = _activeFileConnection;
            mainConn = fallbackMainConn ?? _activeConnection;
        }

        if (fileConn is { IsOpen: true })
        {
            try
            {
                await fileConn.WriteJsonAsync(message, ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                // Fallback transparente para o canal principal se o canal secundário oscilar
            }
        }

        if (mainConn is { IsOpen: true })
        {
            await mainConn.WriteLowPriorityJsonAsync(message, ct).ConfigureAwait(false);
        }
        return false;
    }

    static async Task RunAgentLoopAsync(CancellationToken serviceToken)
    {
        using var capturer = new AcessoRemotoScreenCapturer(0, _quality);
        _currentCapturer = capturer;
        lock (SyncRoot)
        {
            _monitors = capturer.RefreshMonitors();
        }
        RaiseStateChanged();

        while (!serviceToken.IsCancellationRequested)
        {
            var settings = AppSettingsStore.Load();
            if (settings.RemotoBloqueado)
            {
                UpdateState(
                    AcessoRemotoConnectionPhase.Stopped,
                    "Acesso remoto bloqueado nesta máquina pelas configurações.",
                    lastError: null,
                    sessionActive: false,
                    inputBlocked: false);
                break;
            }

            var serverUrl = string.IsNullOrWhiteSpace(settings.RemotoServerUrl)
                ? AppSettingsStore.DefaultRemotoServerUrl
                : settings.RemotoServerUrl.Trim();
            var fixedId = AppSettingsStore.EnsureRemotoId(settings);
            var unattendedPassword = settings.RemotoUnattendedPassword ?? "";
            var hostToken = AppSettingsStore.EnsureRemotoHostToken(settings);

            string currentPassword;
            string requestedId;
            lock (SyncRoot)
            {
                _serverUrl = serverUrl;
                _assignedId = fixedId;
                _requestedId = fixedId;
                _unattendedPassword = unattendedPassword;
                _password = unattendedPassword;
                currentPassword = unattendedPassword;
                requestedId = fixedId;
            }

            using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
            lock (SyncRoot)
            {
                _connectionCycleCts = cycleCts;
            }
            var cycleToken = cycleCts.Token;

            UpdateState(
                AcessoRemotoConnectionPhase.Connecting,
                $"Conectando ao servidor relay...",
                lastError: null,
                sessionActive: false,
                inputBlocked: false);

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)
                || (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)))
            {
                UpdateState(
                    AcessoRemotoConnectionPhase.Error,
                    "URL do servidor WSS inválida nas configurações.",
                    lastError: $"URL inválida: {serverUrl}",
                    sessionActive: false,
                    inputBlocked: false);

                if ( await DelayOrCancelAsync(3000, serviceToken).ConfigureAwait(false))
                    break;
                continue;
            }

            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cycleToken);
                connectCts.CancelAfter(TimeSpan.FromSeconds(15));
                await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (serviceToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (cycleToken.IsCancellationRequested)
            {
                // Reconexão manual solicitada
                continue;
            }
            catch (Exception ex)
            {
                AppDebugLog.Warn("AcessoRemoto", $"Não foi possível conectar ao relay ({ex.Message}). Tentando novamente em 3s...");
                UpdateState(
                    AcessoRemotoConnectionPhase.Reconnecting,
                    "Não foi possível conectar ao servidor. A tentar novamente...",
                    lastError: ex.Message,
                    sessionActive: false,
                    inputBlocked: false);

                if (await DelayOrCancelAsync(3000, cycleToken).ConfigureAwait(false))
                    continue;
                continue;
            }

            using var safeConn = new SafeWebSocketConnection(ws);

            try
            {
                var regMsg = new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeRegisterHost,
                    Id = requestedId,
                    HostToken = hostToken,
                    Password = string.IsNullOrWhiteSpace(currentPassword) ? null : currentPassword,
                    UnattendedPassword = string.IsNullOrWhiteSpace(currentPassword) ? null : currentPassword,
                };

                await safeConn.WriteJsonAsync(regMsg, cycleToken).ConfigureAwait(false);

                var (msgType, rawBytes) = await safeConn.ReadFullMessageAsync(cycleToken).ConfigureAwait(false);
                if (msgType != WebSocketMessageType.Text || rawBytes.Length == 0)
                {
                    throw new InvalidOperationException("Resposta inesperada do servidor relay durante o registo.");
                }

                var resp = JsonSerializer.Deserialize<AcessoRemotoMessage>(rawBytes);
                if (resp is null || !string.Equals(resp.Type, AcessoRemotoProtocol.TypeHostRegistered, StringComparison.Ordinal))
                {
                    lock (SyncRoot)
                    {
                        _requestedId = "";
                    }
                    var errMsg = resp?.Message ?? "Registo rejeitado pelo servidor.";
                    AppDebugLog.Warn("AcessoRemoto", $"Registo rejeitado pelo servidor: {errMsg}");
                    UpdateState(
                        AcessoRemotoConnectionPhase.Error,
                        $"Registo rejeitado: {errMsg}",
                        lastError: errMsg,
                        sessionActive: false,
                        inputBlocked: false);

                    await safeConn.CloseQuietlyAsync().ConfigureAwait(false);
                    await DelayOrCancelAsync(3000, cycleToken).ConfigureAwait(false);
                    continue;
                }

                lock (SyncRoot)
                {
                    _assignedId = resp.Id ?? "";
                    _requestedId = _assignedId;
                    _activeConnection = safeConn;
                    _monitors = capturer.RefreshMonitors();
                }

                AppDebugLog.Info("AcessoRemoto", $"Host registado no relay ({serverUrl}) com ID: {_assignedId}");
                UpdateState(
                    AcessoRemotoConnectionPhase.ConnectedWaiting,
                    "Conectado! Aguardando o técnico conectar...",
                    lastError: null,
                    sessionActive: false,
                    inputBlocked: false);

                await HandleActiveAgentAsync(safeConn, capturer, _fps, cycleToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (serviceToken.IsCancellationRequested)
            {
                await safeConn.CloseQuietlyAsync().ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (cycleToken.IsCancellationRequested)
            {
                await safeConn.CloseQuietlyAsync().ConfigureAwait(false);
                continue;
            }
            catch (Exception ex)
            {
                AppDebugLog.Warn("AcessoRemoto", $"Conexão com o relay interrompida: {ex.Message}");
            }
            finally
            {
                lock (SyncRoot)
                {
                    if (ReferenceEquals(_activeConnection, safeConn))
                        _activeConnection = null;
                }
                AcessoRemotoInputHandler.BlockUserInput(false);
                await safeConn.CloseQuietlyAsync().ConfigureAwait(false);
            }

            if (serviceToken.IsCancellationRequested)
                break;

            UpdateState(
                AcessoRemotoConnectionPhase.Reconnecting,
                "Conexão perdida. Reconectando em 2 segundos...",
                sessionActive: false,
                inputBlocked: false);

            await DelayOrCancelAsync(2000, cycleToken).ConfigureAwait(false);
        }

        AcessoRemotoInputHandler.BlockUserInput(false);
    }

    static async Task HandleActiveAgentAsync(
        SafeWebSocketConnection safeConn,
        AcessoRemotoScreenCapturer capturer,
        int fps,
        CancellationToken ct)
    {
        CancellationTokenSource? streamCts = null;
        Task? streamTask = null;
        var streamLock = new object();

        void StartStreaming()
        {
            EnsureClipboardListenerStarted();
            lock (streamLock)
            {
                if (streamCts is not null)
                    return;

                streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var token = streamCts.Token;

                streamTask = Task.Run(async () =>
                {
                    int intervalMs = Math.Max(16, 1000 / Math.Clamp(fps, 10, 60));
                    double lastSendMs = 0;

                    AppDebugLog.Info("AcessoRemoto", $"Transmissão de vídeo iniciada ({fps} FPS)");
                    try
                    {
                        while (!token.IsCancellationRequested && safeConn.IsOpen)
                        {
                            long frameStartTick = Stopwatch.GetTimestamp();
                            bool fileTransferActive = (Environment.TickCount64 - Volatile.Read(ref _lastFileActivityTick)) < 600;
                            int effectiveQuality = (fileTransferActive || lastSendMs > 25)
                                ? Math.Min(_quality, 52)
                                : _quality;
                            capturer.SetQuality(effectiveQuality);

                            byte[]? frameData;
                            byte[]? helperCursorPacket = null;
                            if (!AcessoRemotoSystemHelper.TryCaptureFrameViaHelper(
                                    capturer.SelectedMonitor,
                                    effectiveQuality,
                                    out _,
                                    out _,
                                    out _,
                                    out _,
                                    out frameData,
                                    out helperCursorPacket))
                            {
                                frameData = capturer.CaptureFrame(out _, out _);
                            }

                            if (frameData is not null && frameData.Length > 0)
                            {
                                long sendStartTick = Stopwatch.GetTimestamp();
                                await safeConn.WriteBinaryAsync(frameData, token).ConfigureAwait(false);
                                lastSendMs = Stopwatch.GetElapsedTime(sendStartTick).TotalMilliseconds;

                                if (helperCursorPacket != null && helperCursorPacket.Length >= 11)
                                {
                                    SendCursorPacket(helperCursorPacket);
                                }
                                else
                                {
                                    try
                                    {
                                        var (ax, ay, aw, ah) = capturer.GetActiveBounds();
                                        bool isClientActive = AcessoRemotoClientPriorityMonitor.IsClientActive;
                                        if (AcessoRemotoCursorHelper.TryGetCurrentState(ax, ay, aw, ah, isClientActive, out var cursorState))
                                        {
                                            if (AcessoRemotoCursorHelper.HasMeaningfulChange(_lastSentCursorState, cursorState))
                                            {
                                                _lastSentCursorState = cursorState;
                                                var cursorPacket = AcessoRemotoCursorHelper.CreatePacket(cursorState);
                                                safeConn.EnqueueLatestCursorPacket(cursorPacket);
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // Ignora falhas de leitura de cursor para manter streaming contínuo
                                    }
                                }
                            }

                            double totalElapsedMs = Stopwatch.GetElapsedTime(frameStartTick).TotalMilliseconds;
                            int waitMs = Math.Max(8, intervalMs - (int)totalElapsedMs);
                            if (lastSendMs > 25)
                            {
                                // Se o socket de vídeo sentiu contenção na rede (ex: durante transferência de arquivos),
                                // aguarda a drenagem do buffer TCP antes de capturar o próximo quadro para zerar o atraso visual.
                                waitMs = Math.Max(waitMs, Math.Clamp((int)(lastSendMs / 2), 12, 40));
                            }
                            await Task.Delay(waitMs, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Transmissão encerrada normalmente
                    }
                    catch (Exception ex)
                    {
                        AppDebugLog.Debug("AcessoRemoto", $"Transmissão encerrada por erro de envio: {ex.Message}");
                    }
                    finally
                    {
                        capturer.SetQuality(_quality);
                        AppDebugLog.Info("AcessoRemoto", "Transmissão de vídeo pausada.");
                    }
                }, token);
            }
        }

        void StopStreaming()
        {
            CancellationTokenSource? toCancel;
            lock (streamLock)
            {
                toCancel = streamCts;
                streamCts = null;
                streamTask = null;
            }

            if (toCancel is not null)
            {
                try { toCancel.Cancel(); } catch { }
                toCancel.Dispose();
            }
        }

        CancellationTokenSource? fileChannelCts = null;
        Task? fileChannelTask = null;
        var fileChannelLock = new object();

        var fileOpsChannel = Channel.CreateBounded<AcessoRemotoMessage>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        async Task ProcessFileOperationItemAsync(AcessoRemotoMessage msg, CancellationToken opToken)
        {
            Volatile.Write(ref _lastFileActivityTick, Environment.TickCount64);
            switch (msg.Type)
            {
                case AcessoRemotoProtocol.TypeClipboardFilesAdvertisement:
                {
                    if (msg.ClipboardFiles is { Count: > 0 } filesAd)
                    {
                        AcessoRemotoClipboard.ApplyRemoteFilesToLocalClipboard(
                            filesAd,
                            fileIdx => OpenPullStreamFromViewer(safeConn, filesAd, fileIdx, opToken));
                    }
                    break;
                }

                case AcessoRemotoProtocol.TypeClipboardFileRequest:
                {
                    int reqIndex = msg.FileIndex ?? -1;
                    string transferId = msg.TransferId ?? "";
                    if (reqIndex >= 0 && !string.IsNullOrEmpty(transferId))
                    {
                        _ = Task.Run(() => HandleClipboardFileRequestAsync(safeConn, reqIndex, transferId, opToken), opToken);
                    }
                    break;
                }

                case AcessoRemotoProtocol.TypeClipboardFileChunk:
                {
                    string transferId = msg.TransferId ?? "";
                    if (!string.IsNullOrEmpty(transferId) && ActiveClipboardPullStreams.TryGetValue(transferId, out var pullStream))
                    {
                        byte[] bytes = !string.IsNullOrEmpty(msg.ChunkB64)
                            ? Convert.FromBase64String(msg.ChunkB64)
                            : Array.Empty<byte>();
                        bool isLast = msg.IsLast == true;
                        pullStream.PushChunk(bytes, isLast, msg.Message);
                        if (isLast || !string.IsNullOrEmpty(msg.Message))
                        {
                            ActiveClipboardPullStreams.TryRemove(transferId, out _);
                        }
                    }
                    break;
                }

                case AcessoRemotoProtocol.TypeFsList:
                {
                    var (entries, drives, resolvedPath, err) = AcessoRemotoFileSystem.ListDirectory(msg.Path);
                    await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                    {
                        Type = AcessoRemotoProtocol.TypeFsListRes,
                        Path = resolvedPath,
                        Drives = drives,
                        Entries = entries,
                        Message = err,
                    }, opToken).ConfigureAwait(false);
                    break;
                }

                case AcessoRemotoProtocol.TypeFsDownloadStart:
                {
                    var remotePath = msg.RemotePath ?? "";
                    var transferId = msg.TransferId ?? "";
                    _ = Task.Run(() => HandleFileDownloadAsync(safeConn, remotePath, transferId, opToken), opToken);
                    break;
                }

                case AcessoRemotoProtocol.TypeFsUploadChunk:
                {
                    if (!string.IsNullOrEmpty(msg.RemotePath) && msg.ChunkB64 is not null)
                    {
                        try
                        {
                            var chunkBytes = msg.ChunkB64.Length > 0
                                ? Convert.FromBase64String(msg.ChunkB64)
                                : Array.Empty<byte>();
                            bool isLast = msg.IsLast == true;
                            bool autoExtract = msg.AutoExtract == true;

                            if (autoExtract)
                            {
                                var rawId = string.IsNullOrWhiteSpace(msg.TransferId)
                                    ? Path.GetFileName(msg.RemotePath.TrimEnd('\\', '/'))
                                    : msg.TransferId;
                                var safeId = new string(rawId.Where(char.IsLetterOrDigit).ToArray());
                                if (string.IsNullOrWhiteSpace(safeId))
                                    safeId = "folder";

                                var tempZipPath = Path.Combine(Path.GetTempPath(), $"sistec_extract_{safeId}.zip");
                                try
                                {
                                    AcessoRemotoFileSystem.WriteFileChunk(tempZipPath, msg.Offset ?? 0, chunkBytes, isLast);

                                    if (isLast)
                                    {
                                        var targetRemotePath = msg.RemotePath;
                                        bool shouldList = msg.SuppressList != true;
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                AcessoRemotoFileSystem.ExtractZipSafe(tempZipPath, targetRemotePath);
                                                if (shouldList && safeConn.IsOpen && !opToken.IsCancellationRequested)
                                                {
                                                    var resolvedTarget = AcessoRemotoFileSystem.ResolvePath(targetRemotePath);
                                                    var dir = Path.GetDirectoryName(resolvedTarget) ?? "";
                                                    var (entries, drives, resolvedDir, _) = AcessoRemotoFileSystem.ListDirectory(dir);
                                                    await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                                                    {
                                                        Type = AcessoRemotoProtocol.TypeFsListRes,
                                                        Path = resolvedDir,
                                                        Drives = drives,
                                                        Entries = entries,
                                                    }, opToken).ConfigureAwait(false);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                AppDebugLog.Warn("AcessoRemoto", $"Falha ao extrair pasta recebida em {targetRemotePath}: {ex.Message}");
                                            }
                                            finally
                                            {
                                                AcessoRemotoFileSystem.CloseActiveUpload(tempZipPath);
                                                try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
                                            }
                                        }, opToken);
                                    }
                                }
                                catch
                                {
                                    AcessoRemotoFileSystem.CloseActiveUpload(tempZipPath);
                                    try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
                                    throw;
                                }
                            }
                            else
                            {
                                AcessoRemotoFileSystem.WriteFileChunk(msg.RemotePath, msg.Offset ?? 0, chunkBytes, isLast);
                            }

                            if (!autoExtract && isLast && msg.SuppressList != true)
                            {
                                var resolvedTarget = AcessoRemotoFileSystem.ResolvePath(msg.RemotePath);
                                var dir = Path.GetDirectoryName(resolvedTarget) ?? "";
                                var (entries, drives, resolvedDir, _) = AcessoRemotoFileSystem.ListDirectory(dir);
                                await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                                {
                                    Type = AcessoRemotoProtocol.TypeFsListRes,
                                    Path = resolvedDir,
                                    Drives = drives,
                                    Entries = entries,
                                }, opToken).ConfigureAwait(false);
                            }
                        }
                        catch
                        {
                            // Ignora falha de chunk individual
                        }
                    }
                    break;
                }

                case AcessoRemotoProtocol.TypeFsDelete:
                {
                    if (!string.IsNullOrEmpty(msg.Path))
                    {
                        var resolvedTarget = AcessoRemotoFileSystem.ResolvePath(msg.Path);
                        try { AcessoRemotoFileSystem.DeleteFileOrDir(resolvedTarget); } catch { }
                        if (msg.SuppressList != true)
                        {
                            var dir = Path.GetDirectoryName(resolvedTarget) ?? "";
                            var (entries, drives, resolvedDir, _) = AcessoRemotoFileSystem.ListDirectory(dir);
                            await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                            {
                                Type = AcessoRemotoProtocol.TypeFsListRes,
                                Path = resolvedDir,
                                Drives = drives,
                                Entries = entries,
                            }, opToken).ConfigureAwait(false);
                        }
                    }
                    break;
                }

                case AcessoRemotoProtocol.TypeFsMkdir:
                {
                    if (!string.IsNullOrEmpty(msg.Path))
                    {
                        var resolvedTarget = AcessoRemotoFileSystem.ResolvePath(msg.Path);
                        try { AcessoRemotoFileSystem.CreateDirectory(resolvedTarget); } catch { }
                        if (msg.SuppressList != true)
                        {
                            var dir = Path.GetDirectoryName(resolvedTarget) ?? "";
                            var (entries, drives, resolvedDir, _) = AcessoRemotoFileSystem.ListDirectory(dir);
                            await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                            {
                                Type = AcessoRemotoProtocol.TypeFsListRes,
                                Path = resolvedDir,
                                Drives = drives,
                                Entries = entries,
                            }, opToken).ConfigureAwait(false);
                        }
                    }
                    break;
                }
            }
        }

        var fileOpsConsumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var fileMsg in fileOpsChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    bool active;
                    lock (SyncRoot)
                    {
                        active = _isSessionActive;
                    }
                    if (!active)
                        continue;

                    try
                    {
                        await ProcessFileOperationItemAsync(fileMsg, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }, ct);

        void StartFileChannel()
        {
            lock (fileChannelLock)
            {
                if (fileChannelCts is not null)
                {
                    bool hasOpenFileConn;
                    lock (SyncRoot)
                    {
                        hasOpenFileConn = _activeFileConnection is { IsOpen: true };
                    }
                    if (hasOpenFileConn)
                        return;

                    try { fileChannelCts.Cancel(); } catch { }
                    fileChannelCts.Dispose();
                    fileChannelCts = null;
                    fileChannelTask = null;
                }

                fileChannelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var token = fileChannelCts.Token;

                fileChannelTask = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested && safeConn.IsOpen)
                    {
                        bool wasRegistered = false;
                        string serverUrl;
                        string assignedId;
                        string unattendedPwd;
                        string? sessionToken;
                        lock (SyncRoot)
                        {
                            serverUrl = _serverUrl;
                            assignedId = _assignedId;
                            unattendedPwd = _unattendedPassword;
                            sessionToken = _activeSessionToken;
                        }

                        if (string.IsNullOrWhiteSpace(assignedId) ||
                            !Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
                        {
                            if (await DelayOrCancelAsync(1000, token).ConfigureAwait(false))
                                break;
                            continue;
                        }

                        var settings = AppSettingsStore.Load();
                        var hostToken = AppSettingsStore.EnsureRemotoHostToken(settings);

                        using var wsFile = new ClientWebSocket();
                        wsFile.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                        try
                        {
                            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                            connectCts.CancelAfter(TimeSpan.FromSeconds(10));
                            await wsFile.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch
                        {
                            if (await DelayOrCancelAsync(2000, token).ConfigureAwait(false))
                                break;
                            continue;
                        }

                        using var fileSafeConn = new SafeWebSocketConnection(wsFile);
                        try
                        {
                            var regFileMsg = new AcessoRemotoMessage
                            {
                                Type = AcessoRemotoProtocol.TypeRegisterHostFile,
                                Id = assignedId,
                                HostToken = hostToken,
                                SessionToken = string.IsNullOrWhiteSpace(sessionToken) ? null : sessionToken,
                                Password = string.IsNullOrWhiteSpace(unattendedPwd) ? null : unattendedPwd,
                                UnattendedPassword = string.IsNullOrWhiteSpace(unattendedPwd) ? null : unattendedPwd,
                            };

                            await fileSafeConn.WriteJsonAsync(regFileMsg, token).ConfigureAwait(false);

                            var (respType, respBytes) = await fileSafeConn.ReadFullMessageAsync(token).ConfigureAwait(false);
                            if (respType != WebSocketMessageType.Text || respBytes.Length == 0)
                            {
                                await fileSafeConn.CloseQuietlyAsync().ConfigureAwait(false);
                                if (await DelayOrCancelAsync(2000, token).ConfigureAwait(false))
                                    break;
                                continue;
                            }

                            var resp = JsonSerializer.Deserialize<AcessoRemotoMessage>(respBytes);
                            if (resp is null || !string.Equals(resp.Type, AcessoRemotoProtocol.TypeHostFileRegistered, StringComparison.Ordinal))
                            {
                                await fileSafeConn.CloseQuietlyAsync().ConfigureAwait(false);
                                if (await DelayOrCancelAsync(3000, token).ConfigureAwait(false))
                                    break;
                                continue;
                            }

                            wasRegistered = true;
                            lock (SyncRoot)
                            {
                                _activeFileConnection = fileSafeConn;
                            }

                            AppDebugLog.Info("AcessoRemoto", $"Canal secundário de arquivos do Host conectado (ID: {assignedId})");

                            while (!token.IsCancellationRequested && fileSafeConn.IsOpen && safeConn.IsOpen)
                            {
                                var (msgType, data) = await fileSafeConn.ReadFullMessageAsync(token).ConfigureAwait(false);
                                if (msgType == WebSocketMessageType.Close)
                                    break;

                                if (msgType == WebSocketMessageType.Text && data.Length > 0)
                                {
                                    AcessoRemotoMessage? fMsg;
                                    try
                                    {
                                        fMsg = JsonSerializer.Deserialize<AcessoRemotoMessage>(data);
                                    }
                                    catch
                                    {
                                        continue;
                                    }

                                    if (fMsg is null || string.IsNullOrEmpty(fMsg.Type))
                                        continue;

                                    bool isSessionActive;
                                    lock (SyncRoot)
                                    {
                                        isSessionActive = _isSessionActive;
                                    }
                                    if (!isSessionActive)
                                        continue;

                                    await fileOpsChannel.Writer.WriteAsync(fMsg, token).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            await fileSafeConn.CloseQuietlyAsync().ConfigureAwait(false);
                            break;
                        }
                        catch (Exception ex)
                        {
                            AppDebugLog.Debug("AcessoRemoto", $"Canal secundário de arquivos desconectou: {ex.Message}");
                        }
                        finally
                        {
                            lock (SyncRoot)
                            {
                                if (ReferenceEquals(_activeFileConnection, fileSafeConn))
                                    _activeFileConnection = null;
                            }
                            await fileSafeConn.CloseQuietlyAsync().ConfigureAwait(false);
                        }

                        if (token.IsCancellationRequested || !safeConn.IsOpen)
                            break;

                        int retryDelayMs = wasRegistered ? 250 : 1500;
                        if (await DelayOrCancelAsync(retryDelayMs, token).ConfigureAwait(false))
                            break;
                    }
                }, token);
            }
        }

        void StopFileChannel()
        {
            CancellationTokenSource? toCancel;
            lock (fileChannelLock)
            {
                toCancel = fileChannelCts;
                fileChannelCts = null;
                fileChannelTask = null;
            }

            SafeWebSocketConnection? fileConn;
            lock (SyncRoot)
            {
                fileConn = _activeFileConnection;
                _activeFileConnection = null;
                _activeSessionToken = null;
            }

            if (fileConn is not null)
            {
                _ = fileConn.CloseQuietlyAsync();
            }

            if (toCancel is not null)
            {
                try { toCancel.Cancel(); } catch { }
                toCancel.Dispose();
            }
        }

        async Task ActivateSessionAsync(string? techName, string? techAvatar, bool isUnattended)
        {
            var monitors = capturer.RefreshMonitors();
            capturer.SetSelectedMonitor(0);

            string resolvedName;
            lock (SyncRoot)
            {
                if (_isSessionActive)
                    return;

                resolvedName = !string.IsNullOrWhiteSpace(techName) && techName != "Técnico de Suporte"
                    ? techName
                    : (!string.IsNullOrWhiteSpace(_connectedTechnicianName) ? _connectedTechnicianName! : "Técnico de Suporte");
                _connectedTechnicianName = resolvedName;

                if (!string.IsNullOrWhiteSpace(techAvatar))
                    _connectedTechnicianAvatar = techAvatar;

                _sessionStartTime = DateTimeOffset.Now;
                _monitors = monitors;
                _selectedMonitorIndex = 0;
                _isSessionActive = true;
            }

            StartFileChannel();
            AcessoRemotoPromptForm.ApproveIfOpen();

            await safeConn.WriteJsonAsync(new AcessoRemotoMessage
            {
                Type = AcessoRemotoProtocol.TypeMonitorsList,
                Monitors = monitors,
                MonitorIndex = 0,
                AutoExtract = true,
            }, ct).ConfigureAwait(false);

            UpdateState(
                AcessoRemotoConnectionPhase.SessionActive,
                isUnattended
                    ? "Técnico conectado via acesso não supervisionado! Compartilhando tela."
                    : "Técnico conectado! Compartilhando tela e controle ativo.",
                lastError: null,
                sessionActive: true,
                inputBlocked: false);

            AppendSystemChat(isUnattended
                ? "Técnico conectou via acesso não supervisionado."
                : "Técnico conectou à sessão de suporte.");
            StartStreaming();
            RaiseSessionStarted();
        }

        async Task HandlePasswordAuthAsync(AcessoRemotoMessage authMsg)
        {
            bool alreadyActive;
            string expectedPwd;
            lock (SyncRoot)
            {
                alreadyActive = _isSessionActive;
                expectedPwd = !string.IsNullOrWhiteSpace(_unattendedPassword)
                    ? _unattendedPassword.Trim()
                    : (_password?.Trim() ?? "");
            }

            if (alreadyActive)
            {
                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeAuthPasswordResult,
                    Success = true,
                    Unattended = true
                }, ct).ConfigureAwait(false);
                return;
            }

            var attemptPwd = (authMsg.Password ?? "").Trim();

            if (string.IsNullOrWhiteSpace(expectedPwd) || !string.Equals(attemptPwd, expectedPwd, StringComparison.Ordinal))
            {
                var loadedSettings = AppSettingsStore.Load();
                var loadedPwd = (loadedSettings.RemotoUnattendedPassword ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(loadedPwd))
                {
                    expectedPwd = loadedPwd;
                    lock (SyncRoot)
                    {
                        _unattendedPassword = loadedPwd;
                        _password = loadedPwd;
                    }
                }
            }

            if (!string.IsNullOrEmpty(attemptPwd) && !string.Equals(attemptPwd, expectedPwd, StringComparison.Ordinal))
            {
                await TryRefreshPasswordFromGlpiWithoutReconnectAsync().ConfigureAwait(false);
                lock (SyncRoot)
                {
                    expectedPwd = !string.IsNullOrWhiteSpace(_unattendedPassword)
                        ? _unattendedPassword.Trim()
                        : (_password?.Trim() ?? "");
                }
            }

            if (string.IsNullOrEmpty(attemptPwd) || string.IsNullOrEmpty(expectedPwd))
            {
                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeAuthPasswordResult,
                    Success = false,
                    Message = string.IsNullOrEmpty(expectedPwd)
                        ? "Este computador não possui senha de acesso configurada. Aguarde o cliente aceitar."
                        : "Informe a senha de acesso."
                }, ct).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(attemptPwd, expectedPwd, StringComparison.Ordinal))
            {
                AppDebugLog.Warn("AcessoRemoto", "Tentativa de autenticação manual por senha recusada (senha incorreta).");
                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeAuthPasswordResult,
                    Success = false,
                    Message = "Senha incorreta. Verifique a senha e tente novamente."
                }, ct).ConfigureAwait(false);
                return;
            }

            AppDebugLog.Info("AcessoRemoto", "Sessão autorizada manualmente via senha durante espera de aceitação.");
            var tName = !string.IsNullOrWhiteSpace(authMsg.TechnicianName)
                ? authMsg.TechnicianName
                : (!string.IsNullOrWhiteSpace(authMsg.Sender) ? authMsg.Sender : "Técnico de Suporte");
            var tAvatar = authMsg.TechnicianAvatar;

            await ActivateSessionAsync(tName, tAvatar, isUnattended: true).ConfigureAwait(false);

            await safeConn.WriteJsonAsync(new AcessoRemotoMessage
            {
                Type = AcessoRemotoProtocol.TypeAuthPasswordResult,
                Success = true,
                Unattended = true
            }, ct).ConfigureAwait(false);
        }

        try
        {
            while (!ct.IsCancellationRequested && safeConn.IsOpen)
            {
                var (msgType, data) = await safeConn.ReadFullMessageAsync(ct).ConfigureAwait(false);
                if (msgType == WebSocketMessageType.Close)
                    break;

                if (msgType == WebSocketMessageType.Binary)
                {
                    bool active;
                    lock (SyncRoot)
                    {
                        active = _isSessionActive;
                    }
                    if (active)
                    {
                        if (AcessoRemotoClientPriorityMonitor.IsClientActive)
                            continue;

                        var (ax, ay, aw, ah) = capturer.GetActiveBounds();
                        AcessoRemotoInputHandler.HandleInputPacket(data, ax, ay, aw, ah);
                    }
                }
                else if (msgType == WebSocketMessageType.Text && data.Length > 0)
                {
                    AcessoRemotoMessage? msg;
                    try
                    {
                        msg = JsonSerializer.Deserialize<AcessoRemotoMessage>(data);
                    }
                    catch
                    {
                        continue;
                    }

                    if (msg is null || string.IsNullOrEmpty(msg.Type))
                        continue;

                    // Proteção de autorização: bloqueia comandos de sessão/controle enquanto a sessão não estiver ativa (consentimento pendente)
                    if (msg.Type is not (
                        AcessoRemotoProtocol.TypeConfigUpdated or
                        AcessoRemotoProtocol.TypeTechnicianInfo or
                        AcessoRemotoProtocol.TypeAuthPassword or
                        AcessoRemotoProtocol.TypeSessionStarted or
                        AcessoRemotoProtocol.TypeSessionEnded))
                    {
                        bool isSessionActive;
                        lock (SyncRoot)
                        {
                            isSessionActive = _isSessionActive;
                        }

                        if (!isSessionActive)
                        {
                            AppDebugLog.Warn("AcessoRemoto", $"Comando '{msg.Type}' bloqueado porque a sessão ainda não foi autorizada.");
                            continue;
                        }
                    }

                    switch (msg.Type)
                    {
                        case AcessoRemotoProtocol.TypeConfigUpdated:
                        {
                            var isUnattended = msg.Unattended == true;
                            var pwd = msg.UnattendedPassword ?? "";
                            lock (SyncRoot)
                            {
                                _unattendedPassword = isUnattended ? pwd : "";
                                _password = _unattendedPassword;
                            }

                            var settings = AppSettingsStore.Load();
                            settings.RemotoUnattendedPassword = _unattendedPassword;
                            if (!string.IsNullOrWhiteSpace(msg.HostToken) && msg.HostToken != settings.RemotoHostToken)
                            {
                                settings.RemotoHostToken = msg.HostToken;
                            }
                            AppSettingsStore.Save(settings);

                            AppDebugLog.Info("AcessoRemoto", $"Configuração não supervisionada atualizada em tempo real via PUSH: Ativo = {isUnattended}");
                            RaiseStateChanged();
                            break;
                        }

                        case AcessoRemotoProtocol.TypeTechnicianInfo:
                        {
                            var tName = msg.TechnicianName ?? msg.Sender ?? "";
                            var tAvatar = msg.TechnicianAvatar;
                            lock (SyncRoot)
                            {
                                _connectedTechnicianName = !string.IsNullOrWhiteSpace(tName) ? tName : "Técnico de Suporte";
                                _connectedTechnicianAvatar = !string.IsNullOrWhiteSpace(tAvatar) ? tAvatar : null;
                            }
                            AcessoRemotoPromptForm.UpdateTechnicianInfo(tName, tAvatar);
                            RaiseStateChanged();

                            if (!string.IsNullOrWhiteSpace(msg.Password))
                            {
                                await HandlePasswordAuthAsync(msg).ConfigureAwait(false);
                            }
                            break;
                        }

                        case AcessoRemotoProtocol.TypeAuthPassword:
                        {
                            await HandlePasswordAuthAsync(msg).ConfigureAwait(false);
                            break;
                        }

                        case AcessoRemotoProtocol.TypeSessionStarted:
                        {
                            var techName = !string.IsNullOrWhiteSpace(msg.TechnicianName)
                                ? msg.TechnicianName
                                : (!string.IsNullOrWhiteSpace(msg.Sender) ? msg.Sender : "Técnico de Suporte");
                            var techAvatar = msg.TechnicianAvatar;

                            lock (SyncRoot)
                            {
                                if (!string.IsNullOrWhiteSpace(msg.SessionToken))
                                    _activeSessionToken = msg.SessionToken;

                                if (!string.IsNullOrWhiteSpace(_connectedTechnicianName))
                                    techName = _connectedTechnicianName!;
                                else
                                    _connectedTechnicianName = techName;

                                if (!string.IsNullOrWhiteSpace(_connectedTechnicianAvatar))
                                    techAvatar = _connectedTechnicianAvatar;
                                else if (!string.IsNullOrWhiteSpace(techAvatar))
                                    _connectedTechnicianAvatar = techAvatar;
                                else
                                    _connectedTechnicianAvatar = null;
                            }

                            // Pré-conecta o canal secundário de arquivos para estar pronto imediatamente
                            StartFileChannel();

                            if (msg.Unattended == true)
                            {
                                // Acesso não supervisionado autenticado via senha fixa:
                                // Não exibe prompt de consentimento, inicia imediatamente.
                                AppDebugLog.Info("AcessoRemoto", "Sessão não supervisionada autorizada por senha fixa.");
                                await ActivateSessionAsync(techName, techAvatar, isUnattended: true).ConfigureAwait(false);
                                break;
                            }

                            UpdateState(
                                AcessoRemotoConnectionPhase.Connecting,
                                "Solicitação de acesso remoto recebida. Aguardando autorização...",
                                lastError: null,
                                sessionActive: false,
                                inputBlocked: false);

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    bool accepted = await AcessoRemotoPromptForm.RequestConsentAsync(techName, techAvatar, timeoutSeconds: 45).ConfigureAwait(false);
                                    if (ct.IsCancellationRequested || !safeConn.IsOpen)
                                        return;

                                    lock (SyncRoot)
                                    {
                                        if (_isSessionActive)
                                            return;
                                    }

                                    if (!accepted)
                                    {
                                        StopFileChannel();
                                        AppDebugLog.Info("AcessoRemoto", "Sessão de acesso remoto recusada pelo utilizador local.");
                                        await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                                        {
                                            Type = AcessoRemotoProtocol.TypeSessionEnded,
                                            Message = "A solicitação de acesso remoto foi recusada pelo utilizador."
                                        }, ct).ConfigureAwait(false);

                                        UpdateState(
                                            AcessoRemotoConnectionPhase.ConnectedWaiting,
                                            "Acesso remoto recusado pelo utilizador.",
                                            lastError: null,
                                            sessionActive: false,
                                            inputBlocked: false);

                                        AppendSystemChat("A solicitação de acesso remoto foi recusada.");
                                        return;
                                    }

                                    await ActivateSessionAsync(techName, techAvatar, isUnattended: false).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    AppDebugLog.Warn("AcessoRemoto", $"Erro ao processar consentimento: {ex.Message}");
                                }
                            }, ct);
                            break;
                        }

                        case AcessoRemotoProtocol.TypeSessionEnded:
                        {
                            lock (SyncRoot)
                            {
                                _connectedTechnicianName = null;
                                _connectedTechnicianAvatar = null;
                                _sessionStartTime = null;
                            }
                            AcessoRemotoPromptForm.CloseIfOpen();
                            AcessoRemotoInputHandler.BlockUserInput(false);
                            AcessoRemotoFileSystem.CloseAllActiveUploads();
                            foreach (var kv in ActiveClipboardPullStreams)
                            {
                                if (ActiveClipboardPullStreams.TryRemove(kv.Key, out var ps))
                                    ps.PushChunk(Array.Empty<byte>(), true, "Sessão remota encerrada durante a transferência.");
                            }
                            StopFileChannel();
                            StopStreaming();

                            UpdateState(
                                AcessoRemotoConnectionPhase.ConnectedWaiting,
                                "Técnico desconectou. Aguardando próxima conexão...",
                                lastError: null,
                                sessionActive: false,
                                inputBlocked: false);

                            AppendSystemChat("Técnico desconectou da sessão.");
                            RaiseSessionEnded();
                            break;
                        }

                        case AcessoRemotoProtocol.TypeSelectMonitor:
                        {
                            int idx = msg.MonitorIndex ?? 0;
                            capturer.SetSelectedMonitor(idx);
                            lock (SyncRoot)
                            {
                                _selectedMonitorIndex = idx;
                            }
                            RaiseStateChanged();
                            break;
                        }

                        case AcessoRemotoProtocol.TypeBlockInput:
                        {
                            bool block = msg.Blocked ?? false;
                            var (success, errStr) = AcessoRemotoInputHandler.BlockUserInput(block);
                            lock (SyncRoot)
                            {
                                _isInputBlocked = block && success;
                            }
                            AcessoRemotoClientPriorityMonitor.SetUserBlockedLocally(_isInputBlocked);
                            RaiseStateChanged();

                            await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                            {
                                Type = AcessoRemotoProtocol.TypeBlockInputStatus,
                                Blocked = block,
                                Success = success,
                                Message = string.IsNullOrEmpty(errStr) ? null : errStr,
                            }, ct).ConfigureAwait(false);
                            break;
                        }

                        case AcessoRemotoProtocol.TypeQuickAction:
                        {
                            AcessoRemotoInputHandler.ExecuteQuickAction(msg.Action);
                            break;
                        }

                        case AcessoRemotoProtocol.TypeChatMessage:
                        {
                            var text = msg.Text ?? "";
                            if (AcessoRemotoClipboard.IsAutomatedFileTransferChatMessage(text))
                                break;

                            var sender = string.IsNullOrWhiteSpace(msg.Sender) ? "Técnico" : msg.Sender;
                            var time = string.IsNullOrWhiteSpace(msg.Time) ? DateTime.Now.ToString("HH:mm") : msg.Time;
                            AddChatEntry(new AcessoRemotoChatEntry(
                                Sender: sender,
                                Text: text,
                                Time: time,
                                IsFromTechnician: true,
                                IsSystemEvent: false));

                            AcessoRemotoChatForm.ShowOrActivateChat();
                            break;
                        }

                        case AcessoRemotoProtocol.TypeClipboardText:
                        {
                            if (!string.IsNullOrEmpty(msg.Text))
                            {
                                AcessoRemotoClipboard.ApplyRemoteTextToLocalClipboard(msg.Text);
                            }
                            break;
                        }

                        case AcessoRemotoProtocol.TypeClipboardFilesAdvertisement:
                        case AcessoRemotoProtocol.TypeClipboardFileRequest:
                        case AcessoRemotoProtocol.TypeClipboardFileChunk:
                        case AcessoRemotoProtocol.TypeFsList:
                        case AcessoRemotoProtocol.TypeFsDownloadStart:
                        case AcessoRemotoProtocol.TypeFsUploadChunk:
                        case AcessoRemotoProtocol.TypeFsDelete:
                        case AcessoRemotoProtocol.TypeFsMkdir:
                        {
                            if (!fileOpsChannel.Writer.TryWrite(msg))
                            {
                                await fileOpsChannel.Writer.WriteAsync(msg, ct).ConfigureAwait(false);
                            }
                            break;
                        }
                    }
                }
            }
        }
        finally
        {
            fileOpsChannel.Writer.TryComplete();
            _currentCapturer = null;
            _lastSentCursorState = null;
            AcessoRemotoFileSystem.CloseAllActiveUploads();
            foreach (var kv in ActiveClipboardPullStreams)
            {
                if (ActiveClipboardPullStreams.TryRemove(kv.Key, out var ps))
                    ps.PushChunk(Array.Empty<byte>(), true, "Sessão remota encerrada durante a transferência.");
            }
            StopFileChannel();
            StopStreaming();
            AcessoRemotoInputHandler.BlockUserInput(false);
            AcessoRemotoClientPriorityMonitor.SetUserBlockedLocally(false);
            RaiseSessionEnded();
        }
    }

    static Stream? OpenPullStreamFromViewer(
        SafeWebSocketConnection safeConn,
        IReadOnlyList<AcessoRemotoClipboardFileItem> filesAd,
        int fileIndex,
        CancellationToken ct)
    {
        if (fileIndex < 0 || fileIndex >= filesAd.Count)
            return null;

        var item = filesAd[fileIndex];
        if (item.IsDir)
            return null;

        string transferId = Guid.NewGuid().ToString("N");
        var pullStream = new RemoteClipboardPullStream(item.Size);
        ActiveClipboardPullStreams[transferId] = pullStream;

        _ = Task.Run(async () =>
        {
            try
            {
                await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeClipboardFileRequest,
                    FileIndex = fileIndex,
                    TransferId = transferId
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ActiveClipboardPullStreams.TryRemove(transferId, out _);
                pullStream.PushChunk(Array.Empty<byte>(), true, ex.Message);
            }
        }, ct);

        return pullStream;
    }

    static async Task HandleClipboardFileRequestAsync(
        SafeWebSocketConnection safeConn,
        int fileIndex,
        string transferId,
        CancellationToken ct)
    {
        AcessoRemotoClipboardFileItem? item;
        lock (SyncRoot)
        {
            item = _hostAdvertisedFiles.FirstOrDefault(f => f.Index == fileIndex);
        }

        if (item is null || string.IsNullOrEmpty(item.FullPath) || !File.Exists(item.FullPath))
        {
            try
            {
                await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                    TransferId = transferId,
                    FileIndex = fileIndex,
                    Message = "Arquivo não encontrado na área de transferência remota.",
                    IsLast = true
                }, ct).ConfigureAwait(false);
            }
            catch { }
            return;
        }

        const int chunkSize = AcessoRemotoProtocol.DefaultFileChunkSize;
        long offset = 0;

        try
        {
            using var fs = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, FileOptions.SequentialScan);
            long totalSize = fs.Length;
            byte[] buffer = new byte[chunkSize];

            if (totalSize == 0)
            {
                Volatile.Write(ref _lastFileActivityTick, Environment.TickCount64);
                await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                    TransferId = transferId,
                    FileIndex = fileIndex,
                    Offset = 0,
                    TotalSize = 0,
                    ChunkB64 = "",
                    IsLast = true
                }, ct).ConfigureAwait(false);
                return;
            }

            while (!ct.IsCancellationRequested && safeConn.IsOpen)
            {
                int read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), ct).ConfigureAwait(false);
                bool isLast = read <= 0 || (offset + read) >= totalSize;
                string b64 = read > 0 ? Convert.ToBase64String(buffer, 0, read) : "";

                Volatile.Write(ref _lastFileActivityTick, Environment.TickCount64);
                long sendStart = Stopwatch.GetTimestamp();
                bool usedFileChannel = await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                    TransferId = transferId,
                    FileIndex = fileIndex,
                    Offset = offset,
                    TotalSize = totalSize,
                    ChunkB64 = b64,
                    IsLast = isLast
                }, ct).ConfigureAwait(false);
                double sendMs = Stopwatch.GetElapsedTime(sendStart).TotalMilliseconds;

                offset += Math.Max(0, read);
                if (isLast)
                    break;

                int paceMs = usedFileChannel
                    ? (sendMs > 12 ? Math.Clamp((int)(sendMs * 2 / 3), 8, 45) : 6)
                    : Math.Clamp(Math.Max(14, (int)sendMs), 14, 60);
                await Task.Delay(paceMs, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                    TransferId = transferId,
                    FileIndex = fileIndex,
                    Message = ex.Message,
                    IsLast = true
                }, ct).ConfigureAwait(false);
            }
            catch { }
        }
    }

    static async Task HandleFileDownloadAsync(
        SafeWebSocketConnection safeConn,
        string remotePath,
        string transferId,
        CancellationToken ct)
    {
        string? tempZipToCleanup = null;
        string resolvedRemotePath = AcessoRemotoFileSystem.ResolvePath(remotePath);
        string actualFilePath = resolvedRemotePath;
        string fileName = Path.GetFileName(resolvedRemotePath.TrimEnd('\\', '/'));
        const int chunkSize = AcessoRemotoProtocol.DefaultFileChunkSize;
        long offset = 0;

        try
        {
            if (Directory.Exists(resolvedRemotePath))
            {
                // Compacta a pasta remotamente para transferi-la completa em formato .zip
                string zipName = $"{fileName}.zip";
                tempZipToCleanup = Path.Combine(Path.GetTempPath(), $"sistec_dir_{Guid.NewGuid():N}_{zipName}");
                System.IO.Compression.ZipFile.CreateFromDirectory(resolvedRemotePath, tempZipToCleanup, System.IO.Compression.CompressionLevel.Fastest, includeBaseDirectory: true);
                actualFilePath = tempZipToCleanup;
                fileName = zipName;
            }
            else if (!File.Exists(resolvedRemotePath))
            {
                throw new FileNotFoundException($"Arquivo ou pasta não encontrada: {resolvedRemotePath}");
            }

            using var fs = new FileStream(actualFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, FileOptions.SequentialScan);
            long totalSize = fs.Length;
            byte[] buffer = new byte[chunkSize];

            if (totalSize == 0)
            {
                Volatile.Write(ref _lastFileActivityTick, Environment.TickCount64);
                await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeFsDownloadChunk,
                    TransferId = transferId,
                    FileName = fileName,
                    Offset = 0,
                    TotalSize = 0,
                    ChunkB64 = "",
                    IsLast = true,
                }, ct).ConfigureAwait(false);
                return;
            }

            while (!ct.IsCancellationRequested && safeConn.IsOpen)
            {
                int read;
                try
                {
                    read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    try
                    {
                        await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                        {
                            Type = AcessoRemotoProtocol.TypeFsDownloadChunk,
                            TransferId = transferId,
                            Message = ex.Message,
                            IsLast = true,
                        }, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignorado
                    }
                    return;
                }

                bool isLast = read <= 0 || (offset + read) >= totalSize;
                string b64 = read > 0 ? Convert.ToBase64String(buffer, 0, read) : "";
                bool usedFileChannel;
                double sendMs;
                try
                {
                    Volatile.Write(ref _lastFileActivityTick, Environment.TickCount64);
                    long sendStart = Stopwatch.GetTimestamp();
                    usedFileChannel = await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                    {
                        Type = AcessoRemotoProtocol.TypeFsDownloadChunk,
                        TransferId = transferId,
                        FileName = fileName,
                        Offset = offset,
                        TotalSize = totalSize,
                        ChunkB64 = b64,
                        IsLast = isLast,
                    }, ct).ConfigureAwait(false);
                    sendMs = Stopwatch.GetElapsedTime(sendStart).TotalMilliseconds;
                }
                catch
                {
                    return;
                }

                offset += Math.Max(0, read);
                if (isLast)
                    return;

                int paceMs = usedFileChannel
                    ? (sendMs > 12 ? Math.Clamp((int)(sendMs * 2 / 3), 8, 45) : 6)
                    : Math.Clamp(Math.Max(14, (int)sendMs), 14, 60);
                await Task.Delay(paceMs, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await WriteFileOrFallbackJsonAsync(safeConn, new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeFsDownloadChunk,
                    TransferId = transferId,
                    Message = ex.Message,
                    IsLast = true,
                }, ct).ConfigureAwait(false);
            }
            catch { }
        }
        finally
        {
            if (tempZipToCleanup != null)
            {
                try { if (File.Exists(tempZipToCleanup)) File.Delete(tempZipToCleanup); } catch { }
            }
        }
    }

    static async Task<bool> DelayOrCancelAsync(int milliseconds, CancellationToken ct)
    {
        try
        {
            await Task.Delay(milliseconds, ct).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    sealed class SafeWebSocketConnection : IDisposable
    {
        readonly ClientWebSocket _ws;
        readonly SemaphoreSlim _writeLock = new(1, 1);
        readonly byte[] _readBuffer = new byte[64 * 1024];
        readonly MemoryStream _readStream = new(64 * 1024);
        int _pendingHighPriorityWrites;
        byte[]? _latestCursorPacket;

        public SafeWebSocketConnection(ClientWebSocket ws)
        {
            _ws = ws;
        }

        public bool IsOpen => _ws.State == WebSocketState.Open;

        public void EnqueueLatestCursorPacket(byte[] packet)
        {
            Interlocked.Exchange(ref _latestCursorPacket, packet);
            if (_writeLock.CurrentCount > 0)
            {
                _ = FlushPendingCursorAsync();
            }
        }

        async Task FlushPendingCursorAsync()
        {
            if (!await _writeLock.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                while (true)
                {
                    var cursor = Interlocked.Exchange(ref _latestCursorPacket, null);
                    if (cursor is null || _ws.State != WebSocketState.Open)
                        break;

                    await _ws.SendAsync(
                        new ArraySegment<byte>(cursor),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                _writeLock.Release();
            }
        }

        async Task FlushPendingCursorUnderLockAsync(CancellationToken ct)
        {
            var cursor = Interlocked.Exchange(ref _latestCursorPacket, null);
            if (cursor is not null && _ws.State == WebSocketState.Open)
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(cursor),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    ct).ConfigureAwait(false);
            }
        }

        public async Task WriteBinaryAsync(byte[] data, CancellationToken ct)
        {
            Interlocked.Increment(ref _pendingHighPriorityWrites);
            try
            {
                await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await FlushPendingCursorUnderLockAsync(ct).ConfigureAwait(false);
                    if (_ws.State == WebSocketState.Open)
                    {
                        await _ws.SendAsync(
                            new ArraySegment<byte>(data),
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            ct).ConfigureAwait(false);
                    }
                    await FlushPendingCursorUnderLockAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pendingHighPriorityWrites);
            }
        }

        public async Task WriteJsonAsync(AcessoRemotoMessage message, CancellationToken ct)
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(message);
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await FlushPendingCursorUnderLockAsync(ct).ConfigureAwait(false);
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(jsonBytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
                }
                await FlushPendingCursorUnderLockAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task WriteLowPriorityJsonAsync(AcessoRemotoMessage message, CancellationToken ct)
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(message);
            int yields = 0;
            while (Volatile.Read(ref _pendingHighPriorityWrites) > 0 && yields < 12 && !ct.IsCancellationRequested)
            {
                await Task.Delay(4, ct).ConfigureAwait(false);
                yields++;
            }

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await FlushPendingCursorUnderLockAsync(ct).ConfigureAwait(false);
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(jsonBytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
                }
                await FlushPendingCursorUnderLockAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<(WebSocketMessageType MessageType, byte[] Data)> ReadFullMessageAsync(CancellationToken ct)
        {
            var firstResult = await _ws.ReceiveAsync(new ArraySegment<byte>(_readBuffer), ct).ConfigureAwait(false);
            if (firstResult.MessageType == WebSocketMessageType.Close)
                return (WebSocketMessageType.Close, Array.Empty<byte>());

            if (firstResult.EndOfMessage)
            {
                if (firstResult.Count <= 0)
                    return (firstResult.MessageType, Array.Empty<byte>());

                var singleData = new byte[firstResult.Count];
                Buffer.BlockCopy(_readBuffer, 0, singleData, 0, firstResult.Count);
                return (firstResult.MessageType, singleData);
            }

            _readStream.Position = 0;
            _readStream.SetLength(0);
            if (firstResult.Count > 0)
                _readStream.Write(_readBuffer, 0, firstResult.Count);

            while (true)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(_readBuffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return (WebSocketMessageType.Close, Array.Empty<byte>());

                if (result.Count > 0)
                    _readStream.Write(_readBuffer, 0, result.Count);

                if (result.EndOfMessage)
                    return (result.MessageType, _readStream.ToArray());
            }
        }

        public async Task CloseQuietlyAsync()
        {
            try
            {
                if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignorado
            }
        }

        public void Dispose()
        {
            _readStream.Dispose();
            _writeLock.Dispose();
            _ws.Dispose();
        }
    }
}

