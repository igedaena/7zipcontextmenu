#include "Extension.h"
#include <shlobj.h>
#include <shellapi.h>
#include <strsafe.h>
#include <atomic>
#include <filesystem>
#include <new>
#include <stdexcept>

namespace {
HMODULE moduleHandle = nullptr;
std::atomic<long> objectCount{0};
std::atomic<long> serverLocks{0};

HRESULT LastErrorResult() { const DWORD error = GetLastError(); return error ? HRESULT_FROM_WIN32(error) : E_FAIL; }

HBITMAP CreateMenuBitmap()
{
    const int width = GetSystemMetrics(SM_CXSMICON), height = GetSystemMetrics(SM_CYSMICON);
    HICON icon = static_cast<HICON>(LoadImageW(moduleHandle, MAKEINTRESOURCEW(101), IMAGE_ICON, width, height, LR_DEFAULTCOLOR));
    if (!icon) return nullptr;
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER); info.bmiHeader.biWidth = width;
    info.bmiHeader.biHeight = -height; info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32;
    void* bits = nullptr;
    HBITMAP bitmap = CreateDIBSection(nullptr, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
    HDC memory = CreateCompatibleDC(nullptr);
    if (bitmap && memory) {
        ZeroMemory(bits, static_cast<size_t>(width) * height * 4);
        HGDIOBJ previous = SelectObject(memory, bitmap);
        if (!DrawIconEx(memory, 0, 0, icon, width, height, 0, nullptr, DI_NORMAL)) { SelectObject(memory, previous); DeleteObject(bitmap); bitmap = nullptr; }
        else SelectObject(memory, previous);
    } else if (bitmap) { DeleteObject(bitmap); bitmap = nullptr; }
    if (memory) DeleteDC(memory);
    DestroyIcon(icon);
    return bitmap;
}

std::filesystem::path ApplicationDirectory()
{
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(moduleHandle, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) throw std::runtime_error("Module path unavailable");
    return std::filesystem::path(std::wstring(buffer.data(), length)).parent_path().parent_path();
}

HRESULT StartWorker(Operation operation, const std::vector<std::wstring>& paths)
{
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return LastErrorResult();
    TOKEN_ELEVATION elevation{};
    DWORD elevationSize = 0;
    const BOOL checked = GetTokenInformation(token, TokenElevation, &elevation, sizeof(elevation), &elevationSize);
    const HRESULT checkResult = checked ? S_OK : LastErrorResult();
    CloseHandle(token);
    if (FAILED(checkResult)) return checkResult;
    // Also stop at the host boundary, before creating even a request file.
    if (elevationSize != sizeof(elevation) || elevation.TokenIsElevated) return E_ACCESSDENIED;
    if (operation != Operation::Settings && (paths.empty() || paths.size() > 100000)) return E_INVALIDARG;
    const auto appDirectory = ApplicationDirectory();
    const auto worker = appDirectory / L"SevenZipContextWorker.exe";
    if (GetFileAttributesW(worker.c_str()) == INVALID_FILE_ATTRIBUTES) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    if (operation == Operation::Settings) {
        std::wstring command = L"\"" + worker.wstring() + L"\" --settings";
        STARTUPINFOW startup{}; startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(worker.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr,
            appDirectory.c_str(), &startup, &process)) return LastErrorResult();
        CloseHandle(process.hThread); CloseHandle(process.hProcess);
        return S_OK;
    }
    PWSTR localAppData = nullptr;
    const HRESULT folderResult = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_CREATE, nullptr, &localAppData);
    if (FAILED(folderResult)) return folderResult;
    const auto requests = std::filesystem::path(localAppData) / L"SevenZipContextMenu" / L"Requests";
    CoTaskMemFree(localAppData);
    const int createResult = SHCreateDirectoryExW(nullptr, requests.c_str(), nullptr);
    if (createResult != ERROR_SUCCESS && createResult != ERROR_ALREADY_EXISTS && createResult != ERROR_FILE_EXISTS)
        return HRESULT_FROM_WIN32(createResult);
    const auto requestAttributes = GetFileAttributesW(requests.c_str());
    if (requestAttributes == INVALID_FILE_ATTRIBUTES || (requestAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return E_ACCESSDENIED;
    GUID requestGuid{};
    HRESULT result = CoCreateGuid(&requestGuid);
    if (FAILED(result)) return result;
    wchar_t guidText[40]{};
    StringFromGUID2(requestGuid, guidText, 40);
    const auto requestPath = requests / (std::wstring(guidText) + L".bin");
    HANDLE file = CreateFileW(requestPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return LastErrorResult();
    bool written = true;
    auto writeBytes = [&](const void* bytes, DWORD size) {
        DWORD actual = 0;
        if (!WriteFile(file, bytes, size, &actual, nullptr) || actual != size) written = false;
    };
    const DWORD header[] = {0x31455a37, static_cast<DWORD>(operation), static_cast<DWORD>(paths.size())};
    writeBytes(header, sizeof(header));
    for (const auto& path : paths) {
        if (path.empty() || path.size() > 32767) { written = false; break; }
        const DWORD size = static_cast<DWORD>(path.size());
        writeBytes(&size, sizeof(size));
        writeBytes(path.data(), size * sizeof(wchar_t));
        if (!written) break;
    }
    CloseHandle(file);
    if (!written) { DeleteFileW(requestPath.c_str()); return E_FAIL; }

    std::wstring commandLine = L"\"" + worker.wstring() + L"\" --request \"" + requestPath.wstring() + L"\"";
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(worker.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0,
                        nullptr, appDirectory.c_str(), &startup, &process)) {
        result = LastErrorResult();
        DeleteFileW(requestPath.c_str());
        return result;
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return S_OK;
}

class Extension final : public IShellExtInit, public IContextMenu {
    std::atomic<ULONG> references{1};
    std::vector<std::wstring> paths;
    std::vector<Command> commands;
    HBITMAP menuBitmap = nullptr;
public:
    Extension() { ++objectCount; }
    ~Extension() { if (menuBitmap) DeleteObject(menuBitmap); --objectCount; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
        if (!object) return E_POINTER;
        *object = nullptr;
        if (iid == IID_IUnknown || iid == IID_IShellExtInit) *object = static_cast<IShellExtInit*>(this);
        else if (iid == IID_IContextMenu) *object = static_cast<IContextMenu*>(this);
        if (!*object) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { const ULONG remaining = --references; if (!remaining) delete this; return remaining; }
    HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE, IDataObject* data, HKEY) override {
        try {
            paths.clear(); commands.clear();
            if (!data) return E_INVALIDARG;
            FORMATETC format{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM medium{};
            const HRESULT result = data->GetData(&format, &medium);
            if (FAILED(result)) return result;
            try {
                const auto drop = static_cast<HDROP>(GlobalLock(medium.hGlobal));
                if (!drop) { ReleaseStgMedium(&medium); return E_FAIL; }
                const UINT count = DragQueryFileW(drop, 0xffffffff, nullptr, 0);
                try {
                    for (UINT i = 0; i < count && count <= 100000; ++i) {
                        const UINT length = DragQueryFileW(drop, i, nullptr, 0);
                        std::vector<wchar_t> buffer(static_cast<size_t>(length) + 1);
                        if (DragQueryFileW(drop, i, buffer.data(), length + 1) == length) paths.emplace_back(buffer.data(), length);
                    }
                } catch (...) { GlobalUnlock(medium.hGlobal); throw; }
                GlobalUnlock(medium.hGlobal);
            } catch (...) { ReleaseStgMedium(&medium); throw; }
            ReleaseStgMedium(&medium);
            return paths.empty() ? E_INVALIDARG : S_OK;
        } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
    }
    HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU menu, UINT index, UINT first, UINT last, UINT flags) override {
        try {
            commands.clear();
            if (flags & CMF_DEFAULTONLY) return S_OK;
            const DWORD options = ReadOptions();
            auto proposed = BuildCommands(paths, options);
            const bool direct = (options & kDirectMenu) != 0;
            UINT required = static_cast<UINT>(proposed.size()) + (direct ? 0U : 1U);
            for (int group : {1, 2})
                for (const auto& command : proposed) if (command.group == group) { ++required; break; }
            if (proposed.empty() || last < first || static_cast<ULONGLONG>(last) - first + 1 < required) return S_OK;
            if ((options & kShowIcons) && !menuBitmap) menuBitmap = CreateMenuBitmap();
            const HBITMAP bitmap = (options & kShowIcons) ? menuBitmap : nullptr;
            // Build off-menu first. Every leaf and popup owns a distinct ID.
            HMENU staging = CreatePopupMenu();
            if (!staging) return LastErrorResult();
            auto insert = [&](HMENU target, UINT position, UINT id, const wchar_t* title, HMENU child) {
                MENUITEMINFOW item{};
                item.cbSize = sizeof(item); item.fMask = MIIM_STRING | MIIM_ID;
                item.dwTypeData = const_cast<PWSTR>(title); item.wID = id;
                if (child) { item.fMask |= MIIM_SUBMENU; item.hSubMenu = child; }
                if (bitmap) { item.fMask |= MIIM_BITMAP; item.hbmpItem = bitmap; }
                return InsertMenuItemW(target, position, TRUE, &item) != FALSE;
            };
            HMENU groups[3]{};
            UINT next = first + static_cast<UINT>(proposed.size());
            for (UINT i = 0; i < proposed.size(); ++i) {
                const auto& command = proposed[i];
                if (command.group && !groups[command.group]) {
                    HMENU group = CreatePopupMenu();
                    if (!group || !insert(staging, static_cast<UINT>(GetMenuItemCount(staging)), next++,
                        command.group == 1 ? L"형식을 지정해 열기" : L"CRC / SHA", group)) {
                        if (group) DestroyMenu(group); DestroyMenu(staging); return E_FAIL;
                    }
                    groups[command.group] = group;
                }
                HMENU target = command.group ? groups[command.group] : staging;
                if (!insert(target, static_cast<UINT>(GetMenuItemCount(target)), first + i, command.title.c_str(), nullptr)) {
                    DestroyMenu(staging); return E_FAIL;
                }
            }
            if (!direct) {
                if (!insert(menu, index, next, L"7-Zip 확장", staging)) { DestroyMenu(staging); return E_FAIL; }
            } else {
                UINT inserted = 0;
                while (GetMenuItemCount(staging) > 0) {
                    wchar_t title[256]{};
                    MENUITEMINFOW item{}; item.cbSize = sizeof(item);
                    item.fMask = MIIM_STRING | MIIM_ID | MIIM_SUBMENU | MIIM_BITMAP;
                    item.dwTypeData = title; item.cch = 256;
                    if (!GetMenuItemInfoW(staging, 0, TRUE, &item) || !InsertMenuItemW(menu, index + inserted, TRUE, &item)) {
                        while (inserted) DeleteMenu(menu, index + --inserted, MF_BYPOSITION);
                        DestroyMenu(staging); return E_FAIL;
                    }
                    RemoveMenu(staging, 0, MF_BYPOSITION); ++inserted;
                }
                DestroyMenu(staging);
            }
            commands = std::move(proposed);
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, static_cast<USHORT>(required));
        } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
    }
    HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR command, UINT flags, UINT*, LPSTR name, UINT capacity) override {
        try {
        if (command >= commands.size()) return E_INVALIDARG;
        if (flags == GCS_VALIDATEA || flags == GCS_VALIDATEW) return S_OK;
        if (!name || !capacity) return E_POINTER;
        if (flags == GCS_VERBA) return StringCchCopyA(name, capacity, commands[command].verb.c_str());
        if (flags == GCS_VERBW) {
            const std::wstring verb(commands[command].verb.begin(), commands[command].verb.end());
            return StringCchCopyW(reinterpret_cast<PWSTR>(name), capacity, verb.c_str());
        }
        if (flags == GCS_HELPTEXTW) return StringCchCopyW(reinterpret_cast<PWSTR>(name), capacity, commands[command].title.c_str());
        return E_NOTIMPL;
        } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
    }
    HRESULT STDMETHODCALLTYPE InvokeCommand(CMINVOKECOMMANDINFO* info) override {
        try {
            if (!info) return E_POINTER;
            Operation operation{};
            bool found = false;
            std::string verb;
            if (info->cbSize >= sizeof(CMINVOKECOMMANDINFOEX) && (info->fMask & CMIC_MASK_UNICODE)) {
                const auto* extended = reinterpret_cast<const CMINVOKECOMMANDINFOEX*>(info);
                if (HIWORD(extended->lpVerbW)) {
                    const std::wstring wide(extended->lpVerbW);
                    for (const wchar_t character : wide) {
                        if (character > 127) return E_INVALIDARG;
                        verb.push_back(static_cast<char>(character));
                    }
                }
            }
            if (verb.empty() && HIWORD(info->lpVerb)) verb = info->lpVerb;
            if (verb == "SevenZipExt.Diagnose") { operation = Operation::Diagnose; found = true; }
            else if (!verb.empty()) {
                // Canonical verbs remain valid if Explorer recreates the handler with the full selection.
                for (const auto& candidate : CommandCatalog()) {
                    if (candidate.verb == verb) { operation = candidate.operation; found = true; break; }
                }
            } else {
                const auto index = LOWORD(info->lpVerb);
                if (index < commands.size()) { operation = commands[index].operation; found = true; }
            }
            return found ? StartWorker(operation, paths) : E_INVALIDARG;
        } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
    }
};

class Factory final : public IClassFactory {
    std::atomic<ULONG> references{1};
public:
    Factory() { ++objectCount; }
    ~Factory() { --objectCount; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
        if (!object) return E_POINTER; *object = nullptr;
        if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
        *object = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { const ULONG remaining = --references; if (!remaining) delete this; return remaining; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** object) override {
        if (!object) return E_POINTER; *object = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* extension = new (std::nothrow) Extension();
        if (!extension) return E_OUTOFMEMORY;
        const HRESULT result = extension->QueryInterface(iid, object); extension->Release(); return result;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) ++serverLocks; else --serverLocks; return S_OK; }
};
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** object)
{
    if (!object) return E_POINTER; *object = nullptr;
    if (clsid != kExtensionClsid) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) Factory();
    if (!factory) return E_OUTOFMEMORY;
    const HRESULT result = factory->QueryInterface(iid, object); factory->Release(); return result;
}
extern "C" HRESULT __stdcall DllCanUnloadNow() { return objectCount == 0 && serverLocks == 0 ? S_OK : S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) moduleHandle = module;
    return TRUE;
}
