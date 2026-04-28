# LlmAgents

A flexible agent framework for interacting with LLMs that are OpenAI API compatible, supporting **tool calling** and **message persistence**.

## Features

- **ConsoleAgent**: Run on the command line for interactive use.
- **XmppAgent**: Run as a user on an XMPP server for real-time chat interactions.
- **ToolTester**: Test individual tools interactively from the command line.
- **ToolServer**: Expose tools via MCP (Model Context Protocol) over HTTP or stdio.
- **AgentManager**: Web-based dashboard for managing agents.
- Supports arbitrary tools via .NET assemblies.
- Persistent message history for context retention.
- Configurable via JSON files.

## Setup

### Prerequisites

- .NET 9 SDK

### Build

```bash
# Build the project
$ dotnet build
```

This will build both `ConsoleAgent` and `XmppAgent`.

### Test

```bash
# Run tests (excludes integration tests)
$ dotnet test --configuration Release --verbosity normal --filter "TestCategory!=Integration"
```

### Integration Tests

Integration tests are located in `LlmAgents.Tests/TestLlmAgent.cs` and make real API calls to an LLM provider. They are excluded from CI runs by default.

**Requirements:**
- `LLMAGENTS_API_KEY` - Your API key
- `LLMAGENTS_API_ENDPOINT` - API endpoint URL (e.g., `https://api.openai.com/v1`)
- `LLMAGENTS_API_MODEL` - Model name (e.g., `gpt-4`)

**Run integration tests:**
```bash
$ export LLMAGENTS_API_KEY="your-api-key"
$ export LLMAGENTS_API_ENDPOINT="https://api.openai.com/v1"
$ export LLMAGENTS_API_MODEL="gpt-4"
$ dotnet test --configuration Release --filter "TestCategory=Integration"
```

## Configuration

All configuration is done via JSON files in the `Agent` directory:

### API Configuration (`Agent/api.json`)

```json
{
  "apiEndpoint": "https://api.openai.com/v1",
  "apiKey": "your-api-key-here",
  "apiModel": "gpt-4"
}
```

> Replace the placeholders with your actual API endpoint, key, and model name.

### Tools Configuration (`Agent/tools.json`)

```json
{
    "assemblies": {
        "LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null": "..\\..\\LlmAgents\\LlmAgents.Tools\\bin\\Debug\\net9.0\\LlmAgents.Tools.dll"
    },
    "types": [
        "LlmAgents.Tools.ShellExec, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "LlmAgents.Tools.ShellRead, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "LlmAgents.Tools.ShellWrite, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "LlmAgents.Tools.ShellStatus, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "LlmAgents.Tools.ShellInterrupt, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "LlmAgents.Tools.ShellStop, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
    ],
    "parameters": {
        "restrictToBasePath": "true",
        "Shell.waitTimeMs": "180000"
    }
}
```

> This config loads the split shell tools (`shell_exec`, `shell_read`, `shell_write`, `shell_status`, `shell_interrupt`, `shell_stop`).

### XMPP Configuration (`Agent/xmpp.json`)

```json
{
  "xmppDomain": "example.com",
  "xmppUsername": "agent@example.com",
  "xmppPassword": "your-xmpp-password",
  "xmppTargetJid": "user@example.com",
  "xmppTrustHost": false
}
```

> Configure the XMPP server details and credentials. Set `xmppTargetJid` to the JID (Jabber ID) of the user you want to interact with.

## Usage

### ConsoleAgent

After building, run:

```bash
$ dotnet run --project ConsoleAgent
```

You'll be prompted to enter messages and interact with the LLM via the command line.

### XmppAgent

After building, run:

```bash
$ dotnet run --project XmppAgent
```

The agent will connect to the XMPP server and respond to messages sent to the configured JID.

### ToolTester

Interactive CLI to test individual tools:

```bash
$ dotnet run --project ToolTester -- --tools-config Agent/tools.json --working-directory /path/to/project
```

This will list available tools by number, let you select one, show its schema, and then prompt for JSON parameters to execute it.

### ToolServer

MCP server that exposes tools via HTTP or stdio:

```bash
$ dotnet run --project ToolServer -- --tools-config Agent/tools.json --working-directory /path/to/project --port 5000
```

Tools are exposed via the MCP protocol and can be accessed by MCP-compatible clients. Use `--host` to change the listen address (default: `127.0.0.1`).

### AgentManager

Web-based dashboard for managing agents:

```bash
$ dotnet run --project AgentManager
```

Access the web UI to manage agents, LXC containers, and Gogs repositories.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

## License

This project is licensed under the **GNU Affero General Public License v3.0** (AGPL-3.0).