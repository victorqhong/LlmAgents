using AgentManager.Data;
using AgentManager.Entities;
using AgentManager.Hubs;
using AgentManager.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Data;

namespace AgentManager.Services;

public class AgentSessionService
{
    private readonly Persistence persistence;

    private readonly ConcurrentDictionary<Guid, AgentConnection> connections = new();

    public event Action<AgentSession>? OnChange;

    public AgentSessionService(IDbContextFactory<AppDbContext> dbFactory)
    {
        persistence = new Persistence(dbFactory);
    }

    public async Task<List<AgentSession>> GetAllAsync()
    {
        return await persistence.GetAllAsync();
    }

    public async Task<AgentSession?> GetSessionById(Guid sessionId)
    {
        return await persistence.GetByIdAsync(sessionId);
    }

    public async Task RemoveSessionAsync(Guid sessionId)
    {
        var session = await persistence.GetByIdAsync(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        if (connections.ContainsKey(sessionId))
        {
            throw new InvalidOperationException("Cannot remove an active session.");
        }

        await persistence.DeleteAsync(sessionId);
        connections.TryRemove(sessionId, out _);

        OnChange?.Invoke(session);
    }

    public async Task Register(RegisterSessionDto registerSession, RegisterConnection registerConnection)
    {
        var session = await persistence.GetByIdAsync(registerSession.SessionId);
        if (session == null)
        {
            session = new AgentSession
            {
                Id = registerSession.SessionId,
                AgentName = registerSession.AgentName,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Status = "NEW",
                Persistent = registerSession.Persistent,
            };
            await persistence.InsertAsync(session);
        }

        connections[registerSession.SessionId] = new AgentConnection
        {
            SessionId = registerConnection.SessionId,
            ConnectionId = registerConnection.ConnectionId,
            IpAddress = registerConnection.IpAddress
        };

        OnChange?.Invoke(session);
    }

    public async Task Unregister(Guid sessionId)
    {
        var session = await persistence.GetByIdAsync(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        await UpdateStatusAsync(new UpdateStatusDto(sessionId, "DISCONNECTED"));
        if (!session.Persistent)
        {
            await persistence.DeleteAsync(sessionId);
        }

        connections.TryRemove(sessionId, out _);

        OnChange?.Invoke(session);
    }

    public async Task Unregister(string connectionId)
    {
        var connection = connections.FirstOrDefault(x => string.Equals(x.Value.ConnectionId, connectionId));
        var key = connection.Key;

        if (key != default)
        {
            await Unregister(key);
        }
    }

    public async Task UpdateStatusAsync(UpdateStatusDto updateStatus)
    {
        var session = await persistence.GetByIdAsync(updateStatus.SessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        session.Status = updateStatus.Status;
        session.UpdatedAt = DateTime.Now;
        await persistence.UpdateAsync(session);

        OnChange?.Invoke(session);
    }

    public async Task UpdateTimestampAsync(Guid sessionId)
    {
        var session = await persistence.GetByIdAsync(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        session.UpdatedAt = DateTime.Now;
        await persistence.UpdateAsync(session);

        OnChange?.Invoke(session);
    }

    private class Persistence(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        public readonly IDbContextFactory<AppDbContext> _dbFactory = dbContextFactory;

        public async Task<List<AgentSession>> GetAllAsync()
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var sessions = await db.Sessions
                .AsNoTracking()
                .ToListAsync();
            return sessions.Select(MapAgentSession).ToList();
        }

        public async Task<AgentSession?> GetByIdAsync(Guid sessionId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var sessionEntity = await db.Sessions.FindAsync(sessionId);
            if (sessionEntity == null)
            {
                return null;
            }

            return MapAgentSession(sessionEntity);
        }

        public async Task InsertAsync(AgentSession session)
        {
            var sessionEntity = new SessionEntity
            {
                Id = session.Id,
                AgentName = session.AgentName,
                CreatedAt = DateTime.Now,
                Status = session.Status,
                Persistent = session.Persistent,
            };

            MapSessionEntity(session, ref sessionEntity, sessionEntity.Logs, sessionEntity.Messages);

            using var db = await _dbFactory.CreateDbContextAsync();
            db.Sessions.Add(sessionEntity);
            await db.SaveChangesAsync();
        }

        public async Task UpdateAsync(AgentSession session)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var sessionEntity = await db.Sessions.FindAsync(session.Id);
            if (sessionEntity == null)
            {
                throw new KeyNotFoundException();
            }

            MapSessionEntity(session, ref sessionEntity, sessionEntity.Logs, sessionEntity.Messages);
            db.Sessions.Update(sessionEntity);

            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid sessionId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var session = await db.Sessions.FindAsync(sessionId);
            if (session != null)
            {
                db.Sessions.Remove(session);
                await db.SaveChangesAsync();
            }
        }

        public static AgentSession MapAgentSession(SessionEntity sessionEntity)
        {
            var session = new AgentSession
            {
                Id = sessionEntity.Id,
                AgentName = sessionEntity.AgentName,
                CreatedAt = sessionEntity.CreatedAt,
                Status = sessionEntity.Status,
                Persistent = sessionEntity.Persistent,
                SessionName = sessionEntity.SessionName,
                UpdatedAt = sessionEntity.UpdatedAt,
            };

            return session;
        }

        public static void MapSessionEntity(AgentSession session, ref SessionEntity sessionEntity, ICollection<LogEntity> logEntities, ICollection<MessageEntity> messageEntities)
        {
            sessionEntity.Id = session.Id;
            sessionEntity.AgentName = session.AgentName;
            sessionEntity.CreatedAt = session.CreatedAt;
            sessionEntity.Status = session.Status;
            sessionEntity.SessionName = session.SessionName;
            sessionEntity.UpdatedAt = session.UpdatedAt;

            sessionEntity.Logs = logEntities;
            sessionEntity.Messages = messageEntities;
        }
    }
}
