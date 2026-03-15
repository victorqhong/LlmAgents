namespace LlmAgents.Tools;

using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "web_search",
            Description = "Search the web using a query",
            Parameters = new()
            {
                Properties = new()
                {
                    { "query", new() { Type = "string", Description = "A query to search" } },
                    { "count", new() { Type = "number", Description = "The integer number of results to return (default 5)" } },
                },
                Required = ["query"]
            }
        }
    };

    public async override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (string.IsNullOrEmpty(apiKey))
        {
            result.Add("error", "apiKey is null or empty");
            return result;
        }

        if (!parameters.TryGetValueString("query", string.Empty, out var query) || string.IsNullOrEmpty(query))
        {
            result.Add("error", "query is null or empty");
            return result;
        }

        if (!parameters.TryGetValueInt("count", out var count))
        {
            count = 5;
        }

        try
        {
            JsonDocument? response = null;
            using (var httpClient = new HttpClient())
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl);
                httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                httpRequest.Content = JsonContent.Create(new { query, max_results = count });

                var httpResponse = httpClient.Send(httpRequest);
                httpResponse.EnsureSuccessStatusCode();

                var content = await httpResponse.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(content))
                {
                    result.Add("error", "response is null or empty");
                    return result;
                }

                response = JsonDocument.Parse(content);
            }

            if (response == null)
            {
                result.Add("error", "could not parse result as json");
                return result;
            }

            if (!response.RootElement.TryGetProperty("results", out var results))
            {
                result.Add("error", "could not parse results");
                return result;
            }

            var searchResults = new JsonArray();
            foreach (var searchResult in results.EnumerateArray())
            {
                var title = searchResult.GetProperty("title").GetString();
                var url = searchResult.GetProperty("url").GetString();
                var content = searchResult.GetProperty("content").GetString();

                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(content))
                {
                    continue;
                }

                searchResults.Add(JsonSerializer.SerializeToNode(new { title, url, content }));
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
