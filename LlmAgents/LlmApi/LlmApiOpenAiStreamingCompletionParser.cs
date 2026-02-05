using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LlmAgents.LlmApi;

public class LlmApiOpenAiStreamingCompletionParser
{
    private readonly Stream stream;

    public IAsyncEnumerable<string>? StreamingCompletion { get; private set; }

    public string? FinishReason { get; private set; }

    public int UsageCompletionTokens { get; private set; }

    public int UsagePromptTokens { get; private set; }

    public int UsageTotalTokens { get; private set; }

    public List<JObject> Messages { get; private set; } = [];

    public IReadOnlyList<Dictionary<string, string>> ParsedToolCalls { get; private set; } = [];

    public LlmApiOpenAiStreamingCompletionParser(Stream stream)
    {
        this.stream = stream;
    }

    public void Parse(CancellationToken cancellationToken)
    {
        StreamingCompletion = ParseCompletion(stream, cancellationToken);
    }

    private async IAsyncEnumerable<string> ParseCompletion(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? role = null;
        var parsedToolCalls = new Dictionary<int, Dictionary<string, string>>();
        var contentBuffer = new StringBuilder();
        var reasoningContentBuffer = new StringBuilder();

        var seenReasoningContent = false;
        var seenContent = false;

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            else if ("data: [DONE]".Equals(line))
            {
                break;
            }

            var data = line[6..];
            var json = JObject.Parse(data);

            if (!json.ContainsKey("object") || json.Value<string>("object") is not string @object || !"chat.completion.chunk".Equals(@object))
            {
                continue;
            }

            if (json.ContainsKey("usage") && json.Value<JObject>("usage") is JObject usage)
            {
                UsageCompletionTokens = usage.Value<int>("completion_tokens");
                UsagePromptTokens = usage.Value<int>("prompt_tokens");
                UsageTotalTokens = usage.Value<int>("total_tokens");
            }

            // by default just use the first choice, technically there may be more
            if (!json.ContainsKey("choices") || json.Value<JArray>("choices") is not JArray choices || choices.Count < 1 || choices[0] is not JObject choice)
            {
                continue;
            }

            if (string.IsNullOrEmpty(FinishReason))
            {
                FinishReason = choice.Value<string>("finish_reason");
            }

            if (!choice.ContainsKey("delta") || choice.Value<JObject>("delta") is not JObject delta)
            {
                continue;
            }

            if (string.IsNullOrEmpty(role))
            {
                role = delta.Value<string>("role");
            }

            if (delta.ContainsKey("reasoning_content") && delta.Value<string>("reasoning_content") is string deltaReasoningContent)
            {
                if (!seenReasoningContent)
                {
                    yield return "<thinking>";
                    seenReasoningContent = true;
                }

                yield return deltaReasoningContent;
                reasoningContentBuffer.Append(deltaReasoningContent);
            }

            if (delta.ContainsKey("content") && delta.Value<string>("content") is string deltaContent)
            {
                if (seenReasoningContent && !seenContent)
                {
                    yield return "</thinking>\n";
                    seenContent = true;
                }

                yield return deltaContent;
                contentBuffer.Append(deltaContent);
            }

            if (delta.ContainsKey("tool_calls") && delta.Value<JArray>("tool_calls") is JArray toolCalls)
            {
                foreach (var element in toolCalls)
                {
                    if (element is not JObject toolCall)
                    {
                        continue;
                    }

                    if (!toolCall.ContainsKey("index") || toolCall.Value<int?>("index") is not int index)
                    {
                        continue;
                    }

                    if (!parsedToolCalls.TryGetValue(index, out Dictionary<string, string>? toolCallData))
                    {
                        toolCallData = [];
                        parsedToolCalls.Add(index, toolCallData);
                    }

                    if (toolCall.ContainsKey("id") && toolCall.Value<string>("id") is string id && !toolCallData.ContainsKey("id"))
                    {
                        toolCallData.Add("id", id);
                    }

                    if (toolCall.ContainsKey("type") && toolCall.Value<string>("type") is string type && !toolCallData.ContainsKey("type"))
                    {
                        toolCallData.Add("type", type);
                    }

                    if (toolCall.ContainsKey("function") && toolCall.Value<JObject>("function") is JObject function)
                    {
                        if (function.ContainsKey("name") && function.Value<string>("name") is string functionName && !toolCallData.ContainsKey("functionName"))
                        {
                            toolCallData.Add("functionName", functionName);
                        }

                        if (function.ContainsKey("arguments") && function.Value<string>("arguments") is string functionArguments)
                        {
                            if (!toolCallData.TryAdd("functionArguments", functionArguments))
                            {
                                toolCallData["functionArguments"] += functionArguments;
                            }
                        }
                    }
                }
            }
        }

        if (seenReasoningContent && !seenContent)
        {
            yield return "</thinking>\n";
            seenContent = true;
        }

        role ??= "assistant";

        var content = contentBuffer.ToString();
        var reasoning_content = reasoningContentBuffer.ToString();

        if (string.Equals(FinishReason, "stop"))
        {
            Messages.Add(JObject.FromObject(new { role, content, reasoning_content }));
        }
        else if (string.Equals(FinishReason, "tool_calls"))
        {
            var tool_calls = parsedToolCalls.Select(kvp =>
            {
                return new
                {
                    id = kvp.Value["id"],
                    type = kvp.Value["type"],
                    function = new
                    {
                        name = kvp.Value["functionName"],
                        arguments = kvp.Value["functionArguments"]
                    }
                };
            });

            ParsedToolCalls = parsedToolCalls.Select(kvp => kvp.Value).ToList();

            Messages.Add(JObject.FromObject(new { role, content, reasoning_content, tool_calls }));
        }
        else
        {
            throw new NotImplementedException(FinishReason);
        }
    }
}
