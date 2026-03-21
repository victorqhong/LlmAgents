using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.Extensions.Logging;

namespace LlmAgents.LlmApi.Llamacpp;

public class LlmApiLlamacpp : LlmApiOpenAi
{
    public LlmApiLlamacpp(ILoggerFactory loggerFactory, LlmApiConfig llmApiConfig)
        : base(loggerFactory, llmApiConfig)
    {
    }

    protected override ChatCompletionRequest CreateChatCompletionRequest(List<ChatCompletionMessageParam> messages, double? temperature, int? maxCompletionTokens, List<ChatCompletionFunctionTool>? tools, string? toolChoice)
    {
        return new LlamacppChatCompletionRequest(true)
        {
            Model = ApiConfig.ApiModel,
            Messages = messages,
            MaxCompletionTokens = maxCompletionTokens,
            Temperature = temperature,
            Tools = tools,
            ToolChoice = toolChoice,
            SlotId = ApiConfig.Llamacpp?.SlotId
        };
    }
}
