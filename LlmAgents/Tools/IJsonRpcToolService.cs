namespace LlmAgents.Tools;

public interface IJsonRpcToolService
{
    Task<string?> CallTool(string name, string parameters);
    Task<string?> GetToolSchema(string name);
    Task<string[]> GetToolNames();
}
