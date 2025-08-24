using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public interface IToolEventBus
{
    void PostToolEvent(ToolEvent e);
    void SubscribeToolEvent<T>(Func<ToolEvent, Task> handler) where T : Tool;
}

public static class IToolEventBusExtensions
{
    public static void PostCallToolEvent<T>(this IToolEventBus eventBus, T sender, JObject arguments, JToken result) where T : Tool
    {
        eventBus.PostToolEvent(new ToolCallEvent
        {
            Sender = sender,
            Arguments  = arguments,
            Result = result
        });
    }
}
