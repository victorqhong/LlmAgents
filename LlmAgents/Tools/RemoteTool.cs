namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;

public class RemoteTool : Tool
{
    private readonly string toolName;
    private readonly IJsonRpcToolService jsonRpcToolService;

    public RemoteTool(string toolName, IJsonRpcToolService jsonRpcToolService)
        : base(null)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(jsonRpcToolService);

        this.toolName = toolName;
        this.jsonRpcToolService = jsonRpcToolService;

        var toolSchema = jsonRpcToolService.GetToolSchema(toolName).ConfigureAwait(false).GetAwaiter().GetResult();
        ArgumentException.ThrowIfNullOrEmpty(toolSchema);

        Schema = JObject.Parse(toolSchema);
    }

    public override JObject Schema { get; protected set; }

    public override JToken Function(JObject parameters)
    {
        var result = jsonRpcToolService.CallTool(toolName, parameters.ToString()).ConfigureAwait(false).GetAwaiter().GetResult();
        ArgumentException.ThrowIfNullOrEmpty(result);

        return JToken.Parse(result);
    }
}

