#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <tlhelp32.h>
#include <stdint.h>
#include <wchar.h>
#include <wctype.h>
#include <stdio.h>
#include <string.h>

// Native Win32 tray host only. The WPF/.NET 10 child may exit completely;
// this host retains no CLR, report cache, UI resources or capture file handles.
#define TRAY_EVENT (WM_APP + 17)
#define TRAY_OPEN  1001
#define TRAY_EXIT  1002
#define TRAY_TIMER 1
#define TRAY_STOP_DONE (WM_APP + 18)

static HINSTANCE g_inst;
static HWND g_window;
static HANDLE g_ui;
static wchar_t g_root[MAX_PATH * 4];
static wchar_t g_app[MAX_PATH * 4];
static wchar_t g_launcher[MAX_PATH * 4];
static wchar_t g_title[160];
static NOTIFYICONDATAW g_icon;
static UINT g_taskbar_created;
static volatile LONG g_stop_started;

static BOOL IsRunning(const wchar_t *name)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return FALSE;
    PROCESSENTRY32W entry = {0};
    entry.dwSize = sizeof(entry);
    BOOL found = FALSE;
    if (Process32FirstW(snap, &entry)) {
        do {
            if (_wcsicmp(entry.szExeFile, name) == 0) { found = TRUE; break; }
        } while (Process32NextW(snap, &entry));
    }
    CloseHandle(snap);
    return found;
}

static BOOL GuiRunning(void)
{
    if (!g_ui) return FALSE;
    if (WaitForSingleObject(g_ui, 0) == WAIT_TIMEOUT) return TRUE;
    CloseHandle(g_ui);
    g_ui = NULL;
    return FALSE;
}

typedef struct { DWORD pid; HWND hwnd; } GuiWindow;
static BOOL CALLBACK FindGuiWindow(HWND hwnd, LPARAM raw)
{
    GuiWindow *data = (GuiWindow *)raw;
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid == data->pid && GetWindow(hwnd, GW_OWNER) == NULL && IsWindowVisible(hwnd)) {
        data->hwnd = hwnd;
        return FALSE;
    }
    return TRUE;
}

static BOOL LaunchOrShowGui(void)
{
    // Any newly opened GUI can start a fresh game run. The prior worker has
    // already posted completion by this point; do not carry a spent stop
    // state across sessions when no timer event fires before GUI closes.
    InterlockedCompareExchange(&g_stop_started, 0, 2);
    if (GuiRunning()) {
        GuiWindow data = { GetProcessId(g_ui), NULL };
        EnumWindows(FindGuiWindow, (LPARAM)&data);
        if (data.hwnd) {
            ShowWindow(data.hwnd, SW_RESTORE);
            SetForegroundWindow(data.hwnd);
        }
        return TRUE;
    }
    wchar_t command[MAX_PATH * 4 + 16];
    if (swprintf_s(command, ARRAYSIZE(command), L"\"%ls\"", g_app) < 0) return FALSE;
    STARTUPINFOW start = {0};
    PROCESS_INFORMATION child = {0};
    start.cb = sizeof(start);
    if (!CreateProcessW(g_app, command, NULL, NULL, FALSE, 0, NULL, g_root, &start, &child))
        return FALSE;
    CloseHandle(child.hThread);
    g_ui = child.hProcess;
    return TRUE;
}

