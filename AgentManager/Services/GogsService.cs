using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text.Json;
using AgentManager.Configuration;
using AgentManager.Models.Gogs;

namespace AgentManager.Services;

public class GogsService
{
    private readonly GogsApi api;

    private readonly Dictionary<string, string> xmppUsers = [];

    public GogsService(GogsHttpClient httpClient, ProvisioningOptions provisioningOptions)
    {
        api = new GogsApi(httpClient);

        if (!File.Exists(provisioningOptions.UserConfigFile))
        {
            throw new FileNotFoundException();
        }

        var userConfig = JsonSerializer.Deserialize<List<XmppUser>>(File.ReadAllText(provisioningOptions.UserConfigFile)) ?? throw new SerializationException();
        foreach (var user in userConfig)
        {
            xmppUsers.Add(user.Jid, user.Password);
        }
    }

    private string? FindUser(string username)
    {
        foreach (var user in xmppUsers)
        {
            if (user.Key.Contains(username))
            {
                return user.Key;
            }
        }

        return null;
    }

    public async Task<bool> UserExists(string username)
    {
        var user = FindUser(username);
        if (user == null)
        {
            return false;
        }

        var getUserResponse = await api.GetUser(username);
        if (getUserResponse == null)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> CreateUser(string username, string publicKey)
    {
        var email = FindUser(username);
        if (email == null)
        {
            return false;
        }

        var password = xmppUsers[email];

        var createUserResponse = await api.CreateUser(new() 
        {
            Username = username,
            Password = password,
            Email = email,
        });

        if (createUserResponse == null)
        {
            return false;
        }

        var createPublicKeyResponse = await api.CreatePublicKey(username, new()
        {
            Key = publicKey,
            Title = "key",
            
        });

        if (createPublicKeyResponse == null)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> CreatePublicKey(string username, string publicKey)
    {
        if (!await UserExists(username))
        {
            return false;
        }

        var createPublicKeyResponse = await api.CreatePublicKey(username, new()
        {
            Key = publicKey,
            Title = DateTime.UtcNow.ToString()
            
        });

        if (createPublicKeyResponse == null)
        {
            return false;
        }

        return true;
    }

    public class GogsApi
    {
        private readonly GogsHttpClient httpClient;

        public GogsApi(GogsHttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public async Task<GetUserResponse?> GetUser(string username)
        {
            var response = await httpClient.Get($"api/v1/users/{username}");
            if (response.StatusCode != HttpStatusCode.OK || await response.Content.ReadFromJsonAsync<GetUserResponse>() is not GetUserResponse getUserResponse)
            {
                return null;
            }

            return getUserResponse;
        }

        public async Task<CreateUserResponse?> CreateUser(CreateUserRequest createUser)
        {
            var response = await httpClient.Post("api/v1/admin/users", createUser);
            if (response.StatusCode != HttpStatusCode.Created || await response.Content.ReadFromJsonAsync<CreateUserResponse>() is not CreateUserResponse createUserResponse)
            {
                return null;
            }

            return createUserResponse;
        }

        public async Task<CreatePublicKeyResponse?> CreatePublicKey(string username, CreatePublicKeyRequest createPublicKey)
        {
            var response = await httpClient.Post($"api/v1/admin/users/{username}/keys", createPublicKey);
            if (response.StatusCode != HttpStatusCode.Created || await response.Content.ReadFromJsonAsync<CreatePublicKeyResponse>() is not CreatePublicKeyResponse createPublicKeyResponse)
            {
                return null;
            }

            return createPublicKeyResponse;
        }
    }

    public class GogsHttpClient
    {
        private readonly HttpClient httpClient;

        public GogsHttpClient(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public async Task<T?> Get<T>(string requestUri)
        {
            return await httpClient.GetFromJsonAsync<T>(requestUri);
        }

        public async Task<HttpResponseMessage> Get(string requestUri)
        {
            return await httpClient.GetAsync(requestUri);
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
