using LlmAgents.State;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace LlmAgents.Tools;

public class ToolFactory
{
    private readonly ILogger log;

    private readonly Dictionary<string, string> assemblyMap = new Dictionary<string, string>();

    private readonly Dictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>();

    private readonly Dictionary<Type, object> container = new Dictionary<Type, object>();

    private readonly Dictionary<string, string> parameters = new Dictionary<string, string>();

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

    public Tool[]? Load(JObject? toolDefinitions = null, Session? session = null, StateDatabase? stateDatabase = null)
    {
        if (toolDefinitions == null)
        {
            return null;
        }

        var toolParameters = toolDefinitions.Value<JObject>("parameters");
        if (toolParameters != null)
        {
            foreach (var property in toolParameters.Properties())
            {
                var value = property.Value.Value<string>();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                AddParameter(property.Name, value);
            }
        }

        var assemblies = toolDefinitions.Value<JObject>("assemblies");
        if (assemblies != null)
        {
            foreach (var assembly in assemblies.Properties())
            {
                var name = assembly.Name;
                var path = assembly.Value.Value<string>();

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                {
                    continue;
                }

                assemblyMap.Add(name, path);
            }
        }

        var types = toolDefinitions.Value<JArray>("types");
        if (types == null)
        {
            return null;
        }

        var tools = new List<Tool>();
        foreach (var type in types)
        {
            var typeName = type.Value<string>();
            if (string.IsNullOrEmpty(typeName))
            {
                continue;
            }

            var parts = typeName.Split(',', 2);

            typeName = parts[0].Trim();
            var assemblyName = parts[1].Trim();

            if (!this.assemblies.ContainsKey(assemblyName))
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

                    var toolAssemblyInitType = assembly.GetTypes().FirstOrDefault(t => t.GetCustomAttribute<ToolAssemblyInitAttribute>() != null && typeof(IToolAssemblyInitializer).IsAssignableFrom(t));
                    if (toolAssemblyInitType != null)
                    {
                        var toolAssemblyInit = Activator.CreateInstance(toolAssemblyInitType);
                        var methodInfo = typeof(IToolAssemblyInitializer).GetMethod(nameof(IToolAssemblyInitializer.Initialize));
                        methodInfo?.Invoke(toolAssemblyInit, [this]);
                    }

                    this.assemblies[assemblyName] = assembly;
                }
                catch (Exception e)
                {
                    log.LogError(e, "Exception while loading assembly: {assemblyPath}", assemblyPath);
                    continue;
                }
            }

            var toolType = this.assemblies[assemblyName].GetType(typeName);
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
