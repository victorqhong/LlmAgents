using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using static GuiAgent.User32;

namespace GuiAgent.Tools;

public class MouseClick : Tool
{
    public MouseClick(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "mouse_click",
            description = "Mouse left click",
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
        throw new NotImplementedException();
    }

    static void SimulateMouseClick(int ndcX, int ndcY)
    {
        // Create the mouse down event
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dx = ndcX;
        inputs[0].u.mi.dy = ndcY;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN;

        // Create the mouse up event
        inputs[1].type = INPUT_MOUSE;
        //inputs[1].u.mi.dx = centerX * (65536 / screenWidth);
        //inputs[1].u.mi.dy = centerY * (65536 / screenHeight);
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;

        // Send the input
        uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

        if (result == 0)
        {
            Console.WriteLine("Failed to send mouse input. Error: " + Marshal.GetLastWin32Error());
        }
        else
        {
            Console.WriteLine("Mouse click simulated successfully.");
        }
    }

}