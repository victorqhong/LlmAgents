namespace ToolServer;

using System.Text.Json;
using LlmAgents.State;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;

public class McpToolAdapter : McpServerTool
{
    private readonly LlmAgents.Tools.Tool tool;

    private readonly Tool protocolTool;

    public McpToolAdapter(LlmAgents.Tools.Tool tool)
    {
        this.tool = tool;

        protocolTool = new Tool()
        {
            Name = tool.Name,
            Description = tool.Schema.Function.Description,
            InputSchema = JsonSerializer.SerializeToElement(tool.Schema.Function.Parameters)
        };
    }

    public bool Debug { get; set; } = false;

    public override Tool ProtocolTool => protocolTool;

    public async override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var result = new CallToolResult();
        if (request.Params == null)
        {
            result.IsError = true;
            result.Content.Add(new TextContentBlock()
            {
                Text = "Params is null"
            });

            return result;
        }

        Session? session = null;
        var httpContextAccessor = request.Services?.GetService<IHttpContextAccessor>();
        var loggerFactory = request.Services?.GetService<ILoggerFactory>() ?? new LoggerFactory();
        var sessionDatabase = request.Services?.GetService<SessionDatabase>();
        if (httpContextAccessor != null && sessionDatabase != null)
        {
            var headers = httpContextAccessor.HttpContext?.Request.Headers;
            if (headers != null)
            {
                var sessionId = headers["X-Session-Id"].FirstOrDefault();
                if (!string.IsNullOrEmpty(sessionId))
                {
                    session = sessionDatabase.GetSession(sessionId);
                    if (session == null)
                    {
                        session = new Session(sessionId, sessionDatabase);
                        sessionDatabase.CreateSession(session);
                    }
                }
            }
        }

        if (session == null)
        {
            session = Session.Ephemeral(loggerFactory);
        }

        try
        {
            var arguments = JsonDocument.Parse(JsonSerializer.Serialize(request.Params.Arguments));
            result.StructuredContent = await tool.Function(session, arguments);
        }
        catch (Exception e)
        {
            result.IsError = true;
            result.Content.Add(new TextContentBlock()
            {
                Text = $"Got exception: {e.Message}"
            });
            if (Debug)
            {
                result.Content.Add(new TextContentBlock()
                {
                    Text = $"Stack trace: {e.StackTrace}"
                });
            }
        }

        return result;
    }
}