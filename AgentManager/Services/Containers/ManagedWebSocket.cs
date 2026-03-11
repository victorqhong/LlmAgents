namespace AgentManager.Services.Containers;

using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

public class ManagedWebSocket : IAsyncDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public string Id { get; }
    public WebSocketState State => _ws.State;

    public ManagedWebSocket(string id, X509Certificate2 clientCertificate, X509Certificate2 serverCertificate)
    {
        Id = id;

        _ws.Options.ClientCertificates.Add(clientCertificate);
        _ws.Options.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
        {
            if (errors == System.Net.Security.SslPolicyErrors.None)
            {
                return true;
            }

            return cert != null && cert.GetCertHashString() == serverCertificate.GetCertHashString();
        };
    }

    public async Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        await _ws.ConnectAsync(uri, ct);
        _ = Task.Run(() => ReceiveLoop(ct), ct);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];

        try
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        ct
                    );
                    break;
                }

                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                OnMessage?.Invoke(this, msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
        }
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open)
            return;

        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(
                Encoding.UTF8.GetBytes(message),
                WebSocketMessageType.Text,
                true,
                ct
            );
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public event Action<ManagedWebSocket, string>? OnMessage;
    public event Action<ManagedWebSocket, Exception>? OnError;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposed", CancellationToken.None);
            }
        }
        catch { }

        _ws.Dispose();
        _sendLock.Dispose();
    }
}
