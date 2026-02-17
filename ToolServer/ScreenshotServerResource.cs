using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Drawing.Imaging;

namespace ToolServer;

public class ScreenshotServerResource : McpServerResource
{
    public override ResourceTemplate ProtocolResourceTemplate => new ResourceTemplate() { Name = "screenshot", UriTemplate = "resource://screenshot" };

    public override IReadOnlyList<object> Metadata => [];

    public override bool IsMatch(string uri)
    {
        return false;
    }

    public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default)
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

        var result = new ReadResourceResult();
        using (var memoryStream = new MemoryStream())
        {
            bitmap.Save(memoryStream, ImageFormat.Png);
            byte[] data = memoryStream.ToArray();
            var base64data = Convert.ToBase64String(data);

            result.Contents = [new BlobResourceContents() { Blob = base64data, Uri = "resource://screenshot", MimeType = "image/png" }];
        }

        return ValueTask.FromResult(result);
    }
}
