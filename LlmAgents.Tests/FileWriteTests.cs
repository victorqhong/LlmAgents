using System.IO;
using System.Threading.Tasks;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Tests;

[TestClass]
public class FileWriteTests
{
    [TestMethod]
    public async Task TestFileWriteResponse()
    {
        Assert.IsFalse(File.Exists("JobStatusTool.cs"));

        var response = File.ReadAllText("Responses/tools/file_write/response.json");
        Assert.IsNotNull(response);

        var parameters = JObject.Parse(response);

        var loggerFactory = new LoggerFactory();
        var toolFactory = new ToolFactory(loggerFactory);
        var tool = new FileWrite(toolFactory);
        try
        {
            var result = await tool.Function(Session.New(), parameters);
            Assert.IsTrue(File.Exists("JobStatusTool.cs"));
            
            var contents = File.ReadAllText("JobStatusTool.cs");
            Assert.IsFalse(contents.Contains(@"\n"), "File written contains unreplaced newline character");
        }
        finally
        {
            if (File.Exists("JobStatusTool.cs"))
            {
                File.Delete("JobStatusTool.cs");
            }
        }

    }
}
