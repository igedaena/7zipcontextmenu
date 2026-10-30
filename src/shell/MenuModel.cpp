// Copyright (C) 2026 igedaena
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This file is part of 7-Zip Extension Menu.
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See LICENSE
// or https://www.gnu.org/licenses/ for the complete license.

#include "Extension.h"
#include <algorithm>
#include <cwctype>
#include <filesystem>

// Count Unicode scalars so truncation never splits a UTF-16 surrogate pair.
static std::wstring ShortArchiveLabel(const std::wstring& name)
{
    std::vector<size_t> boundaries{0};
    for (size_t i = 0; i < name.size();) {
        const auto first = name[i++];
        if (first >= 0xd800 && first <= 0xdbff && i < name.size() && name[i] >= 0xdc00 && name[i] <= 0xdfff) ++i;
        boundaries.push_back(i);
    }
    constexpr size_t limit = 60;
    const auto length = boundaries.size() - 1;
    if (length <= limit) return name;
    constexpr size_t head = 34, tail = limit - head - 3;
    return name.substr(0, boundaries[head]) + L"..." + name.substr(boundaries[length - tail]);
}

// Keep the displayed output name consistent with Engine.QuickArchivePath.
static std::wstring QuickArchiveName(const std::vector<std::wstring>& paths, const wchar_t* extension)
{
    std::filesystem::path first(paths[0]);
    while (first.has_relative_path() && first.filename().empty()) first = first.parent_path();
    const auto parent = first.parent_path();
    std::wstring stem;
    if (paths.size() > 1) stem = parent.filename().wstring();
    else {
        stem = first.filename().wstring();
        if (!(GetFileAttributesW(paths[0].c_str()) & FILE_ATTRIBUTE_DIRECTORY)) {
            const auto dot = stem.find_last_of(L'.');
            if (dot != std::wstring::npos) stem.resize(dot);
        }
    }
    if (stem.empty()) stem = L"archive";
    for (int i = 1; i <= 10000; ++i) {
        const auto name = stem + (i == 1 ? L"" : L" (" + std::to_wstring(i) + L")") + extension;
        if (GetFileAttributesW((parent / name).c_str()) != INVALID_FILE_ATTRIBUTES) continue;
        std::wstring escaped;
        for (const auto ch : ShortArchiveLabel(name)) { escaped += ch; if (ch == L'&') escaped += ch; }
        return L"\"" + escaped + L"\" 에 추가";
    }
    return {};
}

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
        {O::ExtractHere, L"여기에 압축 풀기", "SevenZipExt.ExtractHere", 128},
        {O::Test, L"압축파일 테스트", "SevenZipExt.Test", 256},
        {O::AddDialog, L"압축파일에 추가…", "SevenZipExt.AddDialog", 512},
        {O::QuickZip, L"ZIP 압축파일에 추가", "SevenZipExt.QuickZip", 1024},
        {O::Quick7z, L"7z 압축파일에 추가", "SevenZipExt.Quick7z", 2048},
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

std::vector<DWORD> ReadMenuOrder()
{
    // Stable option IDs, shared with OptionsStore.DefaultOrder. Child menus move as a unit.
    const std::vector<DWORD> defaults{16, 32, 64, 128, 1, 2, 256, 512, 1024, 2048, 4, 8, 4096};
    std::vector<DWORD> order;
    wchar_t stored[1024]{};
    DWORD bytes = sizeof(stored);
    if (RegGetValueW(HKEY_CURRENT_USER, kOptionsKey, L"MenuOrderV1",
        RRF_RT_REG_MULTI_SZ | RRF_SUBKEY_WOW6464KEY, nullptr, stored, &bytes) == ERROR_SUCCESS) {
        // Bound every entry even for externally written or malformed registry data.
        const size_t end = (std::min)(static_cast<size_t>(bytes) / sizeof(wchar_t), std::size(stored));
        for (size_t start = 0; start < end && stored[start];) {
            size_t finish = start;
            while (finish < end && stored[finish]) ++finish;
            if (finish == end) break;
            const std::wstring token(stored + start, finish - start);
            for (const auto id : defaults) {
                if (token == std::to_wstring(id) && std::find(order.begin(), order.end(), id) == order.end()) {
                    order.push_back(id); break;
                }
            }
            start = finish + 1;
        }
    }
    for (const auto id : defaults)
        if (std::find(order.begin(), order.end(), id) == order.end()) order.push_back(id);
    return order;
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
        if (op == Operation::Open && !archivesOnly) continue;
        if (op == Operation::QuickZip || op == Operation::Quick7z) {
            command.title = QuickArchiveName(paths, op == Operation::QuickZip ? L".zip" : L".7z");
            if (command.title.empty()) continue;
        }
        if (paths.size() > 1 && (op == Operation::SmartExtract || op == Operation::ExtractToFolder ||
            op == Operation::CompressZip || op == Operation::Compress7z)) command.title = L"각각 " + command.title;
        result.push_back(std::move(command));
    }
    if (paths.size() == 1) {
        // Prefer the visible output name, but keep the per-item command if its
        // quick counterpart could not be built (for example, no unused name).
        const auto has = [&](Operation operation) {
            return std::any_of(result.begin(), result.end(), [&](const Command& command) { return command.operation == operation; });
        };
        const bool quickZip = has(Operation::QuickZip), quick7z = has(Operation::Quick7z);
        std::erase_if(result, [&](const Command& command) {
            return (command.operation == Operation::CompressZip && quickZip) || (command.operation == Operation::Compress7z && quick7z);
        });
    }
    const auto order = ReadMenuOrder();
    const auto rank = [&](DWORD option) { return std::find(order.begin(), order.end(), option) - order.begin(); };
    // Stable sorting preserves the order inside Open As / CRC menus; settings stays last.
    std::stable_sort(result.begin(), result.end(), [&](const Command& left, const Command& right) {
        return rank(left.option) < rank(right.option);
    });
    return result;
}
