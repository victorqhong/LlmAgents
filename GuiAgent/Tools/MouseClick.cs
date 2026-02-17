using LlmAgents.State;
using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using static GuiAgent.User32;

namespace GuiAgent.Tools;

public class MouseClick : Tool
{
    private int screenWidth;
    private int screenHeight;

    public MouseClick(ToolFactory toolFactory)
        : base(toolFactory)
    {
        screenWidth = Screen.PrimaryScreen.Bounds.Width;
        screenHeight = Screen.PrimaryScreen.Bounds.Height;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "mouse_click",
            description = "Left click the mouse. Omit the x and y parameters to click the mouse at its current location.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    location = new
                    {
                        type = "string",
                        description = "location of the mouse click location as a string 'x y' leave empty to click at the current location"
                    }
                },
                required = new[] { "location" } 
            }
        }
    });

    public override Task<JToken> Function(Session session,JObject parameters)
    {
        var result = new JObject();
        if (!parameters.ContainsKey("location") || !parameters.TryGetValue("location", out var l) || l.Value<string>() is not string location)
        {
            result.Add("error", "location parameter is null or missing");
            return Task.FromResult<JToken>(result);
        }

        int x, y;
        if (string.IsNullOrEmpty(location))
        {
            var mousePosition = Control.MousePosition;
            x = mousePosition.X;
            y = mousePosition.Y;
        }
        else
        {
            var parts = location.Split(' ');
            if (parts.Length != 2)
            {
                result.Add("error", "location parameter does not contain two coordinates");
                return Task.FromResult<JToken>(result);
            }

            x = int.Parse(parts[0]);
            y = int.Parse(parts[1]);
        }

        // Create the mouse down event
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN;
        inputs[0].u.mi.dx = (int)(65535.0f / screenWidth * x);
        inputs[0].u.mi.dy = (int)(65535.0f / screenHeight * y);

        // Create the mouse up event
        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;

        // Send the input
        uint inputResult = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

        if (inputResult == 0)
        {
            result.Add("error", "Failed to send mouse input. Error: " + Marshal.GetLastWin32Error());
        }
        else
        {
            result.Add("result", "success");
        }

        return Task.FromResult<JToken>(result);
    }
}