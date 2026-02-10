using LlmAgents.State;
using LlmAgents.Tools.BackgroundJob;
using LlmAgents.Tools.Todo;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Tools;

[ToolAssemblyInit]
public class ToolAssemblyInitializer : IToolAssemblyInitializer
{
    public void Initialize(ToolFactory toolFactory)
    {
        var loggerFactory = toolFactory.Resolve<ILoggerFactory>();
        var stateDatabase = toolFactory.Resolve<StateDatabase>();
        var todoDatabase = new TodoDatabase(loggerFactory, stateDatabase);
        toolFactory.Register(todoDatabase);

        toolFactory.Register(new JobManager());
    }
}
