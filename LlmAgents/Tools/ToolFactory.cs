using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace LlmAgents.Tools;

public class ToolFactory
{
    private readonly ILogger log;

    private readonly JObject? toolDefinitions;

    private readonly Dictionary<string, string> assemblyMap = new Dictionary<string, string>();

    private readonly Dictionary<Type, object> container = new Dictionary<Type, object>();

    private readonly Dictionary<string, string> parameters = new Dictionary<string, string>();

    public ToolFactory(ILoggerFactory loggerFactory, JObject? toolDefinitions = null)
    {
        log = loggerFactory.CreateLogger(nameof(ToolFactory));

        this.toolDefinitions = toolDefinitions;

        if (this.toolDefinitions == null)
        {
            return;
        }

        var toolParameters = this.toolDefinitions.Value<JObject>("parameters");
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

        var assemblies = this.toolDefinitions.Value<JArray>("assemblies");
        if (assemblies != null)
        {
            foreach (var assembly in assemblies)
            {
                var assemblyObject = assembly.Value<JObject>();
                if (assemblyObject == null)
                {
                    continue;
                }

                var name = assemblyObject.Value<string>("name");
                var path = assemblyObject.Value<string>("path");

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                {
                    continue;
                }

                assemblyMap.Add(name, path);
            }
        }
    }

    public void Register<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        container.Add(typeof(T), value);
    }

    public T Resolve<T>()
    {
        return (T)container[typeof(T)];
    }

    public void AddParameter(string key, string value)
    {
        if (parameters.ContainsKey(key))
        {
            parameters[key] = value;
        }
        else
        {
            parameters.Add(key, value);
        }
    }

    public string? GetParameter(string key)
    {
        if (!parameters.ContainsKey(key))
        {
            return null;
        }

        return parameters[key];
    }

    public Tool? Create(Type toolType)
    {
        ArgumentNullException.ThrowIfNull(toolType);

        if (!toolType.IsAssignableTo(typeof(Tool)))
        {
            return null;
        }

        var result = Activator.CreateInstance(toolType, this);
        if (result is not Tool tool)
        {
            return null;
        }

        return tool;
    }

    public Tool[]? Load()
    {
        if (toolDefinitions == null)
        {
            return null;
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

            var toolType = Type.GetType(typeName);
            if (toolType == null)
            {
                var parts = typeName.Split(',', 2);

                var assemblyName = parts[1].Trim();
                if (!assemblyMap.ContainsKey(assemblyName))
                {
                    continue;
                }

                var assemblyPath = assemblyMap[assemblyName];
                if (!File.Exists(assemblyPath))
                {
                    continue;
                }

                try
                {
                    var assembly = Assembly.LoadFile(assemblyPath);
                    if (assembly == null)
                    {
                        continue;
                    }

                    toolType = assembly.GetType(typeName);
                    if (toolType == null)
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    log.LogError(e, "Could not load tool: {typeName}", typeName);
                    continue;
                }
            }

            var tool = Create(toolType);
            if (tool == null)
            {
                continue;
            }

            tools.Add(tool);
        }

        return tools.ToArray();
    }
}