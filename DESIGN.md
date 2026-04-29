# LlmAgents — Design Document

## 1. Overview

**LlmAgents** is a flexible, extensible agent framework for interacting with Large Language Models (LLMs) that expose an OpenAI-compatible chat completions API. It provides a structured agent loop, persistent conversation sessions, a dynamically-loaded tool ecosystem, and multiple front-end interfaces (console, XMPP, web).

**Primary goals:**
- Enable autonomous, tool-calling LLM agents that operate continuously until a user ends the conversation.
- Support interchangeable communication channels without changing agent logic.
- Allow tools to be composed at runtime from .NET assemblies or remote MCP servers.
- Persist conversation history so agents can resume long-running sessions.

**License:** AGPL-3.0  
**Runtime:** .NET 9

---

## 2. Repository Structure

```
LlmAgents/
├── LlmAgents/               # Core library: agent loop, session, LLM API client, tool system
├── LlmAgents.Tools/         # Built-in tool implementations
├── LlmAgents.Api/           # ASP.NET JWT/SignalR auth helpers
├── LlmAgents.CommandLineParser/  # Shared CLI option parsing & config resolution
├── LlmAgents.Tests/         # Unit & integration tests
├── ConsoleAgent/            # Interactive CLI front-end
├── XmppAgent/               # XMPP chat front-end
├── AgentManager/            # Blazor Server management UI
├── ToolServer/              # Standalone MCP tool server
└── ToolTester/              # Manual tool testing harness
```

---

## 3. Core Concepts

### 3.1 Agent Loop (`LlmAgents/Agents/LlmAgent.cs`)

The central agent loop follows this pattern:

```
while not cancelled:
  1. GetUserInput         — wait for content from the communication channel
  2. GetAssistantResponse — send messages to LLM, stream response
  3. while finish_reason == "tool_calls":
       ToolCalls              — execute every requested tool call in parallel
       GetAssistantResponse   — send tool results back, get next response
```

Each step is encapsulated as a `LlmAgentWork` subclass. The loop is composable via factory delegates on `LlmAgent`, allowing front-ends to substitute custom work implementations.

### 3.2 Capabilities

`LlmAgent` exposes two capability objects that can be independently configured:

| Capability | Purpose |
|---|---|
| `SessionCapability` | Manages the active `Session`, optionally persisting messages after each step |
| `ToolCallCapability` | Maintains tool definitions and dispatches tool calls to registered `Tool` objects |

### 3.3 Communication Abstraction (`IAgentCommunication`)

All input/output goes through `IAgentCommunication`:

```csharp
Task<MessageContent[]?> WaitForContent();
Task SendMessage(string message, bool newline);
```

Implementations: `ConsoleCommunication`, XMPP via Sharp XMPP, SignalR hub for the web UI.

---

## 4. LLM API Layer (`LlmAgents/LlmApi/`)

### 4.1 `LlmApiOpenAi`
- Issues streaming HTTP POST requests to `/v1/chat/completions` with `stream: true`.
- Parses Server-Sent Events (SSE) via `ChatCompletionStreamParser`.
- Handles HTTP 429 throttling with configurable exponential retry.
- Supports tool definitions and structured tool call responses.

### 4.2 `LlmApiLlamacpp` (subclass)
- Adds llama.cpp-specific request fields for local model inference.
- Selected automatically when `LlmApiConfig.Llamacpp` is non-null.

### 4.3 Configuration (`LlmApiConfig`)
```json
{
  "apiEndpoint": "https://api.openai.com/v1/chat/completions",
  "apiKey": "...",
  "apiModel": "gpt-4o",
  "temperature": 0.7,
  "maxCompletionTokens": 4096
}
```

---

## 5. Session & State Management (`LlmAgents/State/`)

### 5.1 `Session`
- Holds the in-memory list of `ChatCompletionMessageParam` objects (the conversation history).
- Persists messages to disk as `messages-{sessionId}.json` (indented JSON).
- Stores arbitrary key-value tool state in a SQLite database via `SessionDatabase`.

### 5.2 Ephemeral vs. Persistent Sessions
- **Ephemeral**: in-memory SQLite (`:memory:`), created fresh each run.
- **Persistent**: file-backed SQLite at `{storageDirectory}/{agentId}.db`, messages saved after every work step.

### 5.3 Session Selection (at startup)
Sessions are selected by string key: `"new"`, `"latest"`, `"choose"` (interactive list), or an explicit UUID.

---

## 6. Tool System (`LlmAgents/Tools/` + `LlmAgents.Tools/`)

### 6.1 `Tool` (abstract base)
Every tool exposes:
- `Schema` — `ChatCompletionFunctionTool` (name, description, JSON parameter schema)
- `Function(Session, JsonDocument) → Task<JsonNode>` — the actual implementation
- Optional `Load(Session)` / `Save(Session)` for stateful tools

### 6.2 `ToolFactory`
- Acts as a lightweight DI container for shared services (logger, communication, state DB, agent).
- Loads tools at runtime by deserializing `tools.json` → resolving assembly names → reflecting on types.
- Calls `IToolAssemblyInitializer` hooks on freshly loaded assemblies.
- Passes string `parameters` map to tools for configuration.

