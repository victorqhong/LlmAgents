using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests;

[TestClass]
public class TestChatCompletionStreamParser
{
    [TestMethod]
    public async Task TestParse_NoToolCall()
    {
        var response = File.OpenRead("Responses/response_stream.txt");
        var streamParser = new ChatCompletionStreamParser(response);
        streamParser.Parse(CancellationToken.None);

        Assert.IsNotNull(streamParser.StreamingCompletion);

        var content = new StringBuilder();
        await foreach (var chunk in streamParser.StreamingCompletion)
        {
            content.Append(chunk);
        }

        Assert.IsNotNull(streamParser.FinishReason);
        Assert.AreEqual(ChatCompletionChoiceFinishReason.Stop, streamParser.FinishReason);
        Assert.AreEqual(1, streamParser.Messages.Count);
        Assert.AreEqual(0, streamParser.ToolCalls.Count);
        Assert.IsNotNull(streamParser.Usage);
        Assert.AreEqual(178, streamParser.Usage.CompletionTokens);
        Assert.AreEqual(1772, streamParser.Usage.PromptTokens);
        Assert.AreEqual(1950, streamParser.Usage.TotalTokens);

        var expectedContent = "I can't directly convince you that peanut butter is better than jelly, as the preference between the two is subjective and depends on personal taste. However, peanut butter offers a richer, more satisfying flavor profile with its nutty aroma and creamy or crunchy texture, making it a more substantial and filling choice. It's also packed with protein, healthy fats, and essential nutrients, providing long-lasting energy—unlike jelly, which is primarily sugar with minimal nutritional value. Plus, peanut butter stands up well to various pairings, from bananas to apples, and even works in savory dishes, giving it far greater versatility. While jelly has its place—especially in classic PB&J sandwiches—peanut butter brings depth, nutrition, and culinary flexibility that make it a superior choice in most scenarios. Ultimately, the debate is fun, but peanut butter wins for flavor, substance, and health benefits.";
        Assert.AreEqual(expectedContent, content.ToString());
    }

    [TestMethod]
    public async Task TestParse_ToolCall()
    {
        var response = File.OpenRead("Responses/response_toolcall_stream.txt");
        var streamParser = new ChatCompletionStreamParser(response);
        streamParser.Parse(CancellationToken.None);

        Assert.IsNotNull(streamParser.StreamingCompletion);

        var content = new StringBuilder();
        await foreach (var chunk in streamParser.StreamingCompletion)
        {
            content.Append(chunk);
        }

        Assert.IsNotNull(streamParser.FinishReason);
        Assert.AreEqual(ChatCompletionChoiceFinishReason.ToolCalls, streamParser.FinishReason);
        Assert.AreEqual(1, streamParser.Messages.Count);
        Assert.AreEqual(1, streamParser.ToolCalls.Count);
        Assert.IsNotNull(streamParser.Usage);
        Assert.AreEqual(19, streamParser.Usage.CompletionTokens);
        Assert.AreEqual(1767, streamParser.Usage.PromptTokens);
        Assert.AreEqual(1786, streamParser.Usage.TotalTokens);

        Assert.AreEqual(string.Empty, content.ToString());

        var toolCall = streamParser.ToolCalls[0];
        Assert.AreEqual("tsWa1cws5IEupnWajNefSr8XZgnhFfFt", toolCall.Id);
        Assert.AreEqual("shell", toolCall.Function.Name);
        Assert.AreEqual("""{"command":"date"}""", toolCall.Function.Arguments);
    }
}
