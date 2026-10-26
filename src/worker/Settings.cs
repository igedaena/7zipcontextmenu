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
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SevenZipContextMenu
{
    internal static class OptionsStore
    {
        internal const string KeyPath = @"Software\SevenZipContextMenu";
        internal const uint Defaults = 0x2000f;
        internal const uint Direct = 0x10000;
        internal const uint Icons = 0x20000;
        internal const uint HideSettings = 0x40000;
        internal static uint Read()
        {
            try {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var key = hive.OpenSubKey(KeyPath)) {
                    if (key == null || !(key.GetValue("OptionsV1") is int)) return Defaults;
                    return unchecked((uint)(int)key.GetValue("OptionsV1"));
                }
            } catch (IOException) { return Defaults; }
            catch (UnauthorizedAccessException) { return Defaults; }
            catch (SecurityException) { return Defaults; }
        }
        internal static void Write(uint value)
        {
            RuntimeSafety.EnsureStandardUser();
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(KeyPath))
                key.SetValue("OptionsV1", unchecked((int)value), RegistryValueKind.DWord);
        }
    }

    internal sealed class SettingsWindow : Form
    {
        internal readonly Dictionary<uint, CheckBox> Commands = new Dictionary<uint, CheckBox>();
        internal readonly RadioButton Cascaded = new RadioButton { Text = "계단식 표시 (7-Zip 확장 안에 모으기)", AutoSize = true };
        internal readonly RadioButton Direct = new RadioButton { Text = "바로 표시 (우클릭 첫 단계에 펼치기)", AutoSize = true };
        internal readonly CheckBox Icons = new CheckBox { Text = "메뉴 아이콘 표시", AutoSize = true };
        internal readonly CheckBox ShowSettings = new CheckBox { Text = "우클릭 메뉴에 ‘7-Zip 확장 설정’ 표시", AutoSize = true };
        internal readonly Button Apply = new Button { Text = "적용", AutoSize = true, Enabled = false };
        internal readonly Button Reset = new Button { Text = "기본값 복원", AutoSize = true };
        internal readonly Button Ok = new Button { Text = "확인", AutoSize = true };
        internal readonly Button Cancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel };
        private uint saved;
        private bool loading;

        internal SettingsWindow()
        {
            Text = "7-Zip 확장 설정";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("맑은 고딕", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 760); MinimumSize = new Size(570, 580);
            StartPosition = FormStartPosition.CenterScreen; Padding = new Padding(16);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            footer.Controls.AddRange(new Control[] { Apply, Cancel, Ok, Reset });
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 12, 0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "표시할 기능을 선택하세요. 설정은 현재 사용자에게 적용됩니다.", AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
            var display = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            display.Controls.AddRange(new Control[] { Cascaded, Direct, Icons, ShowSettings });
            layout.Controls.Add(Group("표시 방식", display));
            var basic = CheckList();
            Add(basic, 16, "압축파일 열기");
            Add(basic, 32, "형식을 지정해 열기 ▶");
            Add(basic, 64, "압축 풀기… (7-Zip 대화상자)");
            Add(basic, 128, "여기에 압축 풀기");
            Add(basic, 2, "파일명 폴더에 풀기 / 각각 파일명 폴더에 풀기");
            Add(basic, 256, "압축파일 테스트");
            Add(basic, 512, "압축파일에 추가… (7-Zip 대화상자)");
            Add(basic, 1024, "\"이름.zip\" 에 추가 (선택 항목을 하나로)");
            Add(basic, 2048, "\"이름.7z\" 에 추가 (선택 항목을 하나로)");
            Add(basic, 4096, "CRC / SHA ▶");
            layout.Controls.Add(Group("기본 기능", basic));
            var extension = CheckList();
            Add(extension, 1, "알아서 풀기 / 각각 알아서 풀기");
            Add(extension, 4, "이름으로 ZIP 압축하기 / 각각 ZIP 압축하기");
            Add(extension, 8, "이름으로 7z 압축하기 / 각각 7z 압축하기");
            layout.Controls.Add(Group("확장 기능", extension));
            layout.Controls.Add(new Label { Text = "선택한 파일·폴더에 맞는 명령만 표시합니다.\r\n형식 선택과 CRC / SHA 목록은 하위 메뉴로 표시합니다.\r\n설정 메뉴를 숨겨도 시작 메뉴의 ‘7-Zip 확장 설정’에서 열 수 있습니다.\r\n적용 후 우클릭 메뉴를 다시 열면 변경한 설정을 사용합니다.", AutoSize = true, Margin = new Padding(0, 10, 0, 6) });
            scroll.Controls.Add(layout); Controls.Add(scroll); Controls.Add(footer);
            saved = OptionsStore.Read(); LoadControls(saved);
            Direct.CheckedChanged += delegate { UpdateDirty(); };
            Cascaded.CheckedChanged += delegate { UpdateDirty(); };
            Icons.CheckedChanged += delegate { UpdateDirty(); };
            ShowSettings.CheckedChanged += delegate { UpdateDirty(); };
            Apply.Click += delegate { TrySave(); };
            Ok.Click += delegate { if (TrySave()) { DialogResult = DialogResult.OK; Close(); } };
            Reset.Click += delegate { LoadControls(OptionsStore.Defaults); };
            AcceptButton = Ok; CancelButton = Cancel;
        }
        private static FlowLayoutPanel CheckList()
        { return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false }; }
        private static GroupBox Group(string title, Control content)
        {
            var group = new GroupBox { Text = title, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(10, 20, 10, 10), Margin = new Padding(0, 0, 0, 8) };
            group.Controls.Add(content); return group;
        }
        private void Add(FlowLayoutPanel panel, uint option, string title)
        {
            var check = new CheckBox { Text = title, AutoSize = true, Margin = new Padding(3, 2, 3, 2) };
            Commands.Add(option, check); panel.Controls.Add(check);
            check.CheckedChanged += delegate { UpdateDirty(); };
        }
        internal uint ReadControls()
        {
            uint value = Direct.Checked ? OptionsStore.Direct : 0;
            if (Icons.Checked) value |= OptionsStore.Icons;
            if (!ShowSettings.Checked) value |= OptionsStore.HideSettings;
            foreach (var pair in Commands) if (pair.Value.Checked) value |= pair.Key;
            return value;
        }
        internal void LoadControls(uint value)
        {
            loading = true;
            foreach (var pair in Commands) pair.Value.Checked = (value & pair.Key) != 0;
            Direct.Checked = (value & OptionsStore.Direct) != 0; Cascaded.Checked = !Direct.Checked;
            Icons.Checked = (value & OptionsStore.Icons) != 0;
            ShowSettings.Checked = (value & OptionsStore.HideSettings) == 0;
            loading = false; UpdateDirty();
        }
        private void UpdateDirty() { if (!loading) Apply.Enabled = ReadControls() != saved; }
        private bool TrySave()
        {
            try { saved = Save(); Apply.Enabled = false; return true; }
            catch (Exception error) {
                MessageBox.Show(this, "설정을 저장하지 못했습니다.\r\n" + error.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return false;
            }
        }
        internal uint Save() { uint value = ReadControls(); OptionsStore.Write(value); return value; }
    }
}
