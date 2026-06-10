namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionMessageParamUser : ChatCompletionMessageParam
{
    public ChatCompletionMessageParamUser() { }
    public ChatCompletionMessageParamUser(string content) { Content = new ChatCompletionMessageParamContentString { Content = content }; }
}
