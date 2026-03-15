namespace LlmAgents.Tools;

using System;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class NextcloudFileList : Nextcloud
{
    public NextcloudFileList(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "nextcloud_file_list",
            Description = "List the Nextcloud files and directories at the specified path",
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

        var xmlData = @"<?xml version=""1.0""?>
<d:propfind xmlns:d=""DAV:"" xmlns:s=""http://sabredav.org/ns"">
    <d:prop>
        <d:resourcetype/>
        <d:getcontenttype/>
        <d:displayname/>
    </d:prop>
</d:propfind>";

        try
        {
            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);

            request.Headers.Add("Depth", "1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{username}:{password}")));

            request.Content = new StringContent(xmlData, System.Text.Encoding.UTF8, "application/xml");

            var response = httpClient.Send(request);

            using var sr = new StreamReader(response.Content.ReadAsStream());

            var xml = sr.ReadToEnd();
            var xmlDoc = XDocument.Parse(xml);
            XNamespace d = "DAV:";

            var responses = xmlDoc.Descendants(d + "response")
                .Select(r => r.Descendants(d + "displayname")?.FirstOrDefault()?.Value);

            result.Add("statuscode", (int)response.StatusCode);
            result.Add("files", JsonSerializer.SerializeToNode(responses));
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}

