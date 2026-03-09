# Copilot Instructions for LlmAgents

## Build and test commands

- Prerequisite: .NET SDK 9 (`net9.0` target across projects).
- Restore solution dependencies:
  - `dotnet restore`
- Build all projects (CI-aligned):
  - `dotnet build --no-restore --configuration Release`
- Run full test suite (CI-aligned):
  - `dotnet test --no-build --configuration Release --filter "TestCategory!=Integration"`
- Run a single test method (MSTest):
  - `dotnet test .\LlmAgents.Tests\LlmAgents.Tests.csproj --filter "FullyQualifiedName~LlmAgents.Tests.TestToolFactoryLoad.Load_Successful"`
- Run autonomous task commands in ConsoleAgent:
  - `dotnet run --project ConsoleAgent -- task submit "Implement feature X"`
  - `dotnet run --project ConsoleAgent -- task runner --apiConfig Agent\api.json --toolsConfig Agent\tools.json`

## High-level architecture

- `LlmAgents` is the core library: it owns the agent loop, OpenAI-compatible streaming API client, tool abstraction, and SQLite-backed session/state persistence.
  - Agent execution flow is modeled as `LlmAgentWork` units (`GetUserInputWork` → `GetAssistantResponseWork` → `ToolCalls` loop while finish reason is `tool_calls`).
  - `LlmAgentFactory` composes runtime dependencies (communication channel, API client, state DB, tool loading, session restore/new session selection).
  - Autonomous orchestration primitives live under `LlmAgents\Agents\Autonomy\` (`TaskInstance` model, `AutonomousTaskStore`, `AutonomyCoordinator`, and `AutonomousTaskRunner`).
- App entrypoints are thin hosts around the core library:
  - `ConsoleAgent`: interactive terminal host.
  - `XmppAgent`: XMPP-backed host (single-agent and multi-agent config modes).
  - `ToolServer`: exposes loaded tools as an MCP server over HTTP/SSE and stdio.
  - `ToolTester`: interactive local harness to manually invoke tools from a tools config file.
- Tool system has two integration directions:
  - Local tools are loaded dynamically from assemblies/types listed in JSON config via `ToolFactory`.
  - External MCP tools are imported into the agent via `mcp.json` (`http` or `stdio`) and wrapped as `McpTool`.
  - `ToolServer` does the inverse by adapting local tools to MCP (`McpToolAdapter`).
- Persistence model:
  - Per-agent SQLite DB: `<storageDirectory>\<agentId>.db` for sessions and key/value state.
  - Optional message persistence: `messages-<agentId>.json` when `--persistent` is enabled.

## Key repository conventions

- CLI config resolution is centralized in `LlmAgents.CommandLineParser.Config` and follows this order for config path options:
  1. file in current working directory
  2. file in `%USERPROFILE%\.llmagents\`
  3. environment-variable path (for the specific option)
- Tool config format is assembly-driven (see `Agent\tools.jsonc`):
  - `assemblies` maps assembly display name → DLL path
  - `types` contains fully-qualified type names including assembly display name
  - `parameters` is string-based and consumed by tools/factory (e.g., `basePath`, `restrictToBasePath`, `Shell.waitTimeMs`)
- Tool assemblies can register dependencies at load time using `[ToolAssemblyInit]` + `IToolAssemblyInitializer` (example in `LlmAgents.Tools\ToolAssemblyInit.cs` registers todo/job services).
- MCP session continuity for HTTP tool servers depends on headers:
  - `X-Session-Id` and `X-Agent-Id` are set by `LlmAgentFactory` for HTTP MCP clients and consumed by `ToolServer` to bind tool calls to persisted session state.
