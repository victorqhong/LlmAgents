using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Todo;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
    description: "");

var apiKeyOption = new Option<string>(
    name: "--apiKey",
    description: "key used to communicate with the api");

var apiModelOption = new Option<string>(
    name: "--apiModel",
    description: "");

var persistentOption = new Option<bool>(
    name: "--persistent",
    description: "",
    getDefaultValue: () => false);

var xmppDomainOption = new Option<string>(
    name: "--xmppDomain",
    description: "");

var xmppUsernameOption = new Option<string>(
    name: "--xmppUsername",
    description: "");

var xmppPasswordOption = new Option<string>(
    name: "--xmppPassword",
    description: "");

var xmppTargetJidOption = new Option<string>(
    name: "--xmppTargetJid",
    description: "");

var xmppTrustHostOption = new Option<bool>(
    name: "--xmppTrustHost",
    description: "",
    getDefaultValue: () => false);

var apiConfigOption = new Option<string>(
    name: "--apiConfig",
    description: "",
    getDefaultValue: () => Environment.GetEnvironmentVariable("API_CONFIG", environmentVariableTarget) ?? string.Empty);

var xmppConfigOption = new Option<string>(
    name: "--xmppConfig",
    description: "",
    getDefaultValue: () => Environment.GetEnvironmentVariable("XMPP_CONFIG", environmentVariableTarget) ?? string.Empty);

var toolsWorkingDirectoryOption = new Option<string>(
    name: "--toolsWorkingDirectory",
    description: "",
    getDefaultValue: () => Environment.CurrentDirectory);

var toolsRestrictToWorkingDirectoryOption = new Option<bool>(
    name: "--toolsRestrictToWorkingDirectory",
    description: "",
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
    if (!string.IsNullOrEmpty(apiConfigValue))
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
    if (!string.IsNullOrEmpty(xmppConfigValue))
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

    var xmppCommunication = new XmppCommunication(xmppUsername, xmppDomain, xmppPassword, trustHost: xmppTrustHost);

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new XmppLoggerProvider(xmppCommunication, xmppTargetJid)));

    var agent = CreateAgent(loggerFactory, apiModel, apiEndpoint, apiKey, apiModel, persistent, basePath: toolsWorkingDirectory, restrictToBasePath: toolsRestrictToWorkingDirectory);

    var cancellationToken = context.GetCancellationToken();
    while (!cancellationToken.IsCancellationRequested)
    {
        xmppCommunication.SendPresence(Show.Chat);

        var line = xmppCommunication.WaitForMessage(xmppTargetJid, cancellationToken);
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

        xmppCommunication.SendMessage(xmppTargetJid, response);

        if (persistent)
        {
            File.WriteAllText(GetMessagesFile(agent.Id), JsonConvert.SerializeObject(agent.Messages));
        }
    }
}

string GetMessagesFile(string id)
{
    return $"messages-{id}.json";
}

LlmAgentApi CreateAgent(ILoggerFactory loggerFactory, string id, string apiEndpoint, string apiKey, string model, bool loadMessages = false, string? systemPrompt = null, string? basePath = null, bool restrictToBasePath = true)
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
    var askQuestionTool = new AskQuestion(loggerFactory);

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
        askQuestionTool.Tool,
    };

    var messagesFile = GetMessagesFile(id);

    List<JObject>? messages = null;
    if (loadMessages)
    {
        if (File.Exists(messagesFile))
        {
            messages = JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(messagesFile));
        }
    }

    if (messages == null)
    {
        if (systemPrompt == null)
        {
            systemPrompt = Prompts.DefaultSystemPrompt;
        }

        messages =
        [
            JObject.FromObject(new { role = "system", content = systemPrompt })
        ];
    }

    return new LlmAgentApi(loggerFactory, id, apiEndpoint, apiKey, model, messages, tools);
}

return await rootCommand.InvokeAsync(args);
