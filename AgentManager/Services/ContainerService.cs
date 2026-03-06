using System.Net.Http.Headers;
using System.Runtime.Serialization;
using AgentManager.Models.Containers;
using AgentManager.Services.Containers;

namespace AgentManager.Services;

public class ContainerService
{
    private readonly LxcHttpClient httpClient;
    private readonly WebsocketManager websocketManager;

    public ContainerService(LxcHttpClient httpClient, WebsocketManager websocketManager)
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
        if (string.IsNullOrEmpty(result.Operation))
        {
            throw new NullReferenceException();
        }

        await WaitOperation(result.Operation);

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

    private async Task ConnectWebsocket(string id, string operation, string secret)
    {
        var websocket = await websocketManager.OpenAsync(id, new Uri($"wss://{httpClient.BaseAddress.Host}:{httpClient.BaseAddress.Port}{operation}/websocket?secret={secret}"), CancellationToken.None);
        websocket.OnMessage += (ws, message) => Console.WriteLine($"{id}: {message}");
        // websocket.OnError += (ws, ex) => Console.WriteLine(ex);
    }

    private async Task<LxdResponse> ChangeInstanceState(string instanceName, string action, string project)
    {
        var payload = new InstanceStatePut
        {
            Action = action
        };

        var response = await httpClient.Put($"1.0/instances/{instanceName}/state?project={project}", payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LxdResponse>() ?? throw new SerializationException();
        return result;
    }

    private async Task<LxdResponse> WaitOperation(string operation, int timeout = -1)
    {
        var response = await httpClient.Get<LxdResponse>($"{operation}/wait?timeout={timeout}") ?? throw new SerializationException();
        return response;
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
