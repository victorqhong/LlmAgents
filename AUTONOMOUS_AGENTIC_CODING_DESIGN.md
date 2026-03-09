# Autonomous Agentic Coding Design

## Context

`LlmAgents` already has strong building blocks for tool-driven execution:

- Core agent loop in `LlmAgents\Agents\LlmAgent.cs` (`GetUserInputWork` -> `GetAssistantResponseWork` -> `ToolCalls` loop).
- Dynamic tool loading and dependency injection in `LlmAgents\Tools\ToolFactory.cs`.
- Session and key-value persistence in `LlmAgents\State\StateDatabase.cs`.
- Background process tools in `LlmAgents.Tools\BackgroundJob\` (`start_job`, `job_status`, `job_output`, `stop_job`).
- Multi-surface hosts (`ConsoleAgent`, `XmppAgent`, `ToolServer`).

The goal is to support autonomous coding runs that can continue reliably for minutes or hours with resumability and observability.

## Current Support vs. Target

### What works today

1. **Autonomous micro-loops inside one turn**
   - Once a response includes `tool_calls`, the agent can execute tool calls and continue iterating without additional user input.
2. **Long-running external commands are possible**
   - `start_job` can spawn a process and return a `job_id`, while status/output can be polled later.
3. **Basic persistence exists**
   - Sessions/state are persisted in SQLite and messages can be persisted when `--persistent` is enabled.

### What is missing for hours-long autonomy

1. **Run model is still user-input-gated**
   - `LlmAgent.Run` always begins by waiting on `GetUserInputWork`; there is no first-class autonomous task queue/scheduler.
2. **No durable orchestration state machine**
   - There is no persisted concept of a long-running task with checkpoints, retries, dependencies, and resumable step state.
3. **Background jobs are process-memory scoped**
   - `JobManager` stores jobs/output in memory only; restart loses job metadata/output and polling context.
4. **No durable log cursoring**
   - `job_output` returns buffered output text but has no persisted offset/cursor model for incremental consumption at scale.
5. **No built-in reliability policies**
   - Missing backoff/retry policies, heartbeat/timeout semantics, and recovery behavior after host crash/restart.
6. **No autonomous control plane**
   - No command/API surface for enqueueing tasks, pausing/resuming, querying progress, or enforcing task budgets.

### Important clarification

- Durability is a key enabler, but it is not the only architectural difference.
- The bigger behavioral change is introducing an explicit orchestration state machine (task/step lifecycle, wait states, retry/backoff, completion checks) around existing `LlmAgent` work primitives.

## Proposed Architecture

### 1) Durable Task Orchestrator (new core layer)

Add a persisted orchestration model in `LlmAgents`:

- `agent_tasks` table: task id, session id, goal, status, priority, created/updated timestamps.
- `agent_task_steps` table: ordered steps with state (`pending`, `running`, `waiting`, `done`, `failed`), retry count, last error, checkpoint payload.
- `agent_task_events` table: append-only progress/events for observability.

New service (example): `AutonomousTaskRunner`

- Pulls runnable tasks.
- Executes a bounded step loop (plan -> act -> observe -> decide next step).
- Writes checkpoint after each step and on tool completion.
- Recovers unfinished tasks on startup.

### 2) Autonomous Run Mode (new entrypoint behavior)

Keep interactive mode unchanged; add autonomous mode in host apps:

- `ConsoleAgent`: add commands like `task submit`, `task status`, `task resume`, `task cancel`.
- `ToolServer`: expose equivalent MCP tools/endpoints to operate task lifecycle remotely.
- `XmppAgent`: optionally map specific message patterns to task submission and async progress replies.

This decouples long-running execution from synchronous chat turn handling.

Single-path chat UX can still be preserved:

- User sends one high-level goal in chat.
- A coordinator decides inline response vs autonomous execution.
- If autonomous, the coordinator creates a task and starts/assigns it to the runner automatically.
- Progress and completion updates are posted back to the same conversation.

### 3) Durable Background Jobs

Upgrade `LlmAgents.Tools.BackgroundJob` behavior:

- Persist job metadata in SQLite (`jobs` table with command, args, start/end, status, exit code).
- Persist output incrementally (chunk table or log file references + offsets).
- Extend APIs:
  - `job_output` supports cursor/offset + max bytes.
  - `job_status` includes heartbeat and stalled-state indicators.
  - Add `job_list` for discovery/recovery.

This allows restart-safe polling and long process tracking.

### 4) Reliability and Safety Controls

Add execution policies at task level:

- Retry policy per step/tool (max attempts + backoff).
- Deadlines and max runtime.
- Budget limits (tool calls, tokens, command wall time).
- Explicit cancellation propagation to running jobs/tool calls.

Store all policy settings with each task for deterministic replay and auditing.

### 5) Observability

Define a consistent telemetry contract:

- Task progress (% / current phase / current step).
- Last successful checkpoint timestamp.
- Recent event stream and last error details.
- Correlation IDs linking task -> tool calls -> background jobs.

Surface this in CLI and MCP so external operators can monitor and intervene.

### 6) Orchestration Domain Model (TaskInstance)

Add a first-class task model (not just message history):

- `TaskInstance`: `id`, `conversationId`, `goal`, `state`, `policy`, `currentStepId`, `checkpoint`, `resultSummary`, `lastError`.
- `TaskStep`: `id`, `kind`, `state`, `retryCount`, `payload`, `lastError`, timestamps.
- `TaskPolicy`: retry limits, backoff strategy, max wall time, tool/token budgets.

Ingress translation from user input:

1. User message arrives on existing chat surface.
2. `AutonomyCoordinator` classifies whether request requires long-running autonomy.
3. If yes, create `TaskInstance` from message + session metadata + default policy.
4. Persist task and return immediate acknowledgement in the same chat.
5. `AutonomousTaskRunner` executes steps until terminal state, emitting events/progress.

## Implementation Plan (Incremental)

### Phase 1: Durable orchestration skeleton

- Add task/step/event schema and repository layer in `LlmAgents`.
- Add autonomous runner loop with checkpointing and restart recovery.
- Add `AutonomyCoordinator` to translate chat requests into `TaskInstance` records and dispatch them.
- Add minimal CLI commands to submit and query tasks.

### Phase 2: Durable background jobs

- Persist job lifecycle and output with cursored reads.
- Update `start_job`/`job_status`/`job_output`/`stop_job` to use durable storage.

### Phase 3: Policy + control plane

- Add retries, backoff, deadlines, and budget enforcement.
- Add pause/resume/cancel semantics and richer status reporting.
- Expose the same controls in `ToolServer`/MCP.

## Acceptance Criteria

1. A submitted coding task continues processing without new user messages.
2. A single high-level chat goal can automatically become a tracked autonomous task in the same conversation.
3. Host restart does not lose task/job state; runner resumes from checkpoints.
4. Long-running commands remain inspectable via cursored output reads.
5. Operators can pause/resume/cancel tasks and see deterministic progress/error state.
6. Existing interactive chat behavior remains backward-compatible.

