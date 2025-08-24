using LlmAgents.LlmApi;
using LlmAgents.LlmApi.Content;
using Newtonsoft.Json.Linq;
using System.Text;

namespace LlmAgents.Tools;

public class LlmApiCall : Tool
{
    private readonly LlmApiOpenAi llmApi;

    public LlmApiCall(ToolFactory toolFactory)
        : base(toolFactory)
    {
        llmApi = toolFactory.Resolve<LlmApiOpenAi>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "llm_call",
            description = "Makes a call to an LLM api in an empty context",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new
                    {
                        type = "string",
                        description = "Query to make"
                    }
                },
                required = new[] { "query" }
            }
        }
    });

    public override async Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var query = parameters["query"]?.ToString();
        if (string.IsNullOrEmpty(query))
        {
            result.Add("error", "query is null or empty");
            return result;
        }

        try
        {
            var messageContents = new MessageContentText { Text = query };
            var message = LlmApiOpenAi.GetMessage([messageContents]);
            var streamingCompletion = await llmApi.GenerateStreamingCompletion([message]);
            if (streamingCompletion == null)
            {
                result.Add("error", "could not generate completion");
                return result;
            }

            var sb = new StringBuilder();
            await foreach (var chunk in streamingCompletion)
            {
                sb.Append(chunk);
            }

            result.Add("result", sb.ToString());
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
