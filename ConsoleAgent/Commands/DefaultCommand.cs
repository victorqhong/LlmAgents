using LlmAgents.Agents;
using LlmAgents.Agents.Capabilities;
using LlmAgents.Api.Extensions;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;
using Parser = LlmAgents.CommandLineParser.Parser;

namespace ConsoleAgent.Commands;

internal class DefaultCommand : RootCommand
{
    private readonly ILoggerFactory loggerFactory;

    public DefaultCommand(ILoggerFactory loggerFactory)
        : base("ConsoleAgent - runs an LLM agent in the console")
    {
        this.loggerFactory = loggerFactory;

        SetAction(CommandHandler);
        Options.Add(LlmAgentsOptions.AgentId);
        Options.Add(LlmAgentsOptions.ApiEndpoint);
        Options.Add(LlmAgentsOptions.ApiKey);
        Options.Add(LlmAgentsOptions.ApiModel);
        Options.Add(LlmAgentsOptions.ContextSize);
        Options.Add(LlmAgentsOptions.ApiConfig);
        Options.Add(LlmAgentsOptions.Persistent);
        Options.Add(LlmAgentsOptions.SystemPromptFile);
        Options.Add(LlmAgentsOptions.WorkingDirectory);
        Options.Add(LlmAgentsOptions.StorageDirectory);
        Options.Add(LlmAgentsOptions.Session);
        Options.Add(LlmAgentsOptions.StreamOutput);
        Options.Add(LlmAgentsOptions.ToolsConfig);
        Options.Add(LlmAgentsOptions.McpConfigPath);
        Options.Add(LlmAgentsOptions.AgentManagerUrl);
        Options.Add(LlmAgentsOptions.Debug);
    }

