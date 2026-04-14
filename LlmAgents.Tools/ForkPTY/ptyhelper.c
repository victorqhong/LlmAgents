#define _XOPEN_SOURCE 600
#include <pty.h>
#include <unistd.h>
#include <stdlib.h>
#include <errno.h>
#include <string.h>
#include <fcntl.h>
#include <sys/ioctl.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int master_fd;
    pid_t child_pid;
    int error_code;
} forkpty_result;

// Forks a PTY and execs the given command with args.
// command: the executable path or name (searched in PATH)
// arg1: first argument (typically the program name again)
// arg2: second argument (e.g., "-i" for interactive)
// arg3: third argument or NULL
forkpty_result forkpty_exec(const char* command, const char* arg1, const char* arg2, const char* arg3)
{
    forkpty_result result = {0};
    
    struct winsize win = { .ws_row = 24, .ws_col = 80 };
    
    result.child_pid = forkpty(&result.master_fd, NULL, NULL, &win);
    
    if (result.child_pid < 0) {
        result.error_code = errno;
        return result;
    }
    
    if (result.child_pid == 0) {
        // Child process
        if (arg3 != NULL) {
            execlp(command, arg1, arg2, arg3, (char *)NULL);
        } else {
            execlp(command, arg1, arg2, (char *)NULL);
        }
        _exit(127);  // exec failed
    }
    
    // Parent
    return result;
}

// Simple wrapper for sh -c "command"
forkpty_result forkpty_sh(const char* shell_command)
{
    forkpty_result result = {0};
    
    struct winsize win = { .ws_row = 24, .ws_col = 80 };
    
    result.child_pid = forkpty(&result.master_fd, NULL, NULL, &win);
    
    if (result.child_pid < 0) {
        result.error_code = errno;
        return result;
    }
    
    if (result.child_pid == 0) {
        // Child - use sh which is more portable
        execlp("sh", "sh", "-c", shell_command, (char *)NULL);
        _exit(127);
    }
    
    return result;
}

// Close the master fd
int pty_close_master(int master_fd)
{
    return close(master_fd);
}

// Write to the master fd
ssize_t pty_write(int master_fd, const void* buf, size_t count)
{
    return write(master_fd, buf, count);
}

// Read from the master fd (blocking)
ssize_t pty_read(int master_fd, void* buf, size_t count)
{
    return read(master_fd, buf, count);
}

#ifdef __cplusplus
}
#endif
