#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wchar.h>

// Public package root launcher. No CLR loaded here, no second runtime.
// The executable and internal app must remain together in the extracted tree.
int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, PWSTR commandLine, int show)
{
    (void)inst; (void)prev; (void)show;
    wchar_t root[MAX_PATH * 4] = {0};
    DWORD len = GetModuleFileNameW(NULL, root, ARRAYSIZE(root));
    if (!len || len >= ARRAYSIZE(root))
        return 1;

    wchar_t *slash = wcsrchr(root, L'\\');
    if (!slash) return 1;
    *slash = 0;

    // Prefer the bundle's single private .NET 10 Desktop Runtime when it is
    // present. This changes only the environment of this launcher's child
    // process, not the user's Windows or system-wide .NET installation.
    // Legacy self-contained shell packages continue to run unchanged.
    wchar_t dotnetRoot[MAX_PATH * 4] = {0};
    wchar_t dotnetExe[MAX_PATH * 4] = {0};
    if (wcscpy_s(dotnetRoot, ARRAYSIZE(dotnetRoot), root) == 0 &&
        wcscat_s(dotnetRoot, ARRAYSIZE(dotnetRoot), L"\\runtime") == 0 &&
        wcscpy_s(dotnetExe, ARRAYSIZE(dotnetExe), dotnetRoot) == 0 &&
        wcscat_s(dotnetExe, ARRAYSIZE(dotnetExe), L"\\dotnet.exe") == 0)
    {
        DWORD rtAttrs = GetFileAttributesW(dotnetExe);
        if (rtAttrs != INVALID_FILE_ATTRIBUTES && !(rtAttrs & FILE_ATTRIBUTE_DIRECTORY))
        {
            SetEnvironmentVariableW(L"DOTNET_ROOT_X64", dotnetRoot);
            SetEnvironmentVariableW(L"DOTNET_ROOT", dotnetRoot);
            SetEnvironmentVariableW(L"G_TOTAL_PRIVATE_DOTNET", dotnetRoot);
            SetEnvironmentVariableW(L"DOTNET_MULTILEVEL_LOOKUP", L"0");
        }
    }

    wchar_t exe[MAX_PATH * 4] = {0};
    if (wcscpy_s(exe, ARRAYSIZE(exe), root) != 0 ||
        wcscat_s(exe, ARRAYSIZE(exe), L"\\app\\G-Total-Optimizer.App.exe") != 0)
        return 1;

    DWORD attrs = GetFileAttributesW(exe);
    if (attrs == INVALID_FILE_ATTRIBUTES || (attrs & FILE_ATTRIBUTE_DIRECTORY))
    {
        MessageBoxW(NULL,
            L"Application files are missing. Extract the entire package together.\r\n"
            L"Expected: app\\G-Total-Optimizer.App.exe",
            L"G's Cyberpunk 2077 Total Optimizer", MB_OK | MB_ICONERROR);
        return 2;
    }

    wchar_t cmd[MAX_PATH * 4 + 16] = {0};
    // Forward intentional headless control arguments to the same managed app.
    // Crucially, the public launcher also supplies the private .NET 10 runtime
    // to elevated child processes without assuming a system .NET install.
    int n = commandLine && *commandLine
        ? swprintf_s(cmd, ARRAYSIZE(cmd), L"\"%ls\" %ls", exe, commandLine)
        : swprintf_s(cmd, ARRAYSIZE(cmd), L"\"%ls\"", exe);
    if (n < 0)
        return 2;

    STARTUPINFOW start = {0};
    PROCESS_INFORMATION child = {0};
    start.cb = sizeof(start);
    // The app resolves components and outputs against root, not the CWD.
    if (!CreateProcessW(exe, cmd, NULL, NULL, FALSE, 0, NULL, root,
                        &start, &child))
    {
        MessageBoxW(NULL, L"Could not launch the internal application.",
                    L"G's Cyberpunk 2077 Total Optimizer", MB_OK | MB_ICONERROR);
        return 3;
    }
    CloseHandle(child.hThread);
    // Normal GUI launch is detached. Headless verification and elevated
    // PresentMon control must propagate the private-runtime child's exit code
    // so the parent can refuse an unverified/failed operation.
    if (commandLine && *commandLine)
    {
        DWORD wait = WaitForSingleObject(child.hProcess, 40000);
        if (wait != WAIT_OBJECT_0)
        {
            CloseHandle(child.hProcess);
            return 4;
        }
        DWORD code = 0;
        if (!GetExitCodeProcess(child.hProcess, &code))
        {
            CloseHandle(child.hProcess);
            return 5;
        }
        CloseHandle(child.hProcess);
        return (int)code;
    }
    CloseHandle(child.hProcess);
    return 0;
}
