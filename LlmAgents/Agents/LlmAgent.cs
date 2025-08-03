using LlmAgents.Communication;
using LlmAgents.LlmApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Agents
{
    public class LlmAgent
    {
        public readonly LlmApiOpenAi llmApi;

        public readonly IAgentCommunication agentCommunication;

        public bool Persistent { get; set; }

        public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

        public Action? PreWaitForContent { get; set; }

        public LlmAgent(LlmApiOpenAi llmApi, IAgentCommunication agentCommunication)
        {
            this.llmApi = llmApi;
            this.agentCommunication = agentCommunication;
        }

        public async Task Run(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PreWaitForContent?.Invoke();

                var messageContent = await agentCommunication.WaitForContent(cancellationToken);
                if (cancellationToken.IsCancellationRequested || messageContent == null)
                {
                    break;
                }

                var response = await llmApi.GenerateCompletion(messageContent, cancellationToken);
                if (!string.IsNullOrEmpty(response))
                {
                    if (Persistent)
                    {
                        SaveMessages();
                    }

                    await agentCommunication.SendMessage(response);
                }

                while ("tool_calls".Equals(llmApi.FinishReason))
                {
                    var toolCallResponse = await llmApi.ProcessToolCalls(cancellationToken);
                    if (Persistent)
                    {
                        SaveMessages();
                    }

                    if (!string.IsNullOrEmpty(toolCallResponse))
                    {
                        await agentCommunication.SendMessage(toolCallResponse);
                    }
                }
            }
        }

        public void LoadMessages()
        {
            var messagesFileName = GetMessagesFilename(llmApi.Id);
            var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

            if (!File.Exists(messagesFilePath))
            {
                return;
            }

            List<JObject>? messages = JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(messagesFilePath));
            if (messages == null)
            {
                return;
            }

            llmApi.Messages.Clear();
            llmApi.Messages.AddRange(messages);
        }

        public void SaveMessages()
        {
            var messagesFileName = GetMessagesFilename(llmApi.Id);
            var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

            File.WriteAllText(messagesFilePath, JsonConvert.SerializeObject(llmApi.Messages));
        }

        private static string GetMessagesFilename(string agentId)
        {
            return $"messages-{agentId}.json";
        }
    }
}
