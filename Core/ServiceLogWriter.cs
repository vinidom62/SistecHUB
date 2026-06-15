using System.Text;

namespace SistecHub.Core;

/// <summary>Log em ficheiro partilhado entre o app, serviço e ServiceSetup (<c>ProgramData\SistecHub\service.log</c>).</summary>
public static class ServiceLogWriter
{
    const int MaxBytes = 5 * 1024 * 1024;

    static readonly object Sync = new();

    public static string LogFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, WindowsServiceConfig.LogFileName);

    public static void Info(string category, string message) =>
        Write("INF", category, message);

    public static void Warn(string category, string message) =>
        Write("WRN", category, message);

    public static void Error(string category, string message) =>
        Write("ERR", category, message);

    public static void LogException(string category, Exception ex, string? context = null)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context))
            builder.AppendLine(context);

        for (var current = ex; current is not null; current = current.InnerException)
        {
            builder.Append('[').Append(current.GetType().Name).Append("] ");
            builder.AppendLine(current.Message);

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
                builder.AppendLine(current.StackTrace);
        }

        Write("ERR", category, builder.ToString().TrimEnd());
    }

    static void Write(string level, string category, string message)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{NormalizeCategory(category)}] {message}";

        lock (Sync)
        {
            try
            {
                SharedMachineStorage.EnsureDirectory();
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Melhor esforço: falha de log não impede instalação/operação.
            }
        }
    }

    static string NormalizeCategory(string category) =>
        string.IsNullOrWhiteSpace(category) ? "Service" : category.Trim();

    static void RotateIfNeeded()
    {
        if (!File.Exists(LogFilePath))
            return;

        var info = new FileInfo(LogFilePath);
        if (info.Length < MaxBytes)
            return;

        var backup = LogFilePath + ".old";
        if (File.Exists(backup))
            File.Delete(backup);

        File.Move(LogFilePath, backup);
    }
}
