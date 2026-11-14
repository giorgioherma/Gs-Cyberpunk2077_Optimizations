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
#include <sddl.h>
#include <stdlib.h>

// Native Win32 tray host only. The WPF/.NET 10 child may exit completely;
// this host retains no CLR, report cache, UI resources or capture file handles.
#define TRAY_EVENT (WM_APP + 17)
#define TRAY_OPEN  1001
#define TRAY_EXIT  1002
#define TRAY_UI_CLOSED 1003

static HINSTANCE g_inst;
static HWND g_window;
static HANDLE g_ui;
static wchar_t g_root[MAX_PATH * 4];
static wchar_t g_app[MAX_PATH * 4];
static wchar_t g_launcher[MAX_PATH * 4];
static wchar_t g_title[160];
static NOTIFYICONDATAW g_icon;
static UINT g_taskbar_created;

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


// The initial elevated GCO START launches this small, native observer with
// the same privilege. It owns only the exact bundled PresentMon PID, never
// other ETW sessions, and remains resident while WPF is unloaded to tray.
// A per-session event lets non-elevated GCO request a graceful stop without
// any second UAC elevation (including a game that never actually starts).
static BOOL IsCaptureId(const wchar_t *id)
{
    if (!id || wcslen(id) != 32) return FALSE;
    for (size_t i = 0; i < 32; ++i)
        if (!iswxdigit(id[i])) return FALSE;
    return TRUE;
}

static BOOL ObserverEventName(const wchar_t *id, wchar_t *out, size_t count)
{
    return IsCaptureId(id) &&
           swprintf_s(out, count, L"Local\\GCO_PM_Stop_%ls", id) >= 0;
}

// Temporary capture troubleshooting: short, append-only event evidence.
// No output from PresentMon itself is intercepted; its real console stays live.
// Each attempt gets its own root RESULTS log so it survives failed CSV creation.
static void LogObserver(const wchar_t *id, const char *event, DWORD pid, DWORD value)
{
    wchar_t path[MAX_PATH * 4];
    if (swprintf_s(path, ARRAYSIZE(path),
                   L"%ls\\RESULTS\\GCO-PresentMon-observer-%ls.log", g_root, id) < 0)
        return;
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME now;
    GetLocalTime(&now);
    char line[256];
    int n = sprintf_s(line, sizeof(line),
                      "%04u-%02u-%02u %02u:%02u:%02u.%03u %s pid=%lu value=%lu\r\n",
                      now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute,
                      now.wSecond, now.wMilliseconds, event,
                      (unsigned long)pid, (unsigned long)value);
    if (n > 0) {
        DWORD written = 0;
        WriteFile(file, line, (DWORD)n, &written, NULL);
    }
    CloseHandle(file);
}

static HANDLE FindGameHandle(void)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return NULL;
    PROCESSENTRY32W entry = {0};
    entry.dwSize = sizeof(entry);
    HANDLE game = NULL;
    if (Process32FirstW(snap, &entry)) {
        do {
            if (_wcsicmp(entry.szExeFile, L"Cyberpunk2077.exe") != 0) continue;
            game = OpenProcess(SYNCHRONIZE, FALSE, entry.th32ProcessID);
            if (game) break;
        } while (Process32NextW(snap, &entry));
    }
    CloseHandle(snap);
    return game;
}

static BOOL StopBundledPresentMon(const wchar_t *exe, HANDLE writer)
{
    wchar_t cmd[MAX_PATH * 4 + 100];
    if (swprintf_s(cmd, ARRAYSIZE(cmd),
                   L"\"%ls\" --terminate_existing_session --session_name GCO", exe) < 0)
        return FALSE;
    STARTUPINFOW startup = {0};
    PROCESS_INFORMATION child = {0};
    startup.cb = sizeof(startup);
    if (!CreateProcessW(exe, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW,
                        NULL, g_root, &startup, &child))
        return FALSE;
    CloseHandle(child.hThread);
    DWORD ended = WaitForSingleObject(child.hProcess, 15000);
    DWORD code = 0;
    BOOL good = ended == WAIT_OBJECT_0 &&
                GetExitCodeProcess(child.hProcess, &code) && code == 0;
    CloseHandle(child.hProcess);
    // An ETW session may already have ended after its writer exited.
    if (WaitForSingleObject(writer, 0) == WAIT_OBJECT_0) return TRUE;
    return good && WaitForSingleObject(writer, 15000) == WAIT_OBJECT_0;
}

