using System.Text;

namespace SistecHub.Core;

/// <summary>Log dedicado ao fluxo de actualização (<c>ProgramData\SistecHub\update.log</c>).</summary>
public static class UpdateActivityLog
{
    const int MaxBytes = 5 * 1024 * 1024;

    static readonly object Sync = new();

    public static string LogFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "update.log");

    public static void Info(string category, string message) => Write("INF", category, message);

    public static void Warn(string category, string message) => Write("WRN", category, message);

    public static void Error(string category, string message) => Write("ERR", category, message);

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

    public static string ReadTail(int maxLines = 40)
    {
        try
        {
            if (!File.Exists(LogFilePath))
                return "(sem entradas em update.log)";

            var lines = File.ReadAllLines(LogFilePath);
            if (lines.Length <= maxLines)
                return string.Join(Environment.NewLine, lines);

            return string.Join(Environment.NewLine, lines[^maxLines..]);
        }
        catch (Exception ex)
        {
            return $"Não foi possível ler update.log: {ex.Message}";
        }
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
                Directory.CreateDirectory(SharedMachineStorage.RootPath);
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Melhor esforço.
            }
        }
    }

    static string NormalizeCategory(string category) =>
        string.IsNullOrWhiteSpace(category) ? "Update" : category.Trim();

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
