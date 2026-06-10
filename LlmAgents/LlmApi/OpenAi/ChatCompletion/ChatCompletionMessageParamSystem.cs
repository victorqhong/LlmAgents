namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionMessageParamSystem : ChatCompletionMessageParam
{
    public ChatCompletionMessageParamSystem() { }
    public ChatCompletionMessageParamSystem(string content) { Content = new ChatCompletionMessageParamContentString { Content = content }; }
}