// The stop event deliberately has medium integrity, so non-elevated WPF
// can signal it even when the watcher has a high-integrity UAC token. Only
// session-local interactive callers may modify this unguessable event.
static HANDLE CreateObserverStopEvent(const wchar_t *name)
{
    PSECURITY_DESCRIPTOR descriptor = NULL;
    SECURITY_ATTRIBUTES sa = { sizeof(sa), NULL, FALSE };
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00100002;;;IU)S:(ML;;NW;;;ME)",
            SDDL_REVISION_1, &descriptor, NULL))
        return NULL;
    sa.lpSecurityDescriptor = descriptor;
    HANDLE result = CreateEventW(&sa, TRUE, FALSE, name);
    DWORD error = GetLastError();
    LocalFree(descriptor);
    SetLastError(error);
    return result;
}

static int ObservePresentMon(DWORD pid, const wchar_t *id)
{
    wchar_t exe[MAX_PATH * 4];
    wchar_t eventName[100];
    if (!ObserverEventName(id, eventName, ARRAYSIZE(eventName)) ||
        swprintf_s(exe, ARRAYSIZE(exe),
                   L"%ls\\components\\presentmon\\PresentMon.exe", g_root) < 0)
        return 11;
    LogObserver(id, "observer-start", pid, 0);
    HANDLE writer = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
                                FALSE, pid);
    if (!writer) {
        LogObserver(id, "writer-open-failed", pid, GetLastError());
        return 12;
    }
    wchar_t actual[MAX_PATH * 4];
    DWORD length = ARRAYSIZE(actual);
    if (!QueryFullProcessImageNameW(writer, 0, actual, &length) ||
        _wcsicmp(exe, actual) != 0) {
        LogObserver(id, "writer-identity-failed", pid, GetLastError());
        CloseHandle(writer);
        return 13; // Never observe or stop an unverified foreign process.
    }

    HANDLE stop = CreateObserverStopEvent(eventName);
    DWORD createError = GetLastError();
    if (!stop || createError == ERROR_ALREADY_EXISTS) {
        if (stop) CloseHandle(stop);
        LogObserver(id, "event-creation-failed", pid, createError);
        CloseHandle(writer);
        return 15;
    }

    HANDLE game = NULL;
    ULONGLONG graceUntil = 0;
    BOOL sawGame = FALSE;
    BOOL stopFailureLogged = FALSE;
    int exitCode = 0;
    LogObserver(id, "observer-ready", pid, 0);
    for (;;) {
        if (WaitForSingleObject(writer, 0) == WAIT_OBJECT_0) {
            DWORD writerExit = 0;
            if (!GetExitCodeProcess(writer, &writerExit)) writerExit = GetLastError();
            LogObserver(id, "writer-exited-unrequested", pid, writerExit);
            break;
        }
        BOOL requestedStop = WaitForSingleObject(stop, 0) == WAIT_OBJECT_0;
        if (requestedStop || (graceUntil && GetTickCount64() >= graceUntil)) {
            if (!stopFailureLogged)
                LogObserver(id, requestedStop ? "explicit-stop" : "game-exit-stop", pid, 0);
            // A failed ETW stop remains retryable by this same authorized
            // observer. Do not kill the writer or invent CSV flush success.
            if (StopBundledPresentMon(exe, writer)) {
                LogObserver(id, "etw-stop-flushed", pid, 0);
                break;
            }
            if (!stopFailureLogged) {
                LogObserver(id, "etw-stop-failed", pid, 0);
                stopFailureLogged = TRUE;
            }
            Sleep(1000);
            continue;
        }
        if (!game) {
            game = FindGameHandle();
            if (game) {
                sawGame = TRUE;
                graceUntil = 0;
                LogObserver(id, "game-observed", GetProcessId(game), 0);
            } else {
                // Before first game process, wait for explicit user request
                // or a fresh START that stops the old writer. Never auto-exit.
                // After a process exit, tolerate short relaunch transitions.
                (void)sawGame;
                WaitForSingleObject(stop, 250);
            }
            continue;
        }
        HANDLE waits[3] = { stop, game, writer };
        DWORD result = WaitForMultipleObjects(3, waits, FALSE, INFINITE);
        if (result == WAIT_OBJECT_0) continue; // stop request on next loop
        if (result == WAIT_OBJECT_0 + 2) {
            DWORD writerExit = 0;
            if (!GetExitCodeProcess(writer, &writerExit)) writerExit = GetLastError();
            LogObserver(id, "writer-exited-unrequested", pid, writerExit);
            break;
        }
        if (result == WAIT_OBJECT_0 + 1) {
            LogObserver(id, "game-process-exited", GetProcessId(game), 0);
            CloseHandle(game);
            game = NULL;
            // Distinguish the final exit from an early transient game PID.
            graceUntil = GetTickCount64() + 3000;
            continue;
        }
        exitCode = 16;
        LogObserver(id, "wait-error", pid, GetLastError());
        break;
    }
    if (game) CloseHandle(game);
    CloseHandle(stop);
    CloseHandle(writer);
    LogObserver(id, "observer-exit", pid, (DWORD)exitCode);
    return exitCode;
}

