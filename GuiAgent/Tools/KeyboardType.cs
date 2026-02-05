using LlmAgents.Tools;
using Newtonsoft.Json.Linq;

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
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        throw new NotImplementedException();
    }
}