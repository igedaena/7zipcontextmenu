#include "Extension.h"
#include <algorithm>
#include <cwctype>

bool IsArchiveName(const std::wstring& path)
{
    std::wstring lower(path);
    std::transform(lower.begin(), lower.end(), lower.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
    if (lower.size() >= 8 && lower[lower.size() - 4] == L'.' &&
        std::all_of(lower.end() - 3, lower.end(), [](wchar_t c) { return c >= L'0' && c <= L'9'; })) {
        const auto base = lower.substr(0, lower.size() - 4);
        if (base.ends_with(L".7z") || base.ends_with(L".zip")) return true;
    }
    for (const auto* suffix : {L".zip", L".7z", L".rar", L".tar", L".gz", L".tgz", L".bz2", L".tbz2", L".xz", L".txz", L".7z.001", L".zip.001"}) {
        if (lower.ends_with(suffix)) return true;
    }
    return false;
}

DWORD ReadOptions()
{
    DWORD value = 0, size = sizeof(value);
    // One atomic value, one registry view for both host architectures.
    if (RegGetValueW(HKEY_CURRENT_USER, kOptionsKey, L"OptionsV1",
        RRF_RT_REG_DWORD | RRF_SUBKEY_WOW6464KEY, nullptr, &value, &size) != ERROR_SUCCESS)
        return kDefaultOptions;
    return value;
}

std::vector<Command> CommandCatalog()
{
    using O = Operation;
    return {
        {O::SmartExtract, L"알아서 풀기", "SevenZipExt.SmartExtract", 1},
        {O::ExtractToFolder, L"파일명 폴더에 풀기", "SevenZipExt.ExtractToFolder", 2},
        {O::CompressZip, L"이름으로 ZIP 압축하기", "SevenZipExt.CompressZip", 4},
        {O::Compress7z, L"이름으로 7z 압축하기", "SevenZipExt.Compress7z", 8},
        {O::Open, L"압축파일 열기", "SevenZipExt.Open", 16},
        {O::OpenAuto, L"자동", "SevenZipExt.OpenAuto", 32, 1},
        {O::OpenStar, L"*", "SevenZipExt.OpenStar", 32, 1},
        {O::OpenHash, L"#", "SevenZipExt.OpenHash", 32, 1},
        {O::OpenHashE, L"#:e", "SevenZipExt.OpenHashE", 32, 1},
        {O::Open7z, L"7z", "SevenZipExt.Open7z", 32, 1},
        {O::OpenZip, L"zip", "SevenZipExt.OpenZip", 32, 1},
        {O::OpenCab, L"cab", "SevenZipExt.OpenCab", 32, 1},
        {O::OpenRar, L"rar", "SevenZipExt.OpenRar", 32, 1},
        {O::ExtractDialog, L"압축 풀기…", "SevenZipExt.ExtractDialog", 64},
        {O::ExtractHere, L"여기에 풀기", "SevenZipExt.ExtractHere", 128},
        {O::Test, L"압축파일 테스트", "SevenZipExt.Test", 256},
        {O::AddDialog, L"압축파일에 추가…", "SevenZipExt.AddDialog", 512},
        {O::QuickZip, L"하나의 ZIP으로 압축하기", "SevenZipExt.QuickZip", 1024},
        {O::Quick7z, L"하나의 7z로 압축하기", "SevenZipExt.Quick7z", 2048},
        {O::HashCrc32, L"CRC-32", "SevenZipExt.HashCrc32", 4096, 2},
        {O::HashCrc64, L"CRC-64", "SevenZipExt.HashCrc64", 4096, 2},
        {O::HashXXH64, L"XXH64", "SevenZipExt.HashXXH64", 4096, 2},
        {O::HashMD5, L"MD5", "SevenZipExt.HashMD5", 4096, 2},
        {O::HashSHA1, L"SHA-1", "SevenZipExt.HashSHA1", 4096, 2},
        {O::HashSHA256, L"SHA-256", "SevenZipExt.HashSHA256", 4096, 2},
        {O::HashSHA384, L"SHA-384", "SevenZipExt.HashSHA384", 4096, 2},
        {O::HashSHA512, L"SHA-512", "SevenZipExt.HashSHA512", 4096, 2},
        {O::HashSHA3, L"SHA3-256", "SevenZipExt.HashSHA3", 4096, 2},
        {O::HashBLAKE2, L"BLAKE2sp", "SevenZipExt.HashBLAKE2", 4096, 2},
        {O::HashAll, L"모두 (*)", "SevenZipExt.HashAll", 4096, 2},
        {O::HashGenerate, L"SHA-256 파일 만들기", "SevenZipExt.HashGenerate", 4096, 2},
        {O::HashTest, L"체크섬 파일 검증", "SevenZipExt.HashTest", 4096, 2},
        {O::Settings, L"7-Zip 확장 설정…", "SevenZipExt.Settings"}
    };
}

std::vector<Command> BuildCommands(const std::vector<std::wstring>& paths, DWORD options)
{
    if (paths.empty()) return {};
    bool archivesOnly = true;
    for (const auto& path : paths) {
        const DWORD attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES) return {};
        if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 || !IsArchiveName(path)) archivesOnly = false;
    }
    std::vector<Command> result;
    for (auto command : CommandCatalog()) {
        if (command.option && !(options & command.option)) continue;
        const auto op = command.operation;
        if (op == Operation::Settings && (options & kHideSettings)) continue;
        if ((op == Operation::SmartExtract || op == Operation::ExtractToFolder ||
            op == Operation::ExtractDialog || op == Operation::ExtractHere || op == Operation::Test) && !archivesOnly) continue;
        if ((op == Operation::CompressZip || op == Operation::Compress7z) && archivesOnly) continue;
        const bool singleFile = paths.size() == 1 && !(GetFileAttributesW(paths[0].c_str()) & FILE_ATTRIBUTE_DIRECTORY);
        if ((op == Operation::Open || command.group == 1) && !singleFile) continue;
        if (paths.size() == 1 && (op == Operation::QuickZip || op == Operation::Quick7z)) {
            const std::wstring target = op == Operation::QuickZip ? L"ZIP으로" : L"7z로";
            command.title = archivesOnly ? L"압축파일을 " + target + L" 감싸기" : target + L" 압축하기";
        }
        if (paths.size() > 1 && (op == Operation::SmartExtract || op == Operation::ExtractToFolder ||
            op == Operation::CompressZip || op == Operation::Compress7z)) command.title = L"각각 " + command.title;
        result.push_back(std::move(command));
    }
    return result;
}
