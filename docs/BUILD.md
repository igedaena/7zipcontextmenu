# 빌드와 검증

Windows x64 개발 PC에서 Visual Studio 2022 Build Tools의 MSVC x86/x64, Windows SDK 26100, CMake 도구와 .NET Framework 4.8을 사용합니다. 구성은 루트 `.vsconfig`에 있습니다. 현재 스크립트는 VS 2022 **Build Tools** 설치를 찾습니다.

## 빌드

필요한 개발 도구가 없다면 `scripts/Install-BuildTools.ps1`을 검토한 뒤 실행합니다. 공식 설치기를 다운로드하고 서명을 확인하며 설치 시 관리자 승인이 필요할 수 있습니다. CMake는 이 Build Tools에 포함된 경로를 사용합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Probe.ps1
```

x64와 Win32 Release DLL·실행기·검사 도구를 `build`에 만듭니다. C# 실행기는 Windows .NET Framework의 `csc.exe`를 사용합니다. C++은 정적 런타임 링크입니다. 스크립트가 생성하는 `.tools` 기록과 빌드 결과는 Git에서 제외합니다.

## 패키징

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-InstallerCompiler.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Portable.ps1
```

첫 명령은 공식 Inno Setup 7.1.0 설치기를 다운로드하고 서명을 확인해 프로젝트의 `.tools/InnoSetup`에 설치합니다. 기존 컴파일러가 있으면 재사용합니다. 이후 명령은 먼저 빌드한 Release 결과를 패키징합니다. 기존 포터블 ZIP을 교체하려면 `Build-Portable.ps1 -Force`를 사용합니다.

결과는 `dist/SevenZipExtension-Setup-0.4.1.exe`와 `dist/SevenZipExtension-Portable-0.4.1.zip`입니다. 두 패키지에는 루트의 LICENSE, NOTICE.txt, SOURCE.txt, THIRD_PARTY_NOTICES.md를 함께 넣습니다. 새 버전을 배포할 때 SOURCE.txt의 태그·다운로드 링크도 해당 소스로 갱신하세요. 빌드는 현재 PC의 메뉴를 등록하지 않습니다. 배포물은 Git 이력 대신 GitHub Releases에 첨부합니다.

## 검증 스크립트

7-Zip 26.05 이상이 설치된 일반 사용자 환경에서 실행합니다. 테스트는 실행 파일과 임시 데이터를 만들고 일부 검사는 개인 옵션을 백업·복원하므로, 각 스크립트의 동작을 먼저 확인하세요.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Integration.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Options.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Portable.ps1
```

포터블 검사는 `Build-Portable.ps1`의 `.tools/portable-build.json`을 사용하며 격리된 HKCU 테스트 경로에서 등록 로직을 검사합니다. `Test-ElevatedRuntime.ps1`은 실제 관리자 토큰 거부를 확인하기 위해 UAC를 사용합니다.

**`Test-PublicInstaller.ps1`은 실제 공용 설치·제거·재설치를 수행합니다.** 기존 설치가 있으면 중단하는 보호 장치가 있으며 일반 회귀 검사처럼 실행하지 않습니다. `Invoke-PublicInstallerCycle.ps1`은 이 설치 검사의 내부 보조 스크립트입니다. `Test-Installer.ps1` 역시 설치 관련 동작을 검토한 뒤 전용 환경에서 사용하세요.

`Install-Menu.ps1`과 `Uninstall-Menu.ps1`은 개발용 등록 보조 스크립트입니다. 배포 사용자는 릴리스 설치 프로그램이나 포터블의 등록·해제 CMD를 사용하세요.

한글을 포함하는 Windows PowerShell 5 스크립트의 UTF-8 BOM을 유지합니다. 빌드 통신이 제한되는 환경에서는 일반 PowerShell에서 실행하세요.
