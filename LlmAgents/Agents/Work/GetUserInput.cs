namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi.Content;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class GetUserInputWork : LlmAgentWork
{
    public GetUserInputWork(LlmAgent agent)
        : base(agent)
    {
    }

    public override Task<ICollection<ChatCompletionMessageParam>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<ChatCompletionMessageParam>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        agent.PreWaitForContent?.Invoke();
        var messageContent = await agent.agentCommunication.WaitForContent(cancellationToken);
        if (messageContent == null)
        {
           return;
        }

        agent.PostReceiveContent?.Invoke();

        Messages = [GetMessage(messageContent)];
    }

    public static ChatCompletionMessageParam GetMessage(IEnumerable<IMessageContent> messageContents)
    {
        ArgumentNullException.ThrowIfNull(messageContents);

        var content = new List<IChatCompletionContentPart>();

        foreach (var messageContent in messageContents)
        {
            if (messageContent is MessageContentText userMessage)
            {
                content.Add(new ChatCompletionContentPartText
                {
                    Type = "text",
                    Text = userMessage.Text
                });

            }
            else if (messageContent is MessageContentImageUrl imageUrl)
            {
                var url = string.Format("data:{0};base64,{1}", imageUrl.MimeType, imageUrl.DataBase64);

                content.Add(new ChatCompletionContentPartImage
                {
                    Type = "image_url",
                    ImageUrl = new ChatCompletionContentPartImageUrl { Url = url }
                });
            }
        }

        return new ChatCompletionMessageParamUser
        {
            Content = new ChatCompletionMessageParamContentParts { Content = content }
        };
    }
}
