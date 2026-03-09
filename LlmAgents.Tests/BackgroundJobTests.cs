using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using LlmAgents.State;
using LlmAgents.Tools;
using LlmAgents.Tools.BackgroundJob;

namespace LlmAgents.Tests;

[TestClass]
public class BackgroundJobTests
{
    private static readonly ILoggerFactory LoggerFactory = new LoggerFactory();

    private static (ToolFactory factory, JobManager manager) CreateFactory(StateDatabase stateDatabase)
    {
        var factory = new ToolFactory(LoggerFactory);
        var jobStore = new BackgroundJobStore(LoggerFactory, stateDatabase);
        var jobManager = new JobManager(jobStore);
        factory.Register(jobStore);
        factory.Register(jobManager);
        return (factory, jobManager);
    }

    [TestMethod]
    public async Task StartAndMonitorJob()
    {
        using var stateDatabase = new StateDatabase(LoggerFactory, ":memory:");
        var (factory, manager) = CreateFactory(stateDatabase);
        using var _ = manager;
        var startTool = new StartJobTool(factory);
        var statusTool = new JobStatusTool(factory);
        var outputTool = new JobOutputTool(factory);

        // Use a fast command that prints something and exits.
        var parameters = new JObject
        {
            ["command"] = "dotnet",
            ["args"] = new JArray { "--info" }
        };

        var startResult = await startTool.Function(null!, parameters); // session not needed for this test
        var jobId = startResult["job_id"]?.ToString();
        Assert.IsFalse(string.IsNullOrEmpty(jobId), "job_id should be returned");

        // Wait a moment for the process to finish.
        Thread.Sleep(500);

        var statusParams = new JObject { ["job_id"] = jobId };
        var statusResult = await statusTool.Function(null!, statusParams);
        var status = statusResult["status"]?.ToString();
        Assert.IsTrue(status == "exited" || status == "running", "status should be exited or running");

        var outputParams = new JObject { ["job_id"] = jobId };
        var outputResult = await outputTool.Function(null!, outputParams);
        var output = outputResult["output"]?.ToString() ?? string.Empty;
        Assert.IsTrue(output.Contains("Version"), "output should contain the word 'Version'");
    }

    [TestMethod]
    public async Task StatusAndOutputCursor_AreDurable()
    {
        using var stateDatabase = new StateDatabase(LoggerFactory, ":memory:");
        var (factory, manager) = CreateFactory(stateDatabase);
        using var _ = manager;
        var startTool = new StartJobTool(factory);
        var outputTool = new JobOutputTool(factory);

        var startResult = await startTool.Function(null!, new JObject
        {
            ["command"] = "dotnet",
            ["args"] = new JArray { "--info" }
        });
        var jobId = startResult["job_id"]?.ToString();
        Assert.IsFalse(string.IsNullOrEmpty(jobId), "job_id should be returned");

        Thread.Sleep(500);

        var firstChunk = await outputTool.Function(null!, new JObject
        {
            ["job_id"] = jobId,
            ["max_bytes"] = 20
        });
        var firstNextCursor = firstChunk["next_cursor"]?.Value<int>() ?? 0;
        Assert.IsTrue(firstNextCursor > 0, "first read should advance cursor");

        var (newFactory, newManager) = CreateFactory(stateDatabase);
        using var __ = newManager;
        var statusTool = new JobStatusTool(newFactory);
        var newOutputTool = new JobOutputTool(newFactory);

        var statusResult = await statusTool.Function(null!, new JObject { ["job_id"] = jobId });
        Assert.IsTrue(statusResult["error"] == null, "status should be available from durable store");

        var secondChunk = await newOutputTool.Function(null!, new JObject
        {
            ["job_id"] = jobId,
            ["max_bytes"] = 20
        });
        var secondCursor = secondChunk["cursor"]?.Value<int>() ?? -1;
        Assert.AreEqual(firstNextCursor, secondCursor, "second read should continue from durable cursor");
    }
}
