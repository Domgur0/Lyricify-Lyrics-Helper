using Microsoft.Extensions.Logging;

namespace Lyricify.Lyrics.App.Services;

/// <summary>Severity level used by <see cref="AppLogService"/>.</summary>
public enum AppLogLevel { Warning, Error }

/// <summary>A single captured log entry.</summary>
public sealed record AppLogEntry(
    DateTimeOffset Timestamp,
    AppLogLevel Level,
    string Category,
    string Message);

/// <summary>
/// Thread-safe, in-memory ring buffer that stores the most recent
/// <see cref="MaxEntries"/> warnings and errors produced by the app.
/// </summary>
/// <remarks>
/// Populated by:
/// <list type="bullet">
///   <item><see cref="AppLogProvider"/> (via MAUI's <see cref="ILoggerFactory"/>).</item>
///   <item>Direct calls from non-DI paths (<see cref="Current"/> static accessor).</item>
/// </list>
/// </remarks>
public sealed class AppLogService
{
    private const int MaxEntries = 500;
    private readonly List<AppLogEntry> _entries = new();
    private readonly object _lock = new();

    // ── Static ambient accessor ───────────────────────────────────────────────

    /// <summary>
    /// The DI-registered singleton.  Available immediately after
    /// <c>MauiProgram.CreateMauiApp()</c> creates the instance.
    /// </summary>
    public static AppLogService? Current { get; private set; }

    public AppLogService()
    {
        Current = this;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Adds an entry to the buffer (thread-safe, drops oldest when full).</summary>
    public void Add(AppLogLevel level, string category, string message)
    {
        var entry = new AppLogEntry(DateTimeOffset.UtcNow, level, category, message);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot of all current entries (oldest first).</summary>
    public IReadOnlyList<AppLogEntry> GetEntries()
    {
        lock (_lock)
            return _entries.ToList();
    }

    /// <summary>Formats all entries as a plain-text block suitable for export.</summary>
    public string ExportText()
    {
        var entries = GetEntries();
        if (entries.Count == 0)
            return "(no warnings or errors logged)";

        return string.Join(
            Environment.NewLine,
            entries.Select(e =>
                $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss} UTC] {e.Level.ToString().ToUpperInvariant(),7} [{e.Category}] {e.Message}"));
    }

    /// <summary>Clears the buffer (e.g. after a successful export).</summary>
    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
    }
}

// ── ILoggerProvider bridge ────────────────────────────────────────────────────

/// <summary>
/// Plugs into the MAUI / Microsoft.Extensions.Logging pipeline and forwards
/// all <see cref="LogLevel.Warning"/> and above messages to <see cref="AppLogService"/>.
/// </summary>
internal sealed class AppLogProvider : ILoggerProvider
{
    private readonly AppLogService _logService;

    public AppLogProvider(AppLogService logService) => _logService = logService;

    public ILogger CreateLogger(string categoryName) =>
        new AppLogLogger(categoryName, _logService);

    public void Dispose() { }
}

internal sealed class AppLogLogger : ILogger
{
    private readonly string _category;
    private readonly AppLogService _logService;

    public AppLogLogger(string category, AppLogService logService)
    {
        _category = category;
        _logService = logService;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel < LogLevel.Warning) return;

        var msg = formatter(state, exception);
        if (exception is not null)
            msg += $"\n{exception}";

        _logService.Add(
            logLevel >= LogLevel.Error ? AppLogLevel.Error : AppLogLevel.Warning,
            _category,
            msg);
    }
}
