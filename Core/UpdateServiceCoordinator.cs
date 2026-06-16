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
        if (status?.Phase is UpdateServicePhase.Applying
            or UpdateServicePhase.PendingAppClose
            or UpdateServicePhase.Completed)
            return true;

        return VelopackUpdateEngine.PendingRestart is not null
            || HasReopenAppRequest();
    }

    public static void RequestImmediateCheck()
    {
        SharedMachineStorage.EnsureDirectory();
        File.WriteAllText(RequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Pedido explícito do utilizador para transferir e instalar a actualização.</summary>
    public static void RequestInstall()
    {
        SharedMachineStorage.EnsureDirectory();
        File.WriteAllText(InstallRequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
        RequestImmediateCheck();
    }

    /// <summary>Pedido para o serviço relançar o SistecHub na sessão do utilizador após actualizar.</summary>
    public static void RequestReopenAppAfterUpdate(string? version = null)
    {
        SharedMachineStorage.EnsureDirectory();
        File.WriteAllText(ReopenAppRequestFilePath, version ?? DateTimeOffset.UtcNow.ToString("O"));
        UpdateActivityLog.Info("Update", "Pedido de reabertura do SistecHub registado.");
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
        File.Exists(InstallRequestFilePath) || File.Exists(RequestFilePath);

    public static bool IsInstallReady(UpdateServiceStatus? status) =>
        VelopackUpdateEngine.PendingRestart is not null
        || status?.Phase is UpdateServicePhase.PendingAppClose or UpdateServicePhase.Applying;

    public static string DescribeStatusForUi(UpdateServiceStatus? status)
    {
        if (VelopackUpdateEngine.PendingRestart is { } pending)
            return $"Versão {pending.Version} pronta para instalar.";

        if (status is null)
            return "A verificar actualizações...";

        return status.Phase switch
        {
            UpdateServicePhase.Checking => "A verificar actualizações...",
            UpdateServicePhase.Downloading => status.AvailableVersion is { } v
                ? $"A transferir versão {v}..."
                : "A transferir actualização...",
            UpdateServicePhase.PendingAppClose => status.AvailableVersion is { } ready
                ? $"Versão {ready} pronta — será instalada ao fechar o SistecHub."
                : status.Message,
            UpdateServicePhase.Applying => "A instalar actualização...",
            UpdateServicePhase.Completed => status.AvailableVersion is { } done
                ? $"Actualização concluída — versão {done}."
                : "Actualização concluída.",
            UpdateServicePhase.UpToDate => $"Versão {status.CurrentVersion ?? VelopackUpdateEngine.DisplayVersion} — sem actualizações.",
            UpdateServicePhase.Error => "Erro: " + status.Message,
            _ => "A aguardar verificação de actualizações...",
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

    public static void WriteStatus(UpdateServiceStatus status)
    {
        try
        {
            SharedMachineStorage.EnsureDirectory();
            var json = JsonSerializer.Serialize(status, JsonOptions);
            File.WriteAllText(StatusFilePath, json);
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
