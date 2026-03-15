using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text.Json;
using AgentManager.Configuration;
using AgentManager.Models.Containers;
using AgentManager.Services.Containers;
using LlmAgents.LlmApi.OpenAi;

namespace AgentManager.Services;

public class ContainerService
{
    private readonly ProvisioningOptions provisioningOptions;
    private readonly LxdApi api;

    private readonly LlmApiOpenAiParameters llmApiParameters;
    private readonly Dictionary<string, string> xmppUsers = [];
    private readonly ConcurrentBag<string> allocatedUsers = [];
    private readonly ConcurrentBag<string> availableUsers = [];

    private readonly TaskCompletionSource initialized = new();

    public Action? OnContainerUpdated;

    public ContainerService(LxcHttpClient httpClient, WebsocketManager websocketManager, ProvisioningOptions provisioningOptions)
    {
        this.provisioningOptions = provisioningOptions;

        api = new LxdApi(httpClient, websocketManager);

        if (!File.Exists(provisioningOptions.ApiConfigFile))
        {
            throw new FileNotFoundException();
        }

        llmApiParameters = JsonSerializer.Deserialize<LlmApiOpenAiParameters>(File.ReadAllText(provisioningOptions.ApiConfigFile)) ?? throw new SerializationException();

        if (!File.Exists(provisioningOptions.UserConfigFile))
        {
            throw new FileNotFoundException();
        }

        var userConfig = JsonSerializer.Deserialize<List<XmppUser>>(File.ReadAllText(provisioningOptions.UserConfigFile)) ?? throw new SerializationException();
        foreach (var user in userConfig)
        {
            xmppUsers.Add(user.Jid, user.Password);
        }

        Task.Run(async () =>
        {
            var response = await api.GetInstances();
            foreach (var instance in response.Instances)
            {
                var matchingUser = userConfig.Find(user =>
                {
                    var parts = user.Jid.Split('@');
                    var username = parts[0];

                    return string.Equals(username, instance);
                });

                if (matchingUser != null)
                {
                    allocatedUsers.Add(instance);
                }
            }

            foreach (var user in userConfig)
            {
                var parts = user.Jid.Split('@');
                var username = parts[0];

                if (!allocatedUsers.Contains(username))
                {
                    availableUsers.Add(username);
                }
            }

            initialized.SetResult();
        });
    }

    public IReadOnlyList<string> GetAllocatedAgents()
    {
        return allocatedUsers.ToList();
    }

    public IReadOnlyList<string> GetAvailableAgents()
    {
        return availableUsers.ToList();
    }

    public async Task AllocateXmppAgent()
    {
        await initialized.Task;

        if (!availableUsers.TryTake(out var instanceName))
        {
            throw new Exception("No available instances to be allocated");
        }

        await AllocateXmppAgent(instanceName);
    }

