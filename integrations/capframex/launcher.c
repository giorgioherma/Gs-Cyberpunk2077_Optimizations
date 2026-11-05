#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wchar.h>

// Windows requires the CapFrameX parent to elevate before launching its
// requireAdministrator apphost, otherwise CreateProcess can return 740.
// This native helper is linked with /MANIFESTUAC:requireAdministrator;
// after UAC succeeds, it supplies the private .NET 10 Desktop runtime to
// its child. No global environment/registry changes or system install.
static int Fail(const wchar_t *reason, int code)
{
    MessageBoxW(NULL, reason, L"G-Total CapFrameX Private Runtime Test",
                MB_OK | MB_ICONERROR);
    return code;
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR args, int show)
{
    (void)instance; (void)previous; (void)args; (void)show;
    wchar_t module[MAX_PATH * 4] = {0};
    DWORD length = GetModuleFileNameW(NULL, module, ARRAYSIZE(module));
    if (length == 0 || length >= ARRAYSIZE(module))
        return Fail(L"Cannot locate the CapFrameX test launcher.", 1);

    wchar_t *slash = wcsrchr(module, L'\\');
    if (!slash) return Fail(L"Cannot locate the CapFrameX folder.", 1);
    *slash = 0;

    wchar_t relative[MAX_PATH * 4] = {0};
    if (swprintf_s(relative, ARRAYSIZE(relative),
                   L"%ls\\..\\..\\runtime", module) < 0)
        return Fail(L"Private runtime path is too long.", 1);
    wchar_t runtime[MAX_PATH * 4] = {0};
    if (!GetFullPathNameW(relative, ARRAYSIZE(runtime), runtime, NULL))
        return Fail(L"Cannot resolve the private runtime folder.", 1);
    wchar_t dotnet[MAX_PATH * 4] = {0};
    if (swprintf_s(dotnet, ARRAYSIZE(dotnet), L"%ls\\dotnet.exe", runtime) < 0 ||
        GetFileAttributesW(dotnet) == INVALID_FILE_ATTRIBUTES)
        return Fail(L"Bundled .NET 10 runtime missing. Extract the entire test ZIP.", 2);

    wchar_t capx[MAX_PATH * 4] = {0};
    if (swprintf_s(capx, ARRAYSIZE(capx), L"%ls\\CapFrameX.exe", module) < 0 ||
        GetFileAttributesW(capx) == INVALID_FILE_ATTRIBUTES)
        return Fail(L"CapFrameX.exe missing from its module folder.", 2);

    SetEnvironmentVariableW(L"DOTNET_ROOT_X64", runtime);
    SetEnvironmentVariableW(L"DOTNET_ROOT", runtime);
    SetEnvironmentVariableW(L"G_TOTAL_PRIVATE_DOTNET", runtime);
    SetEnvironmentVariableW(L"DOTNET_MULTILEVEL_LOOKUP", L"0");

    wchar_t logDir[MAX_PATH * 4] = {0};
    wchar_t logs[MAX_PATH * 4] = {0};
    wchar_t trace[MAX_PATH * 4] = {0};
    if (swprintf_s(logDir, ARRAYSIZE(logDir), L"%ls\\Portable", module) >= 0 &&
        swprintf_s(logs, ARRAYSIZE(logs), L"%ls\\Logs", logDir) >= 0 &&
        swprintf_s(trace, ARRAYSIZE(trace), L"%ls\\private-dotnet-host-trace.txt", logs) >= 0)
    {
        CreateDirectoryW(logDir, NULL);
        CreateDirectoryW(logs, NULL);
        SetEnvironmentVariableW(L"DOTNET_HOST_TRACE", L"1");
        SetEnvironmentVariableW(L"DOTNET_HOST_TRACEFILE", trace);
    }

    wchar_t command[MAX_PATH * 4] = {0};
    if (swprintf_s(command, ARRAYSIZE(command), L"\"%ls\"", capx) < 0)
        return Fail(L"CapFrameX launch command too long.", 2);
    STARTUPINFOW si = {0};
    PROCESS_INFORMATION child = {0};
    si.cb = sizeof(si);
    if (!CreateProcessW(capx, command, NULL, NULL, FALSE, 0, NULL,
                        module, &si, &child))
        return Fail(L"Failed to start CapFrameX using the private runtime.", 3);
    CloseHandle(child.hThread);
    CloseHandle(child.hProcess);
    return 0;
}
