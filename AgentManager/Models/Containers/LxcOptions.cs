namespace AgentManager.Models.Containers;

public sealed class LxcOptions
{
    public const string SectionName = "Lxc";

    public required string ClientCertFilePath { get; set; }
    public required string ClientKeyFilePath { get; set; }
    public required string ServerCertFilePath { get; set; }
    public required string BaseAddress { get; set; }
}
