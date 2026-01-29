namespace LlmAgents.Agents;

using Newtonsoft.Json.Linq;

internal interface ILlmAgentWork
{
    Task<ICollection<JObject>?> GetState(CancellationToken cancellationToken);
    ICollection<JObject>? Messages { get; }
}
