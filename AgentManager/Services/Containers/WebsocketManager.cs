namespace AgentManager.Services.Containers;

using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using AgentManager.Models.Containers;

public class WebsocketManager
{
    private readonly ConcurrentDictionary<string, ManagedWebSocket> _sockets = new();

    private readonly LxcOptions lxcOptions;

    public WebsocketManager(LxcOptions lxcOptions)
    {
        this.lxcOptions = lxcOptions;
    }

    public ICollection<ManagedWebSocket> ActiveSockets => _sockets.Values;

    public async Task<ManagedWebSocket> OpenAsync(string id, Uri uri, CancellationToken ct)
    {
        var clientCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(lxcOptions.ClientCertFilePath), File.ReadAllText(lxcOptions.ClientKeyFilePath));
        var serverCert = X509Certificate2.CreateFromPem(File.ReadAllText(lxcOptions.ServerCertFilePath));
        var socket = new ManagedWebSocket(id, clientCertificate, serverCert);

        if (!_sockets.TryAdd(id, socket))
            throw new InvalidOperationException("Socket already exists");

        socket.OnError += async (_, _) => await CloseAsync(id, ct);
        await socket.ConnectAsync(uri, ct);

        return socket;
    }

    public async Task SendAsync(string id, string message, CancellationToken ct)
    {
        if (_sockets.TryGetValue(id, out var socket))
            await socket.SendAsync(message, ct);
    }

    public async Task CloseAsync(string id, CancellationToken ct)
    {
        if (_sockets.TryRemove(id, out var socket))
        {
            await socket.DisposeAsync();
        }
    }
}
