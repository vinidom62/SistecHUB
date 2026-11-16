using System.Text.Json;

namespace SistecHub.Core;

/// <summary>Estado partilhado e pedidos de verificação imediata entre app e serviço.</summary>
public static class UpdateServiceCoordinator
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string RequestFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "update-check.request");

    /// <summary>Pedido manual para incluir pré-releases (beta) na verificação Velopack.</summary>
    public static string BetaRequestFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "update-check-beta.request");

    public static string InstallRequestFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "update-install.request");

    public static string StatusFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "update-status.json");

    public static string ReopenAppRequestFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "reopen-app.request");

    public static bool UsesServiceForUpdates => VelopackUpdateEngine.IsInstalled;

    /// <summary>Indica arranque provável durante ou logo após uma actualização.</summary>
    public static bool IsServiceRecoveryLikelyUpdateRelated()
    {
        var status = TryReadStatus();
        if (status is not null)
        {
            if (status.Phase == UpdateServicePhase.Completed)
            {
                // Reconhece a fase Completed recente (últimos 3 minutos) como parte válida do ciclo pós-atualização
                return DateTimeOffset.UtcNow - status.LastUpdateUtc <= TimeSpan.FromMinutes(3);
            }

            // Se o status está há mais de 4 minutos em Applying, provavelmente já foi aplicado ou expirou
            if (status.Phase == UpdateServicePhase.Applying
                && DateTimeOffset.UtcNow - status.LastUpdateUtc > TimeSpan.FromMinutes(4))
            {
                return false;
            }

            if (status.Phase is UpdateServicePhase.Applying or UpdateServicePhase.PendingAppClose)
                return true;
        }

        return VelopackUpdateEngine.PendingRestart is not null
            || HasReopenAppRequest();
    }

    public static void RequestImmediateCheck(bool resetStatus = true)
    {
        SharedMachineStorage.EnsureDirectory();
        if (resetStatus)
        {
            var current = TryReadStatus();
            var isActivelyDownloading = current?.Phase == UpdateServicePhase.Downloading
                && DateTimeOffset.UtcNow - current.LastUpdateUtc <= TimeSpan.FromSeconds(30);

            if (current?.Phase is not UpdateServicePhase.Applying && !isActivelyDownloading)
            {
                WriteStatus(new UpdateServiceStatus
                {
                    LastUpdateUtc = DateTimeOffset.UtcNow,
                    Phase = UpdateServicePhase.Checking,
                    ProgressPercent = 0,
                    Message = "A verificar atualizações...",
                    CurrentVersion = VelopackUpdateEngine.DisplayVersion,
                });
            }
        }

        File.WriteAllText(RequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Só via botão manual — nunca usado pela verificação automática.</summary>
    public static void RequestImmediateBetaCheck(bool resetStatus = true)
    {
        SharedMachineStorage.EnsureDirectory();
        if (resetStatus)
        {
            var current = TryReadStatus();
            var isActivelyDownloading = current?.Phase == UpdateServicePhase.Downloading
                && DateTimeOffset.UtcNow - current.LastUpdateUtc <= TimeSpan.FromSeconds(30);

            if (current?.Phase is not UpdateServicePhase.Applying && !isActivelyDownloading)
            {
                WriteStatus(new UpdateServiceStatus
                {
                    LastUpdateUtc = DateTimeOffset.UtcNow,
                    Phase = UpdateServicePhase.Checking,
                    ProgressPercent = 0,
                    Message = "A verificar atualizações Beta...",
                    CurrentVersion = VelopackUpdateEngine.DisplayVersion,
                });
            }
        }

        File.WriteAllText(BetaRequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Pedido explícito do utilizador para transferir e instalar a actualização.</summary>
    public static void RequestInstall(string? version = null)
    {
        SharedMachineStorage.EnsureDirectory();
        var current = TryReadStatus();
        var targetVer = !string.IsNullOrWhiteSpace(version) ? version : current?.AvailableVersion;

        WriteStatus(new UpdateServiceStatus
        {
            LastUpdateUtc = DateTimeOffset.UtcNow,
            Phase = UpdateServicePhase.Applying,
            ProgressPercent = 100,
            Message = string.IsNullOrWhiteSpace(targetVer)
                ? "A iniciar aplicação da atualização..."
                : $"A instalar versão {targetVer}...",
            CurrentVersion = VelopackUpdateEngine.DisplayVersion,
            AvailableVersion = targetVer,
        });

        File.WriteAllText(InstallRequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Pedido para o serviço relançar o SistecHub na sessão do utilizador após actualizar.</summary>
    public static void RequestReopenAppAfterUpdate(string? version = null, bool? startMinimized = null)
    {
        SharedMachineStorage.EnsureDirectory();

        bool resolvedMinimized;
        if (startMinimized.HasValue)
        {
            resolvedMinimized = startMinimized.Value;
        }
        else if (TryReadReopenAppRequest(out var existingVer, out var existingMinimized))
        {
            version ??= existingVer;
            resolvedMinimized = existingMinimized;
        }
        else
        {
            // Se o pedido foi criado diretamente pelo serviço sem sinal prévio da UI aberta, reabre discretamente.
            resolvedMinimized = true;
        }

        var verPart = string.IsNullOrWhiteSpace(version) ? DateTimeOffset.UtcNow.ToString("O") : version.Trim();
        var modePart = resolvedMinimized ? "minimized" : "normal";
        File.WriteAllText(ReopenAppRequestFilePath, $"{verPart}|{modePart}");
        UpdateActivityLog.Info("Update", $"Pedido de reabertura do SistecHub registado (versão='{version}', modo={modePart}).");
    }

    public static bool TryReadReopenAppRequest(out string? version, out bool startMinimized)
    {
        version = null;
        startMinimized = false;

        try
        {
            if (!File.Exists(ReopenAppRequestFilePath))
                return false;

            var raw = File.ReadAllText(ReopenAppRequestFilePath).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return true;

            var parts = raw.Split('|', 2, StringSplitOptions.TrimEntries);
            var first = parts[0];
            if (!string.IsNullOrWhiteSpace(first) && !DateTimeOffset.TryParse(first, out _))
                version = first;

            if (parts.Length > 1)
                startMinimized = string.Equals(parts[1], "minimized", StringComparison.OrdinalIgnoreCase);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasReopenAppRequest() => File.Exists(ReopenAppRequestFilePath);

    public static void ClearReopenAppRequest()
    {
        try
        {
            if (File.Exists(ReopenAppRequestFilePath))
                File.Delete(ReopenAppRequestFilePath);
        }
        catch
        {
            // Melhor esforço.
        }
    }

    public static bool TryConsumeInstallRequest()
    {
        if (!File.Exists(InstallRequestFilePath))
            return false;

        try
        {
            File.Delete(InstallRequestFilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasPendingWorkRequest() =>
        File.Exists(InstallRequestFilePath)
        || File.Exists(RequestFilePath)
        || File.Exists(BetaRequestFilePath);

    public static bool IsInstallReady(UpdateServiceStatus? status) =>
        (VelopackUpdateEngine.PendingRestart is { } p && !string.Equals(p.Version.ToString(), VelopackUpdateEngine.DisplayVersion, StringComparison.OrdinalIgnoreCase))
        || status?.Phase is UpdateServicePhase.PendingAppClose or UpdateServicePhase.Applying;

    public static string DescribeStatusForUi(UpdateServiceStatus? status)
    {
        if (VelopackUpdateEngine.PendingRestart is { } pending
            && !string.Equals(pending.Version.ToString(), VelopackUpdateEngine.DisplayVersion, StringComparison.OrdinalIgnoreCase))
        {
            return $"Versão {pending.Version} pronta para instalar.";
        }

        if (status is null)
            return "A verificar atualizações...";

        return status.Phase switch
        {
            UpdateServicePhase.Checking => "A verificar atualizações...",
            UpdateServicePhase.Downloading => status.AvailableVersion is { } v
                ? $"A transferir versão {v} ({status.ProgressPercent}%)..."
                : $"A transferir atualização ({status.ProgressPercent}%)...",
            UpdateServicePhase.PendingAppClose => status.AvailableVersion is { } ready
                ? $"Versão {ready} pronta — será instalada ao fechar o SistecHub."
                : status.Message,
            UpdateServicePhase.Applying => "A instalar atualização...",
            UpdateServicePhase.Completed => status.AvailableVersion is { } done
                ? $"Atualização concluída — versão {done}."
                : "Atualização concluída.",
            UpdateServicePhase.UpToDate => $"Versão {status.CurrentVersion ?? VelopackUpdateEngine.DisplayVersion} — sem atualizações.",
            UpdateServicePhase.Error => "Erro: " + status.Message,
            _ => "A aguardar verificação de atualizações...",
        };
    }

    public static bool TryConsumeCheckRequest()
    {
        if (!File.Exists(RequestFilePath))
            return false;

        try
        {
            File.Delete(RequestFilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryConsumeBetaCheckRequest()
    {
        if (!File.Exists(BetaRequestFilePath))
            return false;

        try
        {
            File.Delete(BetaRequestFilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteStatus(UpdateServiceStatus status, bool logActivity = true)
    {
        try
        {
            SharedMachineStorage.EnsureDirectory();
            var json = JsonSerializer.Serialize(status, JsonOptions);
            var tempPath = StatusFilePath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, StatusFilePath, overwrite: true);
            }
            catch
            {
                File.WriteAllText(StatusFilePath, json);
            }

            if (logActivity)
                UpdateActivityLog.Info("Update", $"Estado: {status.Phase} — {status.Message}");
        }
        catch
        {
            // Melhor esforço.
        }
    }

    public static UpdateServiceStatus? TryReadStatus()
    {
        try
        {
            if (!File.Exists(StatusFilePath))
                return null;

            var json = File.ReadAllText(StatusFilePath);
            return JsonSerializer.Deserialize<UpdateServiceStatus>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<UpdateServiceStatus?> WaitForStatusChangeAsync(
        UpdateServicePhase? previousPhase,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = TryReadStatus();
            if (status is not null && status.Phase != previousPhase)
                return status;

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        return TryReadStatus();
    }
}

public sealed class UpdateServiceStatus
{
    public DateTimeOffset LastUpdateUtc { get; init; } = DateTimeOffset.UtcNow;

    public UpdateServicePhase Phase { get; init; } = UpdateServicePhase.Idle;

    public int ProgressPercent { get; init; }

    public string Message { get; init; } = "";

    public string? CurrentVersion { get; init; }

    public string? AvailableVersion { get; init; }
}

public enum UpdateServicePhase
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    PendingAppClose,
    Applying,
    Completed,
    Error,
}
