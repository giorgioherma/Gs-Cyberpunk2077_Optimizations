// G-Total-Optimizer integration build of the CapFrameX 1.9.1.2 beta MIT source.
// Original source: CXWorld/CapFrameX, tag v1.9.1_beta, commit d1ab1e7e87a989ce8d55f38d5769327bb9f89369.
// Only the managed runtime/VC++ availability check changes. We do not modify native
// capture logic, hooks, measurement logic or existing installed-mode behavior.
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CapFrameX.Configuration
{
    public class DependencyCheckReport
    {
        public bool Valid { get; set; }
        public string MissingDotNetFrameworkVersion { get; set; }
        public List<string> MissingVCRedistVersions { get; set; }
    }

    public static class AppDependencyChecker
    {
        public const int MajorDotNetVersionRequired = DotNetRuntimeDetector.RequiredMajorVersion;
        private const string VCRedistx64RegistryKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

        // A private runtime is accepted only when explicitly selected by the
        // G-Total native launcher and when BOTH .NET 10 core + desktop layouts
        // are present. If there is no private selection, retain vendor behavior.
        private static string DotNetBasePath
        {
            get
            {
                var chosen = Environment.GetEnvironmentVariable("G_TOTAL_PRIVATE_DOTNET");
                if (!string.IsNullOrWhiteSpace(chosen))
                {
                    try
                    {
                        var path = Path.GetFullPath(chosen);
                        if (DotNetRuntimeDetector.IsRequiredDesktopRuntimeInstalled(path))
                            return path;
                    }
                    catch (Exception) { /* Existing machine runtime remains the fallback. */ }
                }
                return DotNetRuntimeDetector.GetSystemWideDotNetBasePath();
            }
        }

        public static DependencyCheckReport CheckAndNotifyMissingDependencies()
        {
            return CheckMissingDependencies(DotNetBasePath, IsVCRedistx64Installed());
        }

        internal static DependencyCheckReport CheckMissingDependencies(
            string dotNetBasePath,
            bool isVCRedistx64Installed)
        {
            var report = new DependencyCheckReport { Valid = true };
            if (!IsRequiredDotNetInstalled(dotNetBasePath))
            {
                report.Valid = false;
                report.MissingDotNetFrameworkVersion = $"{MajorDotNetVersionRequired}.0 (x64)";
            }

            var missingVCRedist = new List<string>();
            if (!isVCRedistx64Installed)
                missingVCRedist.Add("2015-2022 (x64)");
            if (missingVCRedist.Count > 0)
            {
                report.Valid = false;
                report.MissingVCRedistVersions = missingVCRedist;
            }
            return report;
        }

        internal static bool IsRequiredDotNetInstalled(string dotNetBasePath) =>
            DotNetRuntimeDetector.IsRequiredDesktopRuntimeInstalled(dotNetBasePath);

        private static DotNetComponents GetInstalledRequiredDotNetComponents() =>
            GetInstalledRequiredDotNetComponents(DotNetBasePath);

        internal static DotNetComponents GetInstalledRequiredDotNetComponents(string dotNetBasePath) =>
            DotNetRuntimeDetector.GetInstalledRequiredComponents(dotNetBasePath);

        public static string GetInstalledComponentsDescription()
        {
            var components = GetInstalledRequiredDotNetComponents();
            if (components == DotNetComponents.None)
                return $".NET {MajorDotNetVersionRequired} ist nicht installiert.";
            var installed = new List<string>();
            if (components.HasFlag(DotNetComponents.Runtime)) installed.Add("Runtime");
            if (components.HasFlag(DotNetComponents.DesktopRuntime)) installed.Add("Desktop Runtime");
            if (components.HasFlag(DotNetComponents.Sdk)) installed.Add("SDK");
            return $".NET {MajorDotNetVersionRequired} installiert: {string.Join(", ", installed)}";
        }

        private static bool IsVCRedistx64Installed()
        {
            // The app-local MSVC CRT DLLs are taken unmodified from the Visual
            // Studio redistributable payload, never from the user's system32.
            // This path is used only when the unified private runtime is selected.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("G_TOTAL_PRIVATE_DOTNET")))
            {
                var dir = AppContext.BaseDirectory;
                if (File.Exists(Path.Combine(dir, "vcruntime140.dll")) &&
                    File.Exists(Path.Combine(dir, "vcruntime140_1.dll")) &&
                    File.Exists(Path.Combine(dir, "msvcp140.dll")))
                    return true;
            }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(VCRedistx64RegistryKey))
                {
                    if (key != null)
                    {
                        var installed = key.GetValue("Installed");
                        if (installed != null && Convert.ToInt32(installed) == 1)
                            return true;
                    }
                }
            }
            catch (Exception) { /* System lookup failed; report missing dependency. */ }
            return false;
        }
    }
}
