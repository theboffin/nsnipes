using System.Collections.Concurrent;
using System.Text;

namespace NSnipes;

/// <summary>
/// Lightweight, async error logger that writes to file without blocking game performance.
/// Uses a background queue to ensure logging never impacts game frame rate.
/// </summary>
public static class ErrorLogger
{
    private static readonly ConcurrentQueue<string> _logQueue = new();
    private static readonly string _logFilePath;
    private static readonly Task _backgroundWriter;
    private static readonly CancellationTokenSource _cancellationTokenSource = new();
    private static bool _initialized = false;
    private static readonly object _initLock = new object();
    
    static ErrorLogger()
    {
        // Create log file in same directory as executable
        try
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            _logFilePath = Path.Combine(exeDir, "nsnipes_errors.log");
        }
        catch
        {
            // Fallback to current directory if we can't determine exe path
            _logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "nsnipes_errors.log");
        }
        
        // Start background writer task
        _backgroundWriter = Task.Run(BackgroundWriterLoop, _cancellationTokenSource.Token);
    }
    
    /// <summary>
    /// Initialize the error logger (called once at application startup)
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;
            
        lock (_initLock)
        {
            if (_initialized)
                return;
                
            _initialized = true;
            LogInfo("ErrorLogger initialized");
        }
    }
    
    /// <summary>
    /// Log an error message (non-blocking, async)
    /// </summary>
    public static void LogError(string message, Exception? exception = null)
    {
        // Always log errors, even if not initialized (for early errors)
        var logEntry = FormatLogEntry("ERROR", message, exception);
        _logQueue.Enqueue(logEntry);
        
        // Also write to console immediately for critical errors
        try
        {
            Console.Error.WriteLine($"[ERROR] {message}");
            if (exception != null)
            {
                Console.Error.WriteLine($"Exception: {exception.GetType().Name}: {exception.Message}");
            }
        }
        catch
        {
            // Ignore console write failures
        }
    }
    
    /// <summary>
    /// Log a warning message (non-blocking, async)
    /// </summary>
    public static void LogWarning(string message, Exception? exception = null)
    {
        if (!_initialized)
            return;
            
        var logEntry = FormatLogEntry("WARNING", message, exception);
        _logQueue.Enqueue(logEntry);
    }
    
    /// <summary>
    /// Log an info message (non-blocking, async)
    /// </summary>
    public static void LogInfo(string message)
    {
        if (!_initialized)
            return;
            
        var logEntry = FormatLogEntry("INFO", message, null);
        _logQueue.Enqueue(logEntry);
    }
    
    /// <summary>
    /// Shutdown the logger gracefully
    /// </summary>
    public static void Shutdown()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            _backgroundWriter.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore timeout during shutdown
        }
        _cancellationTokenSource.Dispose();
    }
    
    private static string FormatLogEntry(string level, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");
        
        if (exception != null)
        {
            sb.AppendLine();
            sb.Append($"Exception: {exception.GetType().Name}: {exception.Message}");
            sb.AppendLine();
            sb.Append($"Stack Trace: {exception.StackTrace}");
        }
        
        return sb.ToString();
    }
    
    private static async Task BackgroundWriterLoop()
    {
        var batch = new List<string>(100); // Batch writes for efficiency
        
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // Collect up to 100 log entries or wait 1 second
                var timeout = Task.Delay(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token);
                
                while (batch.Count < 100 && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (_logQueue.TryDequeue(out var entry))
                    {
                        batch.Add(entry);
                    }
                    else
                    {
                        // No more entries, wait a bit or timeout
                        await Task.WhenAny(
                            Task.Run(async () => { while (_logQueue.IsEmpty && !_cancellationTokenSource.Token.IsCancellationRequested) await Task.Delay(50); }, _cancellationTokenSource.Token),
                            timeout
                        );
                        
                        if (timeout.IsCompleted)
                            break;
                    }
                }
                
                // Write batch to file
                if (batch.Count > 0)
                {
                    await WriteBatchToFile(batch);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch
            {
                // Ignore errors in logger itself to prevent infinite loops
            }
        }
        
        // Write any remaining entries on shutdown
        while (_logQueue.TryDequeue(out var entry))
        {
            batch.Add(entry);
        }
        
        if (batch.Count > 0)
        {
            try
            {
                await WriteBatchToFile(batch);
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }
    }
    
    private static async Task WriteBatchToFile(List<string> batch)
    {
        try
        {
            var content = string.Join(Environment.NewLine, batch) + Environment.NewLine;
            await File.AppendAllTextAsync(_logFilePath, content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // If file write fails, try writing to console as fallback
            try
            {
                Console.Error.WriteLine($"[ErrorLogger] Failed to write to log file: {ex.Message}");
                foreach (var entry in batch)
                {
                    Console.Error.WriteLine(entry);
                }
            }
            catch
            {
                // Ignore console write failures too
            }
        }
    }
}
