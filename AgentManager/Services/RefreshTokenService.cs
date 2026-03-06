using System.Security.Cryptography;
using System.Text;
using AgentManager.Data;
using AgentManager.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentManager.Services;

public class RefreshTokenService
{
    private readonly IDbContextFactory<AppDbContext> dbContextFactory;

    public RefreshTokenService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory;
    }

    public async Task<string> CreateAsync(UserEntity user)
    {
        var refreshToken = CreateRefreshToken();
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

        using var db = await dbContextFactory.CreateDbContextAsync();
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            TokenHash = hash,
            ExpiresAt = DateTime.Now.AddDays(7),
            UserId = user.Id

        });
        await db.SaveChangesAsync();

        return refreshToken;
    }

    public async Task<RefreshTokenEntity?> FindAsync(string tokenHash)
    {
        using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.RefreshTokens
            .Where(t => string.Equals(t.TokenHash, tokenHash))
            .Include(t => t.User)
            .FirstOrDefaultAsync();
    }

    public async Task RevokeAsync(RefreshTokenEntity refreshToken)
    {
        using var db = await dbContextFactory.CreateDbContextAsync();
        refreshToken.RevokedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    private static string CreateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}

