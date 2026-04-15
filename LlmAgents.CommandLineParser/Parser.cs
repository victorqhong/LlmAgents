using System.CommandLine;
using System.Text.Json;
using LlmAgents.Agents;
using LlmAgents.Configuration;
using LlmAgents.State;
using LlmAgents.Tools;

namespace LlmAgents.CommandLineParser;

public static class Parser
{
    public static LlmApiConfig? ParseApiParameters(ParseResult parseResult)
    {
        string? apiEndpoint = null;
        string? apiKey = null;
        int contextSize = 0;
        string? apiModel = null;

        var apiConfigValue = parseResult.GetValue(Options.ApiConfig);
        if (!string.IsNullOrEmpty(apiConfigValue) && File.Exists(apiConfigValue))
        {
            var apiConfig = JsonSerializer.Deserialize<LlmApiConfig>(File.ReadAllText(apiConfigValue));
            if (apiConfig != null)
            {
                return apiConfig;
            }
        }
        else
        {
            apiEndpoint = parseResult.GetValue(Options.ApiEndpoint);
            apiKey = parseResult.GetValue(Options.ApiKey);
            apiModel = parseResult.GetValue(Options.ApiModel);
            contextSize = parseResult.GetValue(Options.ContextSize);
        }

        if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
        {
            return null;
        }

        return new LlmApiConfig
        {
            ApiEndpoint = apiEndpoint,
            ApiKey = apiKey,
            ApiModel = apiModel,
            ContextSize = contextSize,
        };
    }

    public static LlmAgentParameters? ParseAgentParameters(ParseResult parseResult)
    {
        string? agentId = parseResult.GetValue(Options.AgentId);
        bool persistent = parseResult.GetValue(Options.Persistent);
        string? storageDirectory = parseResult.GetValue(Options.StorageDirectory);
        bool streamOutput = parseResult.GetValue(Options.StreamOutput);
        string? managerUrl = parseResult.GetValue(Options.AgentManagerUrl);

        if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(storageDirectory))
        {
            return null;
        }

        Uri.TryCreate(managerUrl, UriKind.Absolute, out var uri);

        return new LlmAgentParameters
        {
            AgentId = agentId,
            Persistent = persistent,
            StorageDirectory = storageDirectory,
            StreamOutput = streamOutput,
            AgentManagerUrl = uri
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
        string? session = parseResult.GetValue(Options.Session);
        string? workingDirectoryValue = parseResult.GetValue(Options.WorkingDirectory);
        string? systemPromptFileValue = parseResult.GetValue(Options.SystemPromptFile);

        return new SessionParameters
        {
            Session = session,
            WorkingDirectory = workingDirectoryValue,
            SystemPromptFile = systemPromptFileValue,
        };
    }
}
