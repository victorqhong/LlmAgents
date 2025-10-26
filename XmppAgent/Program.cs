using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;
using XmppAgent.Communication;
using XmppAgent.Logging;

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

var apiContextSizeOption = new Option<int>(
    name: "--contextSize",
    description: "The context window size available from the API"
);

var persistentOption = new Option<bool>(
    name: "--persistent",
    description: "Whether messages are saved",
    getDefaultValue: () => false);

var systemPromptOption = new Option<string?>(
    name: "--systemPromptFile",
    description: "File that contains the system prompt",
    getDefaultValue: () => null);

var xmppDomainOption = new Option<string>(
    name: "--xmppDomain",
    description: "XMPP domain used for agent communication");

var xmppUsernameOption = new Option<string>(
    name: "--xmppUsername",
    description: "XMPP username used for agent communication");

var xmppPasswordOption = new Option<string>(
    name: "--xmppPassword",
    description: "XMPP password used for agent communication");

var xmppTargetJidOption = new Option<string>(
    name: "--xmppTargetJid",
    description: "The target address the agent should communicate with");

var xmppTrustHostOption = new Option<bool>(
    name: "--xmppTrustHost",
    description: "Whether the XMPP connection should accept untrusted TLS certificates",
    getDefaultValue: () => false);

var apiConfigOption = new Option<string?>(
    name: "--apiConfig",
    description: "Path to a JSON file with configuration for api values",
    getDefaultValue: () => Environment.GetEnvironmentVariable("API_CONFIG", environmentVariableTarget) ?? "api.json");

var xmppConfigOption = new Option<string?>(
    name: "--xmppConfig",
    description: "Path to a JSON file with configuration for XMPP values",
    getDefaultValue: () => Environment.GetEnvironmentVariable("XMPP_CONFIG", environmentVariableTarget) ?? "xmpp.json");

var toolsConfigOption = new Option<string>(
    name: "--toolsConfig",
    description: "Path to a JSON file with configuration for tool values",
    getDefaultValue: () => Environment.GetEnvironmentVariable("TOOLS_CONFIG", environmentVariableTarget) ?? "tools.json");

var workingDirectoryOption = new Option<string>(
    name: "--workingDirectory",
    description: "",
    getDefaultValue: () => Environment.CurrentDirectory);

var agentDirectoryOption = new Option<string>(
    name: "--agentDirectory",
    description: "",
    getDefaultValue: () => Environment.CurrentDirectory);

var agentsConfigOption = new Option<string>(
    name: "--agentsConfig",
    description: "Path to a JSON file with configuration for agents",
    getDefaultValue: () => "agents.json"
);

var rootCommand = new RootCommand("XmppAgent");
rootCommand.SetHandler(RootCommandHander);
rootCommand.AddOption(agentsConfigOption);

var agentCommand = new Command("agent", "Run a single agent.");
agentCommand.SetHandler(AgentCommandHandler);
agentCommand.AddOption(apiEndpointOption);
agentCommand.AddOption(apiKeyOption);
agentCommand.AddOption(apiModelOption);
agentCommand.AddOption(apiContextSizeOption);
agentCommand.AddOption(apiConfigOption);
agentCommand.AddOption(persistentOption);
agentCommand.AddOption(systemPromptOption);
agentCommand.AddOption(xmppDomainOption);
agentCommand.AddOption(xmppUsernameOption);
agentCommand.AddOption(xmppPasswordOption);
agentCommand.AddOption(xmppTargetJidOption);
agentCommand.AddOption(xmppTrustHostOption);
agentCommand.AddOption(xmppConfigOption);
agentCommand.AddOption(toolsConfigOption);
agentCommand.AddOption(workingDirectoryOption);
agentCommand.AddOption(agentDirectoryOption);
agentCommand.AddOption(agentIdOption);
rootCommand.AddCommand(agentCommand);

