using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace LlmAgents.Tests;

public class DummyTool : Tool
{
    public DummyTool(ToolFactory toolFactory) : base(toolFactory) { }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new() 
    {
        Type = "function",
        Function = new() 
        {
            Name = "dummy",
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters) => Task.FromResult<JsonNode>(new JsonObject());
}

[TestClass]
public class TestToolFactoryLoad
{
    private static readonly ILoggerFactory LoggerFactory = new LoggerFactory();

    private static ToolsConfig CreateToolDefinition(string typeName, string assemblyName, string assemblyPath)
    {
        return new ToolsConfig
        {
            Assemblies = new()
            {
                { assemblyName, assemblyPath }
            },
            Types = [$"{typeName}, {assemblyName}"]
        };
    }

    [TestMethod]
    public async Task Load_Successful()
    {
        // Arrange: definition pointing to DummyTool in the test assembly.
        var assembly = typeof(DummyTool).Assembly;
        var assemblyName = assembly.GetName().Name!;
        var typeName = typeof(DummyTool).FullName!;
        var definition = CreateToolDefinition(typeName, assemblyName, assembly.Location);
        var factory = new ToolFactory(LoggerFactory);

        // Act
        var tools = await factory.Load(definition);

        // Assert
        Assert.IsNotNull(tools);
        Assert.AreEqual(1, tools.Length);
        var actualType = tools[0].GetType();
        Console.WriteLine($"Loaded tool type: {actualType.FullName}, Assembly: {actualType.Assembly.FullName}");
        // Compare full name strings to avoid reflection context issues.
        Assert.AreEqual(typeof(DummyTool).FullName, actualType.FullName, "Loaded tool type name mismatch");
    }

    [TestMethod]
    public async Task Load_MissingAssemblyPath_ReturnsEmpty()
    {
        // Arrange: provide a non-existent path.
        var assembly = typeof(DummyTool).Assembly;
        var assemblyName = assembly.GetName().Name!;
        var typeName = typeof(DummyTool).FullName!;
        var definition = CreateToolDefinition(typeName, assemblyName, "nonexistent.dll");
        var factory = new ToolFactory(LoggerFactory);

        // Act
        var tools = await factory.Load(definition);

        // Assert: Load should not throw and return an empty array.
        Assert.IsNotNull(tools);
        Assert.AreEqual(0, tools.Length);
    }

    [TestMethod]
    public async Task Load_TypeNotFound_ReturnsEmpty()
    {
        // Arrange: correct assembly path but wrong type name.
        var assembly = typeof(DummyTool).Assembly;
        var assemblyName = assembly.GetName().Name!;
        var definition = CreateToolDefinition("NonExistent.Type", assemblyName, assembly.Location);
        var factory = new ToolFactory(LoggerFactory);

        // Act
        var tools = await factory.Load(definition);

        // Assert: Load should not throw and return an empty array.
        Assert.IsNotNull(tools);
        Assert.AreEqual(0, tools.Length);
    }
}
