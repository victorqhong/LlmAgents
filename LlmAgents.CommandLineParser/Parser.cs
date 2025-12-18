using LlmAgents.Agents;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
using System.CommandLine.Invocation;

namespace LlmAgents.CommandLineParser;

public static class Parser
{
    public static LlmApiOpenAiParameters? ParseApiParameters(InvocationContext context)
    {
        string? apiEndpoint;
        string? apiKey;
        int contextSize;
        int maxCompletionTokens;
        string? apiModel;

        var apiConfigValue = context.ParseResult.GetValueForOption(Options.ApiConfig);
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
            apiEndpoint = context.ParseResult.GetValueForOption(Options.ApiEndpoint);
            apiKey = context.ParseResult.GetValueForOption(Options.ApiKey);
            apiModel = context.ParseResult.GetValueForOption(Options.ApiModel);
            contextSize = context.ParseResult.GetValueForOption(Options.ContextSize);
            maxCompletionTokens = context.ParseResult.GetValueForOption(Options.MaxCompletionTokens);
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

    public static LlmAgentParameters? ParseAgentParameters(InvocationContext invocationContext)
    {
        string? agentId = invocationContext.ParseResult.GetValueForOption(Options.AgentId);
        bool persistent = invocationContext.ParseResult.GetValueForOption(Options.Persistent);
        string? storageDirectory = invocationContext.ParseResult.GetValueForOption(Options.StorageDirectory);
        bool streamOutput = invocationContext.ParseResult.GetValueForOption(Options.StreamOutput);

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

    public static ToolParameters ParseToolParameters(InvocationContext invocationContext)
    {
        var toolsConfigValue = invocationContext.ParseResult.GetValueForOption(Options.ToolsConfig);
        var toolServerAddressValue = invocationContext.ParseResult.GetValueForOption(Options.ToolServerAddress);
        var toolServerPortValue = invocationContext.ParseResult.GetValueForOption(Options.ToolServerPort);

        return new ToolParameters
        {
            ToolsConfig = toolsConfigValue,
            ToolServerAddress = toolServerAddressValue,
            ToolServerPort = toolServerPortValue
        };
    }

    public static SessionParameters ParseSessionParameters(InvocationContext invocationContext)
    {
        string? sessionId = invocationContext.ParseResult.GetValueForOption(Options.SessionId);
        string? workingDirectoryValue = invocationContext.ParseResult.GetValueForOption(Options.WorkingDirectory);
        string? systemPromptFileValue = invocationContext.ParseResult.GetValueForOption(Options.SystemPromptFile);

        return new SessionParameters
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectoryValue,
            SystemPromptFile = systemPromptFileValue,
        };
    }
}