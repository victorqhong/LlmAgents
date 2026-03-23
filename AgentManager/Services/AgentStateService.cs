using AgentManager.Data;
using AgentManager.Entities;
using AgentManager.Hubs;
using AgentManager.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentManager.Services;

public class AgentStateService
{
    private readonly Persistence persistence;

    public AgentStateService(IDbContextFactory<AppDbContext> dbFactory)
    {
        persistence = new Persistence(dbFactory);
    }

    public async Task SetState(Guid sessionId, string key, string value)
    {
        await persistence.SetState(sessionId, key, value);
    }

    public async Task<Dictionary<string, string>> GetAllState(Guid sessionId)
    {
        return await persistence.GetAllState(sessionId);
    }

    private class Persistence(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory = dbContextFactory;

        public async Task SetState(Guid sessionId, string key, string value)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var sessionEntity = db.Sessions.Find(sessionId);
            if (sessionEntity == null)
            {
                throw new KeyNotFoundException();
            }

            var stateEntity = await db.SessionStates
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.Key == key);

            if (stateEntity == null)
            {
                stateEntity = new SessionStateEntity
                {
                    Id = 0,
                    SessionId = sessionId,
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.UtcNow
                };
                db.SessionStates.Add(stateEntity);
            }
            else
            {
                stateEntity.Value = value;
                stateEntity.UpdatedAt = DateTime.UtcNow;
                db.SessionStates.Update(stateEntity);
            }

            await db.SaveChangesAsync();
        }

        public async Task<Dictionary<string, string>> GetAllState(Guid sessionId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var states = await db.SessionStates
                .Where(s => s.SessionId == sessionId)
                .ToListAsync();

            return states.ToDictionary(s => s.Key, s => s.Value);
        }
    }
}
