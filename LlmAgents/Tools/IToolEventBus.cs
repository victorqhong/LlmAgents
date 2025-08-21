using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public interface IToolEventBus
{
    void PostToolEvent<T>(ToolEvent e) where T: Tool;
    void SubscribeToolEvent<T>(Func<ToolEvent, Task> handler) where T : Tool;
}

public static class IToolEventBusExtensions
{
    public static void PostCallToolEvent<T>(this IToolEventBus eventBus, T sender, JObject arguments, JToken result) where T : Tool
    {
        eventBus.PostToolEvent<T>(new ToolCallEvent
        {
            Sender = sender,
            Arguments  = arguments,
            Result = result
        });
    }
}
