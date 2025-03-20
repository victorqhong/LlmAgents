using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.Todo;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;
using XmppAgent.Communication;
using XmppAgent.Logging;
using XmppDotNet.Xmpp;

var environmentVariableTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;

var apiEndpointOption = new Option<string>(
    name: "--apiEndpoint",
    description: "HTTP(s) endpoint of OpenAI compatiable API");

var apiKeyOption = new Option<string>(
    name: "--apiKey",
    description: "Key used to authenticate to the api");

var apiModelOption = new Option<string>(
    name: "--apiModel",
    description: "Name of the model to include in requests");

var persistentOption = new Option<bool>(
    name: "--persistent",
    description: "Whether messages are saved",
    getDefaultValue: () => false);

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

var toolsWorkingDirectoryOption = new Option<string>(
    name: "--toolsWorkingDirectory",
    description: "The working directory tools will use",
    getDefaultValue: () => Environment.CurrentDirectory);

var toolsRestrictToWorkingDirectoryOption = new Option<bool>(
    name: "--toolsRestrictToWorkingDirectory",
    description: "Whether tools should be restricted to the working directory",
    getDefaultValue: () => true);

var rootCommand = new RootCommand("XmppAgent");
rootCommand.SetHandler(RootCommandHandler);
rootCommand.AddOption(apiEndpointOption);
rootCommand.AddOption(apiKeyOption);
rootCommand.AddOption(apiModelOption);
rootCommand.AddOption(apiConfigOption);
rootCommand.AddOption(persistentOption);
rootCommand.AddOption(xmppDomainOption);
rootCommand.AddOption(xmppUsernameOption);
rootCommand.AddOption(xmppPasswordOption);
rootCommand.AddOption(xmppTargetJidOption);
rootCommand.AddOption(xmppTrustHostOption);
rootCommand.AddOption(xmppConfigOption);
rootCommand.AddOption(toolsWorkingDirectoryOption);
rootCommand.AddOption(toolsRestrictToWorkingDirectoryOption);

void RootCommandHandler(InvocationContext context)
{
    var apiEndpoint = string.Empty;
    var apiKey = string.Empty;
    var apiModel = string.Empty;
    var persistent = false;
    var xmppTargetJid = string.Empty;
    var xmppDomain = string.Empty;
    var xmppUsername = string.Empty;
    var xmppPassword = string.Empty;
    var xmppTrustHost = false;
    var toolsWorkingDirectory = string.Empty;
    var toolsRestrictToWorkingDirectory = true;

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
        Console.Error.WriteLine("apiEndpoint, apiKey, and/or apiModel is null or empty.");
        return;
    }

    var xmppConfigValue = context.ParseResult.GetValueForOption(xmppConfigOption);
    if (!string.IsNullOrEmpty(xmppConfigValue) && File.Exists(xmppConfigValue))
    {
        var xmppConfig = JObject.Parse(File.ReadAllText(xmppConfigValue));

        xmppDomain = xmppConfig.Value<string>("xmppDomain");
        xmppUsername = xmppConfig.Value<string>("xmppUsername");
        xmppPassword = xmppConfig.Value<string>("xmppPassword");
        xmppTargetJid = xmppConfig.Value<string>("xmppTargetJid");
        xmppTrustHost = xmppConfig.Value<bool>("xmppTrustHost");
    }
    else
    {
        xmppDomain = context.ParseResult.GetValueForOption(xmppDomainOption);
        xmppUsername = context.ParseResult.GetValueForOption(xmppUsernameOption);
        xmppPassword = context.ParseResult.GetValueForOption(xmppPasswordOption);
        xmppTargetJid = context.ParseResult.GetValueForOption(xmppTargetJidOption);
        xmppTrustHost = context.ParseResult.GetValueForOption(xmppTrustHostOption);
    }

    if (string.IsNullOrEmpty(xmppDomain) || string.IsNullOrEmpty(xmppUsername) || string.IsNullOrEmpty(xmppPassword) || string.IsNullOrEmpty(xmppTargetJid))
    {
        Console.Error.WriteLine("xmppDomain, xmppUsername, xmppPassword and/or xmppTargetJid is null or empty.");
        return;
    }

    persistent = context.ParseResult.GetValueForOption(persistentOption);

    toolsWorkingDirectory = context.ParseResult.GetValueForOption(toolsWorkingDirectoryOption);
    toolsRestrictToWorkingDirectory = context.ParseResult.GetValueForOption(toolsRestrictToWorkingDirectoryOption);

    var xmppCommunication = new XmppCommunication(xmppUsername, xmppDomain, xmppPassword, trustHost: xmppTrustHost)
    {
        TargetJid = xmppTargetJid
    };

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new XmppLoggerProvider(xmppCommunication)));

    var agent = CreateAgent(loggerFactory, xmppCommunication, apiModel, apiEndpoint, apiKey, apiModel, persistent, basePath: toolsWorkingDirectory, restrictToBasePath: toolsRestrictToWorkingDirectory);

    var cancellationToken = context.GetCancellationToken();
    while (!cancellationToken.IsCancellationRequested)
    {
        var line = xmppCommunication.WaitForMessage(cancellationToken);
        if (string.IsNullOrEmpty(line))
        {
            continue;
        }

        xmppCommunication.SendPresence(Show.DoNotDisturb);

        var response = agent.GenerateCompletion(line, cancellationToken);
        if (string.IsNullOrEmpty(response))
        {
            continue;
        }

        xmppCommunication.SendMessage(response);

        if (persistent)
        {
            LlmAgentApi.SaveMessages(agent);
        }
    }
}

