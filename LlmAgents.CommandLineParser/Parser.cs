using LlmAgents.Agents;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
using System.CommandLine;

namespace LlmAgents.CommandLineParser;

public static class Parser
{
    public static LlmApiOpenAiParameters? ParseApiParameters(ParseResult parseResult)
    {
        string? apiEndpoint;
        string? apiKey;
        int contextSize;
        int maxCompletionTokens;
        string? apiModel;

        var apiConfigValue = parseResult.GetValue(Options.ApiConfig);
        if (!string.IsNullOrEmpty(apiConfigValue) && File.Exists(apiConfigValue))
        {
            var apiConfig = JObject.Parse(File.ReadAllText(apiConfigValue));

            apiEndpoint = apiConfig.Value<string>("apiEndpoint");
            apiKey = apiConfig.Value<string>("apiKey");
            apiModel = apiConfig.Value<string>("apiModel");
            contextSize = apiConfig.Value<int>("contextSize");
            maxCompletionTokens = apiConfig.Value<int>("maxCompletionTokens");
        }
        else
        {
            apiEndpoint = parseResult.GetValue(Options.ApiEndpoint);
            apiKey = parseResult.GetValue(Options.ApiKey);
            apiModel = parseResult.GetValue(Options.ApiModel);
            contextSize = parseResult.GetValue(Options.ContextSize);
            maxCompletionTokens = parseResult.GetValue(Options.MaxCompletionTokens);
        }

        if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
        {
            return null;
        }

        return new LlmApiOpenAiParameters
        {
            ApiEndpoint = apiEndpoint,
            ApiKey = apiKey,
            ApiModel = apiModel,
            ContextSize = contextSize,
            MaxCompletionTokens = maxCompletionTokens,
        };
    }

    public static LlmAgentParameters? ParseAgentParameters(ParseResult parseResult)
    {
        string? agentId = parseResult.GetValue(Options.AgentId) ?? Environment.MachineName;
        bool persistent = parseResult.GetValue(Options.Persistent);
        string? storageDirectory = parseResult.GetValue(Options.StorageDirectory);
        bool streamOutput = parseResult.GetValue(Options.StreamOutput);

        if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(storageDirectory))
        {
            return null;
        }

        return new LlmAgentParameters
        {
            AgentId = agentId,
            Persistent = persistent,
            StorageDirectory = storageDirectory,
            StreamOutput = streamOutput
        };
    }

    public static ToolParameters ParseToolParameters(ParseResult parseResult)
    {
        var toolsConfigValue = parseResult.GetValue(Options.ToolsConfig);
        var mcpConfigPathValue = parseResult.GetValue(Options.McpConfigPath);

        return new ToolParameters
        {
            ToolsConfig = toolsConfigValue,
            McpConfigPath = mcpConfigPathValue
        };
    }

    public static SessionParameters ParseSessionParameters(ParseResult parseResult)
    {
        string? sessionId = parseResult.GetValue(Options.SessionId);
        string? workingDirectoryValue = parseResult.GetValue(Options.WorkingDirectory);
        string? systemPromptFileValue = parseResult.GetValue(Options.SystemPromptFile);

        return new SessionParameters
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectoryValue,
            SystemPromptFile = systemPromptFileValue,
        };
    }
}
