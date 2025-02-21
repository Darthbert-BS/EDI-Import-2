using System.Runtime.Versioning;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using System.Data;


using BundabergSugar.Core.Extensions;

namespace BundabergSugar.Core.Providers.Logging;

[UnsupportedOSPlatform("browser")]
[ProviderAlias("DBLogger")]
public sealed class DBLoggerProvider : ILoggerProvider {
    private readonly IDisposable? _onChangeToken;
    private DBLoggerConfiguration _currentConfig;
    private readonly ConcurrentDictionary<string, DBLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);


    public DBLoggerProvider(IOptionsMonitor<DBLoggerConfiguration> config) {
        _currentConfig = config.CurrentValue;
        _onChangeToken = config.OnChange(updatedConfig => _currentConfig = updatedConfig);
    }

    public ILogger CreateLogger(string categoryName) =>_loggers
        .GetOrAdd(categoryName, name => new DBLogger(name, GetCurrentConfig, false));

    private DBLoggerConfiguration GetCurrentConfig() => _currentConfig;

    public void Dispose() {
        _onChangeToken?.Dispose();
        _loggers.Clear();
    }

}


public enum DBLoggerTargetType {
    Table,
    StoredProcedure
}


public class DBLoggerParameter {
    public string FieldName { get; set; } = string.Empty;
    public string FieldType { get; set;} = string.Empty;
    public int FieldSize { get; set;} = 0;
}


public sealed class DBLoggerConfiguration {
    public int EventId { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    public bool LogEnabled { get; set; } = true;
    public required string ConnectionString  { get; set; } = string.Empty;
    public required string TargetName { get; set; } = string.Empty;
    public DBLoggerTargetType TargetType { get; set; } = DBLoggerTargetType.Table;
    public required IList<DBLoggerParameter> Parameters { get; set; } = [];    
}


public sealed class DBLogger(string name, Func<DBLoggerConfiguration> getCurrentConfig): ILogger, IDisposable {
    private bool _disposed;
    private readonly bool _persistConnection = true;
    private SqlConnection? _sqlConnection;
    private readonly string _sqlString = string.Empty;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => 
        logLevel >= getCurrentConfig().LogLevel && getCurrentConfig().LogEnabled; 


    private SqlConnection Connection { get {
        _sqlConnection ??= OpenConnection();    
        return _sqlConnection;
    }}

    
    public DBLogger(string name, Func<DBLoggerConfiguration> getCurrentConfig, bool persistConnection = true): this (name, getCurrentConfig) {
        Console.WriteLine($"{name} created.");
        _persistConnection = persistConnection;
        if (persistConnection) {
            _sqlConnection = OpenConnection();  
        }

        // prepares the query
        var parameters = getCurrentConfig().Parameters;
        if (getCurrentConfig().TargetType == DBLoggerTargetType.Table) {
            _sqlString = $@"INSERT INTO {getCurrentConfig().TargetName} 
                ({ string.Join(", ", parameters.Select(p => $"[{p.FieldName}]")) }) 
                VALUES ({ string.Join(", ", parameters.Select(p => "@"+ p.FieldName)) });";    
        } else {
            throw new NotImplementedException("Logging with stored procedure is not implemented yet");
        }
    }


    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) { return; }

        try { 
            string message = formatter(state, exception);
         
            using SqlCommand command = Connection.CreateCommand();
            command.CommandText = _sqlString;
            var parameters = getCurrentConfig().Parameters;
            foreach (var parameter in parameters) {
                SqlParameter sqlParam = command.CreateParameter();
                sqlParam.ParameterName = "@" + parameter.FieldName;
                switch (parameter.FieldType) {   
                    case "timeStamp":
                        sqlParam.Value = DateTime.Now;
                        break;
                    case "logLevel": 
                        sqlParam.Size = parameter.FieldSize > 0 ? parameter.FieldSize : 50;
                        sqlParam.Value = logLevel.ToString().SafeSubstring(len: sqlParam.Size).ToUpper();
                        break;
                    case "system":
                        sqlParam.Size = parameter.FieldSize > 0 ? parameter.FieldSize : 50;
                        sqlParam.Value = Common.GetAppName().SafeSubstring(len: sqlParam.Size);
                        break;
                    case "subSystem":
                        sqlParam.Size = parameter.FieldSize > 0 ? parameter.FieldSize : 50;
                        sqlParam.Value = $"{eventId.Id} - {eventId.Name}".SafeSubstring(len: sqlParam.Size);                    
                        break;                        
                    case "message":    
                        sqlParam.Size = parameter.FieldSize > 0 ? parameter.FieldSize : 2147483647;
                        sqlParam.Value = message.SafeSubstring(len: sqlParam.Size);
                        break;
                    case "exception":
                        sqlParam.Size = parameter.FieldSize > 0 ? parameter.FieldSize : 50;
                        sqlParam.IsNullable = true;
                        sqlParam.Value = exception?.GetType().ToString().SafeSubstring(len: sqlParam.Size) ?? string.Empty;
                        break;
                    case "stackTrace":
                        sqlParam.Size = parameter.FieldSize > 0 ? parameter.FieldSize : 50; 
                        sqlParam.IsNullable = true;
                        sqlParam.Value = exception?.StackTrace?.SafeSubstring(len: sqlParam.Size) ?? string.Empty;
                        break;
                    default: 
                        Console.WriteLine($"Unknown parameter type: {parameter.FieldType}"); 
                        break;
                }
                command.Parameters.Add(sqlParam);
            }
            command.CommandType = (getCurrentConfig().TargetType == DBLoggerTargetType.Table) 
                ? CommandType.Text 
                : CommandType.StoredProcedure;

            command.ExecuteNonQuery();

        } catch (Exception ex) {    
            Console.WriteLine($"{name} Error: {ex.Message}");   
            
        } finally {
            if (!_persistConnection) {
                CloseExistingConnection();
            }
        }
    }


    private SqlConnection OpenConnection() {
        try { 
            CloseExistingConnection();
            DBLoggerConfiguration config = getCurrentConfig();
            var conn = new SqlConnection(getCurrentConfig().ConnectionString);
            conn.Open();
            return conn;
        } catch (Exception ex) {    
            Console.WriteLine($"{name} Error: {ex.Message}");   
            throw;
        }
    }    

    private void CloseExistingConnection() {
        if (_sqlConnection != null && _sqlConnection.State == ConnectionState.Open) {
            _sqlConnection.Close();
        }          
        _sqlConnection?.Dispose();  
        _sqlConnection= null;
    }
    
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
               if (_sqlConnection != null && _sqlConnection.State == ConnectionState.Open) {
                    _sqlConnection.Close();
                }          
                _sqlConnection?.Dispose();  
            }
            _disposed = true;
        }
    }

     ~DBLogger() {
        Dispose(false);
    }
}