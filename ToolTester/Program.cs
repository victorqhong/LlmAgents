using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;

string GetProfileConfig(string file)
{
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".llmagents", file);
}

string? GetConfigOptionDefaultValue(string fileName, string environmentVariableName, EnvironmentVariableTarget environmentVariableTarget)
{
    if (File.Exists(fileName))
    {
        return fileName;
    }

    var profileConfig = GetProfileConfig(fileName);
    if (File.Exists(profileConfig))
    {
        return profileConfig;
    }

    var environmentVariable = Environment.GetEnvironmentVariable(environmentVariableName, environmentVariableTarget);
    if (File.Exists(environmentVariable))
    {
        return environmentVariable;
    }

    return null;
}

var environmentVariableTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;

var toolsConfigOption = new Option<string?>(
    name: "--toolsConfig",
    description: "Path to a JSON file with configuration for tool values",
    getDefaultValue: () => GetConfigOptionDefaultValue("tools.json", "LLMAGENTS_TOOLS_CONFIG", environmentVariableTarget));

var workingDirectoryOption = new Option<string>(
    name: "--workingDirectory",
    description: "",
    getDefaultValue: () => Environment.CurrentDirectory);

var rootCommand = new RootCommand("XmppAgent");
rootCommand.SetHandler(RootCommandHandler);
rootCommand.AddOption(toolsConfigOption);
rootCommand.AddOption(workingDirectoryOption);
return await rootCommand.InvokeAsync(args);

async Task RootCommandHandler(InvocationContext context)
{
    var toolsConfigValue = context.ParseResult.GetValueForOption(toolsConfigOption);
    if (string.IsNullOrEmpty(toolsConfigValue) || !File.Exists(toolsConfigValue))
    {
        Console.Error.WriteLine("toolsConfig is null or empty or file cannot be found.");
        return;
    }

    var workingDirectoryValue = context.ParseResult.GetValueForOption(workingDirectoryOption) ?? Environment.CurrentDirectory;

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

    var toolEventBus = new ToolEventBus();
    var toolsFile = JObject.Parse(File.ReadAllText(toolsConfigValue));
    var toolFactory = new ToolFactory(loggerFactory, toolsFile);

    toolFactory.Register<IAgentCommunication>(new ConsoleCommunication());
    toolFactory.Register(loggerFactory);
    toolFactory.Register<ILlmApiMessageProvider>(new MockLlmApiMessageProvider());
    toolFactory.Register<IToolEventBus>(toolEventBus);

    toolFactory.AddParameter("basePath", workingDirectoryValue);

    var tools = toolFactory.Load();
    if (tools == null || tools.Length == 0)
    {
        Console.Error.WriteLine("Tools could not be created");
        return;
    }

    var cancellationToken = context.GetCancellationToken();

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
            var toolOutput = await tools[toolChoice].Function(toolParameters);
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
