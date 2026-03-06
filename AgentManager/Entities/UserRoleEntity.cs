namespace AgentManager.Entities;

public class UserRoleEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    
    public UserEntity User { get; set; } = null!;
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string ReadOnly = "ReadOnly";
}
