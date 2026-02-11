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
    [DataTestMethod]
    [DataRow("response1.json", "out1.txt")]
    [DataRow("response2.json", "out2.txt")]
    [DataRow("response3.json", "out3.txt")]
    public async Task TestFileWriteResponse(string responseFile, string fileName)
    {
        if (File.Exists(fileName))
        {
            File.Delete(fileName);
        }

        var response = File.ReadAllText($"Responses/tools/file_write/{responseFile}");
        Assert.IsNotNull(response);

        var parameters = JObject.Parse(response);

        var loggerFactory = new LoggerFactory();
        var toolFactory = new ToolFactory(loggerFactory);
        var tool = new FileWrite(toolFactory);

        var result = await tool.Function(Session.New(), parameters);
        Assert.IsNotNull(result);
        Assert.IsTrue((result is JObject obj) && string.Equals(obj.Value<string>("result"), "success"));
        Assert.IsTrue(File.Exists(fileName));

        var contents = File.ReadAllText(fileName);
        Assert.IsFalse(contents.Contains(@"\"""), "File written contains '\\\"'");
        Assert.IsFalse(contents.Contains(@"\r\n"), "File written contains '\\r\\n'");
        Assert.IsFalse(contents.Contains(@"\n"), "File written contains '\\n'");
    }
}
