using System;
using System.Threading.Tasks;

namespace LlmAgents.Tools.Shell;

public interface IShellSession : IDisposable
{
    int? Pid { get; }
    int? ExitCode { get; }
    string? StartError { get; }
    bool Exited { get; }
    DateTime StartedUtc { get; }
    event Action<string> OutputReceived;

    Task StartAsync(string workingDirectory);
    Task WriteAsync(string input);
    Task WriteLineAsync(string input);
    Task InterruptAsync();
    Task StopAsync();
}
