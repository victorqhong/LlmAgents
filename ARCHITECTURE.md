# LlmAgents Architecture Documentation

## 1. System Overview
**LlmAgents** is a flexible, modular framework for building and running AI agents in C# (.NET 9). It enables applications to interact with LLMs (OpenAI API-compatible) with support for tool calling, persistent state, and multiple communication channels (Console, XMPP). The system is designed to be extensible, allowing custom tools to be loaded dynamically and agents to be deployed as standalone processes or provisioned containers.

---

## 2. Architectural Style
The system follows a **Layered** and **Component-Based** architecture:
*   **Adapter Pattern**: Communication channels (Console, XMPP) are abstracted to keep the core logic agnostic of I/O.
*   **Strategy Pattern**: The agent loop is composed of discrete "Work" steps (Input, LLM Call, Tool Execution) that can be swapped or extended.
*   **Factory Pattern**: Agents and Tools are instantiated via factories, enabling dynamic loading and dependency injection.
*   **Event-Driven**: The core agent loop and tool execution rely on events and asynchronous callbacks.

---

## 3. Core Framework (`LlmAgents` Library)
The heart of the system is the `LlmAgents` library, which provides the reusable logic for agent orchestration.

### 3.1 The Agent Engine (`Agents/`)
*   **`LlmAgent`**: The central orchestrator. It runs a continuous loop:
    1.  **`GetUserInputWork`**: Waits for input from the `IAgentCommunication` channel.
    2.  **`GetAssistantResponseWork`**: Sends conversation history to the LLM.
    3.  **`ToolCalls`** (Conditional): If the LLM requests tools, this work executes them and feeds results back into the loop until the LLM finishes.
*   **`LlmAgentWork`**: The abstract base class for all steps in the agent loop.
*   **`LlmAgentFactory`**: A static factory class responsible for wiring the LLM API, Communication, Session, and Tools into a runnable `LlmAgent`.

### 3.2 Tool System (`Tools/`)
*   **`Tool`**: Abstract base class defining a tool's schema (JSON) and execution logic (`Function`).
*   **`ToolFactory`**:
    *   Dynamically loads tool assemblies from disk based on `tools.jsonc`.
    *   Provides a lightweight Dependency Injection (DI) container for tools (e.g., injecting `ILogger`, `Session`).
*   **`McpTool`**: Implements support for the **Model Context Protocol (MCP)**, allowing the agent to use tools exposed by remote servers (HTTP or Stdio).

### 3.3 Communication Layer (`Communication/`)
*   **`IAgentCommunication`**: The interface abstracting how the agent speaks to the user.
    *   `WaitForContent`: Asynchronous input waiting.
    *   `SendMessage`: Output streaming.

### 3.4 LLM Abstraction (`LlmApi/`)
*   **`LlmApiOpenAi`**: Handles streaming completions, retry logic for throttling, and request formatting for OpenAI-compatible endpoints.
*   **`LlmApiLlamacpp`**: An alternative implementation for local LLM inference.

### 3.5 State & Persistence (`State/`)
*   **`Session`**: Represents a conversation. Manages loading/saving message history to JSON files.
*   **`StateDatabase`**: An SQLite-backed database for persisting structured state (key-value pairs) that survives session reloads.

---

## 4. Agent Implementations (Deployment Units)
These are the executables that consume the core library.

### 4.1 ConsoleAgent
*   **Purpose**: Interactive CLI application.
*   **I/O**: Uses `ConsoleCommunication` (from the core library).
*   **Flow**: Blocking/Synchronous reads from `StdIn`.
*   **Usage**: Quick testing, local scripts.

### 4.2 XmppAgent
*   **Purpose**: Networked chatbot connecting to XMPP servers (Jabber).
*   **I/O**: Uses `XmppCommunication` (custom implementation).
*   **Flow**: Event-driven. Bridges asynchronous XMPP message streams to the agent loop.
*   **Usage**: Persistent chat integration, multi-user scenarios.

---

## 5. Orchestration & Management (`AgentManager`)
**AgentManager** is a standalone Web Application (ASP.NET Core + Blazor) used to provision and monitor agents.

*   **Tech Stack**:
    *   **UI**: Blazor Server with **MudBlazor** (Material Design).
    *   **Backend**: ASP.NET Core, Entity Framework Core (SQLite).
    *   **Real-time**: SignalR (`AgentHub`).
    *   **Containerization**: LXC (via `ContainerService`).

### Key Services
*   **`ContainerService`**:
    *   Provisioning engine that spins up LXC containers.
    *   Injects configuration files (`api.json`, `xmpp.json`) and scripts into containers to launch `XmppAgent` instances automatically.
*   **`AgentHub` (SignalR)**:
    *   Allows agents to register themselves, push logs, and persist messages to the central database.
    *   Provides real-time updates to the Blazor UI (e.g., live logs).
*   **`GogsService`**:
    *   Integrates with a Gogs Git server to provision users and SSH keys for the agent containers.

---

## 6. Configuration Strategy
The system uses a file-based configuration approach located in the `Agent/` directory (or injected via ContainerService).

*   **`api.jsonc`**: LLM Endpoint, API Key, Model selection.
*   **`tools.jsonc`**:
    *   Maps .NET assemblies to file paths.
    *   Lists specific tool types to load.
    *   Defines tool-specific parameters.
*   **`xmpp.jsonc`**: XMPP server details, credentials, and target JIDs.

---

## 7. Data Flow Example

1.  **User** sends message via XMPP.
2.  **XmppAgent** receives packet → `XmppCommunication` queues content.
3.  **LlmAgent** Loop:
    *   `GetUserInputWork` retrieves content.
    *   `GetAssistantResponseWork` sends history + input to **OpenAI API**.
    *   API returns streaming response.
4.  **Tool Invocation**:
    *   LLM requests `FileRead`.
    *   `ToolCalls` work invokes `ToolFactory`'s registered `FileRead` tool.
    *   Result is added to history.
5.  **Persistence**:
    *   `Session` saves the updated message history to `messages-{id}.json`.
    *   `AgentManager` (if connected) receives a SignalR update and stores the message in its central SQLite DB for the dashboard.

---

## 8. Summary of Design Patterns

| Pattern | Usage | Benefit |
| :--- | :--- | :--- |
| **Adapter** | `IAgentCommunication` | Decouples agent logic from specific I/O drivers. |
| **Strategy** | `LlmAgentWork` | Enables modular, swappable steps in the agent loop. |
| **Factory** | `LlmAgentFactory`, `ToolFactory` | Simplifies complex object creation and dependency injection. |
| **Dynamic Loading** | `ToolFactory.Load` | Allows tools to be updated/reloaded without recompiling the core agent. |
| **Repository** | `Persistence` classes in Manager | Separates data access logic from business services. |
| **Observer** | `IToolEventBus` | Enables side-effects (logging, analytics) without blocking tool execution. |
