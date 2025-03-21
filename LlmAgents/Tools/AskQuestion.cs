namespace LlmAgents.Tools;

using LlmAgents.Communication;
using Newtonsoft.Json.Linq;
using System;

public class AskQuestion : Tool
{
    private readonly IAgentCommunication agentCommunication;

    public AskQuestion(ToolFactory toolFactory)
        : base(toolFactory)
    {
        agentCommunication = toolFactory.Resolve<IAgentCommunication>();
    }

    public override JObject Schema { get ; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "question_ask",
            description = "Ask a question to someone knowledgeable only when there is a choice to be made",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    question = new
                    {
                        type = "string",
                        description = "The question to ask"
                    }
                },
                required = new[] { "question" }
            }
        }
    });

    public override JToken Function(JObject parameters)
    {
        var result = new JObject();

        var question = parameters["question"]?.ToString();
        if (string.IsNullOrEmpty(question))
        {
            result.Add("error", "question is null or empty");
            return result;
        }

        try
        {
            var answer = string.Empty;

            agentCommunication.SendMessage(question);
            answer = agentCommunication.WaitForMessage();

            result.Add("answer", answer);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}


