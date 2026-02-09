using LlmAgents.Communication;
using LlmAgents.State;
using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using ToolServer;

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

var workingDirectory = new Option<string>(
    name: "--workingDirectory",
    description: "Directory which tools will be run by default",
    getDefaultValue: () => Environment.CurrentDirectory);

var rootCommand = new RootCommand("ToolServer");
rootCommand.AddOption(listenAddressOption);
rootCommand.AddOption(listenPortOption);
rootCommand.AddOption(toolsConfigOption);
rootCommand.AddOption(workingDirectory);
rootCommand.SetHandler(RootCommandHandler);

async Task RootCommandHandler(InvocationContext context)
{
    var cancellationToken = context.GetCancellationToken();

    var listenAddress = context.ParseResult.GetValueForOption(listenAddressOption);
    var listenPort = context.ParseResult.GetValueForOption(listenPortOption);
    var toolsConfigValue = context.ParseResult.GetValueForOption(toolsConfigOption);
    var workingDirectoryValue = context.ParseResult.GetValueForOption(workingDirectory);

    ArgumentException.ThrowIfNullOrEmpty(listenAddress);
    ArgumentException.ThrowIfNullOrEmpty(toolsConfigValue);

    await RunServer(listenAddress, listenPort, toolsConfigValue, workingDirectoryValue, new ConsoleCommunication());
}

return await rootCommand.InvokeAsync(args);

async Task RunServer(string listenAddress, int listenPort, string toolsConfigPath, string? workingDirectory, IAgentCommunication agentCommunication, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrEmpty(workingDirectory))
    {
        workingDirectory = Environment.CurrentDirectory;
    }

    var toolsFile = JObject.Parse(File.ReadAllText(toolsConfigPath));
    var toolFactory = new ToolFactory(loggerFactory, toolsFile);

    var stateDatabase = new StateDatabase(loggerFactory, ":memory:");
    var toolEventBus = new ToolEventBus();

    toolFactory.Register(agentCommunication);
    toolFactory.Register(loggerFactory);
    toolFactory.Register(stateDatabase);
    toolFactory.Register<IToolEventBus>(toolEventBus);

    toolFactory.AddParameter("basePath", workingDirectory);

    var tools = toolFactory.Load() ?? [];
    var mcpTools = tools.Select(tool => new McpToolAdapter(tool));

    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost
        .UseUrls($"http://{listenAddress}:{listenPort}");

    // Add services
    builder.Services.AddSingleton(stateDatabase);
    builder.Services.AddHttpContextAccessor();

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithStdioServerTransport()
        .WithTools(mcpTools);

    var app = builder.Build();

    app.MapMcp();

    await app.RunAsync(cancellationToken);
}
