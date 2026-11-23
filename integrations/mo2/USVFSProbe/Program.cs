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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void InitLogging([MarshalAs(UnmanagedType.I1)] bool toLocal);
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
            string mod = Path.Combine(folder, "gco-profiler-filemapper-overlay");
            string profile = Path.Combine(folder, "active-profile-mod");
            string overwrite = Path.Combine(folder, "overwrite");
            string relativeAsi = Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks.asi");
            string relativeReds = Path.Combine("r6", "scripts", "TestMod", "test.reds");
            string originalAsi = Path.Combine(game, relativeAsi);
            string virtualAsi = Path.Combine(mod, relativeAsi);
            string virtualReds = Path.Combine(mod, relativeReds);
            string capture = Path.Combine("red4ext", "plugins", "G-REDscript-Profiler",
                "RESULTS", "GCO-CI-capture.csv");
            Put(originalAsi, "ORIGINAL_CET_BINARY_UNCHANGED");
            Put(Path.Combine(profile, relativeAsi), "PROFILE_SELECTED_CET_ASI");
            Put(Path.Combine(profile, relativeReds), "PROFILE_SELECTED_REDSCRIPT");
            Put(Path.Combine(overwrite, relativeAsi), "USER_OVERWRITE_ASI_UNCHANGED");
            Put(virtualAsi, "MO2_PROFILER_OVERRIDE");
            Put(virtualReds, "MO2_VIRTUAL_REDSCRIPT");
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

            // Match the order used by USVFS's own native test fixture.
            Export<InitLogging>(library, "usvfsInitLogging")(false);
            parameters = Export<CreateParams>(library, "usvfsCreateParameters")();
            if (parameters == IntPtr.Zero) throw new InvalidOperationException("USVFS parameters unavailable");
            Export<SetInstance>(library, "usvfsSetInstanceName")(
                parameters, "gco_ci_" + Guid.NewGuid().ToString("N")[..16]);
            var create = Export<CreateVfs>(library, "usvfsCreateVFS");
            if (!create(parameters))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create real USVFS");
            connected = true;
            var link = Export<LinkDirectory>(library, "usvfsVirtualLinkDirectoryStatic");
            // Reproduce OrganizerCore::fileMapping(profileName, customOverwrite):
            // selected-profile mods, configured Overwrite, then the plugin's
            // one-shot IPluginFileMapper entries. Other profiles are untouched.
            if (!link(profile, game, Recursive))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to mount selected-profile mod");
            if (!link(overwrite, game, Recursive | CreateTarget))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to mount MO2 Overwrite");
            if (!link(mod, game, Recursive))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to mount GCO last-priority file mapper");

            // A native Windows x64 child is a better stand-in for the game
            // loader than the managed .NET apphost (which faulted during hook
            // initialization on the first probe). Assert actual file bytes.
            string obsAsi = Path.Combine(folder, "observed-asi.txt");
            string obsReds = Path.Combine(folder, "observed-reds.txt");
            string captureTarget = Path.Combine(game, capture);
            string batch = Path.Combine(folder, "usvfs-child.cmd");
            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("type \"" + originalAsi + "\" > \"" + obsAsi + "\"");
            script.AppendLine("if errorlevel 1 exit /b 11");
            script.AppendLine("type \"" + Path.Combine(game, relativeReds) + "\" > \"" + obsReds + "\"");
            script.AppendLine("if errorlevel 1 exit /b 12");
            script.AppendLine("echo owner,result > \"" + captureTarget + "\"");
            script.AppendLine("if errorlevel 1 exit /b 13");
            script.AppendLine("echo GRSP,real_usvfs_child >> \"" + captureTarget + "\"");
            script.AppendLine("if errorlevel 1 exit /b 14");
            File.WriteAllText(batch, script.ToString(), Encoding.ASCII);

            string commandInterpreter = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            if (!File.Exists(commandInterpreter))
                throw new FileNotFoundException("Native Windows command interpreter missing");
            var command = new StringBuilder("\"" + commandInterpreter +
                "\" /D /C \"" + batch + "\"");
            var startup = new StartupInfo { cb = (uint)Marshal.SizeOf<StartupInfo>() };
            var hooked = Export<CreateHooked>(library, "usvfsCreateProcessHooked");
            if (!hooked(commandInterpreter, command, IntPtr.Zero, IntPtr.Zero, false,
                    0, IntPtr.Zero, folder, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Real USVFS could not launch hooked x64 native child");
            try
            {
                uint wait = WaitForSingleObject(info.process, 30_000);
                bool gotExit = GetExitCodeProcess(info.process, out uint code);
                Console.WriteLine("USVFS hooked native observations: asi=" +
                    (File.Exists(obsAsi) ? File.ReadAllText(obsAsi).Trim() : "(missing)") +
                    " reds=" + (File.Exists(obsReds) ? File.ReadAllText(obsReds).Trim() : "(missing)"));
                if (wait != 0 || !gotExit || code != 0)
                    throw new InvalidOperationException(
                        "Hooked native x64 child failed: wait=" + wait +
                        " exit=0x" + code.ToString("X8") + " readExitCode=" + gotExit);
            }
            finally
            {
                CloseHandle(info.process);
                CloseHandle(info.thread);
            }
            if (!File.Exists(obsAsi) ||
                File.ReadAllText(obsAsi).Trim() != "MO2_PROFILER_OVERRIDE" ||
                !File.Exists(obsReds) ||
                File.ReadAllText(obsReds).Trim() != "MO2_VIRTUAL_REDSCRIPT")
                throw new InvalidDataException(
                    "Hooked native process did not observe the selected-profile file overrides");
            if (File.ReadAllText(originalAsi) != "ORIGINAL_CET_BINARY_UNCHANGED" ||
                File.ReadAllText(virtualAsi) != "MO2_PROFILER_OVERRIDE" ||
                File.ReadAllText(Path.Combine(profile, relativeAsi)) != "PROFILE_SELECTED_CET_ASI" ||
                File.ReadAllText(Path.Combine(overwrite, relativeAsi)) != "USER_OVERWRITE_ASI_UNCHANGED")
                throw new InvalidDataException("Original physical game/profile/Overwrite inputs were modified");

            string redirected = Path.Combine(overwrite, capture);
            string direct = Path.Combine(game, capture);
            if (!File.Exists(redirected))
                throw new InvalidDataException(
                    "MO2-generated native-style capture was not routed to Overwrite; " +
                    "direct=" + File.Exists(direct) + ". Do not assume collection paths.");
            if (File.Exists(direct) ||
                !File.ReadAllText(redirected).Contains("GRSP,real_usvfs_child", StringComparison.Ordinal))
                throw new InvalidDataException("USVFS captured output location/content not isolated");
            Console.WriteLine("PASS real USVFS x64: profile -> Overwrite -> GCO mapper precedence, " +
                "CET ASI and REDscript overrides, user bytes intact, outputs to Overwrite");

            // Same running MO2/USVFS controller, different profile mapping.
            // GCO's one-shot mapper is NOT appended for this unrelated launch.
            // No profile-specific files are ever edited by this test.
            string profileB = Path.Combine(folder, "unselected-profile-B");
            Put(Path.Combine(profileB, relativeAsi), "PROFILE_B_CET_ASI");
            Put(Path.Combine(profileB, relativeReds), "PROFILE_B_REDSCRIPT");
            Export<ClearMappings>(library, "usvfsClearVirtualMappings")();
            if (!link(profileB, game, Recursive) ||
                !link(overwrite, game, Recursive | CreateTarget))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to construct unrelated profile VFS");

            string bAsi = Path.Combine(folder, "profile-b-asi.txt");
            string bReds = Path.Combine(folder, "profile-b-reds.txt");
            string bCmd = Path.Combine(folder, "profile-b-child.cmd");
            var scriptB = new StringBuilder();
            scriptB.AppendLine("@echo off");
            scriptB.AppendLine("type \"" + originalAsi + "\" > \"" + bAsi + "\"");
            scriptB.AppendLine("if errorlevel 1 exit /b 21");
            scriptB.AppendLine("type \"" + Path.Combine(game, relativeReds) +
                "\" > \"" + bReds + "\"");
            scriptB.AppendLine("if errorlevel 1 exit /b 22");
            File.WriteAllText(bCmd, scriptB.ToString(), Encoding.ASCII);
            var commandB = new StringBuilder("\"" + commandInterpreter +
                "\" /D /C \"" + bCmd + "\"");
            if (!hooked(commandInterpreter, commandB, IntPtr.Zero, IntPtr.Zero, false,
                    0, IntPtr.Zero, folder, ref startup, out var infoB))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to launch unrelated profile B");
            try
            {
                uint waitB = WaitForSingleObject(infoB.process, 30_000);
                bool haveExitB = GetExitCodeProcess(infoB.process, out uint exitB);
                if (waitB != 0 || !haveExitB || exitB != 0)
                    throw new InvalidOperationException("Profile B child failed: exit=0x" +
                        exitB.ToString("X8") + " wait=" + waitB);
            }
            finally
            {
                CloseHandle(infoB.process);
                CloseHandle(infoB.thread);
            }
            string fromBAsi = File.Exists(bAsi) ? File.ReadAllText(bAsi).Trim() : "";
            string fromBReds = File.Exists(bReds) ? File.ReadAllText(bReds).Trim() : "";
            if (fromBAsi != "USER_OVERWRITE_ASI_UNCHANGED" ||
                fromBReds != "PROFILE_B_REDSCRIPT" ||
                File.ReadAllText(Path.Combine(profileB, relativeAsi)) != "PROFILE_B_CET_ASI")
                throw new InvalidDataException(
                    "Profile B saw GCO profiler overlay or its own source/Overwrite was modified: " +
                    "ASI=" + fromBAsi + " RED=" + fromBReds);
            Console.WriteLine("PASS real USVFS x64: unrelated profile B sees its own REDscript + " +
                "user Overwrite, NOT the temporary GCO profiler overlay");
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
