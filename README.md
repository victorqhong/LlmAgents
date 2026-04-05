# LlmAgents

A flexible agent framework for interacting with LLMs that are OpenAI API compatible, supporting **tool calling** and **message persistence**.

## Features

- **ConsoleAgent**: Run on the command line for interactive use.
- **XmppAgent**: Run as a user on an XMPP server for real-time chat interactions.
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

You’ll be prompted to enter messages and interact with the LLM via the command line.

### XmppAgent

After building, run:

```bash
$ dotnet run --project XmppAgent
```

The agent will connect to the XMPP server and respond to messages sent to the configured JID.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

## License

This project is licensed under the **GNU Affero General Public License v3.0** (AGPL-3.0). 
