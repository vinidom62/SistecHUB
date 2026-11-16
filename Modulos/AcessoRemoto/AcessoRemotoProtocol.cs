using System.Text.Json.Serialization;

namespace SistecHub.Modulos.AcessoRemoto;

internal static class AcessoRemotoProtocol
{
    public const byte OpFrame = 0x01; // Host -> Viewer (Screen Frame)
    public const byte OpInput = 0x02; // Viewer -> Host (Input Event)
    public const byte OpCursor = 0x03; // Host -> Viewer (Cursor Update: shape, coords, client active)

    public const byte InputMouseMove = 0x01;
    public const byte InputMouseDown = 0x02;
    public const byte InputMouseUp = 0x03;
    public const byte InputMouseWheel = 0x04;
    public const byte InputKeyDown = 0x05;
    public const byte InputKeyUp = 0x06;
    public const byte InputSpecialCad = 0x07; // Ctrl+Alt+Del

    public const string TypeRegisterHost = "register_host";
    public const string TypeHostRegistered = "host_registered";
    public const string TypeConnectViewer = "connect_viewer";
    public const string TypeViewerConnected = "viewer_connected";
    public const string TypeSessionStarted = "session_started";
    public const string TypeSessionEnded = "session_ended";
    public const string TypeError = "error";

    public const string TypeMonitorsList = "monitors_list";
    public const string TypeSelectMonitor = "select_monitor";
    public const string TypeBlockInput = "block_input";
    public const string TypeBlockInputStatus = "block_input_status";
    public const string TypeTechnicianInputBlocked = "technician_input_blocked";
    public const string TypeQuickAction = "quick_action";
    public const string TypeChatMessage = "chat_message";
    public const string TypeTechnicianInfo = "technician_info";
    public const string TypeAccessRequest = "access_request";
    public const string TypeAccessResponse = "access_response";
    public const string TypeAuthPassword = "auth_password";
    public const string TypeAuthPasswordResult = "auth_password_result";
    public const string TypeFsList = "fs_list";
    public const string TypeFsListRes = "fs_list_res";
    public const string TypeFsDownloadStart = "fs_download_start";
    public const string TypeFsDownloadChunk = "fs_download_chunk";
    public const string TypeFsUploadChunk = "fs_upload_chunk";
    public const string TypeFsDelete = "fs_delete";
    public const string TypeFsMkdir = "fs_mkdir";
    public const string TypeConfigUpdated = "config_updated";

    public const string TypeClipboardText = "clipboard_text";
    public const string TypeClipboardFilesAdvertisement = "clipboard_files_ad";
    public const string TypeClipboardFileRequest = "clipboard_file_req";
    public const string TypeClipboardFileChunk = "clipboard_file_chunk";

    public const string TypeCursorUpdate = "cursor_update";
}

internal enum RemoteCursorType : byte
{
    Default = 0,
    Arrow = 1,
    Hand = 2,
    IBeam = 3,
    Wait = 4,
    Cross = 5,
    SizeWE = 6,
    SizeNS = 7,
    SizeNWSE = 8,
    SizeNESW = 9,
    SizeAll = 10,
    No = 11,
    AppStarting = 12,
    Help = 13,
    UpArrow = 14
}

internal sealed class AcessoRemotoClipboardFileItem
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = "";

    [JsonPropertyName("full_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FullPath { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("is_dir")]
    public bool IsDir { get; set; }

    [JsonPropertyName("last_write_utc_ticks")]
    public long LastWriteUtcTicks { get; set; }
}

internal sealed class AcessoRemotoMonitorInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

internal sealed class AcessoRemotoFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("is_dir")]
    public bool IsDir { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mod_time")]
    public string ModTime { get; set; } = "";
}

internal sealed class AcessoRemotoMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; set; }

    [JsonPropertyName("unattended")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Unattended { get; set; }

    [JsonPropertyName("unattended_password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnattendedPassword { get; set; }

    [JsonPropertyName("host_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostToken { get; set; }

    [JsonPropertyName("target_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetId { get; set; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    // Multi-monitor
    [JsonPropertyName("monitors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AcessoRemotoMonitorInfo>? Monitors { get; set; }

    [JsonPropertyName("monitor_index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MonitorIndex { get; set; }

    // Bloqueio de entrada
    [JsonPropertyName("blocked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Blocked { get; set; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; set; }

    // Ações rápidas
    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; set; }

    // Chat
    [JsonPropertyName("sender")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sender { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Time { get; set; }

    // Identificação do técnico e consentimento de acesso
    [JsonPropertyName("technician_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TechnicianName { get; set; }

    [JsonPropertyName("technician_avatar")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TechnicianAvatar { get; set; }

    [JsonPropertyName("accepted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Accepted { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    // Transferência de arquivos
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonPropertyName("remote_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RemotePath { get; set; }

    [JsonPropertyName("drives")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Drives { get; set; }

    [JsonPropertyName("entries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AcessoRemotoFileEntry>? Entries { get; set; }

    [JsonPropertyName("transfer_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TransferId { get; set; }

    [JsonPropertyName("file_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    [JsonPropertyName("offset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Offset { get; set; }

    [JsonPropertyName("total_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalSize { get; set; }

    [JsonPropertyName("chunk_b64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChunkB64 { get; set; }

    [JsonPropertyName("is_last")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsLast { get; set; }

    // Área de transferência (Clipboard)
    [JsonPropertyName("clipboard_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AcessoRemotoClipboardFileItem>? ClipboardFiles { get; set; }

    [JsonPropertyName("file_index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FileIndex { get; set; }

    // Sincronização do cursor e prioridade do cliente
    [JsonPropertyName("cursor_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CursorType { get; set; }

    [JsonPropertyName("cursor_x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? CursorX { get; set; }

    [JsonPropertyName("cursor_y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? CursorY { get; set; }

    [JsonPropertyName("cursor_visible")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CursorVisible { get; set; }

    [JsonPropertyName("client_active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClientActive { get; set; }
}

