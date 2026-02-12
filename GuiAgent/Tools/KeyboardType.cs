using LlmAgents.State;
using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using static GuiAgent.User32;

namespace GuiAgent.Tools;

public class KeyboardType : Tool
{
    public KeyboardType(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "keyboard_type",
            description = "Keyboard type text",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    text = new
                    {
                        type = "string",
                        description = "The keyboard text to type"
                    }
                },
                required = new[] { "text" }
            }
        }
    });

    public override Task<JToken> Function(Session session, JObject parameters)
    {
        var result = new JObject();

        if (!parameters.ContainsKey("text") || parameters.Value<string>("text") is not string text)
        {
            result.Add("error", "text parameter is missing");
            return Task.FromResult<JToken>(result);
        }

        INPUT[] inputs = new INPUT[text.Length * 2]; // One down, one up for each character

        for (int i = 0; i < text.Length; i++)
        {
            // Key down event
            inputs[i * 2].type = INPUT_KEYBOARD;
            inputs[i * 2].u.ki.wVk = 0;
            inputs[i * 2].u.ki.wScan = (ushort)text[i];
            inputs[i * 2].u.ki.dwFlags = KEYEVENTF_UNICODE;

            // Key up event
            inputs[i * 2 + 1].type = INPUT_KEYBOARD;
            inputs[i * 2 + 1].u.ki.wVk = 0;
            inputs[i * 2 + 1].u.ki.wScan = (ushort)text[i];
            inputs[i * 2 + 1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        }

        uint inputResult = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

        if (inputResult == 0)
        {
            result.Add("error", Marshal.GetLastWin32Error());
        }
        else
        {
            result.Add("result", "success");
        }

        return Task.FromResult<JToken>(result);
    }
}