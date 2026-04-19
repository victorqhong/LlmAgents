using LlmAgents.Communication;
using LlmAgents.Configuration;
using LlmAgents.State;
using LlmAgents.Tools;
using System.CommandLine;
using System.Text.Json;
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

var noStdioTransportOption = new Option<bool>(name: "--no-stdio")
{
    Description = "Disable stdio transport",
    DefaultValueFactory = result => false
};

var noHttpTransportOption = new Option<bool>(name: "--no-http")
{
    Description = "Disable http transport",
    DefaultValueFactory = result => false
};

var rootCommand = new RootCommand("ToolServer");
rootCommand.Options.Add(listenAddressOption);
rootCommand.Options.Add(listenPortOption);
rootCommand.Options.Add(Options.ToolsConfig);
rootCommand.Options.Add(Options.WorkingDirectory);
rootCommand.Options.Add(noStdioTransportOption);
rootCommand.Options.Add(noHttpTransportOption);
rootCommand.Options.Add(Options.Debug);
rootCommand.SetAction(RootCommandHandler);

async Task RootCommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
{
    var listenAddress = parseResult.GetValue(listenAddressOption);
    var listenPort = parseResult.GetValue(listenPortOption);
    var toolsConfigValue = parseResult.GetValue(Options.ToolsConfig);
    var workingDirectoryValue = parseResult.GetValue(Options.WorkingDirectory);
    var noStdioTransportValue = parseResult.GetValue(noStdioTransportOption);
    var noHttpTransportValue = parseResult.GetValue(noHttpTransportOption);
    var debug = parseResult.GetValue(Options.Debug);

    ArgumentException.ThrowIfNullOrEmpty(listenAddress);
    ArgumentException.ThrowIfNullOrEmpty(toolsConfigValue);

    await RunServer(listenAddress, listenPort, toolsConfigValue, workingDirectoryValue, new ConsoleCommunication(), noStdioTransportValue, noHttpTransportValue, debug, cancellationToken);
}

return await rootCommand.Parse(args).InvokeAsync();

async Task RunServer(string listenAddress, int listenPort, string toolsConfigPath, string? workingDirectory, IAgentCommunication agentCommunication, bool noStdioTransport, bool noHttpTransport, bool debug, CancellationToken cancellationToken = default)
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

    var toolFactory = new ToolFactory(loggerFactory);

    var stateDatabase = new StateDatabase(loggerFactory, ":memory:");
    var sessionDatabase = new SessionDatabase(stateDatabase);
    var toolEventBus = new ToolEventBus();

    toolFactory.Register(agentCommunication);
    toolFactory.Register(loggerFactory);
    toolFactory.Register(sessionDatabase);
    toolFactory.Register< IToolEventBus>(toolEventBus);

    toolFactory.AddParameter("basePath", workingDirectory);

    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost
        .UseUrls($"http://{listenAddress}:{listenPort}");

    // Add services
    builder.Services.AddSingleton(loggerFactory);
    builder.Services.AddSingleton(stateDatabase);
    builder.Services.AddHttpContextAccessor();

    var toolsFile = JsonSerializer.Deserialize<ToolsConfig>(File.ReadAllText(toolsConfigPath));
    if (toolsFile == null)
    {
        Console.Error.WriteLine("Could not parse tools config file");
        return;
    }

    var tools = await toolFactory.Load(toolsFile) ?? [];
    var mcpTools = tools.Select(tool => new McpToolAdapter(tool) { Debug = debug });

    var mcpBuilder = builder.Services
        .AddMcpServer()
        .WithTools(mcpTools);

    if (noStdioTransport == false)
    {
        mcpBuilder.WithStdioServerTransport();
    }

    if (noHttpTransport == false)
    {
        mcpBuilder.WithHttpTransport();
    }

    var app = builder.Build();

    app.MapMcp();

    await app.RunAsync(cancellationToken);
}
