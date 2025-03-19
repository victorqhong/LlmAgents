using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Todo;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using XmppAgent.Communication;
using XmppAgent.Logging;
using XmppDotNet.Xmpp;

var apiEndpoint = "";
var apiKey = "";
var model = "gpt-4o";
var persistent = false;

var environmentVariableTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;
var credentialsFile = Environment.GetEnvironmentVariable("LLM_CREDENTIALS_FILE", environmentVariableTarget);
if (File.Exists(credentialsFile))
{
    var json = JObject.Parse(File.ReadAllText(credentialsFile));
    apiEndpoint = $"{json.Value<string>("AZURE_OPENAI_ENDPOINT")}/openai/deployments/{model}/chat/completions?api-version=2024-08-01-preview";
    apiKey = json.Value<string>("AZURE_OPENAI_API_KEY");
}

if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey))
{
    Console.Error.WriteLine("apiEndpoint or apiKey is null or empty.");
    return;
}

var targetJid = string.Empty;
var domain = string.Empty;
var username = string.Empty;
var password = string.Empty;

var xmppSettingsFile = Environment.GetEnvironmentVariable("XMPP_SETTINGS_FILE", environmentVariableTarget);
if (File.Exists(xmppSettingsFile))
{
    var json = JObject.Parse(File.ReadAllText(xmppSettingsFile));
    username = json.Value<string>("username");
    password = json.Value<string>("password");
    domain = json.Value<string>("domain");
    targetJid = json.Value<string>("targetJid");
}

if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("domain, username, and/or password is null or empty.");
    return;
}

XmppCommunication xmppCommunication = new XmppCommunication(username, domain, password, trustHost: true);

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Trace)
        .AddProvider(new XmppLoggerProvider(xmppCommunication, targetJid));
});

var basePath = Environment.CurrentDirectory;
var restrictToBasePath = true;

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

string GetMessagesFile(string id)
{
    return $"messages-{id}.json";
}

LlmAgentApi LoadAgent(string id, string apiEndpoint, string apiKey, string model, bool loadMessages = false, string? systemPrompt = null)
{
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

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    if (e.SpecialKey == ConsoleSpecialKey.ControlC)
    {
        e.Cancel = true;
        cancellationTokenSource.Cancel();
    }
};

var agent = LoadAgent("agent1", apiEndpoint, apiKey, model, persistent);
while (true)
{
    xmppCommunication.SendPresence(Show.Chat);

    var line = xmppCommunication.WaitForMessage(targetJid, cancellationTokenSource.Token);
    if (string.IsNullOrEmpty(line))
    {
        break;
    }

    xmppCommunication.SendPresence(Show.DoNotDisturb);

    var response = agent.GenerateCompletion(line, cancellationTokenSource.Token);
    if (string.IsNullOrEmpty(response))
    {
        continue;
    }

    xmppCommunication.SendMessage(targetJid, response);

    if (persistent)
    {
        File.WriteAllText(GetMessagesFile(agent.Id), JsonConvert.SerializeObject(agent.Messages));
    }
}
