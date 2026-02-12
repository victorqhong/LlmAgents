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
                    x = new
                    {
                        type = "number",
                        description = "x-coordinate of the mouse click location"
                    },
                    y = new
                    {
                        type = "string",
                        description = "y-coordinate of the mouse click location"
                    }
                },
            }
        }
    });

    public override Task<JToken> Function(Session session,JObject parameters)
    {
        // Create the mouse down event
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN;

        if (parameters.ContainsKey("x") && parameters.TryGetValue("x", out var x) && parameters.ContainsKey("y") && parameters.TryGetValue("y", out var y))
        {
            inputs[0].u.mi.dx = 65535 / screenWidth * x.Value<int>();
            inputs[0].u.mi.dy = 65535 / screenHeight * y.Value<int>();
        }
        else
        {
            var mousePosition = Control.MousePosition;
            inputs[0].u.mi.dx = 65535 / screenWidth * mousePosition.X;
            inputs[0].u.mi.dy = 65535 / screenHeight * mousePosition.Y;
        }

        // Create the mouse up event
        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;

        // Send the input
        uint inputResult = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

        var result = new JObject();
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