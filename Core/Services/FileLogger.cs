using System.Runtime.Versioning;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using BundabergSugar.Core.Extensions;

namespace BundabergSugar.Core.Providers.Logging;

[UnsupportedOSPlatform("browser")]
[ProviderAlias("FileLogger")]
public sealed class FileLoggerProvider : ILoggerProvider {
    private readonly IDisposable? _onChangeToken;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private FileLoggerConfiguration _currentConfig;

    public FileLoggerProvider(IOptionsMonitor<FileLoggerConfiguration> config) {
        _currentConfig = config.CurrentValue;
        _onChangeToken = config.OnChange(updatedConfig => _currentConfig = updatedConfig);
    }

    public ILogger CreateLogger(string categoryName) =>_loggers
        .GetOrAdd(categoryName, name => new FileLogger(name, GetCurrentConfig));

    private FileLoggerConfiguration GetCurrentConfig() => _currentConfig;

    public void Dispose() {
        _loggers.Clear();
        _onChangeToken?.Dispose();
    }

}



public sealed class FileLoggerConfiguration {
    public int EventId { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    public bool LogEnabled { get; set; } = true;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = $"{Core.Common.GetAppName()}.log";
    public double MaxFileSizeMB  { get; set; } = 10;
    
}



public sealed class FileLogger(string name, Func<FileLoggerConfiguration> getCurrentConfig) : ILogger, IDisposable {
    private bool _disposed;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;
    
    /// <summary>
    /// Checks if the logger is enabled for the specified <paramref name="logLevel"/>.
    /// </summary>
    /// <param name="logLevel">The <see cref="LogLevel"/> to check.</param>
    /// <returns>True if the logger is enabled for <paramref name="logLevel"/>, false otherwise.</returns>
    /// <remarks>
    /// This method is used to filter out log messages that are not enabled.
    /// It is called by the <see cref="ILogger"/> implementation before calling <see cref="Log{TState}(LogLevel, EventId, TState, Exception, Func{TState, Exception, string})"/>.
    /// </remarks>
    public bool IsEnabled(LogLevel logLevel) => 
        logLevel >= getCurrentConfig().LogLevel && getCurrentConfig().LogEnabled; 

    
    /// <summary>
    /// Checks whether the current process has write permissions to the specified <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path to check. If empty or null, the method will return false.</param>
    /// <returns>True if the process has write permissions to the file, false otherwise.</returns>
    private bool HasPermissions(string path) {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        return Common.CanWriteToDirectory(Path.GetDirectoryName(path)!) && Common.CanWriteToFile(path);  
    } 

    /// <summary>
    /// Writes a log entry to a file.
    /// </summary>
    /// <param name="logLevel">The <see cref="LogLevel"/> of the log entry.</param>
    /// <param name="eventId">The <see cref="EventId"/> of the log entry.</param>
    /// <param name="state">The object to be serialized as the log message.</param>
    /// <param name="exception">The exception to be logged, if any.</param>
    /// <param name="formatter">A function to format the log message.</param>
    /// <remarks>
    /// This method checks the <see cref="FileLoggerConfiguration"/> for the logger.
    /// If the configuration is invalid or the logger is disabled, the method will return without writing to the file.
    /// </remarks>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try{
            // Make sure the logger is enabled and configured
            if (!IsEnabled(logLevel)) { return; }
            
            // Check for read/rwite permissions
            FileLoggerConfiguration config = getCurrentConfig();

            if (!Directory.Exists(config.FilePath)) {
                Directory.CreateDirectory(config.FilePath);
                //nsole.WriteLine($"{name} FileLogger Error: Directory does not exists: {config.FilePath}. Logging to file is disabled.");
                //turn;
            }  
            var file = Path.Join(config.FilePath, config.FileName);
            if (!HasPermissions(Path.Join(config.FilePath, config.FileName))) {
                Console.WriteLine($"{name} FileLogger Error: Cannot write to file: {file}. Logging to file is disabled.");
                return;
            }


            // build the message
            string message = formatter(state, exception);
            string logHeader = $"{DateTime.Now:o} [{logLevel}] [{eventId.Id} - {eventId.Name}]";
            string logEntry = $"{logHeader} {message}";
            if (exception != null) {
                logEntry += Environment.NewLine + $"{logHeader} {exception.GetType()} - {exception.StackTrace?.Trim() ?? "Stacktrace not found."}";
            }    

            // Check the file size and archives if needed        
            if (File.Exists(file) && config.MaxFileSizeMB > 0) {
                CheckFileSize(file, logEntry, config);
            }

            using var _writer = new StreamWriter(file, append: true);
            _writer.WriteLine(logEntry);
            _writer.Flush();
            _writer.Close(); 
        } catch (Exception ex) {
            Console.WriteLine($"File Logger Error: {ex.Message}");
        }
    }

    
    /// <summary>
    /// Checks the size of the log file and archives it if the file size has exceeded the configured limit.
    /// </summary>
    /// <param name="filePath">Full path to the log file.</param>
    /// <param name="entry">The log entry to be written.</param>
    /// <param name="config">FileLoggerConfiguration object.</param>
    /// <remarks>
    /// If the file size is exceeded, the file is moved to a new file name with the current date and a sequence number.
    /// The sequence number starts from 1 and increments for each new file created for the same day.
    /// </remarks>
    private void CheckFileSize(string filePath, string entry, FileLoggerConfiguration config) {
        FileInfo fileInfo = new(filePath);
        var encoding  = fileInfo.GetFileEncoding();
        var messageSizeBytes = encoding.GetByteCount(entry);

        // Check size in Megabbytes
        if ((fileInfo.Length + messageSizeBytes) >= config.MaxFileSizeMB.ConvertMbToBytes()) {
            // checks how many backup files we already have for today
            string date = DateTime.Now.ToString("yyyyMMdd");
            string ext = fileInfo.Extension;
            string name = Path.GetFileNameWithoutExtension(fileInfo.Name);
            string pattern = $"{name}-{date}.*";
            var files = Directory.GetFiles(fileInfo.DirectoryName!, pattern)
                .Where(f => !f.EndsWith(ext)).ToList();
            
            File.Move(fileInfo.FullName, fileInfo.FullName.Replace(ext, $"-{date}.{files.Count+1}"));
        }
    }


    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) { }
            _disposed = true;
        }
    }

     ~FileLogger() {
        Dispose(false);
    }



}