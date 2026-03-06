namespace AgentManager.Entities;

public class UserEntity
{
    public int Id { get; set; }
    public string GitHubLogin { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    
    public ICollection<UserRoleEntity> Roles { get; set; } = new List<UserRoleEntity>();
    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = new List<RefreshTokenEntity>();
}