    public async Task AllocateXmppAgent(string instanceName)
    {
        await initialized.Task;

        allocatedUsers.Add(instanceName);

        var newUsers = availableUsers.Except([instanceName]).ToList();
        availableUsers.Clear();
        foreach (var user in newUsers)
        {
            availableUsers.Add(user);
        }

        OnContainerUpdated?.Invoke();

        var key = xmppUsers.Keys.First(key => key.StartsWith(instanceName));
        var parts = key.Split('@');
        var username = parts[0];
        var domain = parts[1];

        await api.CreateInstance(new InstancesPost
        {
            Name = instanceName,
            Type = "container",
            Start = true,
            Source = new InstanceSource
            {
                Type = "image",
                Protocol = "simplestreams",
                Server = "https://images.lxd.canonical.com",
                Alias = provisioningOptions.ContainerImage
            }
        });

        await api.StartInstance(instanceName);
        await api.CreateFile(instanceName, "/opt/install-xmppagent.sh", File.OpenRead("wwwroot/install-xmppagent.sh"));

        var xmppJson = JsonSerializer.Serialize(new { xmppDomain = domain, xmppUsername = username, xmppPassword = xmppUsers[key], xmppTargetJid = provisioningOptions.XmppTargetJid, xmppTrustHost = true });
        var xmppConfig = new MemoryStream();
        var xmppWriter = new StreamWriter(xmppConfig);
        xmppWriter.Write(xmppJson);
        xmppWriter.Flush();
        xmppConfig.Position = 0;
        await api.CreateFile(instanceName, "/opt/xmpp.json", xmppConfig);
        xmppWriter.Close();

        var apiJson = JsonSerializer.Serialize(new
        {
            apiEndpoint = llmApiParameters.ApiEndpoint,
            apiKey = llmApiParameters.ApiKey,
            apiModel = llmApiParameters.ApiModel,
            contextSize = llmApiParameters.ContextSize,
            maxCompletionTokens = llmApiParameters.MaxCompletionTokens
        });
        var apiConfig = new MemoryStream();
        var apiWriter = new StreamWriter(apiConfig);
        apiWriter.Write(apiJson);
        apiWriter.Flush();
        apiConfig.Position = 0;
        await api.CreateFile(instanceName, "/opt/api.json", apiConfig);
        apiWriter.Close();

        var toolsJson = JsonSerializer.Serialize(new
        {
            assemblies = new Dictionary<string, string> {
                { "LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "/opt/LlmAgents/LlmAgents.Tools/bin/Debug/net9.0/LlmAgents.Tools.dll" }
            },
            types = new List<string> {
                "LlmAgents.Tools.FileList, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "LlmAgents.Tools.FileRead, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "LlmAgents.Tools.FileWrite, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "LlmAgents.Tools.Shell, LlmAgents.Tools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            }
        });
        var toolsConfig = new MemoryStream();
        var toolsWriter = new StreamWriter(toolsConfig);
        toolsWriter.Write(toolsJson);
        toolsWriter.Flush();
        toolsConfig.Position = 0;
        await api.CreateFile(instanceName, "/opt/tools.json", toolsConfig);
        toolsWriter.Close();

        await api.ExecInstance(instanceName, new InstanceExecPost
        {
            Command = ["bash", "/opt/install-xmppagent.sh"],
            CurrentWorkingDirectory = "/opt",
            Environment = [],
            User = 0,
            Group = 0,
            WaitForWebsocket = true,
            Interactive = false,
            Width = 212,
            Height = 56
        });
    }

    public async Task DeallocateXmppAgent(string instanceName)
    {
        await initialized.Task;
        await api.StopInstance(instanceName);
        await api.DeleteInstance(instanceName, force: true);

        var newUsers = allocatedUsers.Except([instanceName]).ToList();
        allocatedUsers.Clear();
        foreach (var user in newUsers)
        {
            allocatedUsers.Add(user);
        }

        availableUsers.Add(instanceName);

        OnContainerUpdated?.Invoke();
    }

    public class LxdApi
    {
        private readonly LxcHttpClient httpClient;
        private readonly WebsocketManager websocketManager;

        public LxdApi(LxcHttpClient httpClient, WebsocketManager websocketManager)
        {
            this.httpClient = httpClient;
            this.websocketManager = websocketManager;
        }

        public async Task<LxdResponse> Get()
        {
            return await httpClient.Get<LxdResponse>("1.0") ?? throw new SerializationException();
        }

        public async Task<InstancesGetResponse> GetInstances(string project = "default")
        {
            return await httpClient.Get<InstancesGetResponse>($"1.0/instances?project={project}") ?? throw new SerializationException();
        }

        public async Task<LxdResponse> GetInstance(string instanceName, string project = "default")
        {
            return await httpClient.Get<LxdResponse>($"1.0/instances/{instanceName}?project={project}") ?? throw new SerializationException();
        }

        public async Task<LxdResponse> CreateInstance(InstancesPost payload, string project = "default")
        {
            var response = await httpClient.Post($"1.0/instances?project={project}", payload);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<LxdResponse>() ?? throw new SerializationException();
            if (!string.IsNullOrEmpty(result.Operation))
            {
                await WaitOperation(result.Operation);
            }

            return result;
        }

        public async Task<LxdResponse> StartInstance(string instanceName, string project = "default") => await ChangeInstanceState(instanceName, "start", project);

        public async Task<LxdResponse> StopInstance(string instanceName, string project = "default") => await ChangeInstanceState(instanceName, "stop", project);

        public async Task<LxdResponse> DeleteInstance(string instanceName, string project = "default", bool force = false)
        {
            var response = await httpClient.Delete($"1.0/instances/{instanceName}?project={project}&force={force}");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<LxdResponse>() ?? throw new SerializationException();
            return result;
        }

        public async Task<LxdResponse> ExecInstance(string instanceName, InstanceExecPost payload, string project = "default")
        {
            var response = await httpClient.Post($"1.0/instances/{instanceName}/exec?project={project}", payload);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<LxdResponse>() ?? throw new SerializationException();
            if (string.IsNullOrEmpty(result.Operation))
            {
                throw new NullReferenceException();
            }

            var operation = result.Operation;
            var metadata = result.Metadata.GetProperty("metadata");
            var fds = metadata.GetProperty("fds");
            var stdout = fds.GetProperty("0").GetString();
            var stdin = fds.GetProperty("1").GetString();
            var stderr = fds.GetProperty("2").GetString();
            var control = fds.GetProperty("control").GetString();

            ArgumentException.ThrowIfNullOrEmpty(stdout);
            ArgumentException.ThrowIfNullOrEmpty(stdin);
            ArgumentException.ThrowIfNullOrEmpty(stderr);
            ArgumentException.ThrowIfNullOrEmpty(control);

            await ConnectWebsocket("0", operation, stdout);
            await ConnectWebsocket("1", operation, stdin);
            await ConnectWebsocket("2", operation, stderr);
            await ConnectWebsocket("control", operation, control);

            await WaitOperation(operation);

            return result;
        }

        public async Task<LxdResponse> CreateFile(string instanceName, string path, Stream content, string project = "default")
        {
            var response = await httpClient.Post($"1.0/instances/{instanceName}/files?path={Uri.EscapeDataString(path)}&project={project}", content);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<LxdResponse>() ?? throw new SerializationException();
            return result;
        }

        public async Task<ManagedWebSocket?> ConnectWebsocket(string id, string operation, string secret)
        {
            var websocket = await websocketManager.OpenAsync(id, new Uri($"wss://{httpClient.BaseAddress.Host}:{httpClient.BaseAddress.Port}{operation}/websocket?secret={secret}"), CancellationToken.None);
            websocket.OnMessage += (ws, message) => Console.WriteLine($"{id}: {message}");
            // websocket.OnError += (ws, ex) => Console.WriteLine(ex);

            return websocket;
        }

        public async Task<LxdResponse> ChangeInstanceState(string instanceName, string action, string project)
        {
            var payload = new InstanceStatePut
            {
                Action = action
            };

            var response = await httpClient.Put($"1.0/instances/{instanceName}/state?project={project}", payload);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<LxdResponse>() ?? throw new SerializationException();
            if (!string.IsNullOrEmpty(result.Operation))
            {
                await WaitOperation(result.Operation);
            }

            return result;
        }

        public async Task<LxdResponse> WaitOperation(string operation, int timeout = -1)
        {
            var response = await httpClient.Get<LxdResponse>($"{operation}/wait?timeout={timeout}") ?? throw new SerializationException();
            return response;
        }
    }

    public class LxcHttpClient
    {
        private readonly HttpClient httpClient;

        public LxcHttpClient(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public Uri BaseAddress { get { return httpClient.BaseAddress!; } }

        public async Task<string> Get(string requestUri)
        {
            return await httpClient.GetStringAsync(requestUri);
        }

        public async Task<T?> Get<T>(string requestUri)
        {
            return await httpClient.GetFromJsonAsync<T>(requestUri);
        }

        public async Task<HttpResponseMessage> Post<T>(string requestUri, T value)
        {
            return await httpClient.PostAsJsonAsync(requestUri, value);
        }

        public async Task<HttpResponseMessage> Post(string requestUri, Stream content)
        {
            using var streamContent = new StreamContent(content);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return await httpClient.PostAsync(requestUri, streamContent);
        }

        public async Task<HttpResponseMessage> Put<T>(string requestUri, T value)
        {
            return await httpClient.PutAsJsonAsync(requestUri, value);
        }

        public async Task<HttpResponseMessage> Delete(string requestUri)
        {
            return await httpClient.DeleteAsync(requestUri);
        }
    }
}
