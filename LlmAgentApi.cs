
using Newtonsoft.Json.Linq;

public class LlmAgentApi
{
    private List<Tuple<string, string>> messages = new List<Tuple<string, string>>();

    public LlmAgentApi(string apiEndpoint, string apiKey, string model, string? systemPrompt = null)
    {
        ApiEndpoint = apiEndpoint;
        ApiKey = apiKey;
        Model = model;

        if (systemPrompt != null)
        {
            messages.Add(new Tuple<string, string>("system", systemPrompt));
        }
    }

    public string ApiEndpoint { get; private set; }

    public string ApiKey { get; private set; }

    public string Model { get; set; }

    public int MaxTokens { get; set; } = 100;

    public double Temperature { get; set; } = 0.7;

    public string GenerateCompletion(string userMessage)
    {
        messages.Add(new Tuple<string, string>("user", userMessage));
        var payload = GetPayload(Model, messages, MaxTokens, Temperature);
        var completion = Post(ApiEndpoint, ApiKey, payload).ConfigureAwait(false).GetAwaiter().GetResult();
        messages.Add(new Tuple<string, string>("assistant", completion));
        return completion ?? string.Empty;
    }

    private static async Task<string?> Post(string apiEndpoint, string apiKey, string content)
    {
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            var body = new StringContent(content, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var response = await client.PostAsync(apiEndpoint, body);
                var responseContent = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    var json = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(responseContent);
                    return json["choices"][0]["message"]["content"].ToString();
                }
                else
                {
                    Console.WriteLine($"Error: {responseContent}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: {e}");
            }
        }

        return null;
    }

    private static string GetPayload(string model, List<Tuple<string, string>> messages, int maxTokens, double temperature)
    {
        var payload = new
        {
            model,
            messages = Enumerable.Select(messages, element =>
            {
                return new { role = element.Item1, content = element.Item2 };
            }),
            /*max_tokens = maxTokens,*/
            /*temperature*/
        };

        return Newtonsoft.Json.JsonConvert.SerializeObject(payload);
    }
}
