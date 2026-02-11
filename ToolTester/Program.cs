using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;

using Options = LlmAgents.CommandLineParser.Options;

var rootCommand = new RootCommand("XmppAgent");
rootCommand.SetAction(RootCommandHandler);
rootCommand.Options.Add(Options.ToolsConfig);
rootCommand.Options.Add(Options.WorkingDirectory);
return await rootCommand.Parse(args).InvokeAsync();

static async Task RootCommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
{
    var toolsConfigValue = parseResult.GetValue(Options.ToolsConfig);
    if (string.IsNullOrEmpty(toolsConfigValue) || !File.Exists(toolsConfigValue))
    {
        Console.Error.WriteLine("toolsConfig is null or empty or file cannot be found.");
        return;
    }

    var workingDirectoryValue = parseResult.GetValue(Options.WorkingDirectory) ?? Environment.CurrentDirectory;

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

    var toolEventBus = new ToolEventBus();
    var toolsFile = JObject.Parse(File.ReadAllText(toolsConfigValue));
    var toolFactory = new ToolFactory(loggerFactory, toolsFile);

    var stateDatabase = new StateDatabase(loggerFactory, ":memory:");

    toolFactory.Register<IAgentCommunication>(new ConsoleCommunication());
    toolFactory.Register(loggerFactory);
    toolFactory.Register<ILlmApiMessageProvider>(new MockLlmApiMessageProvider());
    toolFactory.Register<IToolEventBus>(toolEventBus);
    toolFactory.Register(stateDatabase);

    toolFactory.AddParameter("basePath", workingDirectoryValue);

    var tools = toolFactory.Load();
    if (tools == null || tools.Length == 0)
    {
        Console.Error.WriteLine("Tools could not be created");
        return;
    }

    var session = Session.New();

    while (!cancellationToken.IsCancellationRequested)
    {
        for (int i = 0; i < tools.Length; i++)
        {
            Console.WriteLine($"{i + 1}) {tools[i].Name}");
        }

        Console.WriteLine("0) Exit");

        Console.Write("Tool choice> ");
        var toolInput = Console.ReadLine();
        if (toolInput == null || string.Equals("0", toolInput))
        {
            break;
        }
        else if (string.Equals(toolInput, string.Empty))
        {
            continue;
        }

        if (!int.TryParse(toolInput, out var toolChoice))
        {
            continue;
        }
        
        toolChoice -= 1;

        Console.WriteLine(tools[toolChoice].Schema);
        Console.WriteLine();

        Console.Write("Tool parameters (JSON)> ");
        var toolParametersInput = Console.ReadLine();
        if (!string.IsNullOrEmpty(toolParametersInput))
        {
            var toolParameters = JObject.Parse(toolParametersInput);
            var toolOutput = await tools[toolChoice].Function(session, toolParameters);
            Console.WriteLine(toolOutput);
        }
    }
}

class MockLlmApiMessageProvider : ILlmApiMessageProvider
{
    public int MessageCount { get; set; }

    public Task<int> CountMessages()
    {
        return Task.FromResult(MessageCount);
    }

    public Task PruneContext(int numMessagesToKeep)
    {
        return Task.CompletedTask;
    }
}
