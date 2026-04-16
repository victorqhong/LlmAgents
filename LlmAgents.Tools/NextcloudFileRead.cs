namespace LlmAgents.Tools;

using System;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class NextcloudFileRead : Nextcloud
{
    public NextcloudFileRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "nextcloud_file_read",
            Description = "Read the Nextcloud file",
            Parameters = new()
            {
                Properties = new()
                {
                    { "path", new() { Type = "string", Description = "The path to list files" } }
                },
                Required = ["path"]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!ValidateParameters())
        {
            result.Add("error", "Nextcloud username, password, or basePath not specified");
            return Task.FromResult<JsonNode>(result);
        }

        if (!parameters.TryGetValueString("path", string.Empty, out var path) || string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        var url = string.Format("{0}/{1}/{2}", basePath, username, path);

        try
        {
            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(new HttpMethod("GET"), url);

            request.Headers.Add("Depth", "1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{username}:{password}")));

            var response = httpClient.Send(request);

            using var sr = new StreamReader(response.Content.ReadAsStream());

            result.Add("statuscode", (int)response.StatusCode);
            result.Add("content", sr.ReadToEnd());
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}