// The tracked CSV is an internal pointer created by GCO before capture.
// Revalidate that it stays inside this exact package's RESULTS subtree;
// the elevated managed stop helper independently checks all ownership.
static BOOL TrackedCsv(wchar_t *out, size_t outSize)
{
    wchar_t pointer[MAX_PATH * 4] = {0};
    if (swprintf_s(pointer, ARRAYSIZE(pointer), L"%ls\\RESULTS\\GCO-current-PresentMon.txt", g_root) < 0)
        return FALSE;
    HANDLE file = CreateFileW(pointer, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return FALSE;
    char utf8[MAX_PATH * 4] = {0};
    DWORD count = 0;
    BOOL ok = ReadFile(file, utf8, sizeof(utf8) - 1, &count, NULL);
    CloseHandle(file);
    if (!ok || count == 0 || count >= sizeof(utf8) - 1) return FALSE;
    unsigned char *begin = (unsigned char *)utf8;
    if (count >= 3 && begin[0] == 0xEF && begin[1] == 0xBB && begin[2] == 0xBF) {
        memmove(utf8, utf8 + 3, count - 3);
        count -= 3;
    }
    while (count > 0 && (utf8[count - 1] == '\r' || utf8[count - 1] == '\n' ||
                         utf8[count - 1] == ' ')) --count;
    if (count == 0) return FALSE;
    int converted = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8, (int)count,
                                        out, (int)outSize - 1);
    if (converted <= 0) return FALSE;
    out[converted] = 0;
    wchar_t prefix[MAX_PATH * 4] = {0};
    if (swprintf_s(prefix, ARRAYSIZE(prefix), L"%ls\\RESULTS\\", g_root) < 0)
        return FALSE;
    return _wcsnicmp(out, prefix, wcslen(prefix)) == 0;
}

static DWORD WINAPI StopOwnedCapture(LPVOID ignored)
{
    (void)ignored;
    wchar_t csv[MAX_PATH * 4] = {0};
    DWORD result = 1;
    if (TrackedCsv(csv, ARRAYSIZE(csv))) {
        wchar_t args[MAX_PATH * 4 + 96];
        if (swprintf_s(args, ARRAYSIZE(args),
                       L"--presentmon-control stop \"%ls\"", csv) >= 0) {
            SHELLEXECUTEINFOW launch = {0};
            launch.cbSize = sizeof(launch);
            launch.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
            launch.lpVerb = L"runas";
            launch.lpFile = g_launcher;
            launch.lpParameters = args;
            launch.lpDirectory = g_root;
            launch.nShow = SW_HIDE;
            if (ShellExecuteExW(&launch) && launch.hProcess) {
                if (WaitForSingleObject(launch.hProcess, 65000) == WAIT_OBJECT_0) {
                    DWORD code = 1;
                    if (GetExitCodeProcess(launch.hProcess, &code)) result = code;
                }
                CloseHandle(launch.hProcess);
            }
        }
    }
    PostMessageW(g_window, TRAY_STOP_DONE, (WPARAM)result, 0);
    return result;
}

static void AddTrayIcon(void)
{
    ZeroMemory(&g_icon, sizeof(g_icon));
    g_icon.cbSize = sizeof(g_icon);
    g_icon.hWnd = g_window;
    g_icon.uID = 1;
    g_icon.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    g_icon.uCallbackMessage = TRAY_EVENT;
    g_icon.hIcon = LoadIconW(g_inst, MAKEINTRESOURCEW(1));
    wcscpy_s(g_icon.szTip, ARRAYSIZE(g_icon.szTip), L"G's Cyberpunk 2077 Optimizer");
    Shell_NotifyIconW(NIM_ADD, &g_icon);
}

static void TrayMenu(void)
{
    HMENU menu = CreatePopupMenu();
    if (!menu) return;
    AppendMenuW(menu, MF_STRING, TRAY_OPEN, L"Open GCO");
    AppendMenuW(menu, MF_STRING, TRAY_EXIT, L"Exit tray");
    POINT point;
    GetCursorPos(&point);
    SetForegroundWindow(g_window);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON, point.x, point.y, 0, g_window, NULL);
    DestroyMenu(menu);
    PostMessageW(g_window, WM_NULL, 0, 0);
}

