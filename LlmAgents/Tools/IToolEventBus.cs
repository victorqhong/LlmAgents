namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;

public interface IToolEventBus
{
    void PostToolEvent<T>(T sender, JObject arguments, JToken result) where T: Tool;
    void SubscribeToolEvent<T>(Func<ToolEvent, Task> handler);
}
