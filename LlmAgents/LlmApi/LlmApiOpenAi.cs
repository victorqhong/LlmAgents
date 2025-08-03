using LlmAgents.LlmApi.Content;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace LlmAgents.LlmApi
{
    public class LlmApiOpenAi : ILlmApiMessageProvider
    {
        private readonly ILogger Log;

        private readonly List<Tool> Tools = [];
        private readonly List<JObject> ToolDefinitions = [];
        private readonly Dictionary<string, Tool> ToolMap = [];

        public LlmApiOpenAi(ILoggerFactory loggerFactory, string id, string apiEndpoint, string apiKey, string model, List<JObject>? messages = null, Tool[]? tools = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(id);
            ArgumentException.ThrowIfNullOrEmpty(apiEndpoint);
            ArgumentException.ThrowIfNullOrEmpty(apiKey);
            ArgumentException.ThrowIfNullOrEmpty(model);

            Log = loggerFactory.CreateLogger(nameof(LlmApiOpenAi));

            Id = id;
            ApiEndpoint = apiEndpoint;
            ApiKey = apiKey;
            Model = model;

            if (messages != null)
            {
                Messages.AddRange(messages);
            }

            if (tools != null)
            {
                AddTool(tools);
            }
        }

        public readonly string Id;

        public List<JObject> Messages { get; private set; } = [];

        public string ApiEndpoint { get; private set; }

        public string ApiKey { get; private set; }

        public string Model { get; set; }

        public int? MaxCompletionTokens { get; set; } = 8192;

        public double Temperature { get; set; } = 1.0;

        public string? FinishReason { get; private set; }

        public void AddTool(params Tool[] tools)
        {
            foreach (var tool in tools)
            {
                AddTool(tool);
            }
        }

        public void AddTool(Tool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);

            Tools.Add(tool);
            ToolDefinitions.Add(tool.Schema);
            ToolMap.Add(tool.Name, tool);
        }

        public async Task<string?> GenerateCompletion(IEnumerable<IMessageContent> messageContents, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messageContents);

            var content = new JArray();

            foreach (var messageContent in messageContents)
            {
                if (messageContent is MessageContentText userMessage)
                {
                    var textContent = new JObject();
                    textContent.Add("type", "text");
                    textContent.Add("text", userMessage.Text);
                    content.Add(textContent);

                }
                else if (messageContent is MessageContentImageUrl imageUrl)
                {
                    var url = string.Format("data:{0};base64,{1}", imageUrl.MimeType, imageUrl.DataBase64);

                    var imageContent = new JObject();
                    imageContent.Add("type", "image_url");
                    imageContent.Add("image_url", JObject.FromObject(new { url = url }));
                    content.Add(imageContent);
                }
            }

            var message = new JObject();
            message.Add("role", "user");
            message.Add("content", content);

            Messages.Add(message);

            return await GenerateCompletion(Messages, cancellationToken);
        }

        public async Task<string?> GenerateCompletion(List<JObject> messages, CancellationToken cancellationToken = default)
        {
            if (messages == null || messages.Count < 1)
            {
                throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var payload = GetPayload(Model, messages, MaxCompletionTokens, Temperature, ToolDefinitions, "auto");

            var completion = await GetCompletion(ApiEndpoint, ApiKey, payload, retryAttempt: 0, cancellationToken);
            if (completion == null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                throw new ApplicationException("Could not retrieve completion");
            }

            return await ProcessCompletion(completion, cancellationToken);
        }

        public async Task<string?> ProcessCompletion(JObject completion, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Log.LogInformation("Cancellation requested");
                return null;
            }

            var choice = completion["choices"]?[0];
            if (choice == null)
            {
                Log.LogError("Could not get choice[0]");
                return null;
            }

            var message = choice["message"];
            if (message == null)
            {
                Log.LogError("Could not get choice[0].message");
                return null;
            }

            FinishReason = choice["finish_reason"]?.ToString();
            if (FinishReason == null)
            {
                Log.LogError("Could not get choice[0].finish_reason");
                return null;
            }

            var content = message["content"]?.ToString();

            if (string.Equals(FinishReason, "stop"))
            {
                if (string.IsNullOrEmpty(content))
                {
                    Log.LogWarning($"Content is null or empty");
                }

                Messages.Add(JObject.FromObject(new { role = "assistant", content }));

                return content;
            }
            else if (string.Equals(FinishReason, "tool_calls"))
            {
                var toolCalls = message["tool_calls"];
                if (toolCalls == null)
                {
                    Log.LogError("Could not get choice[0].message.tool_calls");
                    return null;
                }

                Messages.Add(JObject.FromObject(new { role = "assistant", content, tool_calls = toolCalls }));

                return content;
            }
            else if (string.Equals(FinishReason, "length"))
            {
                throw new ApplicationException(FinishReason);
            }
            else
            {
                throw new NotImplementedException(FinishReason);
            }
        }

        public async Task<string?> ProcessToolCalls(CancellationToken cancellationToken = default)
        {
            string? completion = null;
            while ("tool_calls".Equals(FinishReason))
            {
                var toolCalls = Messages[Messages.Count - 1].Value<JToken>("tool_calls");
                if (toolCalls == null)
                {
                    return null;
                }

                foreach (var toolCall in toolCalls)
                {
                    var id = toolCall["id"]?.Value<string>();

                    var function = toolCall["function"];
                    if (function == null)
                    {
                        Log.LogError("Could not get choice[0].message.tool_calls.function");
                        return null;
                    }

                    var name = function["name"]?.Value<string>();
                    if (string.IsNullOrEmpty(name))
                    {
                        Log.LogError("Could not get choice[0].message.tool_calls.name");
                        return null;
                    }

                    if (!ToolMap.TryGetValue(name, out Tool? value))
                    {
                        Log.LogError("Could not get tool");
                        return null;
                    }

                    var tool = value.Function;

                    var arguments = function["arguments"]?.Value<string>();
                    if (arguments == null)
                    {
                        Log.LogError("Could not get choice[0].message.tool_calls.arguments");
                        return null;
                    }

                    Log.LogInformation("Calling tool '{name}' with arguments '{arguments}'", name, arguments);

                    var toolResult = await tool(JObject.Parse(arguments));
                    Messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        name,
                        content = Newtonsoft.Json.JsonConvert.SerializeObject(toolResult)
                    }));
                }

                completion = await GenerateCompletion(Messages, cancellationToken);
            }

            return completion;
        }

        private async Task<JObject?> GetCompletion(string apiEndpoint, string apiKey, string content, int retryAttempt = 0, CancellationToken cancellationToken = default)
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("api-key", apiKey);
            client.Timeout = Timeout.InfiniteTimeSpan;

            var body = new StringContent(content, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var response = await client.PostAsync(apiEndpoint, body, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseMessage = JObject.Parse(responseContent);

                if (response.IsSuccessStatusCode)
                {
                    return responseMessage;
                }
                else
                {
                    var error = responseMessage.Value<JObject>("error");
                    if (error != null)
                    {
                        var message = error.Value<string>("message");
                        var code = error.Value<string>("code");
                        if (string.Equals("429", code) && retryAttempt < 3)
                        {
                            var seconds = 30 * (retryAttempt + 1);

                            string pattern = @"retry\s+after\s+(\d+)\s+seconds";
                            Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);
                            Match match = regex.Match(message ?? string.Empty);
                            if (match.Success)
                            {
                                seconds = int.Parse(match.Groups[1].Value) + 5;
                            }

                            Log.LogInformation("Request throttled... waiting {seconds} seconds and retrying.", seconds);
                            System.Threading.Thread.Sleep(seconds * 1000);
                            return await GetCompletion(apiEndpoint, apiKey, content, retryAttempt + 1, cancellationToken);
                        }
                        else
                        {
                            Log.LogError("Error: {message}", message);
                        }
                    }
                    else
                    {
                        Log.LogError("Error: {responseContent}", responseContent);
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError(e, "Got exception");
            }

            return null;
        }

        public static string GetPayload(string model, List<JObject> messages, int? maxCompletionTokens, double temperature, List<JObject>? tools = null, string toolChoice = "auto")
        {
            var payload = new JObject();
            payload.Add("model", model);
            payload.Add("messages", JArray.FromObject(messages));
            if (maxCompletionTokens > 0)
            {
                payload.Add("max_completion_tokens", maxCompletionTokens);
            }
            payload.Add("temperature", temperature);
            if (tools != null && tools.Count > 0)
            {
                payload.Add("tools", JArray.FromObject(tools));
                payload.Add("tool_choice", toolChoice);
            }

            return payload.ToString(Newtonsoft.Json.Formatting.None);
        }

        public async Task<int> CountMessages()
        {
            return Messages.Count;
        }

        public async Task PruneContext(int numMessagesToKeep)
        {
            if (Messages.Count <= numMessagesToKeep)
            {
                return;
            }

            // keep the first message (system prompt) and last message (a tool call is needed before a tool result)
            var pruneCount = Math.Max(Messages.Count - numMessagesToKeep, 0) - 1;
            Messages.RemoveRange(1, pruneCount - 1);

            // remove messages from the beginning to maintain a well-formatted message list
            while (Messages.Count > 1 && (!string.Equals(Messages[0].Value<string>("role"), "user") && !string.Equals(Messages[0].Value<string>("role"), "system")))
            {
                Messages.RemoveAt(1);
            }
        }
    }
}