### 6.3 `McpTool` — MCP Client Adapter
Wraps a remote MCP (Model Context Protocol) server tool as a local `Tool`. Connects via:
- **HTTP/SSE** (`SseClientTransport`) — e.g. to a running `ToolServer` instance
- **stdio** (`StdioClientTransport`) — subprocess launch

### 6.4 Built-in Tools (`LlmAgents.Tools/`)

| Category | Tools |
|---|---|
| **Shell** | `shell_exec`, `shell_read`, `shell_write`, `shell_status`, `shell_interrupt`, `shell_stop` — manage long-running interactive shell processes |
| **File** | `file_read`, `file_write`, `file_list`, `directory_change` — all path-restricted to a `basePath` |
| **Web** | `web_search` — Tavily API integration |
| **Todo** | `todo_create/read/update/delete`, `todo_group_*` — structured task tracking persisted in SQLite |
| **Background Jobs** | `start_job`, `stop_job`, `job_status`, `job_output` — run shell commands asynchronously |
| **SQLite** | `sqlite_file_run`, `sqlite_sql_run` — execute SQL queries |
| **Agent meta** | `agent_context_count`, `agent_context_prune`, `session_new` — manage conversation context |
| **Diff** | `apply_diff` — apply unified diff patches to files |
| **Ask** | `ask_question` — prompt the user for input mid-tool-chain |

### 6.5 Shell Session Architecture
Shell tools manage processes via `ShellSessionManager`. Two backends:
- **`ProcessShellSession`** — standard `System.Diagnostics.Process`
- **`PtyShellSession`** — wraps a native `libptyhelper.so` (C, built via `build.sh`) for full PTY support, enabling interactive programs like `vim` or `python`

---

## 7. Front-Ends

### 7.1 ConsoleAgent
- Uses `System.CommandLine` for argument parsing.
- Sub-commands: default (run agent) and `sessions` (list/manage saved sessions).
- Config resolved from: current directory → `~/.llmagents` → environment variables (`LLMAGENTS_*`).

### 7.2 XmppAgent
- Connects to an XMPP server using a configured JID/password.
- Filters incoming messages to a single trusted target JID (`xmppTargetJid`).
- Runs the standard agent loop over the XMPP channel.

### 7.3 AgentManager (Blazor Server)
A web management UI with:
- **Authentication**: GitHub OAuth + JWT/refresh-token flow (`LlmAgents.Api`)
- **Agent hub**: SignalR (`AgentHub`) for real-time streaming of agent messages to the browser
- **Pages**: Home, Sessions, Messages, Logs, Containers (LXC provisioning), Admin
- **Services**: `AgentMessageService`, `AgentSessionService`, `AgentStateService`, `AgentLogService`, `ContainerService`, `GogsService`
- **Storage**: EF Core + SQLite (`agents.db`)
- **Proxy support**: Configurable `X-Forwarded-*` headers for reverse proxy deployments

### 7.4 ToolServer
- Exposes any tool set as a standalone **MCP server**.
- Supports both **stdio** and **HTTP** transports simultaneously.
- Tools are configured via the same `tools.json` format used by agents.
- Tools are wrapped by `McpToolAdapter` to satisfy the MCP SDK's interface.

---

## 8. Configuration Files

| File | Consumed By | Purpose |
|---|---|---|
| `api.json` | All agents | LLM endpoint, key, model |
| `tools.json` | Agent / ToolServer | Assembly list + tool type list + parameters |
| `mcp.json` | Agent | MCP server connections (HTTP or stdio) |
| `xmpp.json` | XmppAgent | XMPP credentials & target JID |
| `appsettings.json` | AgentManager | ASP.NET, DB, proxy, auth config |

---

## 9. Extension Points

| Extension Point | How |
|---|---|
| New tool | Implement `Tool`, deploy in any .NET assembly, reference in `tools.json` |
| New LLM backend | Subclass `LlmApiOpenAi` or implement equivalent |
| New communication channel | Implement `IAgentCommunication` |
| Custom agent work | Replace `CreateUserInputWork` / `CreateAssistantResponseWork` / `CreateToolCallsWork` delegates |
| Assembly initialization | Apply `[ToolAssemblyInit]` attribute, implement `IToolAssemblyInitializer` |
| Remote tools | Run `ToolServer` and connect via `mcp.json` |

---

## 10. CI / Build

- **Build**: `dotnet build -c Release`
- **Tests**: `dotnet test --filter TestCategory!=Integration` (integration tests require live credentials and are excluded from CI)
- **Workflows**: `.github/workflows/ci.yml` (build + test on push/PR), `.github/workflows/publish.yml` (release artifacts)

---

## 11. Key Design Decisions

1. **OpenAI-compatible API as the universal interface** — Any endpoint (OpenAI, Azure, Ollama, llama.cpp, etc.) works without code changes.
2. **Tool loading via reflection** — Tools are decoupled from the agent binary; new tools are deployed as DLLs, not recompiled into the host.
3. **MCP as the interoperability boundary** — `ToolServer` bridges LlmAgents tools to any MCP-aware client; `McpTool` bridges MCP servers into the LlmAgents tool system.
4. **SQLite everywhere** — Both session metadata and tool state use SQLite for zero-infrastructure persistence.
5. **PTY support for interactive shells** — The `libptyhelper.so` native helper enables full terminal emulation, allowing the agent to operate programs that require a TTY.
6. **Session as a first-class object** — Conversations are identifiable, listable, resumable, and shareable across sessions (via session IDs).
