using System.Text.Json.Serialization;

namespace AgentManager.Models.Gogs;

public class CreateUserResponse
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }

    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("login")]
    public required string Login { get; set; }

    [JsonPropertyName("full_name")]
    public required string FullName { get; set; }

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [JsonPropertyName("avatar_url")]
    public required string AvatarUrl { get; set; }
}
