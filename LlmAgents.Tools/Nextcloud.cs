namespace LlmAgents.Tools;

using System;

public abstract class Nextcloud : Tool
{
    protected readonly string username;

    protected readonly string password;

    protected readonly string basePath;

    public Nextcloud(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = toolFactory.GetParameter("Nextcloud.basePath") ?? string.Empty;
        username = toolFactory.GetParameter("Nextcloud.username") ?? string.Empty;
        password = toolFactory.GetParameter("Nextcloud.password") ?? string.Empty;
    }

    protected bool ValidateParameters()
    {
        return !(string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(basePath));
    }
}

