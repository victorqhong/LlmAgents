using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace LlmAgents.Agents.Capabilities;

public class ToolCallCapability : AgentCapability
{
    private class SessionToolContext
    {
        public readonly Dictionary<string, Tool> ToolMap = [];
        public readonly List<ChatCompletionFunctionTool> ToolDefinitions = [];
    }

    private readonly ILogger log;

    private readonly List<Tool> tools = [];
    private readonly ConcurrentDictionary<Session, SessionToolContext> SessionTools = [];

    public readonly IToolEventBus ToolEventBus;
    public readonly ToolFactory ToolFactory;

    public ToolCallCapability(ILoggerFactory loggerFactory, LlmAgent agent)
        : base(agent)
    {
        log = loggerFactory.CreateLogger<ToolCallCapability>();

        ToolEventBus = new ToolEventBus();

        ToolFactory = new ToolFactory(loggerFactory);
        ToolFactory.Register(ToolEventBus);
        ToolFactory.Register(loggerFactory);
    }

    public Action<Session, string, JsonDocument, JsonNode>? ToolCalled { get; set; }

    public void AddToolDefinition(params Tool[] tools)
    {
        foreach (var tool in tools)
        {
            AddToolDefinition(tool);
        }
    }

    public void AddToolDefinition(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        tools.Add(tool);
    }

    public async Task InitializeSessionTools(Session session)
    {
        var context = new SessionToolContext();
        if (!SessionTools.TryAdd(session, context))
        {
            throw new Exception();
        }

        foreach (var tool in tools)
        {
            context.ToolDefinitions.Add(tool.Schema);
            context.ToolMap.Add(tool.Name, tool);
        }

        if (session != null)
        {
            foreach (var tool in tools)
            {
                await tool.Load(session);
            }
        }
    }

    public List<ChatCompletionFunctionTool> GetToolDefinitions(Session session)
    {
        if (!SessionTools.TryGetValue(session, out var context))
        {
            return [];
        }

        return context.ToolDefinitions;
    }

    public async Task<JsonNode?> CallTool(Session session, string toolName, JsonDocument arguments)
    {
        if (!SessionTools.TryGetValue(session, out var context))
        {
            return null;
        }

        if (!context.ToolMap.TryGetValue(toolName, out var tool))
        {
            return null;
        }

        var result = await tool.Function(session, arguments);
        ToolEventBus?.PostCallToolEvent(tool, arguments, result);
        ToolCalled?.Invoke(session, toolName, arguments, result);

        if (session != null)
        {
            await tool.Save(session);
        }

        return result;
    }

    public async Task<Tool[]> CreateTools(ToolParameters toolParameters)
    {
        List<Tool> tools = [];

        if (File.Exists(toolParameters.McpConfigPath) && JsonSerializer.Deserialize<McpConfig>(File.ReadAllText(toolParameters.McpConfigPath)) is McpConfig mcpConfig)
        {
            foreach (var kvp in mcpConfig.Servers)
            {
                var mcpServer = kvp.Value;
                if (mcpServer is McpServerConfigHttp httpMcpServer)
                {
                    if (!Uri.TryCreate(httpMcpServer.Url, UriKind.Absolute, out var toolServerUri))
                    {
                        continue;
                    }

                    var httpClient = new HttpClient();
                    if (httpMcpServer.Headers != null)
                    {
                        foreach (var header in httpMcpServer.Headers)
                        {
                            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }
                    }

                    // TODO: determine if this is still needed
                    // httpClient.DefaultRequestHeaders.Add("X-Session-Id", session.SessionId);
                    // httpClient.DefaultRequestHeaders.Add("X-Agent-Id", agent.Id);

                    var clientTransport = new HttpClientTransport(
                        new HttpClientTransportOptions { Endpoint = toolServerUri },
                        httpClient
                    );

                    tools.AddRange(await CreateMcpTools(clientTransport, ToolFactory));
                }
                else if (mcpServer is McpServerConfigStdio stdioMcpServer)
                {
                    var stdioTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Command = stdioMcpServer.Command,
                        Arguments = stdioMcpServer.Args,
                        EnvironmentVariables = stdioMcpServer.Env
                    });

                    tools.AddRange(await CreateMcpTools(stdioTransport, ToolFactory));
                }
            }
        }

        if (!string.IsNullOrEmpty(toolParameters.ToolsConfig) && File.Exists(toolParameters.ToolsConfig))
        {
            var toolsFile = JsonSerializer.Deserialize<ToolsConfig>(File.ReadAllText(toolParameters.ToolsConfig));
            if (toolsFile != null)
            {
                tools.AddRange(await ToolFactory.Load(toolsFile));
            }
        }

        return tools.ToArray();
    }

    private async Task<IEnumerable<Tool>> CreateMcpTools(IClientTransport clientTransport, ToolFactory toolFactory)
    {
        try
        {
            var client = await McpClient.CreateAsync(clientTransport);
            var tools = await client.ListToolsAsync();
            return tools.Select(tool => new McpTool(tool, client, toolFactory));
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not create McpTools");
        }

        return [];
    }
}
