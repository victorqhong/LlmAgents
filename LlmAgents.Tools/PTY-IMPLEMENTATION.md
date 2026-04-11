# PTY Implementation for Shell Interrupt

## Overview

This document describes how to implement proper pseudo-terminal (PTY) support for the shell tools, specifically to make `shell_interrupt` work correctly for stopping long-running processes like webservers.

### The Problem

Currently, `shell_interrupt` sends `SIGINT` to the bash process, but **without a PTY, bash does not reliably forward the signal to child processes**. This is because:

1. The shell runs with redirected stdin/stdout/stderr (pipes, not terminal)
2. Job control is disabled without a PTY
3. Process group management is limited

**Result:** Pressing Ctrl+C (via `shell_interrupt`) may stop bash but leave child processes (e.g., `python3 server.py`) running as orphans.

---

## What is a PTY?

A **pseudo-terminal (PTY)** is a pair of virtual character devices that simulate a physical terminal:

```
┌─────────────────────────────────────────────────────────┐
│                     Application                          │
│                    (Terminal Emulator)                   │
│                          │                               │
│                    Master Side (FD)                      │
│                          │                               │
│                      ┌───▼───┐                          │
│                      │  PTY  │                          │
│                      └───┬───┘                          │
│                          │                               │
│                    Slave Side (/dev/pts/N)              │
│                          │                               │
│                    Child Process                         │
│                    (bash, python, etc.)                  │
└─────────────────────────────────────────────────────────┘
```

- **Master side**: Controlled by your application (read/write output and input)
- **Slave side**: Used by the shell/program (appears as `/dev/pts/N`)

When you write to the master, it appears as input on the slave. When the slave writes output, you read it from the master.

---

## POSIX PTY Functions

### `openpty()`

Creates a PTY pair and opens both sides:

```c
#include <pty.h>

int openpty(int *amaster, int *aslave, char *name, 
            const struct termios *termp, const struct winsize *winp);
```

**What it does:**
- Allocates a new PTY pair
- Opens both master and slave file descriptors
- Optionally sets terminal attributes and window size
- Returns FDs for both sides

**Usage pattern:**
```c
int master_fd, slave_fd;
openpty(&master_fd, &slave_fd, NULL, NULL, NULL);

// Now you can:
// - Read/write master_fd from your app
// - Pass slave_fd to child process as stdin/stdout/stderr
// - Must fork() manually
```

---

### `forkpty()`

Combines `openpty()` + `fork()` + login setup in one call:

```c
#include <pty.h>

pid_t forkpty(int *amaster, char *name,
              const struct termios *termp, const struct winsize *winp);
```

**What it does:**
- Creates PTY pair (like `openpty`)
- Forks the process
- **Parent**: Returns child PID, gets master FD
- **Child**: Has slave FD as stdin/stdout/stderr, ready to exec a program

**Usage pattern:**
```c
int master_fd;
pid_t pid = forkpty(&master_fd, NULL, NULL, NULL);

if (pid == 0) {
    // Child process - exec bash
    execlp("bash", "bash", NULL);
} else if (pid > 0) {
    // Parent - master_fd is your handle to the PTY
    // Write commands to master_fd, read output from master_fd
}
```

---

### Comparison

| Aspect | `openpty()` | `forkpty()` |
|--------|-------------|-------------|
| **Forking** | No - you fork manually | Yes - built-in |
| **Setup** | More control, more work | Convenient, opinionated |
| **Child setup** | You configure stdin/stdout/stderr | Done automatically |
| **Login setup** | Manual | Automatic (utmp, session, etc.) |
| **Recommendation** | Fine-grained control needed | **Simple shell spawning** |

**For the LlmAgents shell system, `forkpty()` is the recommended choice.**

---

## Integration Plan

### Current Implementation (No PTY)

```csharp
// ShellSessionManager.cs - StartProcess()
private void StartProcess(ShellSessionState state)
{
    var process = new Process();
    process.StartInfo.FileName = "bash";
    process.StartInfo.UseShellExecute = false;
    process.StartInfo.RedirectStandardOutput = true;  // ❌ Pipes, not PTY
    process.StartInfo.RedirectStandardError = true;   // ❌ Pipes, not PTY
    process.StartInfo.RedirectStandardInput = true;   // ❌ Pipes, not PTY
    // ...
}
```

### Proposed Implementation (With PTY)

```csharp
private void StartProcess(ShellSessionState state)
{
    int masterFd;
    pid_t pid = NativeMethods.forkpty(&masterFd, NULL, NULL, NULL);
    
    if (pid == 0)
    {
        // Child - exec bash
        NativeMethods.execlp("bash", "bash", "-i", NULL);  // -i for interactive
        Environment.Exit(1); // Should not reach here
    }
    else if (pid > 0)
    {
        // Parent - store master FD and PID
        state.PtyMasterFd = masterFd;
        state.Process = Process.GetProcessById((int)pid);
        state.StartedUtc = DateTime.UtcNow;
        
        // Start reader thread for master FD
        StartPtyReader(state, masterFd);
    }
    else
    {
        throw new IOException($"forkpty failed: {Marshal.GetLastPInvokeError()}");
    }
}
```

