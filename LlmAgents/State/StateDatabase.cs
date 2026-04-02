namespace LlmAgents.State;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

public class StateDatabase : IDisposable
{
    private readonly ILogger Log;

    public readonly string Database;

    private readonly SqliteConnection readConnection;

    private readonly SqliteConnection writeConnection;

    private readonly Lock writeLock = new();

    public StateDatabase(ILoggerFactory LoggerFactory, string database)
    {
        Database = database;
        Log = LoggerFactory.CreateLogger(nameof(StateDatabase));

        readConnection = CreateConnection();
        writeConnection = CreateConnection();

        if (readConnection == null || writeConnection == null)
        {
            throw new ApplicationException("Could not initialize database connections");
        }
    }

    public void Write(Action<SqliteCommand> writeStatement)
    {
        try
        {
            lock (writeLock)
            {
                using var command = writeConnection.CreateCommand();
                writeStatement(command);
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while writing data");
        }
    }

    public void Read(Action<SqliteCommand> readQuery)
    {
        try
        {
            using var command = readConnection.CreateCommand();
            readQuery(command);
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while reading data");
        }
    }

    public void Dispose()
    {
        writeConnection?.Close();
        writeConnection?.Dispose();

        readConnection?.Close();
        readConnection?.Dispose();

        GC.SuppressFinalize(this);
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder()
        {
            DataSource = Database,
            ForeignKeys = true,
            Cache = SqliteCacheMode.Shared,
            Mode = Database.Equals(":memory:") ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }
}
