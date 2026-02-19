using System.Text.Json;
using AgentManager.Models;
using AgentManager.Services;
using Microsoft.AspNetCore.SignalR;

namespace AgentManager.Hubs;

public record RegisterSessionDto(Guid SessionId, string AgentName, bool Persistent);
public record RegisterConnection(Guid SessionId, string ConnectionId, string? IpAddress);
public record UpdateStatusDto(Guid SessionId, string Status);
public record LogOperationDto(Guid SessionId, string Category, string Message, string Level);
public record AddMessageDto(Guid SessionId, ICollection<AgentMessage> Messages);

public class AgentHub : Hub<IAgentClient>
{
    private readonly AgentSessionService agentSessionService;
    private readonly AgentLogService agentLogService;
    private readonly AgentMessageService agentMessageService;

    public AgentHub(AgentSessionService agentSessionService, AgentLogService agentLogService, AgentMessageService agentMessageService)
    {
        this.agentSessionService = agentSessionService;
        this.agentLogService = agentLogService;
        this.agentMessageService = agentMessageService;
    }

    public async Task Register(string agentName, string sessionId, bool persistent)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException(nameof(sessionId));
        }

        var registerSession = new RegisterSessionDto(id, agentName, persistent);
        var registerConnection = new RegisterConnection(id, Context.ConnectionId, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString());
        await agentSessionService.Register(registerSession, registerConnection);
    }

    public async Task UpdateStatus(string sessionId, string status)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            throw new ArgumentException(nameof(sessionId));
        }

        var updateStatus = new UpdateStatusDto(id, status);
        await agentSessionService.UpdateStatusAsync(updateStatus);
    }

    public async Task Log(string sessionId, string category, string message, string level = "Info")
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

        await agentLogService.Log(new LogOperationDto(id, category, message, level));
    }

    public async Task AddMessages(string sessionId, string messageJson)
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

       var messages = new List<AgentMessage>();

       using var doc = JsonDocument.Parse(messageJson);
       foreach (var element in doc.RootElement.EnumerateArray())
       {
           var role = element.GetProperty("role").GetString();
           var contentProperty = element.GetProperty("content");

           string? message = null;
           if (contentProperty.ValueKind == JsonValueKind.Array)
           {
               foreach (var e in contentProperty.EnumerateArray())
               {
                   var type = e.GetProperty("type").GetString();
                   if (string.Equals(type, "text"))
                   {
                       message = e.GetProperty("text").GetString()!;
                       break;
                   }
               }
           }
           else if (contentProperty.ValueKind == JsonValueKind.String)
           {
               message = contentProperty.GetString()!;
           }
           else
           {
               throw new NotImplementedException();
           }

           if (!string.IsNullOrEmpty(message))
           {
               messages.Add(new AgentMessage
               {
                   Message = message,
                   Session = session
               });
           }
       }

       await agentMessageService.AddMessages(new AddMessageDto(id, messages));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await agentSessionService.Unregister(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
