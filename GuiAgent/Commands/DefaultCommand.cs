using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Agents.Work;
using LlmAgents.CommandLineParser;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.Content;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Drawing.Imaging;
using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;
using Parser = LlmAgents.CommandLineParser.Parser;

namespace GuiAgent.Commands;

internal class DefaultCommand : RootCommand
{
    private readonly ILoggerFactory loggerFactory;

    public DefaultCommand(ILoggerFactory loggerFactory)
        : base("ConsoleAgent - runs an LLM agent in the console")
    {
        this.loggerFactory = loggerFactory;

        this.SetHandler(CommandHandler);
        AddOption(LlmAgentsOptions.AgentId);
        AddOption(LlmAgentsOptions.ApiEndpoint);
        AddOption(LlmAgentsOptions.ApiKey);
        AddOption(LlmAgentsOptions.ApiModel);
        AddOption(LlmAgentsOptions.ContextSize);
        AddOption(LlmAgentsOptions.MaxCompletionTokens);
        AddOption(LlmAgentsOptions.ApiConfig);
        AddOption(LlmAgentsOptions.Persistent);
        AddOption(LlmAgentsOptions.SystemPromptFile);
        AddOption(LlmAgentsOptions.WorkingDirectory);
        AddOption(LlmAgentsOptions.StorageDirectory);
        AddOption(LlmAgentsOptions.SessionId);
        AddOption(LlmAgentsOptions.StreamOutput);
        AddOption(LlmAgentsOptions.ToolsConfig);
        AddOption(LlmAgentsOptions.ToolServerAddress);
        AddOption(LlmAgentsOptions.ToolServerPort);
    }

    private async Task CommandHandler(InvocationContext context)
    {
        var logger = loggerFactory.CreateLogger(nameof(GuiAgent));

        var apiParameters = Parser.ParseApiParameters(context) ?? Config.InteractiveApiConfigSetup();
        if (apiParameters == null)
        {
            Console.Error.WriteLine("apiEndpoint, apiKey, and/or apiModel is null or empty.");
            return;
        }

        if (apiParameters.ContextSize < 1)
        {
            logger.LogWarning("Context size must be greater than zero. Setting to default 8192");
            apiParameters.ContextSize = 8192;
        }

        if (apiParameters.MaxCompletionTokens < 1)
        {
            logger.LogWarning("Maximum completion tokens must be greater than zero. Setting to default 8192");
            apiParameters.MaxCompletionTokens = 8192;
        }

        var agentParameters = Parser.ParseAgentParameters(context);
        if (agentParameters == null)
        {
            logger.LogError("agentParameters not configured correctly");
            return;
        }

        var toolParameters = Parser.ParseToolParameters(context);
        if (string.IsNullOrEmpty(toolParameters.ToolsConfig) || !File.Exists(toolParameters.ToolsConfig))
        {
            toolParameters.ToolsConfig = Config.InteractiveToolsConfigSetup();
        }

        var sessionParameters = Parser.ParseSessionParameters(context);

        string? systemPrompt = Prompts.DefaultSystemPrompt;
        if (!string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            systemPrompt = File.ReadAllText(sessionParameters.SystemPromptFile);
        }

        var consoleCommunication = new ConsoleCommunication();

        var agent = await LlmAgentFactory.CreateAgent(loggerFactory, consoleCommunication, apiParameters, agentParameters, toolParameters, sessionParameters);

        agent.PreWaitForContent = () => { consoleCommunication.SendMessage("> "); };
        agent.PostParseUsage += (usage) => { consoleCommunication.SendMessage(string.Format("\nPromptTokens: {0}, CompletionTokens: {1}, TotalTokens: {2}, Context Used: {3}", usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, ((double)usage.TotalTokens / agent.llmApi.ContextSize).ToString("P"))); };

        var cancellationToken = context.GetCancellationToken();

        // Use SystemInformation.VirtualScreen to handle multi-monitor setups correctly
        // This provides the bounds of the entire virtual desktop
        int left = SystemInformation.VirtualScreen.Left;
        int top = SystemInformation.VirtualScreen.Top;
        int width = SystemInformation.VirtualScreen.Width;
        int height = SystemInformation.VirtualScreen.Height;

        // Create a Bitmap with the dimensions of the virtual screen
        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        // Create a Graphics object from the Bitmap
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            // Copy the screen content to the Bitmap
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        MessageContentImageUrl messageContentImageUrl;
        using (var memoryStream = new MemoryStream())
        {
            bitmap.Save(memoryStream, ImageFormat.Png);
            byte[] data = memoryStream.ToArray();
            var base64data = Convert.ToBase64String(data);

            messageContentImageUrl = new MessageContentImageUrl
            {
                MimeType = "image/png",
                DataBase64 = base64data
            };
        }

        var messageContentText = new MessageContentText { Text = "Describe this image" };

        var messages = LlmApiOpenAi.GetMessage([messageContentText, messageContentImageUrl]);
        var staticMessages = await agent.RunWork(new StaticMessages([messages], agent), null, cancellationToken);

        await agent.RunWork(new GetAssistantResponseWork(agent), staticMessages, cancellationToken);

        //await agent.Run(cancellationToken);
    }
}