    private async Task CommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(ConsoleAgent));

        var apiParameters = Parser.ParseApiParameters(parseResult);
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

        var agentParameters = Parser.ParseAgentParameters(parseResult);
        if (agentParameters == null)
        {
            logger.LogError("agentParameters not configured correctly");
            return;
        }

        var toolParameters = Parser.ParseToolParameters(parseResult);
        var sessionParameters = Parser.ParseSessionParameters(parseResult);

        var promptUser = new SemaphoreSlim(0);
        var loaded = false;

        var consoleCommunication = new ConsoleCommunication();
        consoleCommunication.PreWaitForContent += async () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!loaded)
            {
                return;
            }

            await consoleCommunication.SendMessage("User: ", false);
        };

        var agent = await LlmAgentFactory.CreateAgent(loggerFactory, apiParameters, agentParameters, toolParameters, sessionParameters);

        agent.ConfigureAssistantResponseWork = (agent, work) =>
        {
            work.OutputReasoning = parseResult.GetValue(LlmAgentsOptions.Debug);
            work.StreamOutput = agentParameters.StreamOutput;
            work.PostParseUsage += async (_, usage) =>
            {
                var message = string.Format("PromptTokens: {0}, CompletionTokens: {1}, TotalTokens: {2}, Context Used: {3}", 
                    usage.PromptTokens,
                    usage.CompletionTokens,
                    usage.TotalTokens,
                    ((double)usage.TotalTokens / agent.llmApi.ApiConfig.ContextSize).ToString("P")
                );

                await consoleCommunication.SendMessage(message, true);
            };
        };

        if (agentParameters.AgentManagerUrl != null)
        {
            await agent.ConfigureAgentHub(agentParameters.AgentManagerUrl, consoleCommunication, logger);
        }

        var (sessionId, newSession) = await GetSessionId(agent, consoleCommunication, sessionParameters);
        var sessionMetadata = new SessionCapability.SessionMetadata("Console", "default", sessionId);
        var handle = await agent.SessionCapability.CreateSession(sessionMetadata, consoleCommunication, cancellationToken);
        var session = agent.SessionCapability.GetSession(handle);

        await LlmAgentFactory.InitializeSession(session, agent, agentParameters, sessionParameters, newSession, cancellationToken);

        await OutputMessages(session, consoleCommunication);

        agent.PostProcessSession += s =>
        {
            if (!string.Equals(session?.SessionId, s.SessionId))
            {
                return;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                promptUser.Release();
            }
        };

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await promptUser.WaitAsync();
                var content = await consoleCommunication.WaitForContent(cancellationToken);
                if (content == null)
                {
                    continue;
                }

                await agent.SessionCapability.PostInput(handle, content, cancellationToken);
            }

        }, cancellationToken);

        loaded = true;
        promptUser.Release();
        await agent.Run(cancellationToken);

        if (!agentParameters.Persistent)
        {
            await PromptToSaveSession(session, consoleCommunication, cancellationToken);
        }
    }

    private static async Task PromptToSaveSession(Session session, ConsoleCommunication consoleCommunication, CancellationToken cancellationToken)
    {
        if (session.GetMessages().Count == 0)
        {
            return;
        }

        await consoleCommunication.SendMessage("\nSave this session to resume later? (y/n): ", false);

        var content = await consoleCommunication.WaitForContent(CancellationToken.None);
        var response = content?.OfType<MessageContentText>().FirstOrDefault()?.Text;
        if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (session.SessionDatabase.GetSession(session.SessionId) == null)
        {
            session.SessionDatabase.CreateSession(session);
        }

        await session.Save(cancellationToken);
        await consoleCommunication.SendMessage($"Saved session: {session.SessionId}", true);
    }

    private static async Task<(string, bool)> GetSessionId(LlmAgent agent, SessionCommunication sessionCommunication, SessionParameters sessionParameters)
    {
        var sessionDatabase = agent.SessionCapability.SessionDatabase;

        var newSession = false;
        string? sessionId = null;
        if (!string.IsNullOrEmpty(sessionParameters.Session))
        {
            if (string.Equals(sessionParameters.Session, "new"))
            {
                sessionId = Guid.NewGuid().ToString();
                newSession = true;
            }
            else if (string.Equals(sessionParameters.Session, "choose"))
            {
                var sessions = sessionDatabase.GetSessions();
                for (int i = 0; i < sessions.Count; i++)
                {
                    await sessionCommunication.SendMessage($"{i + 1}) {sessions[i].SessionId} (Last Active: {sessions[i].LastActive.ToLocalTime()})", true);
                }

                await sessionCommunication.SendMessage("> ", false);
                var content = await sessionCommunication.WaitForContent(CancellationToken.None);
                if (content is MessageContentText[] textContent && !string.IsNullOrEmpty(textContent[0].Text) && int.TryParse(textContent[0].Text, out var sessionChoice)) 
                {
                    sessionId = sessions[sessionChoice - 1].SessionId;
                }
            }
            else if (string.Equals(sessionParameters.Session, "latest"))
            {
                var session = sessionDatabase.GetLatestSession();
                if (session != null)
                {
                    sessionId = session.SessionId;
                }
            }
            else
            {
                sessionId = sessionParameters.Session;
            }
        }

        if (sessionId == null)
        {
            sessionId = Guid.NewGuid().ToString();
            newSession = true;
        }

        return (sessionId, newSession);
    }

    private static async Task OutputMessages(Session session, SessionCommunication sessionCommunication)
    {
        foreach (var message in session.GetMessages())
        {
            if (message is ChatCompletionMessageParamUser userMessage)
            {
                if (userMessage.Content is ChatCompletionMessageParamContentString contentString)
                {
                    await sessionCommunication.SendMessage($"User: {contentString.Content}", true);
                }
                else if (userMessage.Content is ChatCompletionMessageParamContentParts contentParts)
                {
                    foreach (var part in contentParts.Content)
                    {
                        if (part is not ChatCompletionContentPartText textPart)
                        {
                            continue;
                        }

                        await sessionCommunication.SendMessage($"User: {textPart.Text}", true);
                    }
                }
            }
            else if (message is ChatCompletionMessageParamAssistant assistantMessage && assistantMessage.Content is ChatCompletionMessageParamContentString stringContent && !string.IsNullOrEmpty(stringContent.Content))
            {
                await sessionCommunication.SendMessage($"Assistant: {stringContent.Content}", true);
            }
        }
    }
}
