using LlmAgents.Agents;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.Content;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;

namespace GuiAgent.Agents.Work;

public class GetScreenshot : LlmAgentWork
{
    private readonly McpClient? mcpClient;

    public GetScreenshot(McpClient? mcpClient, LlmAgent agent)
        : base(agent)
    {
        this.mcpClient = mcpClient;
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        var resource = await mcpClient.ReadResourceAsync("resource://screenshot");
        await agent.agentCommunication.SendMessage($"Agent: took screenshot", true);

        var blobContent = resource.Contents[0] as BlobResourceContents;

        await mcpClient.SendMessageAsync(new JsonRpcRequest() { Method = "test" });

        MessageContentImageUrl messageContentImageUrl = new MessageContentImageUrl
        {
            MimeType = blobContent.MimeType,
            DataBase64 = blobContent.Blob
        };

        Messages = [LlmApiOpenAi.GetMessage([messageContentImageUrl])];
    }
}
