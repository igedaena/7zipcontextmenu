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

#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>

#include <iostream>
#include <type_traits>

static_assert(std::is_base_of_v<IUnknown, IShellExtInit>);
static_assert(std::is_base_of_v<IUnknown, IContextMenu>);

int wmain()
{
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) {
        std::wcerr << L"COM initialization failed: " << initialized << L'\n';
        return 1;
    }

    PWSTR localAppData = nullptr;
    const HRESULT folderResult = SHGetKnownFolderPath(
        FOLDERID_LocalAppData, KF_FLAG_DONT_VERIFY, nullptr, &localAppData);
    CoTaskMemFree(localAppData);
    CoUninitialize();
    if (FAILED(folderResult)) {
        std::wcerr << L"Windows Shell SDK check failed: " << folderResult << L'\n';
        return 2;
    }

    std::wcout << L"MSVC " << _MSC_FULL_VER
               << L", " << sizeof(void*) * 8 << L"-bit: C++20, COM, and Shell SDK OK\n";
    return 0;
}