---

## P/Invoke Declarations

Add to `ShellSessionManager.cs` or a dedicated native methods class:

```csharp
using System.Runtime.InteropServices;

private static class NativeMethods
{
    public const int SIGINT = 2;
    public const int SIGKILL = 9;
    public const int STDIN_FILENO = 0;
    public const int STDOUT_FILENO = 1;
    public const int STDERR_FILENO = 2;
    
    // Signal handling
    [DllImport("libc.so.6", SetLastError = true)]
    public static extern int kill(int pid, int sig);
    
    // PTY functions
    [DllImport("libc.so.6", EntryPoint = "forkpty", SetLastError = true)]
    public static extern int forkpty(int* amaster, byte* name, 
                                     IntPtr termp, IntPtr winp);
    
    [DllImport("libc.so.6", EntryPoint = "openpty", SetLastError = true)]
    public static extern int openpty(int* amaster, int* aslave, byte* name,
                                     IntPtr termp, IntPtr winp);
    
    [DllImport("libc.so.6", EntryPoint = "execlp", SetLastError = true)]
    public static extern int execlp(byte* file, byte* arg, IntPtr end);
    
    // File descriptor operations
    [DllImport("libc.so.6", SetLastError = true)]
    public static extern int read(int fd, byte* buf, int count);
    
    [DllImport("libc.so.6", SetLastError = true)]
    public static extern int write(int fd, byte* buf, int count);
    
    [DllImport("libc.so.6", SetLastError = true)]
    public static extern int close(int fd);
    
    [DllImport("libc.so.6", SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);
    
    // Terminal control
    [DllImport("libc.so.6", SetLastError = true)]
    public static extern int ioctl(int fd, int request, IntPtr argp);
    
    // Window size structure
    [StructLayout(LayoutKind.Sequential)]
    public struct winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }
    
    // Termios structure (simplified)
    [StructLayout(LayoutKind.Sequential)]
    public struct termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] c_cc;
    }
}
```

---

## PTY Reader Thread

Since we can't use `Process.StandardOutput`, we need to read from the master FD directly:

```csharp
private void StartPtyReader(ShellSessionState state, int masterFd)
{
    Task.Run(() =>
    {
        var buffer = new byte[4096];
        fixed (byte* ptr = buffer)
        {
            while (state.PtyMasterFd.HasValue)
            {
                var bytesRead = NativeMethods.read(masterFd, ptr, buffer.Length);
                if (bytesRead <= 0)
                {
                    // EOF or error - process exited
                    break;
                }
                
                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                AppendOutput(state, text);
                log.LogInformation("pty: {text}", text.Trim());
            }
        }
    });
}
```

---

## Writing to PTY

```csharp
private static void WritePty(ShellSessionState state, string input)
{
    if (!state.PtyMasterFd.HasValue) return;
    
    var bytes = Encoding.UTF8.GetBytes(input);
    fixed (byte* ptr = bytes)
    {
        NativeMethods.write(state.PtyMasterFd.Value, ptr, bytes.Length);
    }
}

// For commands (with newline)
private static void WriteLinePty(ShellSessionState state, string line)
{
    WritePty(state, line + "
");
}
```

---

## Fixed shell_interrupt Implementation

With PTY, signal forwarding works correctly:

```csharp
public async Task<JsonNode> InterruptAsync(Session? session, int? timeoutMs)
{
    var state = GetOrCreateState(GetSessionId(session));
    await state.OperationLock.WaitAsync();
    try
    {
        EnsureProcessStarted(state);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Option 1: Send SIGINT to bash process group
            // PTY ensures bash forwards to foreground children
            NativeMethods.Kill(state.Process.Id, NativeMethods.SIGINT);
            
            // Option 2: Write Ctrl+C character to PTY master
            // This simulates actual terminal Ctrl+C
            // WritePty(state, "\x03");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows fallback (no PTY support yet)
            WritePty(state, "\x03");
        }

        // Verification logic remains the same
        var timeout = timeoutMs.GetValueOrDefault(Math.Min(waitTimeMs, 3000));
        var sentinel = $"__llmagents_interrupt_probe_{Guid.NewGuid():N}__";
        var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetPendingSentinel(state, sentinel, sentinelTask);
        WriteLinePty(state, EchoCommand(sentinel));

        var completed = await Task.WhenAny(sentinelTask.Task, Task.Delay(timeout));
        var responsive = completed == sentinelTask.Task;
        if (!responsive)
        {
            RestartProcess(state);
        }

        return new JsonObject
        {
            { "session_id", state.SessionId },
            { "status", "interrupted" },
            { "responsive", responsive },
            { "restarted", !responsive }
        };
    }
    finally
    {
        ClearPendingSentinel(state);
        state.OperationLock.Release();
    }
}
```