static LRESULT CALLBACK TrayProc(HWND window, UINT message, WPARAM wp, LPARAM lp)
{
    if (message == g_taskbar_created) { AddTrayIcon(); return 0; }
    switch (message) {
        case TRAY_EVENT:
            if (lp == WM_LBUTTONUP || lp == WM_LBUTTONDBLCLK) {
                if (!LaunchOrShowGui())
                    MessageBoxW(window, L"Could not reopen the GCO interface.", L"GCO", MB_ICONERROR);
            } else if (lp == WM_RBUTTONUP) TrayMenu();
            return 0;
        case WM_COMMAND:
            if (LOWORD(wp) == TRAY_OPEN) {
                if (!LaunchOrShowGui())
                    MessageBoxW(window, L"Could not reopen the GCO interface.", L"GCO", MB_ICONERROR);
            } else if (LOWORD(wp) == TRAY_EXIT) {
                wchar_t csv[MAX_PATH * 4];
                if (TrackedCsv(csv, ARRAYSIZE(csv)) && IsRunning(L"PresentMon.exe")) {
                    MessageBoxW(window, L"PresentMon is still recording. Reopen GCO to collect or restore first.",
                                L"GCO capture active", MB_OK | MB_ICONINFORMATION);
                } else DestroyWindow(window);
            }
            return 0;
        case WM_TIMER:
            if (wp == TRAY_TIMER && GuiRunning()) {
                // GCO may begin another capture after collecting this one.
                // Re-arm only after the previous stop worker has exited.
                InterlockedCompareExchange(&g_stop_started, 0, 2);
                return 0;
            }
            if (wp == TRAY_TIMER &&
                InterlockedCompareExchange(&g_stop_started, 0, 0) == 0) {
                wchar_t csv[MAX_PATH * 4];
                // No CLR and no busy spin during gameplay. After game exit,
                // gracefully flush the existing GCO-owned ETW session.
                if (!IsRunning(L"Cyberpunk2077.exe") &&
                    TrackedCsv(csv, ARRAYSIZE(csv)) &&
                    IsRunning(L"PresentMon.exe")) {
                    InterlockedExchange(&g_stop_started, 1);
                    HANDLE worker = CreateThread(NULL, 0, StopOwnedCapture, NULL, 0, NULL);
                    if (worker) CloseHandle(worker);
                    else InterlockedExchange(&g_stop_started, 0);
                }
            }
            return 0;
        case TRAY_STOP_DONE:
            // A failed/cancelled UAC stop never deletes data or invents a
            // successful capture. GCO will retry when reopened.
            InterlockedExchange(&g_stop_started, 2);
            return 0;
        case WM_DESTROY:
            KillTimer(window, TRAY_TIMER);
            Shell_NotifyIconW(NIM_DELETE, &g_icon);
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProcW(window, message, wp, lp);
}

static uint64_t RootHash(const wchar_t *root)
{
    uint64_t value = UINT64_C(14695981039346656037);
    while (*root) {
        value ^= (uint64_t)towlower(*root++);
        value *= UINT64_C(1099511628211);
    }
    return value;
}

// Root public launcher. No CLR is loaded in this process, even when the tray
// remains resident. CLI args retain the original awaited child/exit semantics.
int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, PWSTR commandLine, int show)
{
    (void)prev; (void)show;
    g_inst = inst;
    DWORD size = GetModuleFileNameW(NULL, g_launcher, ARRAYSIZE(g_launcher));
    if (!size || size >= ARRAYSIZE(g_launcher)) return 1;
    wcscpy_s(g_root, ARRAYSIZE(g_root), g_launcher);
    wchar_t *slash = wcsrchr(g_root, L'\\');
    if (!slash) return 1;
    *slash = 0;

    wchar_t dotnetRoot[MAX_PATH * 4] = {0};
    wchar_t dotnetExe[MAX_PATH * 4] = {0};
    if (wcscpy_s(dotnetRoot, ARRAYSIZE(dotnetRoot), g_root) == 0 &&
        wcscat_s(dotnetRoot, ARRAYSIZE(dotnetRoot), L"\\runtime") == 0 &&
        wcscpy_s(dotnetExe, ARRAYSIZE(dotnetExe), dotnetRoot) == 0 &&
        wcscat_s(dotnetExe, ARRAYSIZE(dotnetExe), L"\\dotnet.exe") == 0) {
        DWORD attrs = GetFileAttributesW(dotnetExe);
        if (attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY)) {
            SetEnvironmentVariableW(L"DOTNET_ROOT_X64", dotnetRoot);
            SetEnvironmentVariableW(L"DOTNET_ROOT", dotnetRoot);
            SetEnvironmentVariableW(L"G_TOTAL_PRIVATE_DOTNET", dotnetRoot);
            SetEnvironmentVariableW(L"DOTNET_MULTILEVEL_LOOKUP", L"0");
        }
    }

    if (swprintf_s(g_app, ARRAYSIZE(g_app),
                   L"%ls\\app\\G-Total-Optimizer.App.exe", g_root) < 0) return 1;
    DWORD appAttrs = GetFileAttributesW(g_app);
    if (appAttrs == INVALID_FILE_ATTRIBUTES || (appAttrs & FILE_ATTRIBUTE_DIRECTORY)) {
        MessageBoxW(NULL, L"Application files missing. Extract the entire package together.",
                    L"GCO", MB_OK | MB_ICONERROR);
        return 2;
    }

    if (commandLine && wcscmp(commandLine, L"--tray-exit") == 0) {
        // Headless native control path. Reuse the exact package-bound
        // window name and never initialize the CLR to close an idle tray.
        wchar_t name[80];
        swprintf_s(name, ARRAYSIZE(name),
                   L"Local\\GCO_Tray_%016llX", (unsigned long long)RootHash(g_root));
        HWND owner = FindWindowW(L"GCO.Native.Tray.Window", name);
        if (!owner) return 6;
        return PostMessageW(owner, WM_COMMAND, TRAY_EXIT, 0) ? 0 : 7;
    }

    if (commandLine && *commandLine) {
        wchar_t cmd[MAX_PATH * 4 + 16] = {0};
        if (swprintf_s(cmd, ARRAYSIZE(cmd), L"\"%ls\" %ls", g_app, commandLine) < 0)
            return 2;
        STARTUPINFOW start = {0};
        PROCESS_INFORMATION child = {0};
        start.cb = sizeof(start);
        if (!CreateProcessW(g_app, cmd, NULL, NULL, FALSE, 0, NULL, g_root, &start, &child))
            return 3;
        CloseHandle(child.hThread);
        if (WaitForSingleObject(child.hProcess, 40000) != WAIT_OBJECT_0) {
            CloseHandle(child.hProcess);
            return 4;
        }
        DWORD exitCode = 0;
        BOOL ok = GetExitCodeProcess(child.hProcess, &exitCode);
        CloseHandle(child.hProcess);
        return ok ? (int)exitCode : 5;
    }

    wchar_t mutexName[80];
    swprintf_s(mutexName, ARRAYSIZE(mutexName),
               L"Local\\GCO_Tray_%016llX", (unsigned long long)RootHash(g_root));
    HANDLE single = CreateMutexW(NULL, TRUE, mutexName);
    if (!single) return 6;
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        for (int retry = 0; retry < 12; ++retry) {
            HWND prior = FindWindowW(L"GCO.Native.Tray.Window", mutexName);
            if (prior) { PostMessageW(prior, WM_COMMAND, TRAY_OPEN, 0); break; }
            Sleep(120);
        }
        CloseHandle(single);
        return 0;
    }
    wcscpy_s(g_title, ARRAYSIZE(g_title), mutexName);
    g_taskbar_created = RegisterWindowMessageW(L"TaskbarCreated");
    WNDCLASSEXW cls = {0};
    cls.cbSize = sizeof(cls);
    cls.hInstance = inst;
    cls.lpfnWndProc = TrayProc;
    cls.lpszClassName = L"GCO.Native.Tray.Window";
    if (!RegisterClassExW(&cls)) { CloseHandle(single); return 7; }
    g_window = CreateWindowExW(0, cls.lpszClassName, g_title, WS_OVERLAPPED,
                             0, 0, 0, 0, NULL, NULL, inst, NULL);
    if (!g_window) { CloseHandle(single); return 8; }
    AddTrayIcon();
    SetTimer(g_window, TRAY_TIMER, 5000, NULL);
    if (!LaunchOrShowGui()) {
        DestroyWindow(g_window);
        CloseHandle(single);
        return 9;
    }
    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    if (g_ui) CloseHandle(g_ui);
    ReleaseMutex(single);
    CloseHandle(single);
    return (int)msg.wParam;
}
