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
                    Message = m.Message,
                    Session = sessionEntity
                };

                return messageEntity;
            });

            await db.Messages.AddRangeAsync(messageEntities);
            await db.SaveChangesAsync();
        }

        private static void MapAgentMessage(AgentMessage agentMessage, ref MessageEntity messageEntity, SessionEntity sessionEntity)
        {
            messageEntity.Session = sessionEntity;
            messageEntity.Message = agentMessage.Message;
        }
    }
}
