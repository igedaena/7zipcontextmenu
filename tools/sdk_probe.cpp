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