async Task RootCommandHander(InvocationContext context)
{
    var agentsConfigValue = context.ParseResult.GetValueForOption(agentsConfigOption);
    if (string.IsNullOrEmpty(agentsConfigValue) || !File.Exists(agentsConfigValue))
    {
        Console.WriteLine("agentsConfig is invalid or does not exist");
        return;
    }

    var agentTasks = new List<Task>();

    var agentsConfig = JObject.Parse(File.ReadAllText(agentsConfigValue));
    foreach (var agentProperty in agentsConfig.Properties())
    {
        var agentId = agentProperty.Name;
        var apiConfig = agentProperty.Value.Value<string>("apiConfig");
        var xmppConfig = agentProperty.Value.Value<string>("xmppConfig");
        var toolsConfig = agentProperty.Value.Value<string>("toolsConfig");
        var workingDirectory = agentProperty.Value.Value<string>("workingDirectory");
        var agentDirectory = agentProperty.Value.Value<string>("agentDirectory");

        if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(apiConfig) || string.IsNullOrEmpty(xmppConfig) || string.IsNullOrEmpty(toolsConfig) || string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(agentDirectory))
        {
            continue;
        }

        var systemPromptFile = agentProperty.Value.Value<string>("systemPromptFile") ?? null;
        var persistent = agentProperty.Value.Value<bool>("persistent");

        var agentParameters = AgentParameters.Create(apiConfig, xmppConfig, toolsConfig, agentId, agentDirectory, workingDirectory, persistent, systemPromptFile);
        if (agentParameters == null)
        {
            continue;
        }

        agentTasks.Add(RunAgent(agentParameters));
    }

    if (agentTasks.Count < 1)
    {
        Console.WriteLine("There were no agentTasks");
        return;
    }

    await Task.WhenAll(agentTasks);
}

async Task AgentCommandHandler(InvocationContext context)
{
    AgentParameters? parameters = null;

    var agentIdValue = context.ParseResult.GetValueForOption(agentIdOption);
    if (string.IsNullOrEmpty(agentIdValue))
    {
        Console.Error.WriteLine("Agent id is not specified");
        return;
    }

    var apiConfigValue = context.ParseResult.GetValueForOption(apiConfigOption);
    var xmppConfigValue = context.ParseResult.GetValueForOption(xmppConfigOption);
    var toolsConfigValue = context.ParseResult.GetValueForOption(toolsConfigOption);
    var persistent = context.ParseResult.GetValueForOption(persistentOption);
    var systemPromptFile = context.ParseResult.GetValueForOption(systemPromptOption);
    var workingDirectoryValue = context.ParseResult.GetValueForOption(workingDirectoryOption) ?? Environment.CurrentDirectory;
    var agentDirectoryValue = context.ParseResult.GetValueForOption(agentDirectoryOption);

    if (!string.IsNullOrEmpty(apiConfigValue) && File.Exists(apiConfigValue) && !string.IsNullOrEmpty(xmppConfigValue) && File.Exists(xmppConfigValue) && !string.IsNullOrEmpty(toolsConfigValue) && File.Exists(toolsConfigValue))
    {
        parameters = AgentParameters.Create(agentIdValue, apiConfigValue, xmppConfigValue, toolsConfigValue, agentDirectoryValue, workingDirectoryValue, persistent, systemPromptFile);
    }

    if (parameters == null)
    {
        var apiEndpoint = context.ParseResult.GetValueForOption(apiEndpointOption);
        var apiKey = context.ParseResult.GetValueForOption(apiKeyOption);
        var apiModel = context.ParseResult.GetValueForOption(apiModelOption);
        var apiContextSize = context.ParseResult.GetValueForOption(apiContextSizeOption);

        var xmppDomain = context.ParseResult.GetValueForOption(xmppDomainOption);
        var xmppUsername = context.ParseResult.GetValueForOption(xmppUsernameOption);
        var xmppPassword = context.ParseResult.GetValueForOption(xmppPasswordOption);
        var xmppTargetJid = context.ParseResult.GetValueForOption(xmppTargetJidOption);
        var xmppTrustHost = context.ParseResult.GetValueForOption(xmppTrustHostOption);

        parameters = AgentParameters.Create(agentIdValue, apiEndpoint, apiKey, apiModel, apiContextSize,
            xmppDomain, xmppUsername, xmppPassword, xmppTargetJid, xmppTrustHost,
            toolsConfigValue, agentDirectoryValue, workingDirectoryValue, persistent, systemPromptFile);
    }

    if (parameters != null)
    {
        await RunAgent(parameters);
    }
}

