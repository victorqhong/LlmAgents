using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Tools;

public sealed class ProcessShellSession : IShellSession
{
    private readonly ILogger log;
    private readonly string sessionId;
    private Process? _process;
    private CancellationTokenSource? _readerCts;
    private int? _exitCode;
    private string? _startError;
    private bool _exited;
    private DateTime _startedUtc;

    public int? Pid => _process?.Id;
    public int? ExitCode => _exitCode;
    public string? StartError => _startError;
    public bool Exited => _exited;
    public DateTime StartedUtc => _startedUtc;

    public event Action<string>? OutputReceived;

    public ProcessShellSession(string sessionId, ILogger logger)
    {
        this.sessionId = sessionId;
        this.log = logger;
    }

    public async Task StartAsync(string workingDirectory)
    {
        try
        {
            var process = new Process();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process.StartInfo.FileName = "pwsh";
                process.StartInfo.ArgumentList.Add("-NoLogo");
            }
            else
            {
                process.StartInfo.FileName = "bash";
            }

            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardInput = true;

            process.Start();
            
            _process = process;
            _startedUtc = DateTime.UtcNow;
            _readerCts = new CancellationTokenSource();
            
            StartReader(process);

            log.LogInformation("Process shell started for {SessionId}: pid={Pid}", sessionId, process.Id);
        }
        catch (Exception ex)
        {
            _startError = ex.Message;
            log.LogError(ex, "Failed to start process shell for {SessionId}", sessionId);
            throw;
        }
        await Task.CompletedTask;
    }

    private void StartReader(Process process)
    {
        var cts = _readerCts!;
        
        _ = Task.Run(async () =>
        {
            try
            {
                var outputTask = ReadStreamAsync(process.StandardOutput, "stdout", cts.Token);
                var errorTask = ReadStreamAsync(process.StandardError, "stderr", cts.Token);
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Shell reader crashed for {SessionId}", sessionId);
            }
            finally
            {
                _exited = true;
                _exitCode = process.ExitCode;
            }
        }, cts.Token);
    }

    private async Task ReadStreamAsync(StreamReader reader, string streamName, CancellationToken token)
    {
        char[] buffer = new char[4096];
        while (!token.IsCancellationRequested && !reader.EndOfStream)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), token);
            if (read > 0)
            {
                string text = new string(buffer, 0, read);
                OutputReceived?.Invoke(text);
            }
        }
    }

    public Task WriteAsync(string input)
    {
        if (_process == null || _process.HasExited) return Task.CompletedTask;
        _process.StandardInput.Write(input);
        _process.StandardInput.Flush();
        return Task.CompletedTask;
    }

    public Task WriteLineAsync(string input)
    {
        if (_process == null || _process.HasExited) return Task.CompletedTask;
        _process.StandardInput.WriteLine(input);
        _process.StandardInput.Flush();
        return Task.CompletedTask;
    }

    public Task InterruptAsync()
    {
        log.LogWarning("Interrupt (Ctrl+C) is not natively supported for process-based shell on Windows.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_readerCts != null)
        {
            _readerCts.Cancel();
            _readerCts.Dispose();
            _readerCts = null;
        }

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            _process.Dispose();
            _process = null;
        }
        _exited = true;
    }
}
