namespace LlmAgents.Tools;

using LlmAgents.Tools.Shell;
using Microsoft.Extensions.Logging;

public abstract class ShellToolBase : Tool
{
    protected readonly ShellSessionManager manager;

    protected ShellToolBase(ToolFactory toolFactory) : base(toolFactory)
    {
        var logger = toolFactory.Resolve<ILoggerFactory>().CreateLogger(nameof(ShellSessionManager));
        manager = toolFactory.ResolveWithDefault<ShellSessionManager>() ?? CreateAndRegisterManager(toolFactory, logger);
    }

    private static ShellSessionManager CreateAndRegisterManager(ToolFactory toolFactory, ILogger logger)
    {
        var created = new ShellSessionManager(toolFactory, logger);
        toolFactory.Register(created);
        return created;
    }
}
