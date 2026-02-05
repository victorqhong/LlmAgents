using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
using System.Drawing.Imaging;

namespace GuiAgent.Tools;

public class Screenshot : Tool
{
    public Screenshot(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "screenshot",
            description = "Gets a screenshot",
            parameters = new
            {
                type = "object",
                properties = new
                {
                },
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

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

        using (var memoryStream = new MemoryStream())
        {
            bitmap.Save(memoryStream, ImageFormat.Png);
            byte[] data = memoryStream.ToArray();
            var base64data = Convert.ToBase64String(data);

            result.Add("DataBase64", base64data);
            result.Add("MimeType", "image/png");
        }

        return Task.FromResult<JToken>(result);
    }
}
