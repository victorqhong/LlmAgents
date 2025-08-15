namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;

public class WebSearch : Tool
{
    private readonly string? apiKey;

    private readonly string market;

    private readonly string safeSearch;

    private readonly string baseUrl;

    public WebSearch(ToolFactory toolFactory)
        : base(toolFactory)
    {
        apiKey = toolFactory.GetParameter("WebSearch.ApiKey");
        market = toolFactory.GetParameter("WebSearch.Market") ?? "en-us";
        safeSearch = toolFactory.GetParameter("WebSearch.SafeSearch") ?? "moderate";
        baseUrl = toolFactory.GetParameter("WebSearch.BaseUrl") ?? "https://api.bing.microsoft.com/v7.0/search";
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
                    },
                    offset = new
                    {
                        type = "number",
                        description = "The integer offset that specify where to start returning results (default 0)"
                    }
                },
                required = new[] { "query" }
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        if (string.IsNullOrEmpty(apiKey))
        {
            result.Add("error", "apiKey is null or empty");
            return Task.FromResult<JToken>(result);
        }

        var query = parameters["query"]?.ToString();
        if (string.IsNullOrEmpty(query))
        {
            result.Add("error", "query is null or empty");
            return Task.FromResult<JToken>(result);
        }

        var count = parameters["count"]?.Value<int>() ?? 5;
        var offset = parameters["offset"]?.Value<int>() ?? 0;

        try
        {
            JObject? response = null;
            using (var httpClient = new HttpClient())
            {
                var uri = $"{baseUrl}?mkt={market}&safeSearch={safeSearch}&count={count}&offset={offset}&q={Uri.EscapeDataString(query)}";
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);

                var httpResponse = httpClient.Send(httpRequest);
                httpResponse.EnsureSuccessStatusCode();

                var content = httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(content))
                {
                    result.Add("error", "response is null or empty");
                    return Task.FromResult<JToken>(result);
                }

                response = JObject.Parse(content);
            }

            var webPages = response?.Value<JObject>("webPages")?.Value<JArray>("value");
            if (webPages == null)
            {
                result.Add("error", "could not parse search results");
                return Task.FromResult<JToken>(result);
            }

            var searchResults = new JArray();
            foreach (var searchResult in webPages)
            {
                var name = searchResult.Value<string>("name");
                var url = searchResult.Value<string>("url");
                var snippet = searchResult.Value<string>("snippet");

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(snippet))
                {
                    continue;
                }

                searchResults.Add(JObject.FromObject(new { name, url, snippet }));
            }

            return Task.FromResult<JToken>(searchResults);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}
