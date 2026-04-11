# Shell Tools Exercise Plan

## Goal
Quickly verify all shell tools are working correctly through practical exercises.

---

## Exercise Sequence

### Step 1: Start a Session & Run a Command
**Tool:** `shell_exec`

```bash
echo "Shell tools test - $(date)"
```
- Set `wait_for_exit: true`
- **Verify:** Command runs and returns output with timestamp

---

### Step 2: Check Session Status
**Tool:** `shell_status`

- Call without parameters
- **Verify:** Returns session state and buffer cursor range (min/max)

---

### Step 3: Read the Output
**Tool:** `shell_read`

- Read from the beginning (default cursor)
- Set `max_chars: 500`
- **Verify:** Returns the output from Step 1

---

### Step 4: Run a Longer Command
**Tool:** `shell_exec`

```bash
echo "Line 1" && echo "Line 2" && echo "Line 3"
```
- Set `wait_for_exit: true`
- **Verify:** All three lines appear in output

---

### Step 5: Read Output in Chunks
**Tool:** `shell_read`

- First read: `max_chars: 20`
- Second read: use returned cursor from first read
- **Verify:** Can retrieve remaining output with second read

---

### Step 6: Start an Interactive Command
**Tool:** `shell_exec`

```bash
cat
```
- Set `wait_for_exit: false` (runs in background)
- **Verify:** Command starts without waiting

---

### Step 7: Write Input to Running Process
**Tool:** `shell_write`

```
Hello from shell_write test
```
- Set `append_newline: true`
- **Verify:** Input is sent to the `cat` process

---

### Step 8: Read the Response
**Tool:** `shell_read`

- Read the output buffer
- **Verify:** Shows the text you wrote in Step 7

---

### Step 9: Interrupt the Running Process
**Tool:** `shell_interrupt`

- Send Ctrl+C to stop the `cat` command
- **Verify:** Process stops, session remains active

---

### Step 10: Verify Session Still Works
**Tool:** `shell_status` then `shell_exec`

- Check status shows session is responsive
- Run: `echo "Session recovered after interrupt"`
- **Verify:** New command executes successfully

---

### Step 11: Test Timeout Handling
**Tool:** `shell_exec`

```bash
sleep 2
```
- Set `timeout_ms: 5000`
- Set `wait_for_exit: true`
- **Verify:** Command completes within timeout

---

### Step 12: Write Without Newline
**Tool:** `shell_write`

- Start a new `cat` command with `shell_exec`
- Write: `test no newline`
- Set `append_newline: false`
- **Verify:** Input sent without trailing newline

---

### Step 13: Test Error Handling
**Tool:** `shell_exec`

```bash
command_that_does_not_exist
```
- **Verify:** Returns error message, session doesn't crash

---

### Step 14: Clean Up Session
**Tool:** `shell_stop`

- Stop the session
- **Verify:** Session terminates cleanly

---

### Step 15: Verify New Session Can Start
**Tool:** `shell_exec`

```bash
echo "New session working"
```
- **Verify:** Fresh session starts and command runs

---

## Quick Checklist

| Step | Tool | Status |
|------|------|--------|
| 1 | shell_exec | ☐ |
| 2 | shell_status | ☐ |
| 3 | shell_read | ☐ |
| 4 | shell_exec | ☐ |
| 5 | shell_read | ☐ |
| 6 | shell_exec | ☐ |
| 7 | shell_write | ☐ |
| 8 | shell_read | ☐ |
| 9 | shell_interrupt | ☐ |
| 10 | shell_status + shell_exec | ☐ |
| 11 | shell_exec (timeout) | ☐ |
| 12 | shell_write | ☐ |
| 13 | shell_exec (error) | ☐ |
| 14 | shell_stop | ☐ |
| 15 | shell_exec (new session) | ☐ |

---

## Expected Outcomes

✅ All 9 shell tools execute without errors
✅ Session persists across multiple commands
✅ Interactive commands (cat) accept input
✅ Interrupt stops running processes
✅ Session recovers after interrupt
✅ New sessions can be created after stopping

---

## If Something Fails

| Issue | Troubleshooting |
|-------|-----------------|
| shell_exec hangs | Check timeout_ms, try shell_interrupt |
| shell_read returns empty | Check cursor position, run shell_status |
| shell_write has no effect | Ensure process is still running |
| shell_interrupt doesn't work | Increase timeout_ms parameter |
| shell_stop fails | Session may already be stopped (idempotent) |

---

*This plan exercises all tools in a logical workflow to verify basic functionality.*
