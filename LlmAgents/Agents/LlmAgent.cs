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

        public readonly string Id;

        public bool Persistent { get; set; }

        public bool StreamOutput { get; set; }

        public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

        public Action? PreWaitForContent { get; set; }

        public LlmAgent(string id, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication)
        {
            Id = id;
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

                if (StreamOutput)
                {
                    var response = await llmApi.GenerateStreamingCompletion(messageContent, cancellationToken);
                    if (response == null)
                    {
                        continue;
                    }

                    await foreach (var chunk in response)
                    {
                        await agentCommunication.SendMessage(chunk, false);
                    }

                    await agentCommunication.SendMessage(string.Empty, true);

                }
                else
                {
                    var response = await llmApi.GenerateCompletion(messageContent, cancellationToken);
                    if (response == null)
                    {
                        continue;
                    }

                    await agentCommunication.SendMessage(response, true);
                }

                if (Persistent)
                {
                    SaveMessages();
                }
            }
        }

        public void LoadMessages()
        {
            var messagesFileName = GetMessagesFilename(Id);
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
            var messagesFileName = GetMessagesFilename(Id);
            var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

            File.WriteAllText(messagesFilePath, JsonConvert.SerializeObject(llmApi.Messages));
        }

        private static string GetMessagesFilename(string agentId)
        {
            return $"messages-{agentId}.json";
        }
    }
}
