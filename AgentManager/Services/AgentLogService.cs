using AgentManager.Data;
using AgentManager.Entities;
using AgentManager.Hubs;
using AgentManager.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentManager.Services;

public class AgentLogService
{
    private readonly AgentSessionService agentSessionService;
    private readonly Persistence persistence;

    public Action<AgentLog>? OnLog;

    public AgentLogService(AgentSessionService agentSessionService, IDbContextFactory<AppDbContext> dbFactory)
    {
        this.agentSessionService = agentSessionService;

        persistence = new Persistence(dbFactory, agentSessionService);
    }

    public async Task Log(LogOperationDto logOperation)
    {
        var session = await agentSessionService.GetSessionById(logOperation.SessionId);
        if (session == null)
        {
            throw new KeyNotFoundException();
        }

        var log = new AgentLog
        {
            Session = session,
            Category = logOperation.Category,
            Level = logOperation.Level,
            Message = logOperation.Message
        };

        await persistence.InsertAsync(log);

        OnLog?.Invoke(log);
    }

    public async Task<List<AgentLog>> GetLogs(Guid sessionId)
    {
       var session = await agentSessionService.GetSessionById(sessionId);
       if (session == null)
       {
           throw new KeyNotFoundException();
       }

       return await persistence.GetLogs(sessionId, session);
    }

    public async Task<List<AgentLog>> GetLogs()
    {
        return await persistence.GetLogs();
    }

    private class Persistence(IDbContextFactory<AppDbContext> dbContextFactory, AgentSessionService agentSessionService)
    {
        public readonly IDbContextFactory<AppDbContext> _dbFactory = dbContextFactory;
        public readonly AgentSessionService agentSessionService = agentSessionService;

        public async Task<List<AgentLog>> GetLogs()
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var logs = await db.Logs
                .Include(l => l.Session)
                .AsNoTracking()
                .ToListAsync();

            var sessions = await agentSessionService.GetAllAsync();
            return logs.Select(log =>
            {
                var session = sessions.Where(s => Equals(s.Id, log.Session.Id));
                return new AgentLog
                {
                    Category = log.Category,
                    Level = log.Level,
                    Message = log.Message,
                    LogTime = log.LogTime,
                    Session = session.First()
                };
            }).ToList();
        }

        public async Task<List<AgentLog>> GetLogs(Guid sessionId, AgentSession session)
        {
           using var db = await _dbFactory.CreateDbContextAsync();
           return await db.Logs
               .Where(log => log.Session.Id == sessionId)
               .AsNoTracking()
               .Select(entity => new AgentLog { Session = session, Category = entity.Category, Level = entity.Level, Message = entity.Message, LogTime = entity.LogTime })
               .ToListAsync();
        }

        public async Task InsertAsync(AgentLog log)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var session = await db.Sessions.FindAsync(log.Session.Id);
            if (session == null)
            {
                throw new KeyNotFoundException();
            }

            db.Logs.Add(MapLogEntity(log, session));
            await db.SaveChangesAsync();
        }

        public static LogEntity MapLogEntity(AgentLog log, SessionEntity session)
        {
            var logEntity = new LogEntity
            {
                Id = 0,
                Category = log.Category,
                Level = log.Level,
                Message = log.Message,
                LogTime = log.LogTime,
                Session = session
            };

            return logEntity;
        }
    }
}
