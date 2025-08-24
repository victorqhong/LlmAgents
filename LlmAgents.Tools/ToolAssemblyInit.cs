using LlmAgents.Tools.Todo;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Tools;

[ToolAssemblyInit]
public class ToolAssemblyInitializer : IToolAssemblyInitializer
{
    public void Initialize(ToolFactory toolFactory)
    {
        var loggerFactory = toolFactory.Resolve<ILoggerFactory>();
        var storageDirectory = toolFactory.GetParameter("storageDirectory");
        var todoDatabase = new TodoDatabase(loggerFactory, Path.Join(storageDirectory, "todo.db"));
        toolFactory.Register(todoDatabase);
    }
}
