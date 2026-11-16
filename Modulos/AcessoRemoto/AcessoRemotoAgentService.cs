using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
                _ = conn.WriteBinaryAsync(packet, CancellationToken.None);
            }
        }
        else
        {
            _ = conn.WriteBinaryAsync(packet, CancellationToken.None);
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
                _ = conn.WriteBinaryAsync(packet, CancellationToken.None);
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
        CancellationTokenSource? cts;
        CancellationTokenSource? cycleCts;
        lock (SyncRoot)
        {
            conn = _activeConnection;
            cts = _serviceCts;
            cycleCts = _connectionCycleCts;
            _serviceCts = null;
            _connectionCycleCts = null;
            _activeConnection = null;
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

                _ = conn.WriteJsonAsync(new AcessoRemotoMessage
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
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

                    AppDebugLog.Info("AcessoRemoto", $"Transmissão de vídeo iniciada ({fps} FPS)");
                    try
                    {
                        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                        {
                            byte[]? frameData;
                            byte[]? helperCursorPacket = null;
                            if (!AcessoRemotoSystemHelper.TryCaptureFrameViaHelper(
                                    capturer.SelectedMonitor,
                                    _quality,
                                    out _,
                                    out _,
                                    out _,
                                    out _,
                                    out frameData,
                                    out helperCursorPacket))
                            {
                                frameData = capturer.CaptureFrame(out _, out _);
                            }

                            if (frameData is null || frameData.Length == 0)
                                continue;

                            await safeConn.WriteBinaryAsync(frameData, token).ConfigureAwait(false);

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
                                            await safeConn.WriteBinaryAsync(cursorPacket, token).ConfigureAwait(false);
                                        }
                                    }
                                }
                                catch
                                {
                                    // Ignora falhas de leitura de cursor para manter streaming contínuo
                                }
                            }
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

            AcessoRemotoPromptForm.ApproveIfOpen();

            await safeConn.WriteJsonAsync(new AcessoRemotoMessage
            {
                Type = AcessoRemotoProtocol.TypeMonitorsList,
                Monitors = monitors,
                MonitorIndex = 0,
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
                            foreach (var kv in ActiveClipboardPullStreams)
                            {
                                if (ActiveClipboardPullStreams.TryRemove(kv.Key, out var ps))
                                    ps.PushChunk(Array.Empty<byte>(), true, "Sessão remota encerrada durante a transferência.");
                            }
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
                        {
                            if (msg.ClipboardFiles is { Count: > 0 } filesAd)
                            {
                                AcessoRemotoClipboard.ApplyRemoteFilesToLocalClipboard(
                                    filesAd,
                                    fileIdx => OpenPullStreamFromViewer(safeConn, filesAd, fileIdx, ct));
                            }
                            break;
                        }

                        case AcessoRemotoProtocol.TypeClipboardFileRequest:
                        {
                            int reqIndex = msg.FileIndex ?? -1;
                            string transferId = msg.TransferId ?? "";
                            if (reqIndex >= 0 && !string.IsNullOrEmpty(transferId))
                            {
                                _ = Task.Run(() => HandleClipboardFileRequestAsync(safeConn, reqIndex, transferId, ct), ct);
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
                            var (entries, drives, err) = AcessoRemotoFileSystem.ListDirectory(msg.Path);
                            await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                            {
                                Type = AcessoRemotoProtocol.TypeFsListRes,
                                Path = msg.Path ?? "",
                                Drives = drives,
                                Entries = entries,
                                Message = err,
                            }, ct).ConfigureAwait(false);
                            break;
                        }

                        case AcessoRemotoProtocol.TypeFsDownloadStart:
                        {
                            var remotePath = msg.RemotePath ?? "";
                            var transferId = msg.TransferId ?? "";
                            _ = Task.Run(() => HandleFileDownloadAsync(safeConn, remotePath, transferId, ct), ct);
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
                                    AcessoRemotoFileSystem.WriteFileChunk(msg.RemotePath, msg.Offset ?? 0, chunkBytes);
                                    if (msg.IsLast == true)
                                    {
                                        var dir = Path.GetDirectoryName(msg.RemotePath) ?? "";
                                        var (entries, drives, _) = AcessoRemotoFileSystem.ListDirectory(dir);
                                        await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                                        {
                                            Type = AcessoRemotoProtocol.TypeFsListRes,
                                            Path = dir,
                                            Drives = drives,
                                            Entries = entries,
                                        }, ct).ConfigureAwait(false);
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
                                try { AcessoRemotoFileSystem.DeleteFileOrDir(msg.Path); } catch { }
                                var dir = Path.GetDirectoryName(msg.Path) ?? "";
                                var (entries, drives, _) = AcessoRemotoFileSystem.ListDirectory(dir);
                                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                                {
                                    Type = AcessoRemotoProtocol.TypeFsListRes,
                                    Path = dir,
                                    Drives = drives,
                                    Entries = entries,
                                }, ct).ConfigureAwait(false);
                            }
                            break;
                        }

                        case AcessoRemotoProtocol.TypeFsMkdir:
                        {
                            if (!string.IsNullOrEmpty(msg.Path))
                            {
                                try { AcessoRemotoFileSystem.CreateDirectory(msg.Path); } catch { }
                                var dir = Path.GetDirectoryName(msg.Path) ?? "";
                                var (entries, drives, _) = AcessoRemotoFileSystem.ListDirectory(dir);
                                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                                {
                                    Type = AcessoRemotoProtocol.TypeFsListRes,
                                    Path = dir,
                                    Drives = drives,
                                    Entries = entries,
                                }, ct).ConfigureAwait(false);
                            }
                            break;
                        }
                    }
                }
            }
        }
        finally
        {
            _currentCapturer = null;
            _lastSentCursorState = null;
            foreach (var kv in ActiveClipboardPullStreams)
            {
                if (ActiveClipboardPullStreams.TryRemove(kv.Key, out var ps))
                    ps.PushChunk(Array.Empty<byte>(), true, "Sessão remota encerrada durante a transferência.");
            }
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
                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
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
                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
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

        const int chunkSize = 64 * 1024;
        long offset = 0;

        while (!ct.IsCancellationRequested && safeConn.IsOpen)
        {
            try
            {
                var (chunk, totalSize, isLast) = AcessoRemotoFileSystem.ReadFileChunk(item.FullPath, offset, chunkSize);
                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                {
                    Type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                    TransferId = transferId,
                    FileIndex = fileIndex,
                    Offset = offset,
                    TotalSize = totalSize,
                    ChunkB64 = Convert.ToBase64String(chunk),
                    IsLast = isLast
                }, ct).ConfigureAwait(false);

                offset += chunk.Length;
                if (isLast)
                    break;
            }
            catch (Exception ex)
            {
                try
                {
                    await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                    {
                        Type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                        TransferId = transferId,
                        FileIndex = fileIndex,
                        Message = ex.Message,
                        IsLast = true
                    }, ct).ConfigureAwait(false);
                }
                catch { }
                break;
            }
        }
    }

    static async Task HandleFileDownloadAsync(
        SafeWebSocketConnection safeConn,
        string remotePath,
        string transferId,
        CancellationToken ct)
    {
        string? tempZipToCleanup = null;
        string actualFilePath = remotePath;
        string fileName = Path.GetFileName(remotePath.TrimEnd('\\', '/'));
        const int chunkSize = 64 * 1024;
        long offset = 0;

        try
        {
            if (Directory.Exists(remotePath))
            {
                // Compacta a pasta remotamente para transferi-la completa em formato .zip
                string zipName = $"{fileName}.zip";
                tempZipToCleanup = Path.Combine(Path.GetTempPath(), $"sistec_dir_{Guid.NewGuid():N}_{zipName}");
                System.IO.Compression.ZipFile.CreateFromDirectory(remotePath, tempZipToCleanup, System.IO.Compression.CompressionLevel.Fastest, includeBaseDirectory: true);
                actualFilePath = tempZipToCleanup;
                fileName = zipName;
            }
            else if (!File.Exists(remotePath))
            {
                throw new FileNotFoundException($"Arquivo ou pasta não encontrada: {remotePath}");
            }

            while (!ct.IsCancellationRequested && safeConn.IsOpen)
            {
                byte[] chunk;
                long totalSize;
                bool isLast;

                try
                {
                    (chunk, totalSize, isLast) = AcessoRemotoFileSystem.ReadFileChunk(actualFilePath, offset, chunkSize);
                }
                catch (Exception ex)
                {
                    try
                    {
                        await safeConn.WriteJsonAsync(new AcessoRemotoMessage
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

                string b64 = Convert.ToBase64String(chunk);
                try
                {
                    await safeConn.WriteJsonAsync(new AcessoRemotoMessage
                    {
                        Type = AcessoRemotoProtocol.TypeFsDownloadChunk,
                        TransferId = transferId,
                        FileName = fileName,
                        Offset = offset,
                        TotalSize = totalSize,
                        ChunkB64 = b64,
                        IsLast = isLast,
                    }, ct).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                offset += chunk.Length;
                if (isLast)
                    return;
            }
        }
        catch (Exception ex)
        {
            try
            {
                await safeConn.WriteJsonAsync(new AcessoRemotoMessage
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

        public SafeWebSocketConnection(ClientWebSocket ws)
        {
            _ws = ws;
        }

        public bool IsOpen => _ws.State == WebSocketState.Open;

        public async Task WriteBinaryAsync(byte[] data, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(data),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task WriteJsonAsync(AcessoRemotoMessage message, CancellationToken ct)
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(message);
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(jsonBytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<(WebSocketMessageType MessageType, byte[] Data)> ReadFullMessageAsync(CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            using var ms = new MemoryStream();

            while (true)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return (WebSocketMessageType.Close, Array.Empty<byte>());

                if (result.Count > 0)
                    ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                    return (result.MessageType, ms.ToArray());
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
            _writeLock.Dispose();
            _ws.Dispose();
        }
    }
}

