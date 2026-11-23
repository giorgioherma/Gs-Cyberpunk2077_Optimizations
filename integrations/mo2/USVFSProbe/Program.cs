using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Disposable, native USVFS black-box probe. Runs only in Windows CI against
// the real binary bundled in an official MO2 release. This does NOT prove
// Cyberpunk game startup, MO2 profile mutation or native CET/GRSP loading.
internal static class Program
{
    private const uint Recursive = 0x00000008;
    private const uint CreateTarget = 0x00000004;
    private const uint Infinite = 0xFFFFFFFF;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr CreateParams();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void FreeParams(IntPtr parameters);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    private delegate void SetInstance(IntPtr parameters, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool CreateVfs(IntPtr parameters);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DisconnectVfs();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ClearMappings();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool LinkDirectory(string source, string destination, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr Version();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool CreateHooked(
        string? app, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint flags, IntPtr environment, string? directory,
        ref StartupInfo startup, out ProcessInformation info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint cb;
        public string? reserved, desktop, title;
        public uint x, y, width, height, xChars, yChars, fill, flags;
        public ushort show, reserved2;
        public IntPtr reservedBytes, stdin, stdout, stderr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr process, thread;
        public uint processId, threadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exit);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string? path);

    private static T Export<T>(IntPtr handle, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--probe")
            {
                Probe(Path.GetFullPath(args[1]));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--child")
            {
                string game = Path.GetFullPath(args[1]);
                string asi = File.ReadAllText(Path.Combine(game, "bin", "x64", "plugins",
                    "cyber_engine_tweaks.asi"));
                string reds = File.ReadAllText(Path.Combine(game, "r6", "scripts", "TestMod", "test.reds"));
                if (asi != "MO2_PROFILER_OVERRIDE" || reds != "MO2_VIRTUAL_REDSCRIPT")
                    throw new InvalidDataException("USVFS did not expose exact higher-priority profiler/source bytes");
                string capture = Path.Combine(game, "red4ext", "plugins", "G-REDscript-Profiler",
                    "RESULTS", "GCO-CI-capture.csv");
                Directory.CreateDirectory(Path.GetDirectoryName(capture)!);
                File.WriteAllText(capture, "owner,result\nGRSP,real_usvfs_child\n");
                Console.WriteLine("PASS hooked child observed exact profiler ASI and REDscript override bytes");
                return 0;
            }
            throw new ArgumentException("Usage: USVFSProbe --probe <actual-usvfs_x64.dll>");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL USVFS real-binary contract: " + ex);
            return 1;
        }
    }

    private static void Probe(string dll)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!File.Exists(dll)) throw new FileNotFoundException("Official MO2 USVFS DLL missing", dll);
        if (IntPtr.Size != 8) throw new InvalidOperationException("x64 USVFS required.");
        string folder = Path.Combine(Path.GetTempPath(), "gco-usvfs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        IntPtr library = IntPtr.Zero;
        IntPtr parameters = IntPtr.Zero;
        bool connected = false;
        try
        {
            string game = Path.Combine(folder, "game");
            string mod = Path.Combine(folder, "mo2-profiler-mod");
            string overwrite = Path.Combine(folder, "overwrite");
            string relativeAsi = Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks.asi");
            string relativeReds = Path.Combine("r6", "scripts", "TestMod", "test.reds");
            string originalAsi = Path.Combine(game, relativeAsi);
            string virtualAsi = Path.Combine(mod, relativeAsi);
            string virtualReds = Path.Combine(mod, relativeReds);
            string capture = Path.Combine("red4ext", "plugins", "G-REDscript-Profiler",
                "RESULTS", "GCO-CI-capture.csv");
            Put(originalAsi, "ORIGINAL_CET_BINARY_UNCHANGED");
            Put(virtualAsi, "MO2_PROFILER_OVERRIDE");
            Put(virtualReds, "MO2_VIRTUAL_REDSCRIPT");
            Directory.CreateDirectory(overwrite);
            Directory.CreateDirectory(Path.Combine(game, "red4ext", "plugins",
                "G-REDscript-Profiler", "RESULTS"));

            if (!SetDllDirectory(Path.GetDirectoryName(dll)))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            library = NativeLibrary.Load(dll);
            var version = Export<Version>(library, "usvfsVersionString");
            string realVersion = Marshal.PtrToStringAnsi(version()) ?? "";
            if (string.IsNullOrWhiteSpace(realVersion))
                throw new InvalidOperationException("USVFS returned no real native version");
            Console.WriteLine("PASS loaded real MO2 USVFS native DLL: " + realVersion);

            parameters = Export<CreateParams>(library, "usvfsCreateParameters")();
            if (parameters == IntPtr.Zero) throw new InvalidOperationException("USVFS parameters unavailable");
            Export<SetInstance>(library, "usvfsSetInstanceName")(
                parameters, "gco_ci_" + Guid.NewGuid().ToString("N")[..16]);
            var create = Export<CreateVfs>(library, "usvfsCreateVFS");
            if (!create(parameters))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create real USVFS");
            connected = true;
            var link = Export<LinkDirectory>(library, "usvfsVirtualLinkDirectoryStatic");
            if (!link(mod, game, Recursive))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to mount temporary profiler mod");
            // Routes new writes without deleting unrelated Overwrite contents.
            if (!link(overwrite, game, Recursive | CreateTarget))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to mount MO2 Overwrite");

            var command = new StringBuilder('"' + Environment.ProcessPath! +
                "\" --child \"" + game + "\"");
            var startup = new StartupInfo { cb = (uint)Marshal.SizeOf<StartupInfo>() };
            var hooked = Export<CreateHooked>(library, "usvfsCreateProcessHooked");
            if (!hooked(null, command, IntPtr.Zero, IntPtr.Zero, false,
                    0, IntPtr.Zero, Path.GetDirectoryName(Environment.ProcessPath!),
                    ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Real USVFS could not launch hooked x64 child");
            try
            {
                uint wait = WaitForSingleObject(info.process, 30_000);
                if (wait != 0 || !GetExitCodeProcess(info.process, out uint code) || code != 0)
                    throw new InvalidOperationException(
                        "Hooked x64 process failed or timed out; wait=" + wait);
            }
            finally
            {
                CloseHandle(info.process);
                CloseHandle(info.thread);
            }
            if (File.ReadAllText(originalAsi) != "ORIGINAL_CET_BINARY_UNCHANGED" ||
                File.ReadAllText(virtualAsi) != "MO2_PROFILER_OVERRIDE")
                throw new InvalidDataException("Original physical game files were modified");

            string redirected = Path.Combine(overwrite, capture);
            string direct = Path.Combine(game, capture);
            if (!File.Exists(redirected))
                throw new InvalidDataException(
                    "MO2-generated native-style capture was not routed to Overwrite; " +
                    "direct=" + File.Exists(direct) + ". Do not assume collection paths.");
            if (File.Exists(direct) ||
                !File.ReadAllText(redirected).Contains("GRSP,real_usvfs_child", StringComparison.Ordinal))
                throw new InvalidDataException("USVFS captured output location/content not isolated");
            Console.WriteLine("PASS real USVFS x64: shadowed CET ASI and REDscript, " +
                "originals intact, generated data isolated to Overwrite");
        }
        finally
        {
            if (connected) Export<DisconnectVfs>(library, "usvfsDisconnectVFS")();
            if (parameters != IntPtr.Zero)
                Export<FreeParams>(library, "usvfsFreeParameters")(parameters);
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
            SetDllDirectory(null);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static void Put(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
