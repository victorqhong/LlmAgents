using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using LlmAgents.Tools;
using LlmAgents.State;

namespace LlmAgents.Tests;

// Simple dummy tool for testing the ToolFactory Load method.
public class DummyTool : Tool
{
    private JObject _schema = new JObject();

    public DummyTool(ToolFactory toolFactory) : base(toolFactory) { }

    public override JObject Schema
    {
        get => _schema;
        protected set => _schema = value;
    }

    public override Task<JToken> Function(Session session, JObject parameters) => Task.FromResult<JToken>(new JValue("dummy"));
}

[TestClass]
public class TestToolFactoryLoad
{
    private static readonly ILoggerFactory LoggerFactory = new LoggerFactory();

    private static JObject CreateToolDefinition(string typeName, string assemblyName, string assemblyPath)
    {
        return new JObject
        {
            ["types"] = new JArray(typeName + ", " + assemblyName),
            ["assemblies"] = new JObject
            {
                [assemblyName] = assemblyPath
            }
        };
    }

    [TestMethod]
    public void Load_Successful()
    {
        // Arrange: definition pointing to DummyTool in the test assembly.
        var assembly = typeof(DummyTool).Assembly;
        var assemblyName = assembly.GetName().Name!;
        var typeName = typeof(DummyTool).FullName!;
        var definition = CreateToolDefinition(typeName, assemblyName, assembly.Location);
        var factory = new ToolFactory(LoggerFactory, definition);

        // Act
        var tools = factory.Load();

        // Assert
        Assert.IsNotNull(tools);
        Assert.AreEqual(1, tools.Length);
        var actualType = tools[0].GetType();
        Console.WriteLine($"Loaded tool type: {actualType.FullName}, Assembly: {actualType.Assembly.FullName}");
        // Compare full name strings to avoid reflection context issues.
        Assert.AreEqual(typeof(DummyTool).FullName, actualType.FullName, "Loaded tool type name mismatch");
    }

    [TestMethod]
    public void Load_MissingAssemblyPath_ReturnsEmpty()
    {
        // Arrange: provide a non-existent path.
        var assembly = typeof(DummyTool).Assembly;
        var assemblyName = assembly.GetName().Name!;
        var typeName = typeof(DummyTool).FullName!;
        var definition = CreateToolDefinition(typeName, assemblyName, "nonexistent.dll");
        var factory = new ToolFactory(LoggerFactory, definition);

        // Act
        var tools = factory.Load();

        // Assert: Load should not throw and return an empty array.
        Assert.IsNotNull(tools);
        Assert.AreEqual(0, tools.Length);
    }

    [TestMethod]
    public void Load_TypeNotFound_ReturnsEmpty()
    {
        // Arrange: correct assembly path but wrong type name.
        var assembly = typeof(DummyTool).Assembly;
        var assemblyName = assembly.GetName().Name!;
        var definition = CreateToolDefinition("NonExistent.Type", assemblyName, assembly.Location);
        var factory = new ToolFactory(LoggerFactory, definition);

        // Act
        var tools = factory.Load();

        // Assert: Load should not throw and return an empty array.
        Assert.IsNotNull(tools);
        Assert.AreEqual(0, tools.Length);
    }
}
