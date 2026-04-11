# PTY Shell Testing Guide

This document provides instructions to test the PTY implementation for proper SIGINT forwarding, job control, and shell tool functionality.

## Prerequisites
- Python3 installed (for webserver test).
- net8+ runtime, Linux OS.
- LLM agent with access to shell_* tools.

## Test Checklist

Run each test sequence using tool calls. Check `shell_status`, `shell_read`, external verification (ps, curl, netstat).

### [ ] 1. Basic Startup &amp; Simple Command
```
shell_status  → {&quot;status&quot;:&quot;not_started&quot;}
shell_exec &quot;echo &#39;hello PTY&#39;&quot; wait=true → {&quot;status&quot;:&quot;completed&quot;}
shell_status → {&quot;status&quot;:&quot;exited&quot;}
shell_read → contains &quot;hello PTY&quot;
```

### [ ] 2. Long-Running Non-Child Process
```
shell_exec &quot;sleep 100&quot; wait=false → started
shell_status → running, pid ~&gt;
shell_interrupt → responsive: true
shell_read → shows interrupt
shell_status → exited
```

### [ ] 3. Child Process Interrupt (Key Test)
```
shell_exec &quot;python3 -m http.server 8080&quot; wait=false → started
# External: curl http://localhost:8080/ → works
shell_status → running
shell_interrupt → responsive: true (may restart if hung)
# External: curl fails, ps aux | grep &#39;http.server&#39; → no process, port free (netstat -tlnp | grep 8080)
shell_status → exited or running (fresh)
```

### [ ] 4. Pipeline Interrupt
```
shell_exec &quot;ping -c 100 google.com&quot; wait=false → started (or find /dev/null | grep foo)
shell_interrupt → responsive: true
shell_read → shows ^C, partial output
```

### [ ] 5. Interactive Programs
```
shell_exec &quot;top&quot; wait=false → started
shell_interrupt → responsive: true (top exits)
shell_read → top output + ^C
```

```
shell_exec &quot;vim /tmp/test.txt&quot; wait=false (if vim available)
# Use shell_write &quot;ihello\x1b:wq&quot; (esc = \x1b)
shell_interrupt → ^C exits vim
```

### [ ] 6. Timeout &amp; Restart
```
shell_exec &quot;sleep 100&quot; timeoutMs=1000 wait=true → status: timeout, restarted=true
shell_status → running (new pid)
```

### [ ] 7. Output Buffering &amp; Read
```
shell_exec &quot;for i in {1..1000}; do echo $i; done&quot; wait=true
shell_read → chunks correctly, has_more, cursors
```

### [ ] 8. Write &amp; AppendNewline
```
shell_exec &quot;&quot; wait=false  # interactive
shell_write &quot;pwd&quot; appendNewline=true → exec pwd

shell_read → pwd output
shell_write &quot;ls&quot; appendNewline=false; shell_write &quot;
&quot; → ls

```

### [ ] 9. Directory Change
```
# Trigger DirectoryChangeEvent or use directory_change tool
shell_read → shows cd effect
```

### [ ] 10. Stop &amp; Cleanup
```
shell_exec sleep... 
shell_stop → stopped, no zombies (ps aux | grep defunct)
```

### [ ] 11. Error Handling
```
# On non-Linux: status=&quot;error&quot;, descriptive msg
# forkpty fail (low mem?): failed_to_start with errno
```

### [ ] 12. ANSI Colors
```
shell_exec &quot;ls --color=always&quot; → check shell_read preserves \x1b[ codes
```

### [ ] 13. Multi-Session
```
Use session_id=foo/bar, test parallel exec/interrupt
```

## External Verification Commands
- Zombies/orphans: `ps aux | grep defunct` or `ps --ppid 1 | grep bash`
- FD leaks: `lsof -p &lt;shell_pid&gt; | wc -l` &lt;10
- Port check: `netstat -tlnp | grep 8080`
- Logs: Check app logs for PTY read/write

## Edge Cases
- Huge output &gt; maxBufferedChars → truncated=false, buffer_start advances
- Partial sentinels (split chunks) → still triggers
- Rapid interrupt/exec → OperationLock serializes

Pass all → PTY ready!

*Created post-implementation*