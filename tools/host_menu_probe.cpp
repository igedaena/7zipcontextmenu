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
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <vector>

int wmain(int argc, wchar_t** argv)
{
    std::vector<PIDLIST_ABSOLUTE> ids;
    IShellFolder* parent = nullptr;
    IContextMenu* context = nullptr;
    HMENU menu = nullptr;
    bool initialized = false;
    int result = 1;
    try {
        if (argc < 2) throw std::runtime_error("Expected selected paths");
        if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) throw std::runtime_error("COM initialization failed");
        initialized = true;
        const auto folder = std::filesystem::path(argv[1]).parent_path();
        for (int i = 1; i < argc; ++i) {
            if (std::filesystem::path(argv[i]).parent_path() != folder) throw std::runtime_error("Probe expects sibling items");
            PIDLIST_ABSOLUTE id = nullptr;
            if (FAILED(SHParseDisplayName(argv[i], nullptr, &id, 0, nullptr))) throw std::runtime_error("SHParseDisplayName failed");
            ids.push_back(id);
        }
        PCUITEMID_CHILD firstChild = nullptr;
        if (FAILED(SHBindToParent(ids[0], IID_IShellFolder, reinterpret_cast<void**>(&parent), &firstChild)))
            throw std::runtime_error("SHBindToParent failed");
        std::vector<PCUITEMID_CHILD> children;
        for (const auto id : ids) children.push_back(ILFindLastID(id));
        if (FAILED(parent->GetUIObjectOf(nullptr, static_cast<UINT>(children.size()), children.data(),
            IID_IContextMenu, nullptr, reinterpret_cast<void**>(&context)))) throw std::runtime_error("GetUIObjectOf failed");
        menu = CreatePopupMenu();
        if (!menu || FAILED(context->QueryContextMenu(menu, 0, 1, 0x7fff, CMF_NORMAL))) throw std::runtime_error("Shell menu construction failed");
        bool found = false;
        for (int index = 0; index < GetMenuItemCount(menu); ++index) {
            wchar_t text[256]{};
            GetMenuStringW(menu, static_cast<UINT>(index), text, 256, MF_BYPOSITION);
            if (std::wstring(text) == L"7-Zip 확장") {
                MENUITEMINFOW item{}; item.cbSize = sizeof(item); item.fMask = MIIM_BITMAP;
                if (!GetMenuItemInfoW(menu, static_cast<UINT>(index), TRUE, &item) || !item.hbmpItem)
                    throw std::runtime_error("Extension icon is absent");
                const auto submenu = GetSubMenu(menu, index);
                if (!submenu || GetMenuItemCount(submenu) != 3) throw std::runtime_error("Unexpected extension submenu");
                found = true;
            }
        }
        if (!found) throw std::runtime_error("Extension absent from full Windows Shell menu");
        std::cout << "PASS: full Windows Shell menu, " << sizeof(void*) * 8 << "-bit, " << ids.size() << " selected\n";
        result = 0;
    } catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; }
    if (menu) DestroyMenu(menu);
    if (context) context->Release();
    if (parent) parent->Release();
    for (const auto id : ids) CoTaskMemFree(id);
    if (initialized) CoUninitialize();
    return result;
}
