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

var apiConfigOption = new Option<string?>(
    name: "--apiConfig",
    description: "Path to a JSON file with configuration for api values",
    getDefaultValue: () => Environment.GetEnvironmentVariable("API_CONFIG", environmentVariableTarget) ?? "api.json");

var toolsConfigOption = new Option<string>(
    name: "--toolsConfig",
    description: "Path to a JSON file with configuration for tool values",
    getDefaultValue: () => Environment.GetEnvironmentVariable("TOOLS_CONFIG", environmentVariableTarget) ?? "tools.json");

var workingDirectoryOption = new Option<string>(
    name: "--workingDirectory",
    description: "",
    getDefaultValue: () => Environment.CurrentDirectory);

var rootCommand = new RootCommand("XmppAgent");
rootCommand.SetHandler(RootCommandHandler);
rootCommand.AddOption(apiEndpointOption);
rootCommand.AddOption(apiKeyOption);
rootCommand.AddOption(apiModelOption);
rootCommand.AddOption(apiConfigOption);
rootCommand.AddOption(persistentOption);
rootCommand.AddOption(toolsConfigOption);
rootCommand.AddOption(workingDirectoryOption);

void RootCommandHandler(InvocationContext context)
{
    var apiEndpoint = string.Empty;
    var apiKey = string.Empty;
    var apiModel = string.Empty;
    var persistent = false;

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

    persistent = context.ParseResult.GetValueForOption(persistentOption);

    var toolsConfigValue = context.ParseResult.GetValueForOption(toolsConfigOption);
    var workingDirectoryValue = context.ParseResult.GetValueForOption(workingDirectoryOption);

    var cancellationToken = context.GetCancellationToken();

    var consoleCommunication = new ConsoleCommunication();

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

    Tool[]? tools = null;
    var agent = CreateAgent(loggerFactory, consoleCommunication, apiModel, apiEndpoint, apiKey, apiModel, out tools, persistent, basePath: workingDirectoryValue, toolsFilePath: toolsConfigValue);

    var optionRunTool = "Run tool";
    var optionChatMode = "Chat mode";
    var optionMeasureContext = "Measure context";
    var optionClearContext = "Clear context";
    var optionPrintContext = "Print context";
    var optionPruneContext = "Prune context";
    var optionRunConversation = "Run conversation";
    var optionExit = "Exit";

    var options = new string[]
    {
        optionRunTool,
        optionChatMode,
        optionPrintContext,
        optionMeasureContext,
        optionClearContext,
        optionPruneContext,
        optionRunConversation,
    };

    var optionsMap = new Dictionary<string, Action>()
    {
        { optionRunTool, RunTool },
        { optionRunConversation, RunConversation },
        { optionChatMode, ChatMode },
        { optionPrintContext, PrintContext },
        { optionMeasureContext, MeasureContext },
        { optionClearContext, ClearContext },
        { optionPruneContext, PruneContext },
    };

    void RunTool()
    {
        if (tools == null)
        {
            return;
        }

        for (int i = 0; i < tools.Length; i++)
        {
            Console.WriteLine($"{i + 1}) {tools[i].Name}");
        }

        Console.Write("Tool choice> ");
        var toolInput = Console.ReadLine();
        if (!string.IsNullOrEmpty(toolInput))
        {
            var toolChoice = int.Parse(toolInput) - 1;

            Console.WriteLine(tools[toolChoice].Schema);
            Console.WriteLine();

            Console.Write("Tool parameters (JSON)> ");
            var toolParametersInput = Console.ReadLine();
            if (!string.IsNullOrEmpty(toolParametersInput))
            {

                var toolParameters = JObject.Parse(toolParametersInput);
                var toolOutput = tools[toolChoice].Function(toolParameters);
                Console.WriteLine(toolOutput);
            }
        }
    }

    void ChatMode()
    {
        while (true)
        {
            Console.Write("> ");

            var line = consoleCommunication.WaitForMessage(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            var response = agent.GenerateCompletion(line, cancellationToken);
            if (string.IsNullOrEmpty(response))
            {
                continue;
            }

            consoleCommunication.SendMessage(response);

            if (persistent)
            {
                LlmAgentApi.SaveMessages(agent);
            }
        }
    }

    void RunConversation()
    {
        JObject CreateMessage(string role, string content)
        {
            return JObject.FromObject(new { role, content });
        }

        Console.Write("Turns> ");
        var turnsInput = Console.ReadLine();
        if (string.IsNullOrEmpty(turnsInput))
        {
            return;
        }

        int turns = int.Parse(turnsInput);

        var systemPromptCommon = $"{Prompts.DefaultSystemPrompt}";
        var systemPrompt1 = $"{systemPromptCommon}";
        var systemPrompt2 = $"You are expected to guide the user.";
        var initialMessage = "Read 'PLAN.md' and start implementing the plan.";

        var messages1 = new List<JObject>()
        {
            CreateMessage("system", systemPrompt1),
        };

        var messages2 = new List<JObject>()
        {
            CreateMessage("system", systemPrompt2)
        };

        var agent1 = new LlmAgentApi(loggerFactory, "Agent1", apiEndpoint, apiKey, apiModel, messages1, tools);
        var agent2 = new LlmAgentApi(loggerFactory, "Agent2", apiEndpoint, apiKey, apiModel, messages2, tools);

        messages1.Add(CreateMessage("user", initialMessage));
        messages2.Add(CreateMessage("assistant", initialMessage));

        Console.WriteLine($"{agent2.Id}> {initialMessage}");
        Console.WriteLine($"====================================");

        var agent1Response = string.Empty;
        var agent2Response = string.Empty;
        for (int i = 0; i < turns; i++)
        {
            if (i > 0)
            {
                messages1.Add(CreateMessage("user", agent2Response));
            }

            agent1Response = agent1.GenerateCompletion(messages1);
            Console.WriteLine($"{agent1.Id}> {agent1Response}");
            Console.WriteLine($"====================================");
            Console.ReadLine();

            messages2.Add(CreateMessage("user", agent1Response));
            agent2Response = agent2.GenerateCompletion(messages2);
            Console.WriteLine($"{agent2.Id}> {agent2Response}");
            Console.WriteLine($"====================================");
            Console.ReadLine();
        }
    }

    void MeasureContext()
    {
        var total = 0;
        foreach (var message in agent.Messages)
        {
            total += message.ToString().Length;
        }

        Console.WriteLine($"Context size: {total}");
        Console.WriteLine($"Message count: {agent.Messages.Count}");
    }

    void ClearContext()
    {
        agent.Messages.Clear();
    }

    void PrintContext()
    {
        foreach (var message in agent.Messages)
        {
            Console.WriteLine(message);
        }
    }

    void PruneContext()
    {
        Console.Write("Number of messages to prune> ");
        var pruneResponse = Console.ReadLine();
        if (string.IsNullOrEmpty(pruneResponse))
        {
            return;
        }

        var pruneCount = int.Parse(pruneResponse);
        agent.Messages.RemoveRange(0, pruneCount);

        while (agent.Messages.Count > 0 && !string.Equals(agent.Messages[0].Value<string>("role"), "user"))
        {
            agent.Messages.RemoveAt(0);
        }
    }

    while (!cancellationToken.IsCancellationRequested)
    {
        for (int i = 0; i < options.Length; i++)
        {
            Console.WriteLine($"{i + 1}) {options[i]}");
        }

        Console.WriteLine($"0) {optionExit}");

        Console.Write("Choice> ");
        var input = Console.ReadLine();
        if (string.IsNullOrEmpty(input))
        {
            break;
        }

        Console.WriteLine();

        if (string.Equals(input, "0"))
        {
            break;
        }

        var choice = int.Parse(input) - 1;
        optionsMap[options[choice]]();
    }
}

LlmAgentApi CreateAgent(ILoggerFactory loggerFactory, IAgentCommunication agentCommunication, string id, string apiEndpoint, string apiKey, string model, out Tool[]? tools, bool loadMessages = false, string? systemPrompt = null, string? basePath = null, string? toolsFilePath = null)
{
    var todoDatabase = new TodoDatabase(loggerFactory, Path.Join(basePath, "todo.db"));

    tools = null;
    if (!string.IsNullOrEmpty(toolsFilePath))
    {
        var toolsFile = JObject.Parse(File.ReadAllText(toolsFilePath));
        var toolFactory = new ToolFactory(loggerFactory, toolsFile);

        toolFactory.Register(agentCommunication);
        toolFactory.Register(loggerFactory);
        toolFactory.Register(todoDatabase);

        toolFactory.AddParameter("basePath", basePath ?? Environment.CurrentDirectory);

        tools = toolFactory.Load();
    }

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
