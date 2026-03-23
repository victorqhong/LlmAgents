using System.Text.Json;
using System.Text.Json.Nodes;
using AgentManager.Models;
using AgentManager.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgentManager.Hubs;

public record RegisterSessionDto(Guid SessionId, string AgentName, bool Persistent);
public record RegisterConnection(Guid SessionId, string ConnectionId, string? IpAddress);
public record UpdateStatusDto(Guid SessionId, string Status);
public record LogOperationDto(Guid SessionId, string Category, string Message, string Level);
public record AddMessageDto(Guid SessionId, ICollection<AgentMessage> Messages);

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class AgentHub : Hub<IAgentClient>
{
    private readonly AgentSessionService agentSessionService;
    private readonly AgentLogService agentLogService;
    private readonly AgentMessageService agentMessageService;
    private readonly AgentStateService agentStateService;

    public AgentHub(AgentSessionService agentSessionService, AgentLogService agentLogService, AgentMessageService agentMessageService, AgentStateService agentStateService)
    {
        this.agentSessionService = agentSessionService;
        this.agentLogService = agentLogService;
        this.agentMessageService = agentMessageService;
        this.agentStateService = agentStateService;
    }

    public async Task Register(string agentName, string sessionId, bool persistent)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException("sessionId is not a GUID", nameof(sessionId));
        }

        var registerSession = new RegisterSessionDto(id, agentName, persistent);
        var registerConnection = new RegisterConnection(id, Context.ConnectionId, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString());
        await agentSessionService.Register(registerSession, registerConnection);
    }

    public async Task UpdateStatus(string sessionId, string status)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException("sessionId is not a GUID", nameof(sessionId));
        }

        var updateStatus = new UpdateStatusDto(id, status);
        await agentSessionService.UpdateStatusAsync(updateStatus);
    }

    public async Task Log(string sessionId, string category, string message, string level = "Info")
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException("sessionId is not a GUID", nameof(sessionId));
        }

        var session = await agentSessionService.GetSessionById(id);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await agentLogService.Log(new LogOperationDto(id, category, message, level));
    }

    public async Task AddMessages(string sessionId, string messageJson)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException("sessionId is not a GUID", nameof(sessionId));
        }

        var session = await agentSessionService.GetSessionById(id);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        var messages = new List<AgentMessage>();

        using var doc = JsonDocument.Parse(messageJson);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            messages.Add(AgentMessage.Parse(element, session));
        }

        await agentMessageService.AddMessages(new AddMessageDto(id, messages));
    }

    public async Task<string> GetMessages(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException("sessionId is not a GUID", nameof(sessionId));
        }

        var session = await agentSessionService.GetSessionById(id);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        var jsonArray = new JsonArray();

        var messages = await agentMessageService.GetSessionMessages(id);
        foreach (var message in messages)
        {
            var jsonObject = JsonNode.Parse(message.Json);
            jsonArray.Add(jsonObject);
        }

        return jsonArray.ToString();
    }

    public async Task SyncState(string sessionId, string key, string value)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException(nameof(sessionId));
        }

        var session = await agentSessionService.GetSessionById(id);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await agentStateService.SetState(id, key, value);
    }

    public async Task<Dictionary<string, string>> GetAllState(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException(nameof(sessionId));
        }

        var session = await agentSessionService.GetSessionById(id);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        return await agentStateService.GetAllState(id);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await agentSessionService.Unregister(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
