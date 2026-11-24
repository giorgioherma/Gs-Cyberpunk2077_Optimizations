using System.Runtime.InteropServices;
using System.Text;

namespace GTotalOptimizer.Core;

// Query-only access works across medium/high integrity for same-user processes.
// Never request process VM, injection, termination or token modification rights.
public static class Mo2ProcessIdentity
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    public static string? Image(int pid)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0) return null;
        var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref length) ? path.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    public static bool IsElevated(int pid)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0) return false;
        var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) return false;
            try
            {
                return GetTokenInformation(token, TokenElevation, out int elevated,
                    sizeof(int), out _) && elevated != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
        StringBuilder path, ref uint size);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int value,
        int length, out int returned);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
