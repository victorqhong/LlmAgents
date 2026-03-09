using LlmAgents.Agents.Work;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Agents.Autonomy;

public class AutonomousTaskRunner
{
    private readonly ILogger log;
    private readonly ILoggerFactory loggerFactory;
    private readonly AutonomousTaskStore taskStore;
    private readonly IAgentCommunication agentCommunication;
    private readonly LlmApiOpenAiParameters apiParameters;
    private readonly LlmAgentParameters agentParameters;
    private readonly ToolParameters toolParameters;
    private readonly SessionParameters sessionParameters;

    public AutonomousTaskRunner(
        ILoggerFactory loggerFactory,
        AutonomousTaskStore taskStore,
        IAgentCommunication agentCommunication,
        LlmApiOpenAiParameters apiParameters,
        LlmAgentParameters agentParameters,
        ToolParameters toolParameters,
        SessionParameters sessionParameters)
    {
        this.loggerFactory = loggerFactory;
        this.taskStore = taskStore;
        this.agentCommunication = agentCommunication;
        this.apiParameters = apiParameters;
        this.agentParameters = agentParameters;
        this.toolParameters = toolParameters;
        this.sessionParameters = sessionParameters;
        log = loggerFactory.CreateLogger(nameof(AutonomousTaskRunner));
    }