LlmAgentApi CreateAgent(ILoggerFactory loggerFactory, IAgentCommunication agentCommunication, string id, string apiEndpoint, string apiKey, string model, bool loadMessages = false, string? systemPrompt = null, string? basePath = null, bool restrictToBasePath = true)
{
    var todoDatabase = new TodoDatabase(loggerFactory, Path.Join(basePath, "todo.db"));

    var shellTool = new Shell(loggerFactory, basePath);
    var fileReadTool = new FileRead(basePath, restrictToBasePath);
    var fileWriteTool = new FileWrite(basePath, restrictToBasePath);
    var fileListTool = new FileList(basePath, restrictToBasePath);
    var sqliteFileRun = new SqliteFileRun(basePath, restrictToBasePath);
    var sqliteSqlRun = new SqliteSqlRun();
    var todoContainerCreate = new TodoGroupCreate(todoDatabase);
    var todoContainerRead = new TodoGroupRead(todoDatabase);
    var todoContainerList = new TodoGroupList(todoDatabase);
    var todoCreate = new TodoCreate(todoDatabase);
    var todoRead = new TodoRead(todoDatabase);
    var todoUpdate = new TodoUpdate(todoDatabase);
    var todoDelete = new TodoDelete(todoDatabase);
    var askQuestionTool = new AskQuestion(agentCommunication);

    var tools = new Tool[]
    {
        shellTool.Tool,
        fileReadTool.Tool,
        fileWriteTool.Tool,
        fileListTool.Tool,
        sqliteFileRun.Tool,
        sqliteSqlRun.Tool,
        todoContainerCreate.Tool,
        todoContainerRead.Tool,
        todoContainerList.Tool,
        todoCreate.Tool,
        todoRead.Tool,
        todoUpdate.Tool,
        todoDelete.Tool,
        askQuestionTool.Tool,
    };

    List<JObject>? messages = null;
    if (loadMessages)
    {
        messages = LlmAgentApi.LoadMessages(id);
    }

    if (messages == null)
    {
        messages =
        [
            JObject.FromObject(new { role = "system", content = systemPrompt ?? Prompts.DefaultSystemPrompt })
        ];
    }

    return new LlmAgentApi(loggerFactory, id, apiEndpoint, apiKey, model, messages, tools);
}

return await rootCommand.InvokeAsync(args);
