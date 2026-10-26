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

#pragma once
#include <windows.h>
#include <string>
#include <vector>

inline constexpr CLSID kExtensionClsid =
    {0xa5cf4977, 0x65a2, 0x4c35, {0xb1, 0x47, 0xeb, 0x22, 0x39, 0xf4, 0xb7, 0x20}};
inline constexpr wchar_t kExtensionClsidText[] = L"{A5CF4977-65A2-4C35-B147-EB2239F4B720}";
// Values are part of the on-disk request protocol; keep Worker.cs in sync.
enum class Operation : DWORD {
    Diagnose, SmartExtract, ExtractToFolder, CompressZip, Compress7z, Settings,
    Open, OpenAuto, OpenStar, OpenHash, OpenHashE, Open7z, OpenZip, OpenCab, OpenRar,
    ExtractDialog, ExtractHere, Test, AddDialog, QuickZip, Quick7z,
    HashCrc32, HashCrc64, HashXXH64, HashMD5, HashSHA1, HashSHA256,
    HashSHA384, HashSHA512, HashSHA3, HashBLAKE2, HashAll, HashGenerate, HashTest
};
inline constexpr DWORD kDefaultOptions = 0x2000f;
inline constexpr DWORD kDirectMenu = 0x10000;
inline constexpr DWORD kShowIcons = 0x20000;
// Inverted so existing 0.4.0 settings continue to show the settings entry.
inline constexpr DWORD kHideSettings = 0x40000;
inline constexpr wchar_t kOptionsKey[] = L"Software\\SevenZipContextMenu";
struct Command {
    Operation operation;
    std::wstring title;
    std::string verb;
    DWORD option = 0;
    int group = 0; // 1: open with type, 2: CRC/SHA
};
DWORD ReadOptions();
std::vector<Command> CommandCatalog();
std::vector<Command> BuildCommands(const std::vector<std::wstring>& paths, DWORD options);
bool IsArchiveName(const std::wstring& path);
