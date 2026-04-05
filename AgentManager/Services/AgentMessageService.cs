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

    public async Task AddMessages(AddMessagesDto addMessage)
    {
        var session = await agentSessionService.GetSessionById(addMessage.SessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await persistence.InsertAsync(addMessage.SessionId, addMessage.Messages);

        OnMessage?.Invoke(addMessage.Messages);
    }

    public async Task SaveMessages(SaveMessagesDto saveMessages)
    {
        var session = await agentSessionService.GetSessionById(saveMessages.SessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await persistence.ReplaceAsync(saveMessages.SessionId, saveMessages.Messages);

        OnMessage?.Invoke(saveMessages.Messages);
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

        public async Task ReplaceAsync(Guid sessionId, ICollection<AgentMessage> messages)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var sessionEntity = db.Sessions.Find(sessionId);
            if (sessionEntity == null)
            {
                throw new KeyNotFoundException();
            }

            var sessionMessages = db.Messages.Where(m => m.Session == sessionEntity);
            db.Messages.RemoveRange(sessionMessages);
            var messageEntities = messages.Select(m =>
            {
                var messageEntity = new MessageEntity
                {
                    Id = 0,
                    Session = sessionEntity,
                    Json = m.Json
                };

                return messageEntity;
            });
            await db.Messages.AddRangeAsync(messageEntities);
            await db.SaveChangesAsync();
        }

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
                    Session = sessionEntity,
                    Json = m.Json
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
                .Select(m => AgentMessage.Parse(m.Json, agentSession))
                .ToListAsync();
        }

        private static void MapAgentMessage(AgentMessage agentMessage, ref MessageEntity messageEntity, SessionEntity sessionEntity)
        {
            messageEntity.Session = sessionEntity;
            messageEntity.Json = agentMessage.Json;
        }
    }
}
