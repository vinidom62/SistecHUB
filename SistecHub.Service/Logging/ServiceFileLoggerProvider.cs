using Microsoft.Extensions.Logging;
using SistecHub.Core;

namespace SistecHub.Service.Logging;

/// <summary>Encaminha logs do <see cref="ILogger"/> para <see cref="ServiceLogWriter"/>.</summary>
public sealed class ServiceFileLoggerProvider : ILoggerProvider
{
    static readonly Dictionary<string, ServiceFileLogger> Loggers = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Sync = new();

    public ILogger CreateLogger(string categoryName)
    {
        lock (Sync)
        {
            if (!Loggers.TryGetValue(categoryName, out var logger))
            {
                logger = new ServiceFileLogger(categoryName);
                Loggers[categoryName] = logger;
            }

            return logger;
        }
    }

    public void Dispose()
    {
        lock (Sync)
            Loggers.Clear();
    }

    sealed class ServiceFileLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = message + Environment.NewLine + exception;

            var shortCategory = ShortCategory(category);

            switch (logLevel)
            {
                case LogLevel.Warning:
                    ServiceLogWriter.Warn(shortCategory, message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    ServiceLogWriter.Error(shortCategory, message);
                    break;
                default:
                    ServiceLogWriter.Info(shortCategory, message);
                    break;
            }
        }

        static string ShortCategory(string value)
        {
            const string prefix = "SistecHub.Service.";
            return value.StartsWith(prefix, StringComparison.Ordinal)
                ? value[prefix.Length..]
                : value;
        }
    }
}
