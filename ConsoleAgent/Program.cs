using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.Todo;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using StreamJsonRpc;
using System.Net;

void EnsureConfigDirectoryExists()
{
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string configDir = Path.Combine(home, ".llmagents");
    if (!Directory.Exists(configDir))
    {
        Directory.CreateDirectory(configDir);
    }
}

string GetProfileConfig(string file)
{
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".llmagents", file);
}

string? InteractiveApiConfigSetup()
{
    Console.WriteLine("Interactive API setup. Leave blank to cancel.");

    Console.Write("API endpoint (e.g. https://api.openai.com/v1/chat/completions): ");
    string? endpoint = Console.ReadLine();
    if (string.IsNullOrEmpty(endpoint))
    {
        return null;
    }

    Console.Write("API key: ");
    string? apiKey = Console.ReadLine();
    if (string.IsNullOrEmpty(apiKey))
    {
        return null;
    }

    Console.Write("Model name (e.g. gpt-3.5-turbo): ");
    string? model = Console.ReadLine();
    if (string.IsNullOrEmpty(model))
    {
        return null;
    }

    var apiConfig = new JObject
    {
        ["apiEndpoint"] = endpoint,
        ["apiKey"] = apiKey,
        ["apiModel"] = model
    };

    EnsureConfigDirectoryExists();

    string configPath = GetProfileConfig("api.json");
    File.WriteAllText(configPath, apiConfig.ToString());
    Console.WriteLine($"Saved API config to: {configPath}");

    return configPath;
}