Task RunAgent(AgentParameters agentParameters, CancellationToken cancellationToken = default)
{
    return Task.Run(async () =>
    {
        try
        {
            var xmppCommunication = new XmppCommunication(
                agentParameters.xmppParameters.xmppUsername, agentParameters.xmppParameters.xmppDomain, agentParameters.xmppParameters.xmppPassword, trustHost: agentParameters.xmppParameters.xmppTrustHost)
            {
                TargetJid = agentParameters.xmppParameters.xmppTargetJid
            };
#if DEBUG
            xmppCommunication.Debug = true;
#endif
            await xmppCommunication.Initialize();

            using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new XmppLoggerProvider(xmppCommunication)));

        var agent = CreateAgent(loggerFactory, xmppCommunication, agentParameters.apiParameters, agentParameters.agentId, agentParameters.persistent,
            workingDirectory: agentParameters.workingDirectory,
            toolsFilePath: agentParameters.toolsConfigPath,
            agentDirectory: agentParameters.agentDirectory,
            systemPromptFile: agentParameters.systemPromptFile);

            await agent.Run(cancellationToken);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error loading agent for: {agentParameters.agentDirectory}");
            Console.WriteLine(e);
        }

    }, cancellationToken);
}

LlmAgent CreateAgent(ILoggerFactory loggerFactory, IAgentCommunication agentCommunication, ApiParameters apiParameters,
    string agentId, bool persistent = false, string? systemPromptFile = null, string? workingDirectory = null, string? agentDirectory = null, string? toolsFilePath = null)
{
    var llmApi = new LlmApiOpenAi(loggerFactory, apiParameters.apiEndpoint, apiParameters.apiKey, apiParameters.apiModel);

    var agent = new LlmAgent(agentId, llmApi, agentCommunication)
    {
        Persistent = persistent,
        PersistentMessagesPath = agentDirectory ?? Environment.CurrentDirectory
    };

    if (!string.IsNullOrEmpty(toolsFilePath))
    {
        var stateDatabase = new StateDatabase(loggerFactory, Path.Join(agentDirectory, $"{agentId}.db"));
        var toolEventBus = new ToolEventBus();
        var toolsFile = JObject.Parse(File.ReadAllText(toolsFilePath));
        var toolFactory = new ToolFactory(loggerFactory, toolsFile);

        toolFactory.Register(agentCommunication);
        toolFactory.Register(loggerFactory);
        toolFactory.Register<ILlmApiMessageProvider>(llmApi);
        toolFactory.Register<IToolEventBus>(toolEventBus);
        toolFactory.Register(stateDatabase);

        toolFactory.AddParameter("basePath", workingDirectory ?? Environment.CurrentDirectory);

        var tools = toolFactory.Load();
        if (tools != null)
        {
            agent.AddTool(tools);
        }
    }

    if (persistent)
    {
        agent.LoadMessages();
    }

    if (agent.llmApi.Messages.Count == 0)
    {
        var systemPrompt = Prompts.DefaultSystemPrompt;
        if (!string.IsNullOrEmpty(systemPromptFile) && File.Exists(systemPromptFile))
        {
            systemPrompt = File.ReadAllText(systemPromptFile);
        }

        agent.llmApi.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));
    }

    return agent;
}

return await rootCommand.InvokeAsync(args);

class ApiParameters
{
    public string apiEndpoint = string.Empty;
    public string apiKey = string.Empty;
    public string apiModel = string.Empty;
    public int contextSize = 8196;

    public static ApiParameters? Create(string? apiEndpoint, string? apiKey, string? apiModel, int contextSize = 8196)
    {
        if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
        {
            Console.Error.WriteLine("apiEndpoint, apiKey, and/or apiModel is null or empty.");
            return null;
        }

        return new ApiParameters
        {
            apiEndpoint = apiEndpoint,
            apiKey = apiKey,
            apiModel = apiModel,
            contextSize = contextSize
        };
    }
}

public class XmppParameters
{
    public string xmppTargetJid = string.Empty;
    public string xmppDomain = string.Empty;
    public string xmppUsername = string.Empty;
    public string xmppPassword = string.Empty;
    public bool xmppTrustHost = false;

