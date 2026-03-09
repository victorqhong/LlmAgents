using LlmAgents.Communication;
using LlmAgents.State;
using LlmAgents.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using ToolServer;

using Options = LlmAgents.CommandLineParser.Options;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var listenAddressOption = new Option<string>(name: "--host")
{
    Description = "The address to listen on",
    DefaultValueFactory = result => "127.0.0.1"
};

var listenPortOption = new Option<int>(name: "--port")
{
    Description = "The port to listen on",
    DefaultValueFactory = result => 5000
};

var agentIdOption = new Option<string>(name: "--agentId")
{
    Description = "Default agent identifier used for autonomous task control-plane tools",
    DefaultValueFactory = _ => Environment.MachineName
};

var rootCommand = new RootCommand("ToolServer");
rootCommand.Options.Add(listenAddressOption);
rootCommand.Options.Add(listenPortOption);
rootCommand.Options.Add(agentIdOption);
rootCommand.Options.Add(Options.StorageDirectory);
rootCommand.Options.Add(Options.ToolsConfig);
rootCommand.Options.Add(Options.WorkingDirectory);
rootCommand.SetAction(RootCommandHandler);

async Task RootCommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
{
    var listenAddress = parseResult.GetValue(listenAddressOption);
    var listenPort = parseResult.GetValue(listenPortOption);
    var agentIdValue = parseResult.GetValue(agentIdOption);
    var storageDirectoryValue = parseResult.GetValue(Options.StorageDirectory);
    var toolsConfigValue = parseResult.GetValue(Options.ToolsConfig);
    var workingDirectoryValue = parseResult.GetValue(Options.WorkingDirectory);

    ArgumentException.ThrowIfNullOrEmpty(listenAddress);
    ArgumentException.ThrowIfNullOrEmpty(agentIdValue);
    ArgumentException.ThrowIfNullOrEmpty(storageDirectoryValue);
    ArgumentException.ThrowIfNullOrEmpty(toolsConfigValue);

    await RunServer(listenAddress, listenPort, agentIdValue, storageDirectoryValue, toolsConfigValue, workingDirectoryValue, new ConsoleCommunication(), cancellationToken);
}

return await rootCommand.Parse(args).InvokeAsync();

async Task RunServer(string listenAddress, int listenPort, string defaultAgentId, string storageDirectory, string toolsConfigPath, string? workingDirectory, IAgentCommunication agentCommunication, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrEmpty(workingDirectory))
    {
        workingDirectory = Environment.CurrentDirectory;
    }

    if (!File.Exists(toolsConfigPath))
    {
        Console.Error.WriteLine($"Tools config file does not exist: {toolsConfigPath}");
        return;
    }

    Directory.CreateDirectory(storageDirectory);

    var toolsFile = JObject.Parse(File.ReadAllText(toolsConfigPath));
    var toolFactory = new ToolFactory(loggerFactory);

    var stateDatabase = new StateDatabase(loggerFactory, ":memory:");
    var toolEventBus = new ToolEventBus();

    toolFactory.Register(agentCommunication);
    toolFactory.Register(loggerFactory);
    toolFactory.Register(stateDatabase);
    toolFactory.Register<IToolEventBus>(toolEventBus);

    toolFactory.AddParameter("basePath", workingDirectory);

    var tools = toolFactory.Load(toolsFile) ?? [];
    var mcpTools = tools.Select(tool => new McpToolAdapter(tool));
    var autonomyControlPlaneTools = new McpServerTool[]
    {
        new AutonomyTaskSubmitTool(loggerFactory, storageDirectory, defaultAgentId),
        new AutonomyTaskStatusTool(loggerFactory, storageDirectory, defaultAgentId),
        new AutonomyTaskListTool(loggerFactory, storageDirectory, defaultAgentId),
        new AutonomyTaskResumeTool(loggerFactory, storageDirectory, defaultAgentId),
        new AutonomyTaskCancelTool(loggerFactory, storageDirectory, defaultAgentId)
    };
    var allMcpTools = mcpTools.Cast<McpServerTool>().Concat(autonomyControlPlaneTools).ToArray();

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
        .WithTools(allMcpTools);

    var app = builder.Build();

    app.MapMcp();

    await app.RunAsync(cancellationToken);
}
