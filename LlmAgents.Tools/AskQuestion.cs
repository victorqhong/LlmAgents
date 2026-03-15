namespace LlmAgents.Tools;

using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using System;
using System.Text.Json.Nodes;
using System.Text.Json;
using LlmAgents.Extensions;
public class AskQuestion : Tool
{
    private readonly IAgentCommunication agentCommunication;

    public AskQuestion(ToolFactory toolFactory)
        : base(toolFactory)
    {
        agentCommunication = toolFactory.Resolve<IAgentCommunication>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "question_ask",
            Description = "Ask a question to someone knowledgeable only when there is a choice to be made",
            Parameters = new()
            {
                Properties = new()
                {
                    { "question", new() { Type = "string", Description = "The question to ask" } }
                },
                Required = ["question"]
            }
        }
    };

    public override async Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("question", string.Empty, out var question) || string.IsNullOrEmpty(question))
        {
            result.Add("error", "question is null or empty");
            return result;
        }

        try
        {
            var answer = string.Empty;

            await agentCommunication.SendMessage(question, true);
            while (string.IsNullOrEmpty(answer))
            {
                var content = await agentCommunication.WaitForContent();
                if (content == null)
                {
                    break;
                }

                foreach (var message in content)
                {
                    if (message is MessageContentText textContent)
                    { answer = textContent.Text; break;
                    }
                }

                await Task.Delay(1000);
            }

            result.Add("answer", answer);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}


