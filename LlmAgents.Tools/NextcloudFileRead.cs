namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.Net.Http.Headers;
using System.Xml.Linq;

public class NextcloudFileRead : Nextcloud
{
    public NextcloudFileRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "nextcloud_file_read",
            description = "Read the Nextcloud file",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path to list files"
                    }
                },
                required = new[] { "path" }
            }
        }
    });

    public override async Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var path = parameters["path"]?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return result;
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

        return result;
    }
}

