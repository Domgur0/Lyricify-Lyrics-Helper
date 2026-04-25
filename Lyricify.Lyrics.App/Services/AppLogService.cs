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
/// Entries are also appended to a rolling log file on disk so that they
/// survive hard crashes (process killed, JVM fault, OOM).
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

    // Disk persistence
    private string? _sessionLogPath;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    // ── Static ambient accessor ───────────────────────────────────────────────

    /// <summary>
    /// The DI-registered singleton.  Available immediately after
    /// <c>MauiProgram.CreateMauiApp()</c> creates the instance.
    /// </summary>
    public static AppLogService? Current { get; private set; }

    public AppLogService()
    {
        // Guard against accidental duplicate registration (last writer wins in production
        // but we throw in debug to surface misconfiguration early).
        System.Diagnostics.Debug.Assert(
            Current is null,
            "AppLogService: a second instance was constructed – check DI registration.");
        Current = this;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the directory used for the rolling session log.
    /// Call once from <c>MauiProgram.CreateMauiApp()</c> or <c>App</c> constructor.
    /// A new session marker is appended so the file distinguishes launches.
    /// </summary>
    public void InitPersistence(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            _sessionLogPath = Path.Combine(logDirectory, "lyricify-session.log");
            // Write session boundary so the exported file separates multiple runs.
            File.AppendAllText(_sessionLogPath,
                $"{Environment.NewLine}=== Session started {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ==={Environment.NewLine}");
        }
        catch
        {
            // Non-critical — in-memory logging still works.
        }
    }

    /// <summary>
    /// Reads the entire persisted session log file (may span multiple launches).
    /// Returns <see cref="string.Empty"/> if no file exists yet.
    /// </summary>
    public async Task<string> ReadPersistedLogAsync()
    {
        if (_sessionLogPath is null || !File.Exists(_sessionLogPath))
            return string.Empty;
        try { return await File.ReadAllTextAsync(_sessionLogPath); }
        catch { return string.Empty; }
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
        // Persist asynchronously so callers are never blocked.
        if (_sessionLogPath is not null)
            _ = AppendToFileAsync(FormatEntry(entry));
    }

    private async Task AppendToFileAsync(string line)
    {
        await _writeSemaphore.WaitAsync().ConfigureAwait(false);
        try { await File.AppendAllTextAsync(_sessionLogPath!, line + Environment.NewLine); }
        catch { /* Never fail in logger */ }
        finally { _writeSemaphore.Release(); }
    }

    private static string FormatEntry(AppLogEntry e) =>
        $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss} UTC] {e.Level.ToString().ToUpperInvariant(),7} [{e.Category}] {e.Message}";

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

        return string.Join(Environment.NewLine, entries.Select(FormatEntry));
    }

    /// <summary>Clears the in-memory buffer and truncates the session log file.</summary>
    public void Clear()
    {
        lock (_lock)
            _entries.Clear();

        if (_sessionLogPath is null) return;

        // Acquire the write semaphore to avoid truncating while an async
        // append is still in flight.
        _writeSemaphore.Wait();
        try { File.WriteAllText(_sessionLogPath, string.Empty); }
        catch { }
        finally { _writeSemaphore.Release(); }
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
