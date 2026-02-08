namespace LlmAgents.State;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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

        Initialize();
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

    public void CreateSession(Session session)
    {
        try
        {
            lock (writeLock)
            {
                using var command = writeConnection.CreateCommand();
                command.CommandText = "INSERT INTO sessions (session_id, start_time, last_active, status, metadata) VALUES ($sessionId, $startTime, $lastActive, $status, $metadata)";
                command.Parameters.AddWithValue("$sessionId", session.SessionId);
                command.Parameters.AddWithValue("$startTime", session.StartTime);
                command.Parameters.AddWithValue("$lastActive", session.LastActive);
                command.Parameters.AddWithValue("$status", session.Status);
                command.Parameters.AddWithValue("$metadata", session.Metadata);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while creating session");
        }
    }

    public Session? GetSession(string sessionId)
    {
        try
        {
            using var command = readConnection.CreateCommand();
            command.CommandText = "SELECT session_id, start_time, last_active, status, metadata FROM sessions WHERE session_id = $sessionId";
            command.Parameters.AddWithValue("$sessionId", sessionId);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new Session
                {
                    SessionId = reader.GetString(0),
                    StartTime = reader.GetDateTime(1),
                    LastActive = reader.GetDateTime(2),
                    Status = reader.GetString(3),
                    Metadata = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                };
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting session");
        }

        return null;
    }

    public List<Session> GetSessions()
    {
        try
        {
            using var command = readConnection.CreateCommand();
            command.CommandText = "SELECT session_id, start_time, last_active, status, metadata FROM sessions";

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return [];
            }

            var sessions = new List<Session>();

            do
            {
                sessions.Add(new Session
                {
                    SessionId = reader.GetString(0),
                    StartTime = reader.GetDateTime(1),
                    LastActive = reader.GetDateTime(2),
                    Status = reader.GetString(3),
                    Metadata = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                });
            } while (reader.Read());

            return sessions;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting session");
        }

        return [];
    }

    public Session? GetLatestSession()
    {
        try
        {
            using var command = readConnection.CreateCommand();
            command.CommandText = "SELECT session_id, start_time, last_active, status, metadata FROM sessions ORDER BY last_active DESC LIMIT 1";

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new Session
                {
                    SessionId = reader.GetString(0),
                    StartTime = reader.GetDateTime(1),
                    LastActive = reader.GetDateTime(2),
                    Status = reader.GetString(3),
                    Metadata = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                };
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting latest session");
        }

        return null;
    }

    public void SetState(string sessionId, string key, string value)
    {
        try
        {
            lock (writeLock)
            {
                using var command = writeConnection.CreateCommand();
                command.CommandText = "INSERT OR REPLACE INTO state (key, value, session_id, updated_at) VALUES ($key, $value, $sessionId, $updatedAt)";
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$value", value);
                command.Parameters.AddWithValue("$sessionId", sessionId);
                command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while setting state");
        }
    }

    public List<State>? GetSessionState(string sessionId)
    {
        try
        {
            using var command = readConnection.CreateCommand();
            command.CommandText = "SELECT key, value, updated_at FROM state WHERE session_id = $sessionId";
            command.Parameters.AddWithValue("$sessionId", sessionId);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            var state = new List<State>();

            do
            {
                state.Add(new State
                {
                    Key = reader.GetString(0),
                    Value = reader.GetString(1),
                    UpdatedAt = reader.GetDateTime(2),
                    SessionId = sessionId
                });
            }
            while (reader.Read());

            return state;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting session");
        }

        return null;
    }

    public string? GetSessionState(string sessionId, string key)
    {
        try
        {
            using var command = readConnection.CreateCommand();
            command.CommandText = "SELECT value FROM state WHERE session_id = $sessionId AND key = $key";
            command.Parameters.AddWithValue("$sessionId", sessionId);
            command.Parameters.AddWithValue("$key", key);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return reader.GetString(0);
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting session");
        }

        return null;
    }

    private bool TableExists(string table)
    {
        try
        {
            using var command = readConnection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master where type = 'table' AND name = $table;";
            command.Parameters.AddWithValue("$table", table);
            var result = command.ExecuteScalar();
            return result != null;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Could not determine if table exists: {table}", table);
        }

        return false;
    }

    public void Dispose()
    {
        writeConnection?.Close();
        writeConnection?.Dispose();

        readConnection?.Close();
        readConnection?.Dispose();

        GC.SuppressFinalize(this);
    }

    private void Initialize()
    {
        var tablesCreated = TableExists("sessions") && TableExists("state");
        if (tablesCreated)
        {
            return;
        }

        var schema =
@"CREATE TABLE sessions (
    session_id TEXT PRIMARY KEY,
    start_time DATETIME NOT NULL,
    last_active DATETIME NOT NULL,
    status TEXT NOT NULL,
    metadata TEXT
);

CREATE TABLE state (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    session_id TEXT NOT NULL,
    updated_at DATETIME NOT NULL,
    FOREIGN KEY (session_id) REFERENCES sessions (session_id) ON DELETE CASCADE
);
";

        using var command = writeConnection.CreateCommand();
        command.CommandText = schema;
        command.ExecuteNonQuery();
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
