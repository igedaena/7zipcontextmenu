// Copyright (C) 2026 igedaena
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace SevenZipContextMenu
{
    internal static class AppearanceStore
    {
        internal const string KeyPath = @"Software\SevenZipContextMenu";
        internal const int DefaultFontSize = 9;

        internal static int ReadFontSize()
        {
            try {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var key = hive.OpenSubKey(KeyPath)) {
                    object value = key == null ? null : key.GetValue("FontSizeV1");
                    return value is int && (int)value >= 9 && (int)value <= 18 ? (int)value : DefaultFontSize;
                }
            } catch (IOException) { return DefaultFontSize; }
            catch (UnauthorizedAccessException) { return DefaultFontSize; }
            catch (SecurityException) { return DefaultFontSize; }
        }

        internal static void WriteFontSize(int points)
        {
            if (points < 9 || points > 18) throw new ArgumentOutOfRangeException("points");
            RuntimeSafety.EnsureStandardUser();
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(KeyPath))
                key.SetValue("FontSizeV1", points, RegistryValueKind.DWord);
        }
    }
}
