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

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SevenZipContextMenu
{
    internal static class RuntimeSafety
    {
        internal const string MinimumSevenZipText = "26.05";
        private static readonly Version MinimumSevenZip = new Version(26, 5, 0, 0);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass,
            out int elevation, int size, out int returnedSize);

        internal static void EnsureStandardUser()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
                int elevated, size;
                // TokenElevation is an actual token check, not a membership or manifest check.
                if (!GetTokenInformation(identity.Token, 20, out elevated, sizeof(int), out size) || size != sizeof(int))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "실행 권한을 확인하지 못해 작업을 중단했습니다.");
                if (elevated != 0)
                    throw new UnauthorizedAccessException("7-Zip 확장은 관리자 권한으로 실행할 수 없습니다. 파일관리자 또는 설정 창을 일반 권한으로 다시 실행해 주세요.");
            }
        }

        internal static void RequireSupportedSevenZip(string executable)
        {
            RequireSupportedVersionFile(executable);
            RequireSupportedVersionFile(Path.Combine(Path.GetDirectoryName(executable), "7z.dll"));
        }

        private static void RequireSupportedVersionFile(string executable)
        {
            // Read the fixed version resource; never launch a candidate to discover its version.
            if (!Path.IsPathRooted(executable) || !File.Exists(executable))
                throw new FileNotFoundException("설치된 7-Zip 실행 파일을 찾을 수 없습니다.", executable);
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
            Version version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            if (version.CompareTo(MinimumSevenZip) < 0)
                throw new InvalidDataException("7-Zip " + MinimumSevenZipText + " 이상이 필요합니다. 공식 7-Zip을 업데이트한 뒤 다시 실행해 주세요.\r\n" + executable);
        }
    }
}
