using System.Reflection;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public static class ToolsConfigGenerator
{
    public static JObject? GenerateToolConfig(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(assemblyPath);
        }

        // Load the assembly
        var assembly = Assembly.LoadFrom(assemblyPath);
        var assemblyDisplayName = assembly.FullName;

        if (string.IsNullOrEmpty(assemblyDisplayName))
        {
            return null;
        }

        // Look for the Tool base type (in this assembly or referenced assemblies)
        var toolBaseType = FindToolBaseType(assembly);
        if (toolBaseType == null)
        {
            return null;
        }

        // Find all public, non-abstract classes inheriting from Tool
        var toolTypes = assembly.GetTypes()
            .Where(t => toolBaseType.IsAssignableFrom(t) && t.IsClass && !t.IsAbstract && t.IsPublic)
            .ToList();

        // Compose config object
        var config = new
        {
            assemblies = new Dictionary<string, string>
                {
                    { assemblyDisplayName, assemblyPath }
                },
            types = toolTypes.Select(t => t.AssemblyQualifiedName).ToArray(),
            parameters = new Dictionary<string, string>()
        };

        return JObject.FromObject(config);
    }

    private static Type? FindToolBaseType(Assembly assembly)
    {
        // Check in this assembly
        var type = assembly.GetTypes().FirstOrDefault(t => t.Name == "Tool" && t.IsAbstract);
        if (type != null)
        {
            return type;
        }

        // Check in referenced assemblies
        foreach (var refAsm in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = refAsm.GetTypes().FirstOrDefault(t => t.Name == "Tool" && t.IsAbstract);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}