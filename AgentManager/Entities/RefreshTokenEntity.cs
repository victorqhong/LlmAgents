namespace AgentManager.Entities;

public class RefreshTokenEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public UserEntity User { get; set; }
}
