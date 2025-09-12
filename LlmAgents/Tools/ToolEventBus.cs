namespace LlmAgents.Tools;

public class ToolEventBus : IToolEventBus
{
    private readonly Dictionary<Type, List<Func<ToolEvent, Task>>> toolEventHandlers = [];

    public void PostToolEvent(ToolEvent @event)
    {
        var handlers = new List<Func<ToolEvent, Task>>();

        var type = @event.Sender.GetType();
        foreach (var handler in toolEventHandlers)
        {
            if (handler.Key.IsAssignableFrom(type))
            {
                handlers.AddRange(handler.Value);
            }
        }

        foreach (var handler in handlers)
        {
            handler(@event).ConfigureAwait(false).GetAwaiter().GetResult();
        }
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
