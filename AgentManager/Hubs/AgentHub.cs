using System.Text.Json;
using System.Text.Json.Nodes;
using AgentManager.Models;
using AgentManager.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgentManager.Hubs;

public record RegisterSessionDto(string SessionId, string AgentName, bool Persistent);
public record RegisterConnection(string SessionId, string ConnectionId, string? IpAddress);
public record UpdateStatusDto(string SessionId, string Status);
public record LogOperationDto(string SessionId, string Category, string Message, string Level);
public record AddMessagesDto(string SessionId, ICollection<AgentMessage> Messages);
public record SaveMessagesDto(string SessionId, ICollection<AgentMessage> Messages);

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
        var registerSession = new RegisterSessionDto(sessionId, agentName, persistent);
        var registerConnection = new RegisterConnection(sessionId, Context.ConnectionId, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString());
        await agentSessionService.Register(registerSession, registerConnection);
    }

    public async Task UpdateStatus(string sessionId, string status)
    {
        var updateStatus = new UpdateStatusDto(sessionId, status);
        await agentSessionService.UpdateStatusAsync(updateStatus);
    }

    public async Task Log(string sessionId, string category, string message, string level = "Info")
    {
        var session = await agentSessionService.GetSessionById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await agentLogService.Log(new LogOperationDto(sessionId, category, message, level));
    }

    public async Task AddMessages(string sessionId, string messageJson)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
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

        await agentMessageService.AddMessages(new AddMessagesDto(sessionId, messages));
        await agentSessionService.UpdateTimestampAsync(sessionId);
    }

    public async Task SaveMessages(string sessionId, string messageJson)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
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

        await agentMessageService.SaveMessages(new SaveMessagesDto(sessionId, messages));
        await agentSessionService.UpdateTimestampAsync(sessionId);
    }

    public async Task<string> GetMessages(string sessionId)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        var jsonArray = new JsonArray();

        var messages = await agentMessageService.GetSessionMessages(sessionId);
        foreach (var message in messages)
        {
            var jsonObject = JsonNode.Parse(message.Json);
            jsonArray.Add(jsonObject);
        }

        return jsonArray.ToString();
    }

    public async Task<string> GetLastUpdated(string sessionId)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        return session.UpdatedAt?.ToString() ?? DateTime.UnixEpoch.ToString();
    }

    public async Task<string?> GetState(string sessionId, string key)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        return await agentStateService.GetState(sessionId, key);
    }

    public async Task SetState(string sessionId, string key, string value)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await agentStateService.SetState(sessionId, key, value);
    }

    public async Task<Dictionary<string, string>> GetAllState(string sessionId)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        return await agentStateService.GetAllState(sessionId);
    }

    public Task Ping()
    {
        return Task.CompletedTask;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await agentSessionService.UnregisterByConnectionId(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
