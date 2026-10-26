; Copyright (C) 2026 igedaena
; SPDX-License-Identifier: GPL-3.0-or-later
;
; This file is part of 7-Zip Extension Menu.
; This program is free software: you can redistribute it and/or modify
; it under the terms of the GNU General Public License as published by
; the Free Software Foundation, either version 3 of the License, or
; (at your option) any later version.
;
; This program is distributed in the hope that it will be useful,
; but WITHOUT ANY WARRANTY; without even the implied warranty of
; MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See LICENSE
; or https://www.gnu.org/licenses/ for the complete license.

#define AppVersion "0.4.2"
#ifndef ProjectRoot
  #define ProjectRoot ".."
#endif

[Setup]
AppId={{E59B3E67-D2E0-4B08-B1DF-A3D277C53D18}
AppName=7-Zip 확장
AppVersion={#AppVersion}
AppPublisher=7-Zip Extension Project
AppComments=7-Zip을 사용하는 독립적인 우클릭 메뉴 확장
DefaultDirName={autopf}\SevenZipContextMenu
DefaultGroupName=7-Zip 확장
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
SourceDir={#ProjectRoot}
OutputDir={#ProjectRoot}\dist
OutputBaseFilename=SevenZipExtension-Setup-{#AppVersion}
SetupIconFile=assets\sevenzip-extension.ico
UninstallDisplayIcon={app}\SevenZipContextWorker.exe
SignedUninstaller=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
UsePreviousAppDir=yes
VersionInfoDescription=7-Zip 확장 설치 프로그램
LicenseFile={#ProjectRoot}\LICENSE

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"



[Files]
Source: "build\x64\Release\SevenZipContextMenu.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "build\Win32\Release\SevenZipContextMenu.dll"; DestDir: "{app}\x86"; Flags: ignoreversion
Source: "build\x64\Release\SevenZipContextWorker.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\x64\Release\SevenZipContextWorker.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\sevenzip-extension.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "installer\사용법.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "SOURCE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\7-Zip 확장 설정"; Filename: "{app}\SevenZipContextWorker.exe"; Parameters: "--settings"; IconFilename: "{app}\SevenZipContextWorker.exe"
Name: "{group}\사용 안내"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\사용법.txt"""; IconFilename: "{app}\SevenZipContextWorker.exe"
Name: "{group}\라이선스 및 소스 안내"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\NOTICE.txt"""; IconFilename: "{app}\SevenZipContextWorker.exe"
Name: "{group}\7-Zip 확장 제거"; Filename: "{uninstallexe}"; IconFilename: "{app}\SevenZipContextWorker.exe"

[Registry]
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{A5CF4977-65A2-4C35-B147-EB2239F4B720}"; ValueType: string; ValueData: "SevenZipContextMenu"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{A5CF4977-65A2-4C35-B147-EB2239F4B720}\InprocServer32"; ValueType: string; ValueData: "{app}\x64\SevenZipContextMenu.dll"
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{A5CF4977-65A2-4C35-B147-EB2239F4B720}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{A5CF4977-65A2-4C35-B147-EB2239F4B720}"; ValueType: string; ValueData: "SevenZipContextMenu"; Flags: uninsdeletekey
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{A5CF4977-65A2-4C35-B147-EB2239F4B720}\InprocServer32"; ValueType: string; ValueData: "{app}\x86\SevenZipContextMenu.dll"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{A5CF4977-65A2-4C35-B147-EB2239F4B720}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKLM64; Subkey: "Software\Classes\*\shellex\ContextMenuHandlers\SevenZipContextMenu"; ValueType: string; ValueData: "{{A5CF4977-65A2-4C35-B147-EB2239F4B720}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\Directory\shellex\ContextMenuHandlers\SevenZipContextMenu"; ValueType: string; ValueData: "{{A5CF4977-65A2-4C35-B147-EB2239F4B720}"; Flags: uninsdeletekey
Root: HKLM32; Subkey: "Software\Classes\*\shellex\ContextMenuHandlers\SevenZipContextMenu"; ValueType: string; ValueData: "{{A5CF4977-65A2-4C35-B147-EB2239F4B720}"; Flags: uninsdeletekey
Root: HKLM32; Subkey: "Software\Classes\Directory\shellex\ContextMenuHandlers\SevenZipContextMenu"; ValueType: string; ValueData: "{{A5CF4977-65A2-4C35-B147-EB2239F4B720}"; Flags: uninsdeletekey

[Code]
function IsSupportedSevenZip(const ExePath: String): Boolean;
var
  Major, Minor, Revision, Build: Word;
begin
  Result := False;
  if not FileExists(ExePath) then Exit;
  if not GetVersionComponents(ExePath, Major, Minor, Revision, Build) then Exit;
  Result := (Major > 26) or ((Major = 26) and (Minor >= 5));
end;

function IsSupportedSevenZipInstallation(const Directory: String): Boolean;
begin
  Result := IsSupportedSevenZip(AddBackslash(Directory) + '7z.exe') and
    IsSupportedSevenZip(AddBackslash(Directory) + '7z.dll');
end;

function HasSevenZip(): Boolean;
var
  Path: String;
begin
  Result := (RegQueryStringValue(HKLM64, 'SOFTWARE\7-Zip', 'Path64', Path) and IsSupportedSevenZipInstallation(Path)) or
    (RegQueryStringValue(HKLM64, 'SOFTWARE\7-Zip', 'Path', Path) and IsSupportedSevenZipInstallation(Path)) or
    (RegQueryStringValue(HKLM32, 'SOFTWARE\7-Zip', 'Path', Path) and IsSupportedSevenZipInstallation(Path)) or
    IsSupportedSevenZipInstallation(ExpandConstant('{pf64}\7-Zip')) or IsSupportedSevenZipInstallation(ExpandConstant('{pf32}\7-Zip'));
end;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
  Name: String;
begin
  Result := False;
  if RegKeyExists(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{E59B3E67-D2E0-4B08-B1DF-A3D277C53D18}_is1') or
    RegKeyExists(HKCU32, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{E59B3E67-D2E0-4B08-B1DF-A3D277C53D18}_is1') then begin
    MsgBox('기존 사용자용 7-Zip 확장을 Windows 앱 목록에서 먼저 제거한 뒤 공용 버전을 설치해 주세요.', mbError, MB_OK); Exit;
  end;
  if RegQueryStringValue(HKLM64, 'Software\Classes\CLSID\{A5CF4977-65A2-4C35-B147-EB2239F4B720}', '', Name) and (Name <> 'SevenZipContextMenu') then begin
    MsgBox('같은 클래스 ID를 사용하는 다른 공용 프로그램 등록이 있습니다.', mbError, MB_OK); Exit;
  end;
  if RegQueryStringValue(HKLM32, 'Software\Classes\CLSID\{A5CF4977-65A2-4C35-B147-EB2239F4B720}', '', Name) and (Name <> 'SevenZipContextMenu') then begin
    MsgBox('같은 클래스 ID를 사용하는 다른 공용 프로그램 등록이 있습니다.', mbError, MB_OK); Exit;
  end;
  if not ((RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040)) or
    (RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040))) then begin
    MsgBox('.NET Framework 4.8 이상이 필요합니다. 이 설치 파일에는 포함되어 있지 않습니다.', mbError, MB_OK); Exit;
  end;
  if not HasSevenZip() then begin
    MsgBox('7-Zip 26.05 이상을 먼저 설치하거나 업데이트해 주세요. 이 설치 파일에는 7-Zip이 포함되어 있지 않습니다.', mbError, MB_OK); Exit;
  end;
  if RegQueryStringValue(HKCU64, 'Software\Classes\CLSID\{A5CF4977-65A2-4C35-B147-EB2239F4B720}', '', Name) and (Name <> 'SevenZipContextMenu') then begin
    MsgBox('같은 클래스 ID를 사용하는 다른 프로그램 등록이 있습니다.', mbError, MB_OK); Exit;
  end;
  if RegQueryStringValue(HKCU32, 'Software\Classes\CLSID\{A5CF4977-65A2-4C35-B147-EB2239F4B720}', '', Name) and (Name <> 'SevenZipContextMenu') then begin
    MsgBox('같은 클래스 ID를 사용하는 다른 프로그램 등록이 있습니다.', mbError, MB_OK); Exit;
  end;
  Result := True;
end;

procedure RemoveOwnedLegacyUserRegistration(Root: Integer);
var
  Name: String;
  ClassKey: String;
  HandlerKey: String;
begin
  ClassKey := 'Software\Classes\CLSID\{A5CF4977-65A2-4C35-B147-EB2239F4B720}';
  if RegQueryStringValue(Root, ClassKey, '', Name) and (Name = 'SevenZipContextMenu') then
    RegDeleteKeyIncludingSubkeys(Root, ClassKey);
  HandlerKey := 'Software\Classes\*\shellex\ContextMenuHandlers\SevenZipContextMenu';
  if RegQueryStringValue(Root, HandlerKey, '', Name) and (Name = '{A5CF4977-65A2-4C35-B147-EB2239F4B720}') then
    RegDeleteKeyIncludingSubkeys(Root, HandlerKey);
  HandlerKey := 'Software\Classes\Directory\shellex\ContextMenuHandlers\SevenZipContextMenu';
  if RegQueryStringValue(Root, HandlerKey, '', Name) and (Name = '{A5CF4977-65A2-4C35-B147-EB2239F4B720}') then
    RegDeleteKeyIncludingSubkeys(Root, HandlerKey);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    RemoveOwnedLegacyUserRegistration(HKCU64);
    RemoveOwnedLegacyUserRegistration(HKCU32);
  end;
end;
