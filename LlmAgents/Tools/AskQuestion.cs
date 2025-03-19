namespace LlmAgents.Tools;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;

public class AskQuestion
{
    private readonly ILogger Log;

    private JObject schema = JObject.FromObject(new
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

    public AskQuestion(ILoggerFactory loggerFactory)
    {
        Log = loggerFactory.CreateLogger(nameof(AskQuestion));

        Tool = new Tool
        {
            Schema = schema,
            Function = Function
        };
    }

    public Tool Tool { get; private set; }

    private JToken Function(JObject parameters)
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

            Console.WriteLine($"Question: {question}");
            Console.Write("Answer> ");
            answer = Console.ReadLine();

            result.Add("answer", answer);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}


