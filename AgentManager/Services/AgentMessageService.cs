using AgentManager.Data;
using AgentManager.Entities;
using AgentManager.Hubs;
using AgentManager.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentManager.Services;

public class AgentMessageService
{
    private readonly AgentSessionService agentSessionService;
    private readonly Persistence persistence;

    public Action<ICollection<AgentMessage>>? OnMessage;

    public AgentMessageService(AgentSessionService agentSessionService, IDbContextFactory<AppDbContext> dbFactory)
    {
        this.agentSessionService = agentSessionService;

        persistence = new Persistence(dbFactory);
    }

    public async Task AddMessages(AddMessageDto addMessage)
    {
        var session = await agentSessionService.GetSessionById(addMessage.SessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await persistence.InsertAsync(addMessage.SessionId, addMessage.Messages);

        OnMessage?.Invoke(addMessage.Messages);
    }

    public async Task<List<AgentMessage>> GetMessages()
    {
        var sessions = await agentSessionService.GetAllAsync();
        var messages = new List<AgentMessage>();
        foreach (var session in sessions)
        {
            messages.AddRange(await persistence.GetAsync(session));
        }

        return messages;
    }

    public async Task<List<AgentMessage>> GetSessionMessages(Guid sessionId)
    {
        var session = await agentSessionService.GetSessionById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        return await persistence.GetAsync(session);
    }

    public class Persistence(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory = dbContextFactory;

        public async Task InsertAsync(Guid sessionId, ICollection<AgentMessage> messages)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var sessionEntity = db.Sessions.Find(sessionId);
            if (sessionEntity == null)
            {
                throw new KeyNotFoundException();
            }

            var messageEntities = messages.Select(m =>
            {
                var messageEntity = new MessageEntity
                {
                    Id = 0,
                    Role = m.Role,
                    TextContent = m.TextContent,
                    Session = sessionEntity
                };

                return messageEntity;
            });

            await db.Messages.AddRangeAsync(messageEntities);
            await db.SaveChangesAsync();
        }

        public async Task<List<AgentMessage>> GetAsync(AgentSession agentSession)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Messages
                .Where(m => string.Equals(m.Session.Id, agentSession.Id))
                .AsNoTracking()
                .Select(m => new AgentMessage { Role = m.Role, TextContent = m.TextContent, ImageContent = m.ImageContent, ImageContentMimeType = m.ImageContentMimeType, Session = agentSession })
                .ToListAsync();
        }

        private static void MapAgentMessage(AgentMessage agentMessage, ref MessageEntity messageEntity, SessionEntity sessionEntity)
        {
            messageEntity.Session = sessionEntity;
            messageEntity.Role = agentMessage.Role;
            messageEntity.TextContent = agentMessage.TextContent;
            messageEntity.ImageContent = agentMessage.ImageContent;
            messageEntity.ImageContentMimeType = agentMessage.ImageContentMimeType;
        }
    }
}
