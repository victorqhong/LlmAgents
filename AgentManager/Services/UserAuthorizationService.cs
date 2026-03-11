using AgentManager.Configuration;
using AgentManager.Data;
using AgentManager.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentManager.Services;

public class UserAuthorizationService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly AuthorizationOptions _authOptions;

    public UserAuthorizationService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        AuthorizationOptions authOptions)
    {
        _dbContextFactory = dbContextFactory;
        _authOptions = authOptions;
    }

    public Task<bool> IsUserAllowedAsync(string gitHubLogin)
    {
        var isAllowed = _authOptions.AllowedUsers.Any(u => 
            u.GitHubLogin.Equals(gitHubLogin, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(isAllowed);
    }

    public Task<string[]> GetUserRolesAsync(string gitHubLogin)
    {
        var allowedUser = _authOptions.AllowedUsers.FirstOrDefault(u => 
            u.GitHubLogin.Equals(gitHubLogin, StringComparison.OrdinalIgnoreCase));
        
        if (allowedUser == null)
        {
            return Task.FromResult(Array.Empty<string>());
        }

        return Task.FromResult(allowedUser.Roles);
    }

    public async Task<UserEntity?> GetUserAsync(string gitHubLogin)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.GitHubLogin == gitHubLogin);
    }

    public async Task<UserEntity?> GetOrCreateUserAsync(string gitHubLogin, string email)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.GitHubLogin == gitHubLogin);

        if (user == null)
        {
            // Check if user is allowed before creating
            if (!await IsUserAllowedAsync(gitHubLogin))
            {
                return null;
            }

            user = new UserEntity
            {
                GitHubLogin = gitHubLogin,
                Email = email,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };

            // Assign roles from configuration
            var roles = await GetUserRolesAsync(gitHubLogin);
            foreach (var role in roles)
            {
                user.Roles.Add(new UserRoleEntity
                {
                    Role = role,
                    AssignedAt = DateTime.UtcNow
                });
            }

            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
        else
        {
            // Update last login
            user.LastLoginAt = DateTime.UtcNow;
            user.Email = email;
            await context.SaveChangesAsync();
        }

        return user;
    }

    public async Task<IEnumerable<UserEntity>> GetAllUsersAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Users
            .Include(u => u.Roles)
            .ToListAsync();
    }
}
