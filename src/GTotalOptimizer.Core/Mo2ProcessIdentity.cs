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

    public static bool IsElevated(int pid) => InspectElevation(pid).Elevated == true;

    // Do not conflate a denied query or an exited process with an
    // actual non-elevated token. Preserve the Win32 failure for diagnosis.
    public static (bool? Elevated, string Detail) InspectElevation(int pid)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0)
            return (null, "Invalid Windows process identity.");
        var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
            return (null, "OpenProcess failed (Windows error " + Marshal.GetLastWin32Error() + ").");
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
                return (null, "OpenProcessToken failed (Windows error " + Marshal.GetLastWin32Error() + ").");
            try
            {
                if (!GetTokenInformation(token, TokenElevation, out int elevated,
                        sizeof(int), out _))
                    return (null, "GetTokenInformation failed (Windows error " + Marshal.GetLastWin32Error() + ").");
                return elevated != 0 ? (true, "MO2 is elevated.") : (false, "MO2 is not running as administrator.");
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
