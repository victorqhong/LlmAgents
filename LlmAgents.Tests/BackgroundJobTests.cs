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

    private ToolFactory CreateFactory()
    {
        var factory = new ToolFactory(LoggerFactory);
        var jobManager = new JobManager();
        factory.Register(jobManager);
        return factory;
    }

    [TestMethod]
    public async Task StartAndMonitorJob()
    {
        var factory = CreateFactory();
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
}

