using Microsoft.EntityFrameworkCore;
using AgentManager.Entities;

namespace AgentManager.Data;

public class AppDbContext : DbContext
{
    public DbSet<SessionEntity> Sessions { get; set; } = null!;
    public DbSet<LogEntity> Logs { get; set; } = null!;
    public DbSet<MessageEntity> Messages { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
