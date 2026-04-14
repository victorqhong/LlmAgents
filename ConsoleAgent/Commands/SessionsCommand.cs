using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;

namespace ConsoleAgent.Commands;

internal class SessionsCommand : Command
{
    private readonly Argument<string> sessionsAgentIdArgument = new("agentId")
    {
        Description = "The agent identifier"
    };

    private readonly ILoggerFactory loggerFactory;

    public SessionsCommand(ILoggerFactory loggerFactory)
        : base("sessions", "Lists the sessions of an LLM agent")
    {
        this.loggerFactory = loggerFactory;

        SetAction(CommandHandler);
        Arguments.Add(sessionsAgentIdArgument);
        Options.Add(LlmAgentsOptions.StorageDirectory);
    }

    private async Task CommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var agentId = parseResult.GetValue(sessionsAgentIdArgument);
        var storageDirectory = parseResult.GetValue(LlmAgentsOptions.StorageDirectory);

        if (storageDirectory == null)
        {
            return;
        }

        if (!Path.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }

        var storgeDirectory = Path.Combine(storageDirectory, $"{agentId}.db");
        using var stateDatabase = new StateDatabase(loggerFactory, storgeDirectory);
        var sessionDatabase = new SessionDatabase(stateDatabase);

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Choose an action:");
            Console.WriteLine("1) Get session details");
            Console.WriteLine("2) Delete session");
            Console.WriteLine("0) Exit");
            Console.Write("> ");
            
            var actionInput = Console.ReadLine();
            if (string.IsNullOrEmpty(actionInput) || string.Equals(actionInput, "0"))
            {
                break;
            }

            int action = int.Parse(actionInput);

            var sessions = sessionDatabase.GetSessions();
            if (sessions.Count == 0)
            {
                Console.WriteLine("No sessions");
                break;
            }
            else
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    Console.WriteLine($"{i + 1}) {s.SessionId} (Last Active: {s.LastActive.ToLocalTime()})");
                }
            }

            Console.WriteLine("0) Back");
            Console.Write("> ");
            var sessionInput = Console.ReadLine();
            if (string.IsNullOrEmpty(sessionInput) || string.Equals(sessionInput, "0"))
            {
                continue;
            }

            int sessionIndex = int.Parse(sessionInput) - 1;
            var session = sessions[sessionIndex];
            switch (action)
            {
                case 1: await GetSession(sessionDatabase, session, storageDirectory); break;
                case 2: DeleteSession(sessionDatabase, session); break;
            }
        }
    }

    private static async Task GetSession(SessionDatabase sessionDatabase, Session session, string storageDirectory)
    {
        session.PersistentMessagesPath = storageDirectory;
        await session.Load();
        var messages = session.GetMessages();
        OutputMessages(messages);
        Console.WriteLine();
    }

    private static void OutputMessages(ICollection<ChatCompletionMessageParam> messages)
    {
        var previewChars = 100;
        foreach (var message in messages)
        {
            if (message is ChatCompletionMessageParamUser userMessage)
            {
                if (userMessage.Content is ChatCompletionMessageParamContentString contentString)
                {
                    Console.WriteLine(FormatOutput($"User: {contentString.Content}", previewChars));
                }
                else if (userMessage.Content is ChatCompletionMessageParamContentParts contentParts)
                {
                    foreach (var part in contentParts.Content)
                    {
                        if (part is not ChatCompletionContentPartText textPart)
                        {
                            continue;
                        }

                        Console.WriteLine(FormatOutput($"User: {textPart.Text}", previewChars));
                    }
                }
            }
            else if (message is ChatCompletionMessageParamAssistant assistantMessage && assistantMessage.Content is ChatCompletionMessageParamContentString stringContent && !string.IsNullOrEmpty(stringContent.Content))
            {
                Console.WriteLine(FormatOutput($"Assistant: {stringContent.Content}", previewChars));
            }
        }
    }

    private static string FormatOutput(string s, int count)
    {
        var lines = s.Split('\n');
        var firstLine = lines[0];
        var lineLength = firstLine.Length;
        return firstLine.Substring(0, Math.Min(lineLength, count)) + (lineLength > count ? "...": "");
    }

    private static void DeleteSession(SessionDatabase sessionDatabase, Session session)
    {
        Console.WriteLine($"Deleting session: {session.SessionId}");
        sessionDatabase.DeleteSession(session);
    }

}
