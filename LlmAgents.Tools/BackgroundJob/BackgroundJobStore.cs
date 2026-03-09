using LlmAgents.State;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace LlmAgents.Tools.BackgroundJob;

public class BackgroundJobRecord
{
    public Guid Id { get; init; }
    public string Command { get; init; } = string.Empty;
    public string ArgsJson { get; init; } = string.Empty;
    public JobStatus Status { get; init; } = JobStatus.Running;
    public int? ExitCode { get; init; }
    public DateTime Started { get; init; }
    public DateTime? Ended { get; init; }
}

public class BackgroundJobStore
{
    private readonly ILogger log;
    private readonly StateDatabase stateDatabase;

    public BackgroundJobStore(ILoggerFactory loggerFactory, StateDatabase stateDatabase)
    {
        log = loggerFactory.CreateLogger(nameof(BackgroundJobStore));
        this.stateDatabase = stateDatabase;
        Initialize();
    }

    public void CreateJob(Guid id, string command, IReadOnlyCollection<string> args)
    {
        stateDatabase.Write(commandBuilder =>
        {
            commandBuilder.CommandText =
                @"INSERT INTO background_jobs (id, command, args_json, status, output, output_cursor, started_at)
                  VALUES ($id, $command, $argsJson, $status, '', 0, $startedAt);";
            commandBuilder.Parameters.AddWithValue("$id", id.ToString());
            commandBuilder.Parameters.AddWithValue("$command", command);
            commandBuilder.Parameters.AddWithValue("$argsJson", JsonConvert.SerializeObject(args));
            commandBuilder.Parameters.AddWithValue("$status", JobStatus.Running.ToString().ToLowerInvariant());
            commandBuilder.Parameters.AddWithValue("$startedAt", DateTime.UtcNow);
            commandBuilder.ExecuteNonQuery();
        });
    }

    public void AppendOutput(Guid id, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE background_jobs
                  SET output = COALESCE(output, '') || $text
                  WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$text", text);
            command.ExecuteNonQuery();
        });
    }

    public void UpdateStatus(Guid id, JobStatus status, int? exitCode = null, DateTime? ended = null)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE background_jobs
                  SET status = $status,
                      exit_code = CASE WHEN $hasExitCode = 1 THEN $exitCode ELSE exit_code END,
                      ended_at = CASE WHEN $hasEnded = 1 THEN $endedAt ELSE ended_at END
                  WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$status", status.ToString().ToLowerInvariant());
            command.Parameters.AddWithValue("$hasExitCode", exitCode.HasValue ? 1 : 0);
            command.Parameters.AddWithValue("$exitCode", exitCode ?? 0);
            command.Parameters.AddWithValue("$hasEnded", ended.HasValue ? 1 : 0);
            command.Parameters.AddWithValue("$endedAt", ended ?? DateTime.UtcNow);
            command.ExecuteNonQuery();
        });
    }

    public BackgroundJobRecord? GetJob(Guid id)
    {
        try
        {
            BackgroundJobRecord? record = null;
            stateDatabase.Read(command =>
            {
                command.CommandText =
                    @"SELECT id, command, args_json, status, exit_code, started_at, ended_at
                      FROM background_jobs
                      WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id.ToString());
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return;
                }

                record = new BackgroundJobRecord
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Command = reader.GetString(1),
                    ArgsJson = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Status = ParseStatus(reader.GetString(3)),
                    ExitCode = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Started = reader.GetDateTime(5),
                    Ended = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                };
            });

            return record;
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not get background job");
            return null;
        }
    }

    public bool TryGetOutputAndCursor(Guid id, out string output, out int cursor)
    {
        var readOutput = string.Empty;
        var readCursor = 0;

        var found = false;
        stateDatabase.Read(command =>
        {
            command.CommandText =
                @"SELECT output, output_cursor
                  FROM background_jobs
                  WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return;
            }

            readOutput = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            readCursor = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            found = true;
        });

        output = readOutput;
        cursor = readCursor;
        return found;
    }

    public void SetCursor(Guid id, int cursor)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE background_jobs
                  SET output_cursor = $cursor
                  WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$cursor", Math.Max(0, cursor));
            command.ExecuteNonQuery();
        });
    }

    private void Initialize()
    {
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"CREATE TABLE IF NOT EXISTS background_jobs (
                    id TEXT PRIMARY KEY,
                    command TEXT NOT NULL,
                    args_json TEXT,
                    status TEXT NOT NULL,
                    output TEXT NOT NULL DEFAULT '',
                    output_cursor INTEGER NOT NULL DEFAULT 0,
                    started_at DATETIME NOT NULL,
                    ended_at DATETIME,
                    exit_code INTEGER
                  );";
            command.ExecuteNonQuery();
        });
    }

    private static JobStatus ParseStatus(string? status) =>
        Enum.TryParse<JobStatus>(status, true, out var parsed) ? parsed : JobStatus.Running;
}
