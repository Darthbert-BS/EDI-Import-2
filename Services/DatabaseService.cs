
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using System.Data;

namespace BundabergSugar.EDIImport.Services;

public interface IDatabaseService { 
    public SqlTransaction GetExclusiveLock(string table = "EDIImport_lock"); // New method
    public SqlCommand GetSqlCommand(string commandText, CommandType commandType = CommandType.StoredProcedure);
    public void ReleaseExclusiveLock(bool commit = true, bool rollback = true);

}


public class DatabaseService : IDatabaseService {
    private static readonly EventId EventId = new(3000, "EDI Database Service");
    private readonly IApplicationOptions _options;
    private readonly ILogger<DatabaseService> _logger;
    
    private SqlConnection _sqlConnection;
    private SqlTransaction? _currentTransaction;

    public SqlConnection SqlConnection {get {
        _sqlConnection ??= OpenConnection();
        return _sqlConnection; 
    }}
    
    public SqlTransaction CurrentTransaction {get {
        _currentTransaction ??= GetExclusiveLock();
        return _currentTransaction;
    }}   


    public DatabaseService(IApplicationOptions options, ILogger<DatabaseService> logger) {
        _options = options;
        _logger = logger;
        _sqlConnection = OpenConnection();
    }


    /// <summary>
    /// Get an exclusive lock on the DataLock Table. This is used to ensure only a single instance of this program is running at a time.
    /// </summary>
    public SqlTransaction GetExclusiveLock(string table = "EDIImport_lock") {
        try { 

            var trx = SqlConnection.BeginTransaction("MyLock");
            
            using var command = SqlConnection.CreateCommand();
            command.Transaction = trx;
            command.CommandText = $"SELECT * FROM {table} WITH (TABLOCKX)";
            command.ExecuteNonQuery();
            _currentTransaction = trx;
            return trx;
        } catch (Exception e) {
            _logger.LogCritical(EventId, e, "Cannot acquire lock on table {table}. Error: {error}", table, e.Message);
            throw;
        }
    }


    /// <summary>
    /// Release the exclusive lock on the DataLock table.
    /// 
    /// If <paramref name="commit"/> is true, then the transaction is committed. If an exception occurs while attempting to commit, 
    /// the transaction is rolled back and the program is exited.
    /// 
    /// If <paramref name="rollback"/> is true, then the transaction is rolled back if an exception occurs while attempting to release the lock.
    /// </summary>
    /// <param name="commit">Whether or not to commit the transaction when releasing the lock. Defaults to true.</param>
    /// <param name="rollback">Whether or not to roll back the transaction if an exception occurs when releasing the lock. Defaults to true.</param>
    public void ReleaseExclusiveLock(bool commit = true, bool rollback = true) {
        if (_currentTransaction != null) {
            try {
                string table = "EDIImport_lock";
                using var command = SqlConnection.CreateCommand();
                command.Transaction = _currentTransaction;
                command.CommandText = $"DELETE FROM {table}";
                command.ExecuteNonQuery();
                _currentTransaction.Commit(); // or transaction.Rollback() if you want to rollback
            } catch (Exception e) {
                _logger.LogCritical(EventId, e,"Error committing transaction: {error}", e.Message);
                if (!rollback) Environment.Exit(1);    
                _currentTransaction.Rollback();                                
            } finally {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }
    }


    public SqlCommand GetSqlCommand(string commandText, CommandType commandType =CommandType.StoredProcedure) {
        var cmd = SqlConnection.CreateCommand();
        cmd.Transaction = CurrentTransaction;
        cmd.CommandText = commandText;
        cmd.CommandType = commandType;
        return cmd;
    }


    private SqlConnection OpenConnection() {
        try {
            var connection = new SqlConnection(_options.ConnectionString);
            connection.Open();
            return connection;
        } catch (Exception e) {
            _logger.LogCritical(EventId, e,"Error Connectoing to database: {error}", e.Message);
            Environment.Exit(1);    
            throw;
        }
    }


    public void Dispose() {

        if (SqlConnection != null && SqlConnection.State == ConnectionState.Open) {
            SqlConnection.Close();
            SqlConnection.Dispose();
        }
    }


}
