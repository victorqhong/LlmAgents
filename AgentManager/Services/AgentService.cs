using AgentManager.Data;
using AgentManager.Entities;
using AgentManager.Hubs;
using AgentManager.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Data;

namespace AgentManager.Services;

public class AgentService 
{
    private readonly Persistence persistence;

    private readonly ConcurrentDictionary<string, AgentConnection> connections = new();

    public event Action<Agent>? OnChange;

    public AgentService(IDbContextFactory<AppDbContext> dbFactory)
    {
        persistence = new Persistence(dbFactory);
    }

    public async Task<List<Agent>> GetAllAsync()
    {
        return await persistence.GetAllAsync();
    }

    public async Task<Agent?> GetAgent(string id)
    {
        return await persistence.GetByIdAsync(id);
    }

    public async Task RemoveAgent(string id)
    {
        var agent = await persistence.GetByIdAsync(id);
        if (agent == null)
        {
            throw new KeyNotFoundException();
        }

        if (connections.ContainsKey(id))
        {
            throw new InvalidOperationException("Cannot remove an active session.");
        }

        await persistence.DeleteAsync(id);
        connections.TryRemove(id, out _);

        OnChange?.Invoke(agent);
    }

    public async Task Register(RegisterAgentDto registerAgent, RegisterConnection registerConnection)
    {
        var agent = await persistence.GetByIdAsync(registerAgent.Id);
        if (agent == null)
        {
            agent = new Agent
            {
                Id = registerAgent.Id,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Status = "NEW",
                Persistent = registerAgent.Persistent,
            };

            await persistence.InsertAsync(agent);
        }

        connections[registerAgent.Id] = new AgentConnection
        {
            ConnectionId = registerConnection.ConnectionId,
            IpAddress = registerConnection.IpAddress
        };

        OnChange?.Invoke(agent);
    }

    public async Task Unregister(string id)
    {
        var agent = await persistence.GetByIdAsync(id);
        if (agent == null)
        {
            throw new KeyNotFoundException();
        }

        await UpdateStatusAsync(new UpdateStatusDto(id, "DISCONNECTED"));
        if (!agent.Persistent)
        {
            await persistence.DeleteAsync(id);
        }

        connections.TryRemove(id, out _);

        OnChange?.Invoke(agent);
    }

    public async Task UnregisterByConnectionId(string connectionId)
    {
        var connection = connections.FirstOrDefault(x => string.Equals(x.Value.ConnectionId, connectionId));
        var key = connection.Key;

        if (key != null)
        {
            await Unregister(key);
        }
    }

    public async Task UpdateStatusAsync(UpdateStatusDto updateStatus)
    {
        var agent = await persistence.GetByIdAsync(updateStatus.Id);
        if (agent == null)
        {
            throw new KeyNotFoundException();
        }

        agent.Status = updateStatus.Status;
        agent.UpdatedAt = DateTime.Now;
        await persistence.UpdateAsync(agent);

        OnChange?.Invoke(agent);
    }

    public async Task UpdateTimestampAsync(string id)
    {
        var agent = await persistence.GetByIdAsync(id);
        if (agent == null)
        {
            throw new KeyNotFoundException();
        }

        agent.UpdatedAt = DateTime.Now;
        await persistence.UpdateAsync(agent);

        OnChange?.Invoke(agent);
    }

    private class Persistence(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        public readonly IDbContextFactory<AppDbContext> _dbFactory = dbContextFactory;

        public async Task<List<Agent>> GetAllAsync()
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var sessions = await db.Agents
                .AsNoTracking()
                .ToListAsync();
            return sessions.Select(MapAgent).ToList();
        }

        public async Task<Agent?> GetByIdAsync(string id)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var agentEntity = await db.Agents.FindAsync(id);
            if (agentEntity == null)
            {
                return null;
            }

            return MapAgent(agentEntity);
        }

        public async Task InsertAsync(Agent agent)
        {
            var agentEntity = new AgentEntity 
            {
                Id = agent.Id,
                Status = agent.Status,
                Persistent = agent.Persistent,
                CreatedAt = DateTime.Now,
            };

            MapAgentEntity(agent, ref agentEntity, agentEntity.Sessions);

            using var db = await _dbFactory.CreateDbContextAsync();
            db.Agents.Add(agentEntity);
            await db.SaveChangesAsync();
        }

        public async Task UpdateAsync(Agent agent)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var agentEntity = await db.Agents.FindAsync(agent.Id);
            if (agentEntity == null)
            {
                throw new KeyNotFoundException();
            }

            MapAgentEntity(agent, ref agentEntity, agentEntity.Sessions);
            db.Agents.Update(agentEntity);

            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(string id)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var agent = await db.Agents.FindAsync(id);
            if (agent != null)
            {
                db.Agents.Remove(agent);
                await db.SaveChangesAsync();
            }
        }

        public static Agent MapAgent(AgentEntity agentEntity)
        {
            var agent = new Agent
            {
                Id = agentEntity.Id,
                Status = agentEntity.Status,
                Persistent = agentEntity.Persistent,
                CreatedAt = agentEntity.CreatedAt,
                UpdatedAt = agentEntity.UpdatedAt,
            };

            return agent;
        }

        public static void MapAgentEntity(Agent agent, ref AgentEntity agentEntity, ICollection<SessionEntity> sessionEntities)
        {
            agentEntity.Id = agent.Id;
            agentEntity.Status = agent.Status;
            agentEntity.Persistent = agent.Persistent;
            agentEntity.CreatedAt = agent.CreatedAt;
            agentEntity.UpdatedAt = agent.UpdatedAt;

            agentEntity.Sessions = sessionEntities;
        }
    }
}
