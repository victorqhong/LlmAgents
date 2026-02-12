namespace GuiAgent.Agents.Work;

using LlmAgents.Agents;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.Content;
using Newtonsoft.Json.Linq;
using System.Drawing.Imaging;

public class GetUserInputWorkAndScreenshot : LlmAgentWork
{
    public GetUserInputWorkAndScreenshot(LlmAgent agent)
        : base(agent)
    {
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        // Use SystemInformation.VirtualScreen to handle multi-monitor setups correctly
        // This provides the bounds of the entire virtual desktop
        int left = SystemInformation.VirtualScreen.Left;
        int top = SystemInformation.VirtualScreen.Top;
        int width = SystemInformation.VirtualScreen.Width;
        int height = SystemInformation.VirtualScreen.Height;

        // Create a Bitmap with the dimensions of the virtual screen
        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        // Create a Graphics object from the Bitmap
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            // Copy the screen content to the Bitmap
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        MessageContentImageUrl messageContentImageUrl;
        using (var memoryStream = new MemoryStream())
        {
            bitmap.Save(memoryStream, ImageFormat.Png);
            byte[] data = memoryStream.ToArray();
            var base64data = Convert.ToBase64String(data);

            messageContentImageUrl = new MessageContentImageUrl
            {
                MimeType = "image/png",
                DataBase64 = base64data
            };
        }

        agent.PreWaitForContent?.Invoke();

        var messageContent = await agent.agentCommunication.WaitForContent(cancellationToken);
        if (messageContent == null)
        {
            return;
        }

        agent.PostReceiveContent?.Invoke();

        Messages = [LlmApiOpenAi.GetMessage(messageContent.Prepend(messageContentImageUrl))];

        foreach (var message in messageContent)
        {
            if (message is not MessageContentText textContent)
            {
                continue;
            }

            await agent.agentCommunication.SendMessage($"User: {textContent.Text}", true);
        }
    }
}
