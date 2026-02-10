using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace LlmAgents.Tools.BackgroundJob;

public enum JobStatus
{
    Running,
    Exited,
    Cancelled,
    Failed
}

public class JobInfo
{
    public Guid Id { get; init; }
    public required Process Process { get; init; }
    public StringBuilder Output { get; } = new();
    public JobStatus Status { get; set; } = JobStatus.Running;
    public int? ExitCode { get; set; }
    public DateTime Started { get; init; } = DateTime.UtcNow;
    public DateTime? Ended { get; set; }
    public CancellationTokenSource Cancellation { get; } = new();
}

public class JobManager : IDisposable
{
    private readonly ConcurrentDictionary<Guid, JobInfo> jobs = new();
    private bool disposed = false;

    public Guid Start(string fileName, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = string.Join(' ', args),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var jobId = Guid.NewGuid();
        var jobInfo = new JobInfo { Id = jobId, Process = process };
        if (!jobs.TryAdd(jobId, jobInfo))
            throw new InvalidOperationException($"Could not add job {jobId}");

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                jobInfo.Output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
                jobInfo.Output.AppendLine(e.Data);
        };

        process.Exited += (s, e) =>
        {
            jobInfo.Status = JobStatus.Exited;
            jobInfo.ExitCode = process.ExitCode;
            jobInfo.Ended = DateTime.UtcNow;
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Monitor cancellation token
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, jobInfo.Cancellation.Token);
            }
            catch (TaskCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(true); } catch { }
                }
                jobInfo.Status = JobStatus.Cancelled;
                jobInfo.Ended = DateTime.UtcNow;
            }
        });

        return jobId;
    }

    public JobInfo? Get(Guid id) => jobs.TryGetValue(id, out var info) ? info : null;

    public void Cancel(Guid id)
    {
        if (jobs.TryGetValue(id, out var info))
        {
            info.Cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var kvp in jobs)
        {
            try { kvp.Value.Cancellation.Cancel(); } catch { }
            try { if (!kvp.Value.Process.HasExited) kvp.Value.Process.Kill(true); } catch { }
            kvp.Value.Process.Dispose();
        }
        jobs.Clear();
    }
}
