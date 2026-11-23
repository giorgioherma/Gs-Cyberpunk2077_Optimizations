// Disposable x64 Windows game-name probe for genuine MO2 startApplication tests.
// NOT Cyberpunk, CET, RED4ext or a profiler. It reads virtual paths and writes
// an observation to a GCO CI-owned directory outside USVFS. Never shipped.
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>

namespace fs = std::filesystem;

static std::string ReadText(const fs::path& path)
{
    std::ifstream in(path, std::ios::binary);
    if (!in.is_open()) return "<missing>";
    return std::string(std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>());
}

int main()
{
    wchar_t path[32768] = {};
    DWORD size = GetModuleFileNameW(nullptr, path, 32768);
    if (size == 0 || size >= 32768) return 10;

    const fs::path gameRoot = fs::path(path).parent_path().parent_path().parent_path();
    char destination[32768] = {};
    const DWORD length = GetEnvironmentVariableA("GCO_MO2_NATIVE_PROBE_OUTPUT", destination, 32768);
    if (length == 0 || length >= 32768) return 11;

    std::ofstream out(fs::path(destination), std::ios::binary | std::ios::trunc);
    if (!out) return 12;
    out << "CET_ASI=" << ReadText(gameRoot / "bin" / "x64" / "plugins" /
                                  "cyber_engine_tweaks.asi") << "\n";
    out << "RED=" << ReadText(gameRoot / "r6" / "scripts" /
                             "GcoMo2ProfileSource.reds") << "\n";
    out << "GRSP=" << ReadText(gameRoot / "red4ext" / "plugins" /
                              "G-REDscript-Profiler.dll") << "\n";
    out.flush();
    return out.good() ? 0 : 13;
}
