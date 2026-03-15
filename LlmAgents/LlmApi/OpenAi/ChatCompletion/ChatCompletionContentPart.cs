using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

[JsonDerivedType(typeof(ChatCompletionContentPartText))]
[JsonDerivedType(typeof(ChatCompletionContentPartImage))]
public class ChatCompletionContentPart
{
}
