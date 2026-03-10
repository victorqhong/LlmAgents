namespace AgentManager.Configuration;

public class ProvisioningOptions
{
    public required string UserConfigFile { get; set; }
    public required string ContainerImage { get; set; }
    public required string ApiConfigFile { get; set; }
    public required string XmppTargetJid { get; set; }
}
