namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;

public class ToolEventBus : IToolEventBus
{
    private readonly Dictionary<Type, List<Func<ToolEvent, Task>>> toolEventHandlers = [];

    public void PostToolEvent<T>(T sender, JObject arguments, JToken result) where T: Tool
    {
        var @event = new ToolEvent
                {
                    Sender = sender,
                    Arguments = arguments,
                    Result = result
                };

        Task.Run(async () =>
                {
                    var type = sender.GetType();
                    if (!toolEventHandlers.TryGetValue(type, out List<Func<ToolEvent, Task>>? handlers))
                    {
                        return;
                    }

                    foreach (var handler in handlers)
                    {
                        await handler(@event);
                    }
                });
    }

    public void SubscribeToolEvent<T>(Func<ToolEvent, Task> handler)
    {
        var type = typeof(T);
        if (!toolEventHandlers.TryGetValue(type, out List<Func<ToolEvent, Task>>? handlers))
        {
            handlers = [];
            toolEventHandlers[type] = handlers;
        }

        handlers.Add(handler);
    }
}
