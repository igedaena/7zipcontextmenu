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
#include <shlobj.h>
#include <shellapi.h>
#include <atomic>
#include <algorithm>
#include <cstring>
#include <iostream>
#include <functional>
#include <set>
#include <stdexcept>

namespace {
void Check(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
class DataObject final : public IDataObject {
    std::atomic<ULONG> references{1};
    std::vector<std::wstring> paths;
public:
    explicit DataObject(std::vector<std::wstring> selected) : paths(std::move(selected)) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
        if (!object) return E_POINTER; *object = nullptr;
        if (iid != IID_IUnknown && iid != IID_IDataObject) return E_NOINTERFACE;
        *object = static_cast<IDataObject*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { const auto remaining = --references; if (!remaining) delete this; return remaining; }
    HRESULT STDMETHODCALLTYPE GetData(FORMATETC* format, STGMEDIUM* medium) override {
        if (!format || !medium) return E_POINTER;
        if (format->cfFormat != CF_HDROP || !(format->tymed & TYMED_HGLOBAL)) return DV_E_FORMATETC;
        size_t characters = 1;
        for (const auto& path : paths) characters += path.size() + 1;
        const size_t bytes = sizeof(DROPFILES) + characters * sizeof(wchar_t);
        const auto memory = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, bytes);
        if (!memory) return E_OUTOFMEMORY;
        auto* drop = static_cast<DROPFILES*>(GlobalLock(memory));
        if (!drop) { GlobalFree(memory); return E_OUTOFMEMORY; }
        drop->pFiles = sizeof(DROPFILES); drop->fWide = TRUE;
        auto* position = reinterpret_cast<wchar_t*>(reinterpret_cast<BYTE*>(drop) + sizeof(DROPFILES));
        for (const auto& path : paths) {
            memcpy(position, path.c_str(), (path.size() + 1) * sizeof(wchar_t)); position += path.size() + 1;
        }
        GlobalUnlock(memory);
        medium->tymed = TYMED_HGLOBAL; medium->hGlobal = memory; medium->pUnkForRelease = nullptr;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetDataHere(FORMATETC*, STGMEDIUM*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE QueryGetData(FORMATETC* format) override { return format && format->cfFormat == CF_HDROP ? S_OK : DV_E_FORMATETC; }
    HRESULT STDMETHODCALLTYPE GetCanonicalFormatEtc(FORMATETC*, FORMATETC* output) override { if (output) output->ptd = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetData(FORMATETC*, STGMEDIUM*, BOOL) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE EnumFormatEtc(DWORD, IEnumFORMATETC**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE DAdvise(FORMATETC*, DWORD, IAdviseSink*, DWORD*) override { return OLE_E_ADVISENOTSUPPORTED; }
    HRESULT STDMETHODCALLTYPE DUnadvise(DWORD) override { return OLE_E_ADVISENOTSUPPORTED; }
    HRESULT STDMETHODCALLTYPE EnumDAdvise(IEnumSTATDATA**) override { return OLE_E_ADVISENOTSUPPORTED; }
};
}

int wmain(int argc, wchar_t** argv)
{
    try {
        Check(argc >= 5, "Usage: shell_probe dll extract|compress|none invokeDiagnostic(0|1) paths...");
        Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)), "COM initialization");
        std::vector<std::wstring> paths;
        for (int i = 4; i < argc; ++i) paths.emplace_back(argv[i]);
        const bool registered = std::wstring(argv[1]) == L"--registered";
        const auto module = registered ? nullptr : LoadLibraryW(argv[1]);
        if (!registered && !module) std::cerr << "LoadLibrary error: " << GetLastError() << '\n';
        Check(registered || module != nullptr, "LoadLibrary");
        const auto getFactory = registered ? nullptr : reinterpret_cast<HRESULT(__stdcall*)(REFCLSID, REFIID, void**)>(GetProcAddress(module, "DllGetClassObject"));
        const auto canUnload = registered ? nullptr : reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(module, "DllCanUnloadNow"));
        Check(registered || (getFactory && canUnload), "COM exports");
        IClassFactory* factory = nullptr;
        const HRESULT factoryResult = registered
            ? CoGetClassObject(kExtensionClsid, CLSCTX_INPROC_SERVER, nullptr, IID_IClassFactory, reinterpret_cast<void**>(&factory))
            : getFactory(kExtensionClsid, IID_IClassFactory, reinterpret_cast<void**>(&factory));
        Check(SUCCEEDED(factoryResult), "GetClassObject");
        IShellExtInit* initialization = nullptr;
        Check(SUCCEEDED(factory->CreateInstance(nullptr, IID_IShellExtInit, reinterpret_cast<void**>(&initialization))), "CreateInstance");
        IContextMenu* context = nullptr;
        Check(SUCCEEDED(initialization->QueryInterface(IID_IContextMenu, reinterpret_cast<void**>(&context))), "IContextMenu");
        auto* data = new DataObject(paths);
        Check(SUCCEEDED(initialization->Initialize(nullptr, data, nullptr)), "Initialize CF_HDROP"); data->Release();
        if (std::wstring(argv[3]) == L"reject-admin") {
            CMINVOKECOMMANDINFO info{}; info.cbSize = sizeof(info); info.lpVerb = "SevenZipExt.Diagnose";
            Check(context->InvokeCommand(&info) == E_ACCESSDENIED, "Elevated host cannot dispatch worker");
            context->Release(); initialization->Release(); factory->Release();
            if (!registered) { Check(canUnload() == S_OK, "COM lifetime/unload"); FreeLibrary(module); }
            CoUninitialize();
            std::cout << "PASS: elevated host rejected before request creation\n";
            return 0;
        }
        HMENU menu = CreatePopupMenu(); Check(menu != nullptr, "CreatePopupMenu");
        const auto hr = context->QueryContextMenu(menu, 0, 100, 200, CMF_NORMAL);
        const std::wstring mode(argv[2]);
        const bool optionsTest = mode.starts_with(L"options-");
        const bool none = mode == L"none";
        if (optionsTest) {
            const bool direct = mode.find(L"direct") != std::wstring::npos;
            const bool off = mode.find(L"off") != std::wstring::npos;
            const bool icons = mode.find(L"icons") != std::wstring::npos;
            const bool hidden = mode.find(L"hidden") != std::wstring::npos;
            if (hidden && off) {
                Check(hr == S_OK && GetMenuItemCount(menu) == 0, "All-off with settings hidden leaves no empty popup");
            } else {
            Check(SUCCEEDED(hr) && HRESULT_CODE(hr) > 0, "Options command allocation");
            if (!direct) Check(GetMenuItemCount(menu) == 1 && GetSubMenu(menu, 0), "Cascaded layout");
            else Check(off ? GetMenuItemCount(menu) == 1 :
                (mode.find(L"dedupe-") != std::wstring::npos || mode.find(L"open-") != std::wstring::npos ? GetMenuItemCount(menu) >= 1 : GetMenuItemCount(menu) > 1), "Direct layout");
            std::set<UINT> ids;
            std::set<std::string> verbs;
            std::function<void(HMENU)> inspect = [&](HMENU current) {
                for (int n = 0; n < GetMenuItemCount(current); ++n) {
                    MENUITEMINFOW item{}; item.cbSize = sizeof(item); item.fMask = MIIM_ID | MIIM_SUBMENU | MIIM_BITMAP;
                    Check(GetMenuItemInfoW(current, static_cast<UINT>(n), TRUE, &item) != FALSE, "Read menu item");
                    Check(item.wID >= 100 && item.wID < 100U + static_cast<UINT>(HRESULT_CODE(hr)), "ID is inside allocation");
                    Check(ids.insert(item.wID).second, "Unique leaf/popup command IDs");
                    Check((item.hbmpItem != nullptr) == icons, "Icon option on every item");
                    if (item.hSubMenu) { inspect(item.hSubMenu); continue; }
                    char verb[128]{};
                    Check(SUCCEEDED(context->GetCommandString(item.wID - 100, GCS_VERBA, nullptr, verb, 128)), "Options canonical verb");
                    Check(verbs.insert(verb).second, "Unique canonical verbs");
                    if (std::string(verb) == "SevenZipExt.QuickZip" || std::string(verb) == "SevenZipExt.Quick7z") {
                        const bool zip = std::string(verb) == "SevenZipExt.QuickZip";
                        wchar_t title[1024]{}; GetMenuStringW(current, static_cast<UINT>(n), title, 1024, MF_BYPOSITION);
                        const std::wstring label(title);
                        Check(label.starts_with(L"\"") && label.ends_with(zip ? L".zip\" 에 추가" : L".7z\" 에 추가"), "7-Zip naming with actual archive target");
                        if (mode.find(L"long-name") != std::wstring::npos) {
                            std::wstring visible;
                            for (size_t offset = 0; offset < label.size(); ++offset) {
                                visible += label[offset];
                                if (label[offset] == L'&') { Check(offset + 1 < label.size() && label[offset + 1] == L'&', "Ampersands stay escaped after shortening"); ++offset; }
                            }
                            size_t scalars = 0;
                            for (size_t offset = 0; offset < visible.size(); ++offset) {
                                const auto ch = visible[offset];
                                if (ch >= 0xd800 && ch <= 0xdbff) {
                                    Check(offset + 1 < visible.size() && visible[offset + 1] >= 0xdc00 && visible[offset + 1] <= 0xdfff, "No split surrogate pairs"); ++offset;
                                } else Check(ch < 0xdc00 || ch > 0xdfff, "No orphan low surrogate");
                                ++scalars;
                            }
                            Check(visible.find(L"...") != std::wstring::npos && scalars == 67, "Long filename is shortened to 60 Unicode scalars plus quotes and suffix");
                            Check(visible.ends_with(zip ? L" (3).zip\" 에 추가" : L" (2).7z\" 에 추가"), "Short label keeps extension and collision number");
                            Check(visible.starts_with(L"\"앞앞앞"), "Short label keeps the beginning of the filename");
                        }
                        if (mode.find(L"folder-preview") != std::wstring::npos)
                            Check(label == (zip ? L"\"preview-folder.zip\" 에 추가" : L"\"preview-folder.7z\" 에 추가"), "Folder trailing separator keeps its actual name");
                        else if (mode.find(L"preview") != std::wstring::npos)
                            Check(label == (zip ? L"\"preview (3).zip\" 에 추가" : L"\"preview (2).7z\" 에 추가"), "Menu skips existing files and folders");
                    }
                }
            };
            inspect(menu);
            HMENU top = direct ? menu : GetSubMenu(menu, 0);
            const std::vector<std::pair<std::string, DWORD>> optionVerbs{
                {"Open",16}, {"ExtractDialog",64}, {"ExtractHere",128}, {"SmartExtract",1},
                {"ExtractToFolder",2}, {"Test",256}, {"AddDialog",512}, {"QuickZip",1024},
                {"Quick7z",2048}, {"CompressZip",4}, {"Compress7z",8}, {"Settings",0}};
            std::vector<DWORD> observed;
            for (int n = 0; n < GetMenuItemCount(top); ++n) {
                MENUITEMINFOW item{}; item.cbSize = sizeof(item); item.fMask = MIIM_ID | MIIM_SUBMENU;
                Check(GetMenuItemInfoW(top, static_cast<UINT>(n), TRUE, &item) != FALSE, "Read ordered top-level item");
                if (item.hSubMenu) {
                    wchar_t text[128]{}; GetMenuStringW(top, static_cast<UINT>(n), text, 128, MF_BYPOSITION);
                    observed.push_back(std::wstring(text) == L"형식을 지정해 열기" ? 32U : 4096U);
                } else {
                    char verb[128]{}; Check(SUCCEEDED(context->GetCommandString(item.wID - 100, GCS_VERBA, nullptr, verb, 128)), "Ordered item canonical verb");
                    bool found = false;
                    for (const auto& entry : optionVerbs) if (std::string(verb) == "SevenZipExt." + entry.first) {
                        observed.push_back(entry.second); found = true; break;
                    }
                    Check(found, "Known ordered command");
                }
            }
            std::vector<DWORD> expectedOrder{16,32,64,128,1,2,256,512,1024,2048,4,8,4096};
            if (mode.find(L"order-reverse") != std::wstring::npos) std::reverse(expectedOrder.begin(), expectedOrder.end());
            if (mode.find(L"order-partial") != std::wstring::npos) {
                expectedOrder.erase(std::find(expectedOrder.begin(), expectedOrder.end(), DWORD{4096}));
                expectedOrder.erase(std::find(expectedOrder.begin(), expectedOrder.end(), DWORD{2}));
                expectedOrder.insert(expectedOrder.begin(), {4096, 2});
            }
            expectedOrder.push_back(0);
            std::vector<DWORD> expected;
            for (const auto id : expectedOrder) if (std::find(observed.begin(), observed.end(), id) != observed.end()) expected.push_back(id);
            Check(observed == expected, "Visible menu follows saved/default order; child menus move as a unit; settings stays last");
            Check(ids.size() == static_cast<size_t>(HRESULT_CODE(hr)), "Exact command budget including popups");
            Check(verbs.contains("SevenZipExt.Settings") == !hidden, "Settings entry visibility");
            if (off) Check(verbs.size() == 1, "All commands hidden");
            else if (mode.find(L"open-visible") != std::wstring::npos || mode.find(L"open-other") != std::wstring::npos) {
                const bool basicEnabled = mode.find(L"-format-only") == std::wstring::npos;
                const bool formatEnabled = mode.find(L"-basic-only") == std::wstring::npos;
                const bool basicVisible = basicEnabled && mode.find(L"open-visible") != std::wstring::npos;
                const bool formatVisible = formatEnabled && paths.size() == 1 && !(GetFileAttributesW(paths[0].c_str()) & FILE_ATTRIBUTE_DIRECTORY);
                Check(verbs.contains("SevenZipExt.Open") == basicVisible, "Basic Open requires one recognized archive and its checkbox");
                Check(verbs.contains("SevenZipExt.OpenZip") == formatVisible, "Explicit Open As retains its independent single-file behavior");
                Check(verbs.size() == 1U + (basicVisible ? 1U : 0U) + (formatVisible ? 8U : 0U), "Open filters leave the expected commands and settings");
            }
            else if (mode.find(L"dedupe-") != std::wstring::npos) {
                const bool both = mode.find(L"dedupe-both") != std::wstring::npos;
                const bool each = mode.find(L"dedupe-each") != std::wstring::npos;
                const bool quick = mode.find(L"dedupe-quick") != std::wstring::npos;
                const bool splitZip = mode.find(L"dedupe-split-zip") != std::wstring::npos;
                const bool split7z = mode.find(L"dedupe-split-7z") != std::wstring::npos;
                bool archivesOnly = true;
                for (const auto& path : paths) {
                    const auto dot = path.find_last_of(L'.');
                    if ((GetFileAttributesW(path.c_str()) & FILE_ATTRIBUTE_DIRECTORY) || dot == std::wstring::npos || path.substr(dot) != L".zip") archivesOnly = false;
                }
                const bool zipQuick = both || quick || splitZip;
                const bool z7Quick = both || quick || split7z;
                const bool zipEach = !archivesOnly && (both || each || split7z) && (paths.size() > 1 || !zipQuick);
                const bool z7Each = !archivesOnly && (both || each || splitZip) && (paths.size() > 1 || !z7Quick);
                Check(verbs.contains("SevenZipExt.QuickZip") == zipQuick, "Quick ZIP follows its own checkbox");
                Check(verbs.contains("SevenZipExt.Quick7z") == z7Quick, "Quick 7z follows its own checkbox");
                Check(verbs.contains("SevenZipExt.CompressZip") == zipEach, "Single-selection ZIP duplicate is hidden; multi/standalone command is preserved");
                Check(verbs.contains("SevenZipExt.Compress7z") == z7Each, "Single-selection 7z duplicate is hidden; multi/standalone command is preserved");
                Check(verbs.size() == 1U + zipQuick + z7Quick + zipEach + z7Each, "Only the selected compression commands and settings are present");
            }
            else if (mode.find(L"filter") != std::wstring::npos) {
                Check(verbs.size() == 2 && verbs.contains("SevenZipExt.Quick7z"), "One enabled command plus settings");
            } else {
                Check(verbs.contains("SevenZipExt.HashSHA256") && verbs.contains("SevenZipExt.HashTest"), "Hash children");
                Check(verbs.contains("SevenZipExt.QuickZip") && verbs.contains("SevenZipExt.AddDialog"), "Shared compression entries");
                Check(verbs.contains("SevenZipExt.OpenZip") == (paths.size() == 1 && !(GetFileAttributesW(paths[0].c_str()) & FILE_ATTRIBUTE_DIRECTORY)), "Open requires one file");
                Check(verbs.contains("SevenZipExt.Open") == (paths.size() == 1 &&
                    !(GetFileAttributesW(paths[0].c_str()) & FILE_ATTRIBUTE_DIRECTORY) && paths[0].ends_with(L".zip")), "Basic Open is hidden for ordinary files and folders");
            }
            // Re-query the same COM object with the exact budget and then one fewer ID.
            UINT count = HRESULT_CODE(hr);
            HMENU exact = CreatePopupMenu();
            Check(static_cast<UINT>(HRESULT_CODE(context->QueryContextMenu(exact, 0, 100, 100 + count - 1, CMF_NORMAL))) == count, "Exact budget works");
            DestroyMenu(exact);
            if (count > 1) {
                HMENU limited = CreatePopupMenu();
                Check(context->QueryContextMenu(limited, 0, 100, 100 + count - 2, CMF_NORMAL) == S_OK && GetMenuItemCount(limited) == 0, "Insufficient budget inserts nothing");
                DestroyMenu(limited);
            }
            HKEY optionsKey = nullptr;
            Check(RegOpenKeyExW(HKEY_CURRENT_USER, kOptionsKey, 0, KEY_QUERY_VALUE | KEY_SET_VALUE | KEY_WOW64_64KEY, &optionsKey) == ERROR_SUCCESS, "Open test options");
            DWORD original = 0, bytes = sizeof(original);
            Check(RegQueryValueExW(optionsKey, L"OptionsV1", nullptr, nullptr, reinterpret_cast<BYTE*>(&original), &bytes) == ERROR_SUCCESS, "Read original options");
            const DWORD changed = original ^ kDirectMenu;
            const LSTATUS written = RegSetValueExW(optionsKey, L"OptionsV1", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&changed), sizeof(changed));
            HMENU refreshed = CreatePopupMenu();
            const auto refreshResult = context->QueryContextMenu(refreshed, 0, 100, 200, CMF_NORMAL);
            const bool refreshedLayout = direct ? GetSubMenu(refreshed, 0) != nullptr
                : static_cast<size_t>(GetMenuItemCount(refreshed)) == observed.size();
            const LSTATUS restored = RegSetValueExW(optionsKey, L"OptionsV1", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&original), sizeof(original));
            RegCloseKey(optionsKey); DestroyMenu(refreshed);
            Check(written == ERROR_SUCCESS && restored == ERROR_SUCCESS && SUCCEEDED(refreshResult) && refreshedLayout, "Same loaded handler picks up changed settings");
            }
        } else {
        Check(SUCCEEDED(hr) && HRESULT_CODE(hr) == (none ? 0 : 4), "Command ID allocation");
        Check(GetMenuItemCount(menu) == (none ? 0 : 1), "Root menu count");
        if (!none) {
            MENUITEMINFOW rootItem{}; rootItem.cbSize = sizeof(rootItem); rootItem.fMask = MIIM_BITMAP;
            Check(GetMenuItemInfoW(menu, 0, TRUE, &rootItem) && rootItem.hbmpItem, "Menu icon resource");
            const auto submenu = GetSubMenu(menu, 0); Check(submenu && GetMenuItemCount(submenu) == 3, "Two commands plus settings");
            char verb[128]{};
            Check(SUCCEEDED(context->GetCommandString(0, GCS_VERBA, nullptr, verb, 128)), "ANSI verb");
            const bool extracting = std::wstring(argv[2]) == L"extract";
            Check(std::string(verb) == (extracting ? "SevenZipExt.SmartExtract" : "SevenZipExt.CompressZip"), "Selection type");
            wchar_t label[128]{}; GetMenuStringW(submenu, 0, label, 128, MF_BYPOSITION);
            Check((std::wstring(label).starts_with(L"각각")) == (paths.size() > 1), "Single/multiple title");
            wchar_t wideVerb[128]{};
            Check(SUCCEEDED(context->GetCommandString(0, GCS_VERBW, nullptr, reinterpret_cast<LPSTR>(wideVerb), 128)), "Unicode verb");
            Check(std::wstring(wideVerb) == std::wstring(verb, verb + strlen(verb)), "Canonical verb consistency");
        }
        }
        DestroyMenu(menu);
        menu = CreatePopupMenu();
        Check(context->QueryContextMenu(menu, 0, 100, 200, CMF_DEFAULTONLY) == S_OK && GetMenuItemCount(menu) == 0, "Default-only menu");
        if (!optionsTest) Check(context->QueryContextMenu(menu, 0, 100, 100, CMF_NORMAL) == S_OK && GetMenuItemCount(menu) == 0, "Small ID budget");
        DestroyMenu(menu);
        if (std::wstring(argv[3]) == L"1") {
            CMINVOKECOMMANDINFO info{}; info.cbSize = sizeof(info); info.lpVerb = "SevenZipExt.Diagnose"; info.nShow = SW_SHOWNORMAL;
            Check(SUCCEEDED(context->InvokeCommand(&info)), "Worker diagnostic dispatch");
        }
        context->Release(); initialization->Release(); factory->Release();
        if (!registered) { Check(canUnload() == S_OK, "COM lifetime/unload"); FreeLibrary(module); }
        CoUninitialize();
        std::wcout << L"PASS: " << sizeof(void*) * 8 << L"-bit, " << paths.size() << L" selected, " << argv[2] << L'\n';
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
