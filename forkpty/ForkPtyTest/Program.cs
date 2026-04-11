using System;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    [DllImport("/home/victor/Code/forkpty/libptyhelper.so", SetLastError = true)]
    private static extern int forkpty_run_sh(out int master_fd);

    [DllImport("libc", SetLastError = true)]
    private static extern long read(int fd, byte[] buf, ulong count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    static void Main()
    {
        int masterFd = -1;
        int pid = forkpty_run_sh(out masterFd);

        if (pid < 0)
        {
            Console.WriteLine($"forkpty_run_sh failed, errno={Marshal.GetLastWin32Error()}");
            return;
        }

        Console.WriteLine($"Parent: pid={pid}, masterFd={masterFd}");

        byte[] buf = new byte[4096];
        while (true)
        {
            long n = read(masterFd, buf, (ulong)buf.Length);
            if (n > 0)
            {
                Console.Write(Encoding.UTF8.GetString(buf, 0, (int)n));
            }
            else
            {
                Console.WriteLine($"\nread ended, errno={Marshal.GetLastWin32Error()}");
                break;
            }
        }

        close(masterFd);

        waitpid(pid, out int status, 0);
        Console.WriteLine($"Child status={status}");
    }
}
