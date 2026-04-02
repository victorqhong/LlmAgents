namespace LlmAgents.State;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

public class SessionDatabase
{
    private readonly ILoggerFactory loggerFactory;

    private readonly StateDatabase stateDatabase;

    public Action<string, string, string>? OnStateChange { get; set; }

    public SessionDatabase(ILoggerFactory loggerFactory, StateDatabase stateDatabase)
    {
        this.loggerFactory = loggerFactory;
        this.stateDatabase = stateDatabase;

        Initialize();
    }

    public void CreateSession(Session session)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText = "INSERT INTO sessions (session_id, start_time, last_active, metadata) VALUES ($sessionId, $startTime, $lastActive, $metadata)";
            command.Parameters.AddWithValue("$sessionId", session.SessionId);
            command.Parameters.AddWithValue("$startTime", session.StartTime);
            command.Parameters.AddWithValue("$lastActive", session.LastActive);
            command.Parameters.AddWithValue("$metadata", session.Metadata);
            command.ExecuteNonQuery();
        });
    }

    public Session? GetSession(string sessionId)
    {
        Session? session = null;
        stateDatabase.Read(command =>
        {
            command.CommandText = "SELECT session_id, start_time, last_active, metadata FROM sessions WHERE session_id = $sessionId";
            command.Parameters.AddWithValue("$sessionId", sessionId);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                session = new Session(sessionId, this)
                {
                    StartTime = reader.GetDateTime(1),
                    LastActive = reader.GetDateTime(2),
                    Metadata = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                };
            }
        });

        return session;
    }
    public List<Session> GetSessions()
    {
        var sessions = new List<Session>();
        stateDatabase.Read(command =>
        {
            command.CommandText = "SELECT session_id, start_time, last_active, metadata FROM sessions";

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return;
            }

            do
            {
                var sessionId = reader.GetString(0);
                sessions.Add(new Session(sessionId, this)
                {
                    StartTime = reader.GetDateTime(1),
                    LastActive = reader.GetDateTime(2),
                    Metadata = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                });
            } while (reader.Read());
        });

        return sessions;
    }

    public Session? GetLatestSession()
    {
        Session? session = null;
        stateDatabase.Read(command =>
        {
            command.CommandText = "SELECT session_id, start_time, last_active, metadata FROM sessions ORDER BY last_active DESC LIMIT 1";

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var sessionId = reader.GetString(0);
                session = new Session(sessionId, this)
                {
                    StartTime = reader.GetDateTime(1),
                    LastActive = reader.GetDateTime(2),
                    Metadata = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                };
            }
        });

        return session;
    }

    public void UpdateSessionTime(string sessionId, DateTime updateTime)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText = "UPDATE sessions SET last_active = $updateTime WHERE session_id = $sessionId";
            command.Parameters.AddWithValue("$sessionId", sessionId);
            command.Parameters.AddWithValue("$updateTime", updateTime);
            command.ExecuteNonQuery();
        });
    }

    public void SetState(string sessionId, string key, string value)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText = "INSERT OR REPLACE INTO state (key, value, session_id, updated_at) VALUES ($key, $value, $sessionId, $updatedAt)";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$sessionId", sessionId);
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
            command.ExecuteNonQuery();
        });

        OnStateChange?.Invoke(sessionId, key, value);
    }

    public List<SessionState>? GetSessionState(string sessionId)
    {
        List<SessionState>? state = null;
        stateDatabase.Read(command =>
        {
            command.CommandText = "SELECT key, value, updated_at FROM state WHERE session_id = $sessionId";
            command.Parameters.AddWithValue("$sessionId", sessionId);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return;
            }

            state = [];

            do
            {
                state.Add(new SessionState
                {
                    Key = reader.GetString(0),
                    Value = reader.GetString(1),
                    UpdatedAt = reader.GetDateTime(2),
                    SessionId = sessionId
                });
            }
            while (reader.Read());

        });

        return state;
    }

    public string? GetSessionState(string sessionId, string key)
    {
        string? state = null;
        stateDatabase.Read(command =>
        {
            command.CommandText = "SELECT value FROM state WHERE session_id = $sessionId AND key = $key";
            command.Parameters.AddWithValue("$sessionId", sessionId);
            command.Parameters.AddWithValue("$key", key);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                state = reader.GetString(0);
            }
        });

        return state;
    }

    private bool TableExists(string table)
    {
        var exists = false;
        stateDatabase.Read(command =>
        {
            command.CommandText = "SELECT 1 FROM sqlite_master where type = 'table' AND name = $table;";
            command.Parameters.AddWithValue("$table", table);
            var result = command.ExecuteScalar();
            exists = result != null;
        });

        return exists;
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

        stateDatabase.Write(command =>
        {
            command.CommandText = schema;
            command.ExecuteNonQuery();
        });
    }

}
