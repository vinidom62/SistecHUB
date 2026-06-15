using System.Collections.ObjectModel;
using System.Text;

namespace SistecHub.Core;

public enum AppDebugLogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

public readonly record struct AppDebugLogEntry(
    DateTimeOffset Timestamp,
    AppDebugLogLevel Level,
    string Category,
    string Message);

/// <summary>Log técnico em memória para o modo debug (testes e diagnóstico).</summary>
public static class AppDebugLog
{
    const int MaxEntries = 3000;

    static readonly object Sync = new();
    static readonly List<AppDebugLogEntry> History = [];
    static bool _handlersInstalled;

    public static event Action<AppDebugLogEntry>? EntryAdded;

    public static void InstallGlobalHandlers()
    {
        if (_handlersInstalled)
            return;

        _handlersInstalled = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogException("App", ex, "Exceção não tratada");
            else
                Error("App", $"Exceção não tratada: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogException("Task", e.Exception, "Exceção não observada em Task");
            e.SetObserved();
        };

        Application.ThreadException += (_, e) =>
            LogException("UI", e.Exception, "Exceção na thread da interface");

        Info("App", "Handlers globais de erro registados.");
    }

    public static IReadOnlyList<AppDebugLogEntry> GetHistory()
    {
        lock (Sync)
            return new ReadOnlyCollection<AppDebugLogEntry>(History.ToArray());
    }

    public static void Debug(string category, string message) =>
        Log(AppDebugLogLevel.Debug, category, message);

    public static void Info(string category, string message) =>
        Log(AppDebugLogLevel.Info, category, message);

    public static void Warn(string category, string message) =>
        Log(AppDebugLogLevel.Warn, category, message);

    public static void Error(string category, string message) =>
        Log(AppDebugLogLevel.Error, category, message);

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

        Log(AppDebugLogLevel.Error, category, builder.ToString().TrimEnd());
    }

    public static void LogStartupContext()
    {
        Info("App", $"SistecHub {AppVersion.Current} — arranque");
        Debug("App", $"Processo: {Environment.ProcessPath ?? "(desconhecido)"}");
        Debug("App", $"Utilizador: {Environment.UserName} | Máquina: {Environment.MachineName}");
        Debug("App", $"SO: {Environment.OSVersion.VersionString} | 64-bit: {Environment.Is64BitOperatingSystem}");
        Debug("App", $"Update Velopack: {(AppUpdateService.IsUpdateSupported ? "sim" : "não")} | Versão exibida: {AppUpdateService.DisplayVersion}");

        var settingsPath = Path.Combine(SharedMachineStorage.RootPath, "settings.json");
        Debug("App", $"Configurações: {settingsPath} | Existe: {File.Exists(settingsPath)}");
        Debug("App", $"Setup inicial completo: {AppSettingsStore.IsInitialSetupComplete()}");
        Debug("App", $"Log de actualização: {UpdateActivityLog.LogFilePath}");
    }

    static void Log(AppDebugLogLevel level, string category, string message)
    {
        var entry = new AppDebugLogEntry(
            DateTimeOffset.Now,
            level,
            NormalizeCategory(category),
            message ?? "");

        lock (Sync)
        {
            History.Add(entry);
            if (History.Count > MaxEntries)
                History.RemoveRange(0, History.Count - MaxEntries);
        }

        System.Diagnostics.Debug.WriteLine(FormatLine(entry));

        EntryAdded?.Invoke(entry);
    }

    static string NormalizeCategory(string category) =>
        string.IsNullOrWhiteSpace(category) ? "App" : category.Trim();

    internal static string FormatLine(AppDebugLogEntry entry) =>
        $"[{entry.Timestamp:HH:mm:ss.fff}] [{LevelTag(entry.Level)}] [{entry.Category}] {entry.Message}";

    static string LevelTag(AppDebugLogLevel level) => level switch
    {
        AppDebugLogLevel.Debug => "DBG",
        AppDebugLogLevel.Info => "INF",
        AppDebugLogLevel.Warn => "WRN",
        AppDebugLogLevel.Error => "ERR",
        _ => "???",
    };
}