---

## Cleanup

```csharp
private static void DisposeProcess(Process? process, int? ptyMasterFd)
{
    if (ptyMasterFd.HasValue)
    {
        try
        {
            NativeMethods.close(ptyMasterFd.Value);
        }
        catch { }
    }
    
    if (process == null) return;
    
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
    }
    catch { }
    finally
    {
        process.Dispose();
    }
}
```

---

## ShellSessionState Changes

Add PTY-related fields:

```csharp
private sealed class ShellSessionState
{
    public required string SessionId { get; init; }
    public required string CurrentDirectory { get; set; }
    public Process Process { get; set; } = null!;
    public int? PtyMasterFd { get; set; }  // NEW: PTY master file descriptor
    public DateTime StartedUtc { get; set; }
    public int? ExitCode { get; set; }
    public object SyncRoot { get; } = new();
    public SemaphoreSlim OperationLock { get; } = new(1, 1);
    public StringBuilder Output { get; } = new();
    public long BufferStartPosition { get; set; }
    public string? PendingSentinel { get; set; }
    public TaskCompletionSource<bool>? PendingSentinelTcs { get; set; }
    public CancellationTokenSource? ReaderCts { get; set; }  // NEW: For reader thread
}
```

---

## Project Configuration

Add to `.csproj`:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

---

## Platform Support

| Platform | PTY Support | Notes |
|----------|-------------|-------|
| **Linux** | ✅ Full | `forkpty()` in libc |
| **macOS** | ✅ Full | `forkpty()` in libc |
| **Windows** | ❌ Limited | No native PTY; need ConPTY API |

### Windows Alternative (ConPTY)

Windows has Console PTY (ConPTY) API available since Windows 10 1809:

```csharp
// Would need separate Windows implementation
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CreatePseudoConsole(...);
```

Consider using a cross-platform library like:
- [Microsoft.Terminal](https://github.com/microsoft/terminal)
- [ConPTY wrapper libraries](https://www.nuget.org/packages?q=conpty)

---

## Testing Checklist

- [ ] Simple command (`echo hello`) works
- [ ] Long-running process (`sleep 1000`) can be interrupted
- [ ] Webserver (`python3 -m http.server`) stops with interrupt
- [ ] Pipeline (`find / | grep x`) interrupts correctly
- [ ] Interactive programs (`vim`, `top`) work
- [ ] Output colors/formatting preserved
- [ ] Session can be stopped cleanly
- [ ] No file descriptor leaks
- [ ] No zombie processes after interrupt/stop

---

## Alternative Approaches

If full PTY implementation is too complex initially, consider:

### Option A: Process Group Signaling (Minimal Change)

```csharp
// Send to process group instead of just bash
NativeMethods.Kill(-state.Process.Id, NativeMethods.SIGINT);
```

Requires starting bash in its own process group.

### Option B: Hybrid Signal Approach

```csharp
// Try process group first
NativeMethods.Kill(-state.Process.Id, NativeMethods.SIGINT);
await Task.Delay(100);

// Fallback to bash directly if children still running
if (ChildrenStillRunning(state.Process.Id))
{
    NativeMethods.Kill(state.Process.Id, NativeMethods.SIGKILL);
}
```

### Option C: Use Existing Library

| Library | NuGet | Notes |
|---------|-------|-------|
| Mono.Posix.NETStandard | `Mono.Posix.NETStandard` | Has some Unix native bindings |
| Tmds.ExecFunction | `Tmds.ExecFunction` | Modern process execution |
| SharpPTY | (check availability) | Dedicated PTY library |

---

## References

- [POSIX `forkpty()` man page](https://man7.org/linux/man-pages/man3/forkpty.3.html)
- [POSIX `openpty()` man page](https://man7.org/linux/man-pages/man3/openpty.3.html)
- [Linux PTY documentation](https://www.kernel.org/doc/Documentation/admin-guide/serial-console.rst)
- [Windows ConPTY documentation](https://learn.microsoft.com/en-us/windows/console/conpty)
- [Unix Terminal I/O](https://www.gnu.org/software/libc/manual/html_node/Terminal-I/O.html)

---

## Implementation Priority

1. **High**: Linux `forkpty()` implementation (core functionality)
2. **Medium**: macOS support (same API as Linux)
3. **Low**: Windows ConPTY support (different API)
4. **Future**: Terminal resize support, color configuration, etc.

---

*Document created for future PTY implementation reference*
*For: LlmAgents Shell Tools*