string? InteractiveToolsConfigSetup()
{
    Console.WriteLine("Interactive tools config setup. Leave blank to cancel.");

    Console.Write("Path to tools assembly: ");
    var toolsAssembly = Console.ReadLine();
    if (string.IsNullOrEmpty(toolsAssembly))
    {
        return null;
    }

    var toolsConfig = ToolsConfigGenerator.GenerateToolConfig(toolsAssembly);
    if (toolsConfig == null)
    {
        return null;
    }

    EnsureConfigDirectoryExists();

    string configPath = GetProfileConfig("tools.json");
    File.WriteAllText(configPath, toolsConfig.ToString());
    Console.WriteLine($"Saved tools config to: {configPath}");

    Console.WriteLine("NOTE: Remember to add any necessary parameters to the generated config file.");

    return configPath;
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

var agentIdOption = new Option<string>(
    name: "--agentId",
    description: "Value used to identify the agent");

var apiEndpointOption = new Option<string>(
    name: "--apiEndpoint",
    description: "HTTP(s) endpoint of OpenAI compatible API");

var apiKeyOption = new Option<string>(
    name: "--apiKey",
    description: "Key used to authenticate to the api");

var apiModelOption = new Option<string>(
    name: "--apiModel",
    description: "Name of the model to include in requests");

var apiConfigOption = new Option<string?>(
    name: "--apiConfig",
    description: "Path to a JSON file with configuration for api values",
    getDefaultValue: () => GetConfigOptionDefaultValue("api.json", "LLMAGENTS_API_CONFIG", environmentVariableTarget));

var persistentOption = new Option<bool>(
    name: "--persistent",
    description: "Whether messages are saved",
    getDefaultValue: () => false);

var systemPromptFileOption = new Option<string>(
    name: "--systemPromptFile",
    description: "The path to a file containing the system prompt text",
    getDefaultValue: () => "");

var workingDirectoryOption = new Option<string>(
    name: "--workingDirectory",
    description: "",
    getDefaultValue: () => Environment.CurrentDirectory);

var toolsConfigOption = new Option<string?>(
    name: "--toolsConfig",
    description: "Path to a JSON file with configuration for tool values",
    getDefaultValue: () => GetConfigOptionDefaultValue("tools.json", "LLMAGENTS_TOOLS_CONFIG", environmentVariableTarget));

var toolServerAddressOption = new Option<string>(
    name: "--toolServerAddress",
    description: "The IP address of the tool server",
    getDefaultValue: () => "");

var toolServerPortOption = new Option<int>(
    name: "--toolServerPort",
    description: "The port of the tool server",
    getDefaultValue: () => 5000);

var rootCommand = new RootCommand("ConsoleAgent");
rootCommand.SetHandler(RootCommandHandler);
rootCommand.AddOption(agentIdOption);
rootCommand.AddOption(apiEndpointOption);
rootCommand.AddOption(apiKeyOption);
rootCommand.AddOption(apiModelOption);
rootCommand.AddOption(apiConfigOption);
rootCommand.AddOption(persistentOption);
rootCommand.AddOption(systemPromptFileOption);
rootCommand.AddOption(workingDirectoryOption);
rootCommand.AddOption(toolsConfigOption);
rootCommand.AddOption(toolServerAddressOption);
rootCommand.AddOption(toolServerPortOption);
return await rootCommand.InvokeAsync(args);

async Task RootCommandHandler(InvocationContext context)
{
    var apiEndpoint = string.Empty;
    var apiKey = string.Empty;
    var apiModel = string.Empty;

    var apiConfigValue = context.ParseResult.GetValueForOption(apiConfigOption);
    if (!string.IsNullOrEmpty(apiConfigValue) && File.Exists(apiConfigValue))
    {
        var apiConfig = JObject.Parse(File.ReadAllText(apiConfigValue));

        apiEndpoint = apiConfig.Value<string>("apiEndpoint");
        apiKey = apiConfig.Value<string>("apiKey");
        apiModel = apiConfig.Value<string>("apiModel");
    }
    else
    {
        apiEndpoint = context.ParseResult.GetValueForOption(apiEndpointOption);
        apiKey = context.ParseResult.GetValueForOption(apiKeyOption);
        apiModel = context.ParseResult.GetValueForOption(apiModelOption);
    }

    if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
    {
        var apiConfigPath = InteractiveApiConfigSetup();
        if (!string.IsNullOrEmpty(apiConfigPath))
        {
            var apiConfig = JObject.Parse(File.ReadAllText(apiConfigPath));

            apiEndpoint = apiConfig.Value<string>("apiEndpoint");
            apiKey = apiConfig.Value<string>("apiKey");
            apiModel = apiConfig.Value<string>("apiModel");
        }
    }

    if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
    {
        Console.Error.WriteLine("apiEndpoint, apiKey, and/or apiModel is null or empty.");
        return;
    }

    var toolsConfigValue = context.ParseResult.GetValueForOption(toolsConfigOption);
    if (string.IsNullOrEmpty(toolsConfigValue) || !File.Exists(toolsConfigValue))
    {
        toolsConfigValue = InteractiveToolsConfigSetup();
    }

    var toolServerAddressValue = context.ParseResult.GetValueForOption(toolServerAddressOption);
    var toolServerPortValue = context.ParseResult.GetValueForOption(toolServerPortOption);

    var persistent = context.ParseResult.GetValueForOption(persistentOption);
    var workingDirectoryValue = context.ParseResult.GetValueForOption(workingDirectoryOption);

    string? systemPrompt = Prompts.DefaultSystemPrompt;
    var systemPromptFileValue = context.ParseResult.GetValueForOption(systemPromptFileOption);
    if (!string.IsNullOrEmpty(systemPromptFileValue) && File.Exists(systemPromptFileValue))
    {
        systemPrompt = File.ReadAllText(systemPromptFileValue);
    }

    string agentId = context.ParseResult.GetValueForOption(agentIdOption) ?? apiModel;

    var cancellationToken = context.GetCancellationToken();

    var consoleCommunication = new ConsoleCommunication();

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

    var agent = await CreateAgent(loggerFactory, consoleCommunication,
        agentId, apiEndpoint, apiKey, apiModel,
        persistent, systemPrompt, workingDirectoryValue,
        toolsFilePath: toolsConfigValue, toolServerAddress: toolServerAddressValue, toolServerPort: toolServerPortValue);

    agent.StreamOutput = true;
    agent.PreWaitForContent = () => { Console.Write("> "); };

    await agent.Run(cancellationToken);
}

async Task<LlmAgent> CreateAgent(ILoggerFactory loggerFactory, IAgentCommunication agentCommunication,
    string agentId, string apiEndpoint, string apiKey, string model,
    bool persistent = false, string? systemPrompt = null, string? basePath = null,
    string? toolsFilePath = null, string? toolServerAddress = null, int toolServerPort = 5000)
{
    var llmApi = new LlmApiOpenAi(loggerFactory, apiEndpoint, apiKey, model);

    Tool[]? tools = null;

    if (!string.IsNullOrEmpty(toolServerAddress))
    {
        try
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Parse(toolServerAddress), toolServerPort);
            var stream = tcpClient.GetStream();

            var rpc = new JsonRpc(stream);

            rpc.AddLocalRpcTarget(agentCommunication);
            rpc.AddLocalRpcTarget<ILlmApiMessageProvider>(llmApi, null);

            rpc.StartListening();

            var jsonRpcToolService = rpc.Attach<IJsonRpcToolService>();

            var toolNames = await jsonRpcToolService.GetToolNames();

            var remoteTools = new RemoteTool[toolNames.Length];
            for (int i = 0; i < remoteTools.Length; i++)
            {
                remoteTools[i] = new RemoteTool(toolNames[i], jsonRpcToolService);
            }

            tools = remoteTools;
        }
        catch
        {
            tools = null;
        }
    }

    if (tools == null && !string.IsNullOrEmpty(toolsFilePath) && File.Exists(toolsFilePath))
    {
        var todoDatabase = new TodoDatabase(loggerFactory, Path.Join(basePath, "todo.db"));

        var toolsFile = JObject.Parse(File.ReadAllText(toolsFilePath));
        var toolFactory = new ToolFactory(loggerFactory, toolsFile);

        toolFactory.Register(agentCommunication);
        toolFactory.Register(loggerFactory);
        toolFactory.Register(todoDatabase);
        toolFactory.Register<ILlmApiMessageProvider>(llmApi);

        toolFactory.AddParameter("basePath", basePath ?? Environment.CurrentDirectory);

        tools = toolFactory.Load();
    }

    if (tools != null)
    {
        llmApi.AddTool(tools);
    }

    var agent = new LlmAgent(agentId, llmApi, agentCommunication)
    {
        Persistent = persistent,
        PersistentMessagesPath = basePath ?? Environment.CurrentDirectory
    };

    if (persistent)
    {
        agent.LoadMessages();
    }

    if (agent.llmApi.Messages.Count == 0 && !string.IsNullOrEmpty(systemPrompt))
    {
        agent.llmApi.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));
    }

    return agent;
}
