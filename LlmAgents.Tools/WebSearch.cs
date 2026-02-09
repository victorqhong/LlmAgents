namespace LlmAgents.Tools;

using LlmAgents.State;
using Newtonsoft.Json.Linq;
using System;

public class WebSearch : Tool
{
    private readonly string? apiKey;

    private readonly string baseUrl;

    public WebSearch(ToolFactory toolFactory)
        : base(toolFactory)
    {
        apiKey = toolFactory.GetParameter("WebSearch.ApiKey");
        baseUrl = toolFactory.GetParameter("WebSearch.BaseUrl") ?? "https://api.tavily.com/search";
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "web_search",
            description = "Search the web using a query",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new
                    {
                        type = "string",
                        description = "A query to search"
                    },
                    count = new
                    {
                        type = "number",
                        description = "The integer number of results to return (default 5)"
                    }
                },
                required = new[] { "query" }
            }
        }
    });

    public async override Task<JToken> Function(Session session, JObject parameters)
    {
        var result = new JObject();

        if (string.IsNullOrEmpty(apiKey))
        {
            result.Add("error", "apiKey is null or empty");
            return result;
        }

        var query = parameters["query"]?.ToString();
        if (string.IsNullOrEmpty(query))
        {
            result.Add("error", "query is null or empty");
            return result;
        }

        var count = parameters["count"]?.Value<int>() ?? 5;

        try
        {
            JObject? response = null;
            using (var httpClient = new HttpClient())
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl);
                httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                httpRequest.Content = new StringContent(JObject.FromObject(new { query, max_results = count }).ToString(), System.Text.Encoding.UTF8, "application/json");

                var httpResponse = httpClient.Send(httpRequest);
                httpResponse.EnsureSuccessStatusCode();

                var content = await httpResponse.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(content))
                {
                    result.Add("error", "response is null or empty");
                    return result;
                }

                response = JObject.Parse(content);
            }

            if (response == null)
            {
                result.Add("error", "could not parse result as json");
                return result;
            }

            if (!response.ContainsKey("results") || response.Value<JArray>("results") is not JArray results)
            {
                result.Add("error", "could not parse results");
                return result;
            }

            var searchResults = new JArray();
            foreach (var searchResult in results)
            {
                var title = searchResult.Value<string>("title");
                var url = searchResult.Value<string>("url");
                var content = searchResult.Value<string>("content");

                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(content))
                {
                    continue;
                }

                searchResults.Add(JObject.FromObject(new { title, url, content }));
            }

            return searchResults;
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