    public async Task Run(CancellationToken cancellationToken = default, TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested)
        {
            var task = taskStore.TryAcquireNextRunnableTask();
            if (task == null)
            {
                await Task.Delay(interval, cancellationToken);
                continue;
            }

            await ProcessTask(task, cancellationToken);
        }
    }

    private async Task ProcessTask(TaskInstance task, CancellationToken cancellationToken)
    {
        TaskStep? activeStep = null;
        try
        {
            taskStore.AppendEvent(task.Id, "runner_acquired", "Task acquired by autonomous runner");
            task.Policy = AutonomousTaskGuardrails.NormalizePolicy(task.Policy);

            if (task.Steps.Count == 0)
            {
                task.Steps.Add(new TaskStep
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Execute task",
                    Kind = "execute",
                    Sequence = 1
                });
                taskStore.SaveTaskSteps(task.Id, task.Steps);
            }

            activeStep = GetRunnableStep(task);
            if (activeStep == null)
            {
                taskStore.MarkCompleted(task.Id, "No runnable steps remaining.");
                return;
            }

            taskStore.MarkStepRunning(task.Id, activeStep.Id);
            taskStore.AppendEvent(task.Id, "step_started", $"Started step '{activeStep.Title}' ({activeStep.Id})");

            var taskSessionParameters = new SessionParameters
            {
                SessionId = string.IsNullOrEmpty(task.SessionId) ? sessionParameters.SessionId : task.SessionId,
                WorkingDirectory = sessionParameters.WorkingDirectory,
                SystemPromptFile = sessionParameters.SystemPromptFile
            };

            var taskAgentParameters = new LlmAgentParameters
            {
                AgentId = task.AgentId,
                Persistent = false, // checkpoints are persisted through task store
                StorageDirectory = agentParameters.StorageDirectory,
                StreamOutput = agentParameters.StreamOutput
            };

            var agent = await LlmAgentFactory.CreateAgent(
                loggerFactory,
                agentCommunication,
                apiParameters,
                taskAgentParameters,
                toolParameters,
                taskSessionParameters);

            var guardrails = new AutonomousTaskGuardrails(task.Policy);
            agent.PostParseUsage = usage => guardrails.RecordTokenUsage(usage.TotalTokens);

            RestoreCheckpoint(agent, task.CheckpointJson);
            if (agent.RenderConversation().Count == 0)
            {
                agent.AddMessages([JObject.FromObject(new { role = "user", content = task.Goal })]);
            }

            while (true)
            {
                try
                {
                    LlmAgentWork? predecessor = null;
                    while (true)
                    {
                        if (IsCancellationRequested(task, cancellationToken))
                        {
                            taskStore.CancelTask(task.Id, "Task cancelled during execution.");
                            return;
                        }

                        if (guardrails.IsDeadlineExceeded())
                        {
                            var message = $"Task runtime exceeded limit ({task.Policy.MaxRuntimeMinutes} minutes).";
                            taskStore.MarkStepFailed(task.Id, activeStep.Id, message);
                            taskStore.MarkFailed(task.Id, message);
                            return;
                        }

                        var assistantWork = await agent.RunWork(new GetAssistantResponseWork(agent), predecessor, cancellationToken);
                        taskStore.SaveCheckpoint(task.Id, JsonConvert.SerializeObject(agent.RenderConversation()));

                        if (assistantWork.Parser == null)
                        {
                            throw new InvalidOperationException("No parser result returned for assistant response.");
                        }

                        if (guardrails.IsTokenBudgetExceeded())
                        {
                            var message = $"Max token budget exceeded ({task.Policy.MaxTokens}).";
                            taskStore.MarkStepFailed(task.Id, activeStep.Id, message);
                            taskStore.MarkFailed(task.Id, message);
                            return;
                        }

                        if (string.Equals(assistantWork.Parser.FinishReason, "tool_calls", StringComparison.Ordinal))
                        {
                            guardrails.RecordToolCallCycle();
                            if (guardrails.IsToolCallBudgetExceeded())
                            {
                                var message = $"Max tool call cycles exceeded ({task.Policy.MaxToolCalls}).";
                                taskStore.MarkStepFailed(task.Id, activeStep.Id, message);
                                taskStore.MarkFailed(task.Id, message);
                                return;
                            }

                            var toolCallsWork = await agent.RunWork(new ToolCalls(agent), assistantWork, cancellationToken);
                            predecessor = toolCallsWork;
                            taskStore.AppendEvent(task.Id, "tool_call_cycle", $"Completed tool call cycle #{guardrails.ToolCallCycles}");
                            taskStore.SaveCheckpoint(task.Id, JsonConvert.SerializeObject(agent.RenderConversation()));
                            continue;
                        }

                        var finalMessage = assistantWork.Messages?.LastOrDefault()?.Value<string>("content");
                        taskStore.MarkStepDone(task.Id, activeStep.Id);
                        taskStore.MarkRemainingStepsDone(task.Id, activeStep.Id);
                        taskStore.MarkCompleted(task.Id, finalMessage);
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    taskStore.CancelTask(task.Id, "Task cancelled during execution.");
                    return;
                }
                catch (Exception e)
                {
                    log.LogWarning(e, "Autonomous task step failed: {taskId} {stepId}", task.Id, activeStep.Id);
                    var retryCount = taskStore.MarkStepWaitingForRetry(task.Id, activeStep.Id, e.Message);
                    if (retryCount > task.Policy.MaxRetriesPerStep)
                    {
                        var message = $"Step retry limit exceeded ({task.Policy.MaxRetriesPerStep}): {e.Message}";
                        taskStore.MarkStepFailed(task.Id, activeStep.Id, message);
                        taskStore.MarkFailed(task.Id, message);
                        return;
                    }

                    var delay = AutonomousTaskGuardrails.GetRetryBackoffDelay(retryCount);
                    taskStore.AppendEvent(task.Id, "step_retry", $"Retrying step '{activeStep.Title}' in {delay.TotalSeconds:0}s (attempt {retryCount})");
                    if (await WaitForRetry(delay, cancellationToken))
                    {
                        taskStore.CancelTask(task.Id, "Task cancelled during retry backoff.");
                        return;
                    }

                    if (taskStore.IsTaskCancelled(task.Id))
                    {
                        taskStore.CancelTask(task.Id, "Task cancelled during retry backoff.");
                        return;
                    }

                    taskStore.MarkStepRunning(task.Id, activeStep.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            taskStore.CancelTask(task.Id, "Task cancelled.");
        }
        catch (Exception e)
        {
            log.LogError(e, "Autonomous task failed: {taskId}", task.Id);
            if (activeStep != null)
            {
                taskStore.MarkStepFailed(task.Id, activeStep.Id, e.Message);
            }

            taskStore.MarkFailed(task.Id, e.Message);
        }
    }

    private bool IsCancellationRequested(TaskInstance task, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested || taskStore.IsTaskCancelled(task.Id);
    }

    private static TaskStep? GetRunnableStep(TaskInstance task)
    {
        if (!string.IsNullOrWhiteSpace(task.CurrentStepId))
        {
            var current = task.Steps.FirstOrDefault(step =>
                step.Id == task.CurrentStepId
                && (step.State == TaskStepState.Pending || step.State == TaskStepState.Waiting || step.State == TaskStepState.Running));
            if (current != null)
            {
                return current;
            }
        }

        return task.Steps
            .OrderBy(step => step.Sequence)
            .FirstOrDefault(step => step.State == TaskStepState.Pending || step.State == TaskStepState.Waiting || step.State == TaskStepState.Running);
    }

    private static async Task<bool> WaitForRetry(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
    }

    private static void RestoreCheckpoint(LlmAgent agent, string? checkpointJson)
    {
        if (string.IsNullOrWhiteSpace(checkpointJson))
        {
            return;
        }

        var messages = JsonConvert.DeserializeObject<List<JObject>>(checkpointJson);
        if (messages == null)
        {
            throw new InvalidOperationException("Checkpoint JSON could not be deserialized.");
        }

        if (messages.Count > 0)
        {
            agent.AddMessages(messages);
        }
    }
}