static int VerifyObserverControl(void)
{
    wchar_t id[] = L"0123456789abcdef0123456789abcdef";
    wchar_t name[100];
    if (!ObserverEventName(id, name, ARRAYSIZE(name)) ||
        ObserverEventName(L"../bad", name, ARRAYSIZE(name))) return 1;
    HANDLE ready = CreateObserverStopEvent(name);
    if (!ready) return 2;
    HANDLE client = OpenEventW(EVENT_MODIFY_STATE | SYNCHRONIZE, FALSE, name);
    BOOL good = client && SetEvent(client) &&
                WaitForSingleObject(ready, 0) == WAIT_OBJECT_0;
    if (client) CloseHandle(client);
    CloseHandle(ready);
    return good ? 0 : 3;
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
            } else if (LOWORD(wp) == TRAY_UI_CLOSED) {
                // User X closes the host, but never touches PresentMon/results.
                DestroyWindow(window);
            }
            return 0;
        case WM_DESTROY:
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

    // Elevated observer uses the same signed package launcher, but skips
    // the native tray singleton and the private .NET runtime entirely.
    if (commandLine && wcscmp(commandLine, L"--verify-presentmon-observer") == 0)
        return VerifyObserverControl();
    if (commandLine && wcsncmp(commandLine, L"--presentmon-observe ", 21) == 0) {
        int count = 0;
        LPWSTR *args = CommandLineToArgvW(GetCommandLineW(), &count);
        int result = 10;
        if (args && count == 4 && wcscmp(args[1], L"--presentmon-observe") == 0) {
            wchar_t *end = NULL;
            unsigned long parsed = wcstoul(args[2], &end, 10);
            if (parsed > 0 && end && *end == 0 && IsCaptureId(args[3]))
                result = ObservePresentMon((DWORD)parsed, args[3]);
        }
        if (args) LocalFree(args);
        return result;
    }

    if (commandLine && (wcscmp(commandLine, L"--tray-exit") == 0 ||
                        wcscmp(commandLine, L"--ui-closed") == 0)) {
        // Both native-only controls target the exact package-bound host.
        // The existing tray menu retains its active-PresentMon exit guard.
        const BOOL userClosed = wcscmp(commandLine, L"--ui-closed") == 0;
        wchar_t name[80];
        swprintf_s(name, ARRAYSIZE(name),
                   L"Local\\GCO_Tray_%016llX", (unsigned long long)RootHash(g_root));
        HWND owner = FindWindowW(L"GCO.Native.Tray.Window", name);
        if (!owner) return userClosed ? 0 : 6;
        return PostMessageW(owner, WM_COMMAND,
                            userClosed ? TRAY_UI_CLOSED : TRAY_EXIT, 0) ? 0 : 7;
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
