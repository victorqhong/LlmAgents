namespace LlmAgents.Tools;

public class ToolEventBus : IToolEventBus
{
    private readonly Dictionary<Type, List<Func<ToolEvent, Task>>> toolEventHandlers = [];

    public void PostToolEvent<T>(ToolEvent @event) where T : Tool
    {
        Task.Run(async () =>
        {
            var type = typeof(T);
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

    public void SubscribeToolEvent<T>(Func<ToolEvent, Task> handler) where T : Tool
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
