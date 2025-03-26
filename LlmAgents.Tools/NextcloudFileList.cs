namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.Net.Http.Headers;
using System.Xml.Linq;

public class NextcloudFileList : Nextcloud
{
    public NextcloudFileList(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "nextcloud_file_list",
            description = "List the Nextcloud files and directories at the specified path",
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

    public override JToken Function(JObject parameters)
    {
        var result = new JObject();

        var path = parameters["path"]?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return result;
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
            result.Add("files", new JArray(responses));
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}