    public static XmppParameters? Create(string? xmppDomain, string? xmppUsername, string? xmppPassword, string? xmppTargetJid, bool xmppTrustHost)
    {
        if (string.IsNullOrEmpty(xmppDomain) || string.IsNullOrEmpty(xmppUsername) || string.IsNullOrEmpty(xmppPassword) || string.IsNullOrEmpty(xmppTargetJid))
        {
            Console.Error.WriteLine("xmppDomain, xmppUsername, xmppPassword and/or xmppTargetJid is null or empty.");
            return null;
        }

        return new XmppParameters
        {
            xmppDomain = xmppDomain,
            xmppUsername = xmppUsername,
            xmppPassword = xmppPassword,
            xmppTargetJid = xmppTargetJid,
            xmppTrustHost = xmppTrustHost
        };
    }
}

class AgentParameters
{
    public readonly ApiParameters apiParameters;
    public readonly XmppParameters xmppParameters;
    public readonly string agentId;
    public string? systemPromptFile = null;
    public bool persistent = false;
    public string? workingDirectory = null;
    public string? toolsConfigPath = null;
    public string? agentDirectory = null;

    public AgentParameters(string agentId, ApiParameters apiParameters, XmppParameters xmppParameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        ArgumentNullException.ThrowIfNull(apiParameters);
        ArgumentNullException.ThrowIfNull(xmppParameters);

        this.agentId = agentId;
        this.apiParameters = apiParameters;
        this.xmppParameters = xmppParameters;
    }

    public static AgentParameters? Create(
        string agentId,
        string? apiEndpoint, string? apiKey, string? apiModel, int contextSize,
        string? xmppDomain, string? xmppUsername, string? xmppPassword, string? xmppTargetJid, bool xmppTrustHost,
        string? toolsConfigPath, string? agentDirectory, string? workingDirectory, bool persistent, string? systemPromptFile)
    {
        var apiParameters = ApiParameters.Create(apiEndpoint, apiKey, apiModel, contextSize);
        if (apiParameters == null)
        {
            return null;
        }

        var xmppParameters = XmppParameters.Create(xmppDomain, xmppUsername, xmppPassword, xmppTargetJid, xmppTrustHost);
        if (xmppParameters == null)
        {
            return null;
        }

        return new AgentParameters(agentId, apiParameters, xmppParameters)
        {
            agentDirectory = agentDirectory,
            workingDirectory = workingDirectory,
            toolsConfigPath = toolsConfigPath,
            persistent = persistent,
            systemPromptFile = systemPromptFile
        };
    }

    public static AgentParameters? Create(string? apiConfigPath, string? xmppConfigPath, string? toolsConfigPath, string agentId, string? agentDirectory, string workingDirectory, bool persistent, string? systemPromptFile)
    {
        if (string.IsNullOrEmpty(apiConfigPath) || !File.Exists(apiConfigPath))
        {
            return null;
        }

        if (string.IsNullOrEmpty(xmppConfigPath) || !File.Exists(xmppConfigPath))
        {
            return null;
        }

        var apiConfig = JObject.Parse(File.ReadAllText(apiConfigPath));
        var xmppConfig = JObject.Parse(File.ReadAllText(xmppConfigPath));

        var apiEndpoint = apiConfig.Value<string>("apiEndpoint");
        var apiKey = apiConfig.Value<string>("apiKey");
        var apiModel = apiConfig.Value<string>("apiModel");
        var contextSize = apiConfig.Value<int>("contextSize");
        var xmppDomain = xmppConfig.Value<string>("xmppDomain");
        var xmppUsername = xmppConfig.Value<string>("xmppUsername");
        var xmppPassword = xmppConfig.Value<string>("xmppPassword");
        var xmppTargetJid = xmppConfig.Value<string>("xmppTargetJid");
        var xmppTrustHost = xmppConfig.Value<bool>("xmppTrustHost");

        return Create(agentId, apiEndpoint, apiKey, apiModel, contextSize, xmppDomain, xmppUsername, xmppPassword, xmppTargetJid, xmppTrustHost, toolsConfigPath, agentDirectory, workingDirectory, persistent, systemPromptFile);
    }
}
