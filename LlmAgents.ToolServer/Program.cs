using LlmAgents.Communication;
using LlmAgents.Todo;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Net.Sockets;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var listenAddressOption = new Option<string>(
    name: "--host",
    description: "The address to listen on",
    getDefaultValue: () => "127.0.0.1");

var listenPortOption = new Option<int>(
    name: "--port",
    description: "The port to listen on",
    getDefaultValue: () => 5000);

var toolsConfigOption = new Option<string>(
    name: "--toolsConfig",
    description: "Path to a JSON file with configuration for tool values",
    getDefaultValue: () => "tools.json");

var rootCommand = new RootCommand("ToolServer");
rootCommand.AddOption(listenAddressOption);
rootCommand.AddOption(listenPortOption);
rootCommand.AddOption(toolsConfigOption);
rootCommand.SetHandler(RootCommandHandler);

async Task RootCommandHandler(InvocationContext context)
{
    var cancellationToken = context.GetCancellationToken();

    var listenAddress = context.ParseResult.GetValueForOption(listenAddressOption);
    var listenPort = context.ParseResult.GetValueForOption(listenPortOption);
    var toolsConfigValue = context.ParseResult.GetValueForOption(toolsConfigOption);

    ArgumentException.ThrowIfNullOrEmpty(listenAddress);
    ArgumentException.ThrowIfNullOrEmpty(toolsConfigValue);

    await RunServer(listenAddress, listenPort, toolsConfigValue, cancellationToken);
}

return await rootCommand.InvokeAsync(args);

async Task RunServer(string listenAddress, int listenPort, string toolsConfigValue, CancellationToken cancellationToken)
{
    var listener = new TcpListener(IPAddress.Parse(listenAddress), listenPort);
    listener.Start();

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using (client)
                    {
                        using var stream = client.GetStream();
                        var rpc = new JsonRpc(stream);
                        var agentCommunication = rpc.Attach<IAgentCommunication>();
                        var toolService = new JsonRpcToolService(loggerFactory, agentCommunication, toolsConfigValue);
                        rpc.AddLocalRpcTarget<IJsonRpcToolService>(toolService, null);
                        rpc.StartListening();
                        await rpc.Completion;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                finally
                {
                    Console.WriteLine("client disconnected");
                }
            }, cancellationToken);
        }
    }
    finally
    {
        listener.Stop();
    }
}

public class JsonRpcToolService : IJsonRpcToolService
{
    private readonly Dictionary<string, Tool> toolMap = new Dictionary<string, Tool>();

    public JsonRpcToolService(ILoggerFactory loggerFactory, IAgentCommunication agentCommunication, string toolsConfigPath, string? basePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolsConfigPath, nameof(toolsConfigPath));

        if (string.IsNullOrEmpty(basePath))
        {
            basePath = Environment.CurrentDirectory;
        }

        var toolsFile = JObject.Parse(File.ReadAllText(toolsConfigPath));
        var toolFactory = new ToolFactory(loggerFactory, toolsFile);

        var todoDatabase = new TodoDatabase(loggerFactory, Path.Join(basePath, "todo.db"));

        toolFactory.Register(agentCommunication);
        toolFactory.Register(loggerFactory);
        toolFactory.Register(todoDatabase);
        //toolFactory.Register(llmApi);

        toolFactory.AddParameter("basePath", basePath);

        var tools = toolFactory.Load() ?? [];
        for (int i = 0; i < tools.Length; i++)
        {
            toolMap.Add(tools[i].Name, tools[i]);
        }
    }

    public Task<string?> CallTool(string name, string parameters)
    {
        if (!toolMap.TryGetValue(name, out Tool? tool))
        {
            return Task.FromResult<string?>(null);
        }

        var result = tool.Function(JObject.Parse(parameters)).ToString();
        return Task.FromResult<string?>(result);
    }

    public Task<string[]> GetToolNames()
    {
        return Task.FromResult(toolMap.Keys.ToArray());
    }

    public Task<string?> GetToolSchema(string name)
    {
        if (!toolMap.TryGetValue(name, out Tool? tool))
        {
            return Task.FromResult<string?>(null);
        }

        var result = tool.Schema.ToString();
        return Task.FromResult<string?>(result);
    }
}