using LlmAgents.State;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace LlmAgents.Agents.Autonomy;

public class AutonomousTaskStore
{
    private readonly ILogger log;
    private readonly StateDatabase stateDatabase;

    public AutonomousTaskStore(ILoggerFactory loggerFactory, StateDatabase stateDatabase)
    {
        log = loggerFactory.CreateLogger(nameof(AutonomousTaskStore));
        this.stateDatabase = stateDatabase;
        Initialize();
    }

    public bool CreateTask(TaskInstance task)
    {
        try
        {
            task.CreatedAt = DateTime.UtcNow;
            task.UpdatedAt = task.CreatedAt;

            stateDatabase.Write(command =>
            {
                command.CommandText =
                    @"INSERT INTO agent_tasks (
                        id, session_id, conversation_id, agent_id, goal, status, priority, current_step_id, checkpoint_json,
                        result_summary, last_error, policy_json, created_at, updated_at
                    ) VALUES (
                        $id, $sessionId, $conversationId, $agentId, $goal, $status, $priority, $currentStepId, $checkpointJson,
                        $resultSummary, $lastError, $policyJson, $createdAt, $updatedAt
                    );";
                command.Parameters.AddWithValue("$id", task.Id);
                command.Parameters.AddWithValue("$sessionId", task.SessionId ?? string.Empty);
                command.Parameters.AddWithValue("$conversationId", task.ConversationId ?? string.Empty);
                command.Parameters.AddWithValue("$agentId", task.AgentId);
                command.Parameters.AddWithValue("$goal", task.Goal);
                command.Parameters.AddWithValue("$status", ToDb(task.State));
                command.Parameters.AddWithValue("$priority", task.Priority);
                command.Parameters.AddWithValue("$currentStepId", task.CurrentStepId ?? string.Empty);
                command.Parameters.AddWithValue("$checkpointJson", task.CheckpointJson ?? string.Empty);
                command.Parameters.AddWithValue("$resultSummary", task.ResultSummary ?? string.Empty);
                command.Parameters.AddWithValue("$lastError", task.LastError ?? string.Empty);
                command.Parameters.AddWithValue("$policyJson", JsonConvert.SerializeObject(task.Policy));
                command.Parameters.AddWithValue("$createdAt", task.CreatedAt);
                command.Parameters.AddWithValue("$updatedAt", task.UpdatedAt);
                command.ExecuteNonQuery();
            });

            SaveTaskSteps(task.Id, task.Steps);
            AppendEvent(task.Id, "task_created", "Task created");
            return true;
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not create task");
            return false;
        }
    }

    public TaskInstance? GetTask(string id)
    {
        try
        {
            TaskInstance? task = null;
            stateDatabase.Read(command =>
            {
                command.CommandText =
                    @"SELECT id, session_id, conversation_id, agent_id, goal, status, priority, current_step_id, checkpoint_json, result_summary, last_error, policy_json, created_at, updated_at
                      FROM agent_tasks
                      WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return;
                }

                task = ReadTask(reader);
            });

            if (task == null)
            {
                return null;
            }

            task.Steps = ListTaskSteps(task.Id);
            return task;
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not get task");
            return null;
        }
    }

    public List<TaskInstance> ListTasks(string? status = null, int limit = 50)
    {
        try
        {
            List<TaskInstance> tasks = [];
            stateDatabase.Read(command =>
            {
                command.CommandText =
                    @"SELECT id, session_id, conversation_id, agent_id, goal, status, priority, current_step_id, checkpoint_json, result_summary, last_error, policy_json, created_at, updated_at
                      FROM agent_tasks
                      WHERE ($status = '' OR status = $status)
                      ORDER BY created_at DESC
                      LIMIT $limit;";
                command.Parameters.AddWithValue("$status", status?.Trim().ToLowerInvariant() ?? string.Empty);
                command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    tasks.Add(ReadTask(reader));
                }
            });

            foreach (var task in tasks)
            {
                task.Steps = ListTaskSteps(task.Id);
            }

            return tasks;
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not list tasks");
            return [];
        }
    }

    public TaskInstance? TryAcquireNextRunnableTask()
    {
        try
        {
            TaskInstance? task = null;
            stateDatabase.Write(command =>
            {
                command.CommandText =
                    @"SELECT id, session_id, conversation_id, agent_id, goal, status, priority, current_step_id, checkpoint_json, result_summary, last_error, policy_json, created_at, updated_at
                      FROM agent_tasks
                      WHERE status IN ('pending', 'waiting')
                      ORDER BY priority DESC, created_at ASC
                      LIMIT 1;";

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return;
                }

                task = ReadTask(reader);
                reader.Close();

                var now = DateTime.UtcNow;
                command.Parameters.Clear();
                command.CommandText =
                    @"UPDATE agent_tasks
                      SET status = 'running', updated_at = $updatedAt
                      WHERE id = $id;";
                command.Parameters.AddWithValue("$id", task.Id);
                command.Parameters.AddWithValue("$updatedAt", now);
                command.ExecuteNonQuery();

                task.State = TaskState.Running;
                task.UpdatedAt = now;
            });

            if (task != null)
            {
                task.Steps = ListTaskSteps(task.Id);
            }

            return task;
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not acquire runnable task");
            return null;
        }
    }

    public void SaveCheckpoint(string taskId, string checkpointJson)
    {
        UpdateTask(taskId, TaskState.Running, checkpointJson: checkpointJson);
    }

    public void MarkCompleted(string taskId, string? resultSummary = null)
    {
        UpdateTask(taskId, TaskState.Completed, resultSummary: resultSummary);
        AppendEvent(taskId, "task_completed", "Task completed");
    }

    public void MarkFailed(string taskId, string? error = null)
    {
        UpdateTask(taskId, TaskState.Failed, error: error);
        AppendEvent(taskId, "task_failed", error ?? "Task failed");
    }

    public void MarkWaiting(string taskId, string? reason = null)
    {
        UpdateTask(taskId, TaskState.Waiting, error: reason);
        AppendEvent(taskId, "task_waiting", reason ?? "Task waiting");
    }

    public bool ResumeTask(string taskId)
    {
        if (GetTask(taskId) == null)
        {
            return false;
        }

        ResetIncompleteStepsToPending(taskId);
        UpdateTask(taskId, TaskState.Pending, clearError: true, clearCurrentStep: true);
        AppendEvent(taskId, "task_resumed", "Task resumed");
        return true;
    }

    public bool CancelTask(string taskId, string? reason = null)
    {
        if (GetTask(taskId) == null)
        {
            return false;
        }

        var message = reason ?? "Task cancelled";
        CancelIncompleteSteps(taskId, message);
        UpdateTask(taskId, TaskState.Cancelled, error: reason, clearCurrentStep: true);
        AppendEvent(taskId, "task_cancelled", message);
        return true;
    }

    public bool IsTaskCancelled(string taskId)
    {
        var isCancelled = false;
        stateDatabase.Read(command =>
        {
            command.CommandText = "SELECT status FROM agent_tasks WHERE id = $id;";
            command.Parameters.AddWithValue("$id", taskId);
            var result = command.ExecuteScalar()?.ToString();
            isCancelled = string.Equals(result, ToDb(TaskState.Cancelled), StringComparison.OrdinalIgnoreCase);
        });

        return isCancelled;
    }

    public void MarkStepRunning(string taskId, string stepId)
    {
        UpdateStep(taskId, stepId, TaskStepState.Running, clearError: true);
        SetCurrentStep(taskId, stepId);
    }

    public int MarkStepWaitingForRetry(string taskId, string stepId, string? error = null)
    {
        var retryCount = UpdateStep(taskId, stepId, TaskStepState.Waiting, error: error, incrementRetry: true);
        SetCurrentStep(taskId, stepId);
        return retryCount;
    }

    public void MarkStepDone(string taskId, string stepId)
    {
        UpdateStep(taskId, stepId, TaskStepState.Done, clearError: true);
    }

    public void MarkStepFailed(string taskId, string stepId, string? error = null)
    {
        UpdateStep(taskId, stepId, TaskStepState.Failed, error: error);
        SetCurrentStep(taskId, null);
    }

    public void MarkStepCancelled(string taskId, string stepId, string? reason = null)
    {
        UpdateStep(taskId, stepId, TaskStepState.Cancelled, error: reason);
        SetCurrentStep(taskId, null);
    }

    public void MarkRemainingStepsDone(string taskId, string completedStepId)
    {
        var now = DateTime.UtcNow;
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE agent_task_steps
                  SET status = 'done',
                      last_error = '',
                      updated_at = $updatedAt
                  WHERE task_id = $taskId
                    AND id != $completedStepId
                    AND status IN ('pending', 'waiting', 'running');";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$completedStepId", completedStepId);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.ExecuteNonQuery();
        });

        SetCurrentStep(taskId, null);
    }

    public void CancelIncompleteSteps(string taskId, string? reason = null)
    {
        var now = DateTime.UtcNow;
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE agent_task_steps
                  SET status = 'cancelled',
                      last_error = CASE
                        WHEN $reason = '' THEN last_error
                        ELSE $reason
                      END,
                      updated_at = $updatedAt
                  WHERE task_id = $taskId
                    AND status IN ('pending', 'running', 'waiting');";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$reason", reason ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.ExecuteNonQuery();
        });

        SetCurrentStep(taskId, null);
    }

    public void AppendEvent(string taskId, string eventType, string message, string? dataJson = null)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"INSERT INTO agent_task_events (task_id, event_type, message, data_json, created_at)
                  VALUES ($taskId, $eventType, $message, $dataJson, $createdAt);";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$eventType", eventType);
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$dataJson", dataJson ?? string.Empty);
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);
            command.ExecuteNonQuery();
        });
    }

    public List<TaskStep> ListTaskSteps(string taskId)
    {
        try
        {
            List<TaskStep> steps = [];
            stateDatabase.Read(command =>
            {
                command.CommandText =
                    @"SELECT id, title, kind, status, retry_count, sequence, payload_json, last_error, updated_at
                      FROM agent_task_steps
                      WHERE task_id = $taskId
                      ORDER BY sequence ASC;";
                command.Parameters.AddWithValue("$taskId", taskId);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    steps.Add(new TaskStep
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        Kind = reader.GetString(2),
                        State = ParseStepState(reader.GetString(3)),
                        RetryCount = reader.GetInt32(4),
                        Sequence = reader.GetInt32(5),
                        PayloadJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                        LastError = reader.IsDBNull(7) ? null : reader.GetString(7),
                        UpdatedAt = reader.GetDateTime(8)
                    });
                }
            });

            return steps;
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not list task steps");
            return [];
        }
    }

    public List<TaskEvent> ListTaskEvents(string taskId, int limit = 50)
    {
        try
        {
            List<TaskEvent> events = [];
            stateDatabase.Read(command =>
            {
                command.CommandText =
                    @"SELECT id, task_id, event_type, message, data_json, created_at
                      FROM agent_task_events
                      WHERE task_id = $taskId
                      ORDER BY id DESC
                      LIMIT $limit;";
                command.Parameters.AddWithValue("$taskId", taskId);
                command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    events.Add(new TaskEvent
                    {
                        Id = reader.GetInt64(0),
                        TaskId = reader.GetString(1),
                        EventType = reader.GetString(2),
                        Message = reader.GetString(3),
                        DataJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                        CreatedAt = reader.GetDateTime(5)
                    });
                }
            });

            events.Reverse();
            return events;
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not list task events");
            return [];
        }
    }

    public void SaveTaskSteps(string taskId, IEnumerable<TaskStep> steps)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText = "DELETE FROM agent_task_steps WHERE task_id = $taskId;";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.ExecuteNonQuery();

            foreach (var step in steps)
            {
                command.Parameters.Clear();
                command.CommandText =
                    @"INSERT INTO agent_task_steps (
                        id, task_id, title, kind, status, retry_count, sequence, payload_json, last_error, updated_at
                      ) VALUES (
                        $id, $taskId, $title, $kind, $status, $retryCount, $sequence, $payloadJson, $lastError, $updatedAt
                      );";
                command.Parameters.AddWithValue("$id", step.Id);
                command.Parameters.AddWithValue("$taskId", taskId);
                command.Parameters.AddWithValue("$title", step.Title);
                command.Parameters.AddWithValue("$kind", step.Kind);
                command.Parameters.AddWithValue("$status", ToDb(step.State));
                command.Parameters.AddWithValue("$retryCount", step.RetryCount);
                command.Parameters.AddWithValue("$sequence", step.Sequence);
                command.Parameters.AddWithValue("$payloadJson", step.PayloadJson ?? string.Empty);
                command.Parameters.AddWithValue("$lastError", step.LastError ?? string.Empty);
                command.Parameters.AddWithValue("$updatedAt", step.UpdatedAt == default ? DateTime.UtcNow : step.UpdatedAt);
                command.ExecuteNonQuery();
            }
        });
    }

    private void Initialize()
    {
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"CREATE TABLE IF NOT EXISTS agent_tasks (
                    id TEXT PRIMARY KEY,
                    session_id TEXT,
                    conversation_id TEXT,
                    agent_id TEXT NOT NULL,
                    goal TEXT NOT NULL,
                    status TEXT NOT NULL,
                    priority INTEGER NOT NULL,
                    current_step_id TEXT,
                    checkpoint_json TEXT,
                    result_summary TEXT,
                    last_error TEXT,
                    policy_json TEXT,
                    created_at DATETIME NOT NULL,
                    updated_at DATETIME NOT NULL
                  );

                  CREATE TABLE IF NOT EXISTS agent_task_steps (
                    id TEXT PRIMARY KEY,
                    task_id TEXT NOT NULL,
                    title TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    status TEXT NOT NULL,
                    retry_count INTEGER NOT NULL,
                    sequence INTEGER NOT NULL,
                    payload_json TEXT,
                    last_error TEXT,
                    updated_at DATETIME NOT NULL,
                    FOREIGN KEY (task_id) REFERENCES agent_tasks (id) ON DELETE CASCADE
                  );

                  CREATE TABLE IF NOT EXISTS agent_task_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    message TEXT NOT NULL,
                    data_json TEXT,
                    created_at DATETIME NOT NULL,
                    FOREIGN KEY (task_id) REFERENCES agent_tasks (id) ON DELETE CASCADE
                  );";
            command.ExecuteNonQuery();
        });
    }

    private static TaskInstance ReadTask(SqliteDataReader reader)
    {
        var policyJson = reader.IsDBNull(11) ? null : reader.GetString(11);
        TaskPolicy policy;
        try
        {
            policy = !string.IsNullOrEmpty(policyJson)
                ? JsonConvert.DeserializeObject<TaskPolicy>(policyJson) ?? new TaskPolicy()
                : new TaskPolicy();
        }
        catch
        {
            policy = new TaskPolicy();
        }

        return new TaskInstance
        {
            Id = reader.GetString(0),
            SessionId = reader.IsDBNull(1) ? null : reader.GetString(1),
            ConversationId = reader.IsDBNull(2) ? null : reader.GetString(2),
            AgentId = reader.GetString(3),
            Goal = reader.GetString(4),
            State = ParseTaskState(reader.GetString(5)),
            Priority = reader.GetInt32(6),
            CurrentStepId = reader.IsDBNull(7) ? null : NullIfEmpty(reader.GetString(7)),
            CheckpointJson = reader.IsDBNull(8) ? null : NullIfEmpty(reader.GetString(8)),
            ResultSummary = reader.IsDBNull(9) ? null : NullIfEmpty(reader.GetString(9)),
            LastError = reader.IsDBNull(10) ? null : NullIfEmpty(reader.GetString(10)),
            Policy = policy,
            CreatedAt = reader.GetDateTime(12),
            UpdatedAt = reader.GetDateTime(13)
        };
    }

    private void UpdateTask(
        string taskId,
        TaskState state,
        string? checkpointJson = null,
        string? resultSummary = null,
        string? error = null,
        bool clearError = false,
        bool clearCurrentStep = false)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE agent_tasks
                  SET status = $status,
                      checkpoint_json = CASE WHEN $checkpointJson = '' THEN checkpoint_json ELSE $checkpointJson END,
                      result_summary = CASE WHEN $resultSummary = '' THEN result_summary ELSE $resultSummary END,
                       last_error = CASE
                         WHEN $clearError = 1 THEN ''
                         WHEN $error = '' THEN last_error
                         ELSE $error
                       END,
                       current_step_id = CASE
                         WHEN $clearCurrentStep = 1 THEN ''
                         ELSE current_step_id
                       END,
                       updated_at = $updatedAt
                   WHERE id = $id;";
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$status", ToDb(state));
            command.Parameters.AddWithValue("$checkpointJson", checkpointJson ?? string.Empty);
            command.Parameters.AddWithValue("$resultSummary", resultSummary ?? string.Empty);
            command.Parameters.AddWithValue("$error", error ?? string.Empty);
            command.Parameters.AddWithValue("$clearError", clearError ? 1 : 0);
            command.Parameters.AddWithValue("$clearCurrentStep", clearCurrentStep ? 1 : 0);
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
            command.ExecuteNonQuery();
        });
    }

    private void SetCurrentStep(string taskId, string? stepId)
    {
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE agent_tasks
                  SET current_step_id = $currentStepId,
                      updated_at = $updatedAt
                  WHERE id = $id;";
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$currentStepId", stepId ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
            command.ExecuteNonQuery();
        });
    }

    private int UpdateStep(
        string taskId,
        string stepId,
        TaskStepState state,
        string? error = null,
        bool incrementRetry = false,
        bool clearError = false)
    {
        var retryCount = 0;
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE agent_task_steps
                  SET status = $status,
                      retry_count = CASE
                        WHEN $incrementRetry = 1 THEN retry_count + 1
                        ELSE retry_count
                      END,
                      last_error = CASE
                        WHEN $clearError = 1 THEN ''
                        WHEN $error = '' THEN last_error
                        ELSE $error
                      END,
                      updated_at = $updatedAt
                  WHERE task_id = $taskId
                    AND id = $stepId;";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$stepId", stepId);
            command.Parameters.AddWithValue("$status", ToDb(state));
            command.Parameters.AddWithValue("$incrementRetry", incrementRetry ? 1 : 0);
            command.Parameters.AddWithValue("$clearError", clearError ? 1 : 0);
            command.Parameters.AddWithValue("$error", error ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
            command.ExecuteNonQuery();

            command.Parameters.Clear();
            command.CommandText = @"SELECT retry_count FROM agent_task_steps WHERE task_id = $taskId AND id = $stepId;";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$stepId", stepId);
            var result = command.ExecuteScalar();
            retryCount = result == null ? 0 : Convert.ToInt32(result);
        });

        return retryCount;
    }

    private void ResetIncompleteStepsToPending(string taskId)
    {
        var now = DateTime.UtcNow;
        stateDatabase.Write(command =>
        {
            command.CommandText =
                @"UPDATE agent_task_steps
                  SET status = 'pending',
                      last_error = '',
                      updated_at = $updatedAt
                  WHERE task_id = $taskId
                    AND status IN ('waiting', 'failed', 'cancelled');";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.ExecuteNonQuery();
        });
    }

    private static string ToDb(TaskState state) => state.ToString().ToLowerInvariant();

    private static string ToDb(TaskStepState state) => state.ToString().ToLowerInvariant();

    private static TaskState ParseTaskState(string? state) =>
        Enum.TryParse<TaskState>(state, true, out var parsed) ? parsed : TaskState.Pending;

    private static TaskStepState ParseStepState(string? state) =>
        Enum.TryParse<TaskStepState>(state, true, out var parsed) ? parsed : TaskStepState.Pending;

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
