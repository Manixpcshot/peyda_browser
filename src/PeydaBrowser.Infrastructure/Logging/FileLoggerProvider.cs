using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PeydaBrowser.Infrastructure.Logging;

/// <summary>
/// Minimal daily rolling file logger (Microsoft.Extensions.Logging provider).
/// One file per day under the profile's Logs folder; writes are serialized
/// through a background queue so logging never blocks the UI thread, and every
/// line passes through <see cref="SensitiveDataRedactor"/>.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly BlockingCollection<string> _queue = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writerTask;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(directory);
        _writerTask = Task.Run(WriteLoop);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writerTask.Wait(TimeSpan.FromSeconds(2));
        _shutdown.Dispose();
        _queue.Dispose();
    }

    internal bool IsEnabled(LogLevel level) => level >= _minimumLevel && level != LogLevel.None;

    internal void Enqueue(string category, LogLevel level, string message, Exception? exception)
    {
        var line = $"[{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] [{LevelTag(level)}] {category}: {SensitiveDataRedactor.Redact(message)}";
        if (exception is not null)
            line += Environment.NewLine + SensitiveDataRedactor.Redact(exception.ToString());
        if (!_queue.IsAddingCompleted)
            _queue.TryAdd(line);
    }

    private void WriteLoop()
    {
        StreamWriter? writer = null;
        var currentDate = string.Empty;
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable(_shutdown.Token))
            {
                var today = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
                if (writer is null || today != currentDate)
                {
                    writer?.Dispose();
                    currentDate = today;
                    writer = new StreamWriter(Path.Combine(_directory, $"peyda-{today}.log"), append: true)
                    {
                        AutoFlush = true
                    };
                }
                writer.WriteLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Enqueue(category, logLevel, formatter(state, exception), exception);
        }
    }
}
