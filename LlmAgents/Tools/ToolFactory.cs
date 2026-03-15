using LlmAgents.Configuration;
using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace LlmAgents.Tools;

public class ToolFactory
{
    private readonly ILogger log;

    private readonly Dictionary<string, string> assemblyMap = [];

    private readonly Dictionary<string, Assembly> assemblies = [];

    private readonly Dictionary<Type, object> container = [];

    private readonly Dictionary<string, string> parameters = [];

    public ToolFactory(ILoggerFactory loggerFactory)
    {
        log = loggerFactory.CreateLogger(nameof(ToolFactory));
    }

    public void Register<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        container.Add(typeof(T), value);
    }

    public T Resolve<T>() where T : class
    {
        var key = typeof(T);
        if (container.TryGetValue(key, out var value))
        {
            return (T)value;
        }

        throw new KeyNotFoundException($"ToolFactory does not contain key of type: {key.Name}");
    }

    public T? ResolveWithDefault<T>(T? @default = null) where T : class
    {
        var key = typeof(T);
        if (container.TryGetValue(key, out var value))
        {
            return (T)value;
        }

        return @default;
    }

    public void AddParameter(string key, string value)
    {
        if (!parameters.TryAdd(key, value))
        {
            parameters[key] = value;
        }
    }

    public string? GetParameter(string key)
    {
        if (!parameters.TryGetValue(key, out string? value))
        {
            return null;
        }

        return value;
    }

    public Tool? Create(Type toolType)
    {
        ArgumentNullException.ThrowIfNull(toolType);

        if (!toolType.IsAssignableTo(typeof(Tool)))
        {
            return null;
        }

        try
        {
            var result = Activator.CreateInstance(toolType, this);
            if (result is not Tool tool)
            {
                return null;
            }

            return tool;
        }
        catch (Exception e)
        {
            log.LogError(e, "Exception while creating tool of type: {type}", toolType.FullName);
            return null;
        }
    }

    public Tool[] Load(ToolsConfig toolsConfig, Session? session = null, StateDatabase? stateDatabase = null)
    {
        if (toolsConfig.Parameters != null)
        {
            foreach (var kvp in toolsConfig.Parameters)
            {
                AddParameter(kvp.Key, kvp.Value);
            }
        }

        if (toolsConfig.Assemblies != null)
        {
            foreach (var assembly in toolsConfig.Assemblies)
            {
                var name = assembly.Key;
                var path = assembly.Value;

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                {
                    continue;
                }

                assemblyMap.Add(name, path);
            }
        }

        var tools = new List<Tool>();
        if (toolsConfig.Types == null)
        {
            return [];
        }

        foreach (var type in toolsConfig.Types)
        {
            var parts = type.Split(',', 2);

            var typeName = parts[0].Trim();
            var assemblyName = parts[1].Trim();

            if (!assemblies.ContainsKey(assemblyName))
            {
                if (!assemblyMap.ContainsKey(assemblyName))
                {
                    log.LogWarning("Assembly definition not found: {assemblyName}", assemblyName);
                    continue;
                }

                var assemblyPath = Path.GetFullPath(assemblyMap[assemblyName]);
                if (!File.Exists(assemblyPath))
                {
                    log.LogWarning("Could not load assembly: {assemblyPath}", assemblyPath);
                    continue;
                }

                try
                {
                    log.LogTrace("Loading assembly from path: {assemblyPath}", assemblyPath);
                    var assembly = Assembly.LoadFile(assemblyPath);
                    if (assembly == null)
                    {
                        log.LogError("Could not load assembly from file: {assemblyPath}", assemblyPath);
                        continue;
                    }

                    assemblies[assemblyName] = assembly;

                    var toolAssemblyInitTypes = assembly.GetTypes() 
                            .Where(t => t.GetCustomAttribute<ToolAssemblyInitAttribute>() != null && typeof(IToolAssemblyInitializer).IsAssignableFrom(t));
                    foreach (var toolAssemblyInitType in toolAssemblyInitTypes)
                    {
                        if (toolAssemblyInitType == null)
                        {
                            continue;
                        }

                        if (Activator.CreateInstance(toolAssemblyInitType) is not IToolAssemblyInitializer toolAssemblyInit)
                        {
                            continue;
                        }

                        toolAssemblyInit.Initialize(this);
                    }
                }
                catch (Exception e)
                {
                    log.LogError(e, "Exception while loading assembly: {assemblyPath}", assemblyPath);
                    continue;
                }
            }

            var toolType = assemblies[assemblyName].GetType(typeName);
            if (toolType == null)
            {
                log.LogError("Could not load type from assembly: {typeName}", typeName);
                continue;
            }

            var tool = Create(toolType);
            if (tool == null)
            {
                log.LogError("Could not create tool: {toolType}", toolType.FullName);
                continue;
            }

            tools.Add(tool);
        }

        if (session != null && stateDatabase != null)
        {
            foreach (var tool in tools)
            {
                tool.Load(session, stateDatabase);
                log.LogInformation("Loaded tool: {tool}", tool.GetType().Name);
            }
        }

        return tools.ToArray();
    }
}
