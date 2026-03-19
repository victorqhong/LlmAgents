using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionStreamParser 
{
    private readonly Stream stream;

    public IAsyncEnumerable<string>? StreamingCompletion { get; private set; }

    public ChatCompletionChoiceFinishReason? FinishReason { get; private set; }

    public List<ChatCompletionMessageParam> Messages { get; private set; } = [];

    public IReadOnlyList<ChatCompletionMessageFunctionToolCall> ToolCalls { get; private set; } = [];

    public ChatCompletionUsage? Usage { get; private set; }

    public bool OutputReasoning { get; set; } = true;

    public ChatCompletionStreamParser(Stream stream)
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
            else if (string.Equals("data: [DONE]", line))
            {
                break;
            }

            var data = line[6..];

            ChatCompletionChunk? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data);
            }
            catch (JsonException) { }

            if (chunk == null)
            {
                continue;
            }

            if (chunk.Usage != null)
            {
                Usage = chunk.Usage;
            }

            if (chunk.Choices.Count < 1)
            {
                continue;
            }

            // by default just use the first choice, technically there may be more
            var choice = chunk.Choices[0];

            if (FinishReason == null)
            {
                FinishReason = choice.FinishReason;
            }

            if (string.IsNullOrEmpty(role))
            {
                role = choice.Delta.Role;
            }

            if (!string.IsNullOrEmpty(choice.Delta.ReasoningContent))
            {
                if (!seenReasoningContent)
                {
                    if (OutputReasoning)
                    {
                        yield return "<thinking>";
                    }

                    seenReasoningContent = true;
                }

                if (OutputReasoning)
                {
                    yield return choice.Delta.ReasoningContent;
                }

                reasoningContentBuffer.Append(choice.Delta.ReasoningContent);
            }

            if (!string.IsNullOrEmpty(choice.Delta.Content))
            {
                if (seenReasoningContent && !seenContent)
                {
                    if (OutputReasoning)
                    {
                        yield return "</thinking>\n";
                    }

                    seenContent = true;
                }

                yield return choice.Delta.Content;
                contentBuffer.Append(choice.Delta.Content);
            }

            if (choice.Delta.ToolCalls != null)
            {
                foreach (var toolCall in choice.Delta.ToolCalls)
                {
                    if (!parsedToolCalls.TryGetValue(toolCall.Index, out Dictionary<string, string>? toolCallData))
                    {
                        toolCallData = [];
                        parsedToolCalls.Add(toolCall.Index, toolCallData);
                    }

                    if (!string.IsNullOrEmpty(toolCall.Id) && !toolCallData.ContainsKey("id"))
                    {
                        toolCallData.Add("id", toolCall.Id);
                    }

                    if (!string.IsNullOrEmpty(toolCall.Type) && !toolCallData.ContainsKey("type"))
                    {
                        toolCallData.Add("type", toolCall.Type);
                    }

                    if (toolCall.Function != null)
                    {
                        if (!string.IsNullOrEmpty(toolCall.Function.Name) && !toolCallData.ContainsKey("functionName"))
                        {
                            toolCallData.Add("functionName", toolCall.Function.Name);
                        }

                        if (!string.IsNullOrEmpty(toolCall.Function.Arguments))
                        {
                            if (!toolCallData.TryAdd("functionArguments", toolCall.Function.Arguments))
                            {
                                toolCallData["functionArguments"] += toolCall.Function.Arguments;
                            }
                        }
                    }
                }
            }
        }

        if (seenReasoningContent && !seenContent)
        {
            if (OutputReasoning)
            {
                yield return "</thinking>\n";
            }

            seenContent = true;
        }

        role ??= "unknown";

        var content = contentBuffer.ToString();
        var reasoningContent = reasoningContentBuffer.ToString();

        if (FinishReason == null)
        {
            throw new NullReferenceException();
        }
        else if (FinishReason == ChatCompletionChoiceFinishReason.Stop)
        {
            Messages.Add(new ChatCompletionMessageParamAssistant
            {
                Content = new ChatCompletionMessageParamContentString { Content = content },
                ReasoningContent = reasoningContent
            });
        }
        else if (FinishReason == ChatCompletionChoiceFinishReason.ToolCalls)
        {
            var toolCalls = parsedToolCalls.Select(kvp =>
            {
                return new ChatCompletionMessageFunctionToolCall
                {
                    Id = kvp.Value["id"],
                    Type = kvp.Value["type"],
                    Function = new ChatCompletionMessageFunctionToolCallFunction
                    {
                        Name = kvp.Value["functionName"],
                        Arguments = kvp.Value["functionArguments"]
                    }
                };
            }).ToList();

            ToolCalls = toolCalls;

            Messages.Add(new ChatCompletionMessageParamAssistant
            {
                Content = new ChatCompletionMessageParamContentString { Content = content },
                ReasoningContent = reasoningContent,
                ToolCalls = toolCalls
            });
        }
        else
        {
            throw new NotImplementedException(FinishReason.ToString());
        }
    }
}
