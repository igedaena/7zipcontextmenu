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
using System.Linq;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SevenZipContextMenu
{
    internal sealed class BandizipOptions
    {
        internal bool Enabled;
        internal int Formats = 3;
        internal int ActiveFormats { get { return Enabled ? Formats : 0; } }
        internal string Executable = "";
        internal static bool TryReadEggVolume(string file, out uint id, out uint previous, out uint next)
        {
            id = previous = next = 0;
            try {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new BinaryReader(stream)) {
                    long limit = Math.Min(stream.Length, 65536);
                    if (limit < 18 || reader.ReadUInt32() != 0x41474745 || reader.ReadUInt16() != 256) return false;
                    id = reader.ReadUInt32(); reader.ReadUInt32();
                    while (stream.Position + 4 <= limit) {
                        uint magic = reader.ReadUInt32();
                        if (magic == 0x08e28222 || stream.Position + 3 > limit) return false;
                        byte flags = reader.ReadByte();
                        if ((flags & 1) != 0 && stream.Position + 4 > limit) return false;
                        uint length = (flags & 1) == 0 ? reader.ReadUInt16() : reader.ReadUInt32();
                        if (length > limit - stream.Position) return false;
                        if (magic == 0x24f5a262 && flags == 0 && length == 8) {
                            previous = reader.ReadUInt32(); next = reader.ReadUInt32();
                            return previous != 0 || next != 0;
                        }
                        stream.Position += length;
                    }
                }
            } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }
        internal static bool Supports(string path, int formats)
        {
            return ((formats & 1) != 0 && path.EndsWith(".alz", StringComparison.OrdinalIgnoreCase)) ||
                ((formats & 2) != 0 && path.EndsWith(".egg", StringComparison.OrdinalIgnoreCase));
        }
        internal static BandizipOptions Read()
        {
            var result = new BandizipOptions();
            try {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var key = hive.OpenSubKey(OptionsStore.KeyPath)) {
                    if (key == null) return result;
                    object formats = key.GetValue("BandizipFormatsV1");
                    result.Formats = formats is int ? (int)formats & 3 : 3;
                    object enabled = key.GetValue("BandizipEnabledV1");
                    result.Enabled = enabled is int && (int)enabled == 1;
                    result.Executable = key.GetValue("BandizipPathV1") as string ?? "";
                }
            } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            return result;
        }
        internal static string FindInstalled()
        {
            var candidates = new List<string>();
            foreach (RegistryHive source in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
                using (var hive = RegistryKey.OpenBaseKey(source, view))
                using (var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Bandizip.exe")) {
                    string value = key == null ? null : key.GetValue(null) as string;
                    if (!String.IsNullOrWhiteSpace(value)) candidates.Add(value.Trim('"'));
                }
            }
            foreach (string variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA" }) {
                string directory = Environment.GetEnvironmentVariable(variable);
                if (!String.IsNullOrEmpty(directory)) candidates.Add(Path.Combine(directory, "Bandizip", "Bandizip.exe"));
            }
            return candidates.FirstOrDefault(File.Exists) ?? "";
        }
        internal static void Validate(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || !File.Exists(executable) ||
                !String.Equals(Path.GetFileName(executable), "Bandizip.exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("설치된 Bandizip.exe를 지정해 주세요.");
        }
    }

    internal static class OptionsStore
    {
        internal const string KeyPath = @"Software\SevenZipContextMenu";
        internal const uint Defaults = 0x2000f;
        internal const uint Direct = 0x10000;
        internal const uint Icons = 0x20000;
        internal const uint HideSettings = 0x40000;
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
        internal static void Write(uint value, uint[] order, int fontSize, BandizipOptions bandizip)
        {
            if (fontSize < 9 || fontSize > 18) throw new ArgumentOutOfRangeException("fontSize");
            RuntimeSafety.EnsureStandardUser();
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(KeyPath)) {
                string[] names = { "FontSizeV1", "BandizipFormatsV1", "BandizipPathV1", "BandizipEnabledV1" };
                object[] values = { fontSize, bandizip.Formats, bandizip.Executable, bandizip.Enabled ? 1 : 0 };
                RegistryValueKind[] kinds = { RegistryValueKind.DWord, RegistryValueKind.DWord, RegistryValueKind.String, RegistryValueKind.DWord };
                object[] previous = names.Select(name => key.GetValue(name)).ToArray();
                var previousKinds = names.Select((name, i) => previous[i] == null ? RegistryValueKind.None : key.GetValueKind(name)).ToArray();
                try {
                    for (int i = 0; i < names.Length; i++) key.SetValue(names[i], values[i], kinds[i]);
                    Write(value, order);
                }
                catch {
                    for (int i = 0; i < names.Length; i++) {
                        if (previous[i] == null) key.DeleteValue(names[i], false);
                        else key.SetValue(names[i], previous[i], previousKinds[i]);
                    }
                    throw;
                }
            }
        }
        // Stable option IDs, shared with ReadMenuOrder in MenuModel.cpp.
        internal static readonly uint[] DefaultOrder = { 16, 32, 64, 128, 1, 2, 256, 512, 1024, 2048, 4, 8, 4096 };
        internal static uint[] NormalizeOrder(string[] entries)
        {
            var order = new List<uint>();
            if (entries != null) foreach (string entry in entries) {
                foreach (uint id in DefaultOrder) if (entry == id.ToString(System.Globalization.CultureInfo.InvariantCulture) && !order.Contains(id)) {
                    order.Add(id); break;
                }
            }
            foreach (uint id in DefaultOrder) if (!order.Contains(id)) order.Add(id);
            return order.ToArray();
        }
        internal static uint[] ReadOrder()
        {
            try {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var key = hive.OpenSubKey(KeyPath)) {
                    if (key == null || key.GetValueKind("MenuOrderV1") != RegistryValueKind.MultiString) return NormalizeOrder(null);
                    var entries = key.GetValue("MenuOrderV1") as string[];
                    // Match the native reader's bounded buffer (including terminating NULs).
                    if (entries == null || entries.Sum(entry => entry.Length + 1) + 1 > 1024) return NormalizeOrder(null);
                    return NormalizeOrder(entries);
                }
            } catch (IOException) { return NormalizeOrder(null); }
            catch (UnauthorizedAccessException) { return NormalizeOrder(null); }
            catch (SecurityException) { return NormalizeOrder(null); }
        }
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
        internal static void Write(uint value, uint[] order)
        {
            RuntimeSafety.EnsureStandardUser();
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(KeyPath)) {
                object previous = key.GetValue("MenuOrderV1");
                RegistryValueKind kind = previous == null ? RegistryValueKind.None : key.GetValueKind("MenuOrderV1");
                key.SetValue("MenuOrderV1", NormalizeOrder(order.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray())
                    .Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), RegistryValueKind.MultiString);
                try { key.SetValue("OptionsV1", unchecked((int)value), RegistryValueKind.DWord); }
                catch {
                    if (previous == null) key.DeleteValue("MenuOrderV1", false);
                    else key.SetValue("MenuOrderV1", previous, kind);
                    throw;
                }
            }
        }
    }

    internal sealed class SettingsWindow : Form
    {
        internal readonly Dictionary<uint, ListViewItem> Commands = new Dictionary<uint, ListViewItem>();
        internal readonly ListView CommandList = new ListView {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false,
            HideSelection = false, ShowItemToolTips = true, Dock = DockStyle.Fill, Height = 340
        };
        internal readonly Button MoveUp = new Button { Text = "위로 ↑", AutoSize = true };
        internal readonly Button MoveDown = new Button { Text = "아래로 ↓", AutoSize = true };
        internal readonly Button ResetOrder = new Button { Text = "기본 순서 복원", AutoSize = true };
        internal readonly RadioButton Cascaded = new RadioButton { Text = "계단식 표시 (7-Zip 확장 안에 모으기)", AutoSize = true };
        internal readonly RadioButton Direct = new RadioButton { Text = "바로 표시 (우클릭 첫 단계에 펼치기)", AutoSize = true };
        internal readonly CheckBox Icons = new CheckBox { Text = "메뉴 아이콘 표시", AutoSize = true };
        internal readonly CheckBox ShowSettings = new CheckBox { Text = "우클릭 메뉴에 ‘7-Zip 확장 설정’ 표시", AutoSize = true };
        internal readonly Button Apply = new Button { Text = "적용", AutoSize = true, Enabled = false };
        internal readonly Button Reset = new Button { Text = "기본값 복원", AutoSize = true };
        internal readonly Button Ok = new Button { Text = "확인", AutoSize = true };
        internal readonly Button Cancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel };
        internal readonly NumericUpDown FontSize = new NumericUpDown { Minimum = 9, Maximum = 18, Value = 9, Width = 90 };
        internal readonly TabControl Tabs = new TabControl { Dock = DockStyle.Fill };
        internal readonly CheckBox UseBandizip = new CheckBox { Text = "7-Zip 미지원 형식을 반디집으로 풀기", AutoSize = true };
        internal readonly CheckBox BandizipAlz = new CheckBox { Text = "ALZ (.alz · 분할 압축의 첫 파일)", AutoSize = true };
        internal readonly CheckBox BandizipEgg = new CheckBox { Text = "EGG (.egg, .vol1.egg 등의 분할 파일)", AutoSize = true };
        internal readonly TextBox BandizipPath = new TextBox { Dock = DockStyle.Fill };
        private BandizipOptions savedBandizip;
        private int savedFontSize;
        private uint saved;
        private uint enabledCommands;
        private uint[] savedOrder;
        private uint[] currentOrder = (uint[])OptionsStore.DefaultOrder.Clone();
        private bool loading;

        internal SettingsWindow()
        {
            Text = "7-Zip 확장 설정";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            savedFontSize = OptionsStore.ReadFontSize();
            Font = new Font("맑은 고딕", savedFontSize);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 780); MinimumSize = new Size(650, 620);
            StartPosition = FormStartPosition.CenterScreen; Padding = new Padding(16);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
            footer.Controls.AddRange(new Control[] { Apply, Cancel, Ok, Reset });
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 12, 0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "표시할 기능을 선택하세요. 설정은 현재 사용자에게 적용됩니다.", AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
            var display = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            display.Controls.AddRange(new Control[] { Cascaded, Direct, Icons, ShowSettings });
            layout.Controls.Add(Group("표시 방식", display));
            CommandList.Columns.Add("메뉴 항목", 490); CommandList.Columns.Add("구분", 100);
            Add(16, "압축파일 열기", false);
            Add(32, "형식을 지정해 열기 ▶", false);
            Add(64, "압축 풀기… (7-Zip 대화상자)", false);
            Add(128, "여기에 압축 풀기", false);
            Add(1, "알아서 풀기 / 각각 알아서 풀기", true);
            Add(2, "파일명 폴더에 풀기 / 각각 파일명 폴더에 풀기", true);
            Add(256, "압축파일 테스트", false);
            Add(512, "압축파일에 추가… (7-Zip 대화상자)", false);
            Add(1024, "\"이름.zip\" 에 추가 (선택 항목을 하나로)", false);
            Add(2048, "\"이름.7z\" 에 추가 (선택 항목을 하나로)", false);
            Add(4, "이름으로 ZIP 압축하기 / 각각 ZIP 압축하기", true);
            Add(8, "이름으로 7z 압축하기 / 각각 7z 압축하기", true);
            Add(4096, "CRC / SHA ▶", false);
            var orderPanel = new Panel { Dock = DockStyle.Fill, Height = 340, MinimumSize = new Size(0, 340) };
            var orderButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            orderButtons.Controls.AddRange(new Control[] { MoveUp, MoveDown, ResetOrder });
            orderPanel.Controls.Add(CommandList); orderPanel.Controls.Add(orderButtons);
            var orderGroup = Group("표시할 기능과 순서", orderPanel);
            // Docked panels do not contribute their height to GroupBox.PreferredSize.
            orderGroup.AutoSize = false; orderGroup.Height = 380; orderGroup.MinimumSize = new Size(0, 380);
            layout.Controls.Add(orderGroup);
            var help = new Label { Text = "항목을 선택하고 위로·아래로 이동하면 실제 메뉴에도 같은 순서로 적용됩니다.\r\n체크를 꺼도 위치는 유지하며, 선택한 파일·폴더에 맞는 명령만 표시합니다.\r\n‘7-Zip 기본’은 기본 기능 연결, ‘확장’은 추가 처리 기능입니다.\r\n형식 선택과 CRC / SHA는 하위 메뉴 단위로 이동합니다. 설정 항목은 맨 아래에 표시합니다.\r\n적용 후 우클릭 메뉴를 다시 열면 반영됩니다. 설정은 시작 메뉴에서도 열 수 있습니다.", AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
            layout.Controls.Add(help);
            layout.SizeChanged += delegate { help.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 24), 0); };
            scroll.Controls.Add(layout);
            var menuTab = new TabPage("메뉴 설정") { Padding = new Padding(8) }; menuTab.Controls.Add(scroll);
            var appearanceTab = new TabPage("화면") { Padding = new Padding(12), AutoScroll = true };
            var appearance = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            appearance.Controls.Add(new Label { Text = "옵션 페이지 글자 크기 (9~18 pt)", AutoSize = true });
            FontSize.Value = savedFontSize; appearance.Controls.Add(FontSize);
            appearance.Controls.Add(new Label { Text = "크기를 바꾸면 이 창에서 바로 미리 볼 수 있습니다.\r\n적용·확인을 누르면 저장하고, 취소하면 마지막 저장값을 유지합니다.", AutoSize = true });
            appearanceTab.Controls.Add(appearance);
            var bandizipTab = new TabPage("반디집 연동") { Padding = new Padding(12), AutoScroll = true };
            var integration = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            integration.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var pathButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var detect = new Button { Text = "자동 찾기", AutoSize = true };
            var browse = new Button { Text = "찾아보기…", AutoSize = true };
            pathButtons.Controls.AddRange(new Control[] { detect, browse });
            integration.Controls.Add(UseBandizip);
            integration.Controls.Add(new Label { Text = "반디집 실행 파일 (Bandizip.exe)", AutoSize = true, Margin = new Padding(0, 14, 0, 4) });
            integration.Controls.Add(BandizipPath); integration.Controls.Add(pathButtons);
            integration.Controls.Add(BandizipAlz); integration.Controls.Add(BandizipEgg);
            var integrationHelp = new Label { AutoSize = true, Margin = new Padding(0, 14, 0, 0),
                Text = "선택한 형식의 ‘알아서 풀기’와 ‘파일명 폴더에 풀기’를 반디집으로 처리합니다.\r\n분할 ALZ는 .alz를 선택하세요. EGG는 연결된 볼륨을 한 번만 풉니다.\r\nZIP·7z·RAR 등 기존 형식은 7-Zip을 사용합니다.\r\n반디집이 설치되어 있어야 하며 암호·진행률은 반디집 창에서 처리합니다." };
            integration.Controls.Add(integrationHelp);
            integration.SizeChanged += delegate { integrationHelp.MaximumSize = new Size(Math.Max(100, integration.ClientSize.Width - 12), 0); };
            bandizipTab.Controls.Add(integration);
            savedBandizip = BandizipOptions.Read();
            UseBandizip.Checked = savedBandizip.Enabled;
            BandizipAlz.Checked = (savedBandizip.Formats & 1) != 0;
            BandizipEgg.Checked = (savedBandizip.Formats & 2) != 0;
            BandizipPath.Text = savedBandizip.Executable;
            UseBandizip.CheckedChanged += delegate { UpdateBandizipControls(); UpdateDirty(); };
            BandizipAlz.CheckedChanged += delegate { UpdateDirty(); };
            BandizipEgg.CheckedChanged += delegate { UpdateDirty(); };
            BandizipPath.TextChanged += delegate { UpdateDirty(); };
            detect.Click += delegate {
                try {
                    string found = BandizipOptions.FindInstalled();
                    if (found.Length == 0) MessageBox.Show(this, "설치된 반디집을 찾지 못했습니다. 찾아보기로 실행 파일을 지정해 주세요.", Text);
                    else BandizipPath.Text = found;
                } catch (Exception error) { MessageBox.Show(this, error.Message, Text); }
            };
            browse.Click += delegate {
                using (var dialog = new OpenFileDialog { Filter = "반디집 실행 파일|Bandizip.exe", CheckFileExists = true }) {
                    if (dialog.ShowDialog(this) == DialogResult.OK) BandizipPath.Text = dialog.FileName;
                }
            };
            UpdateBandizipControls();
            Tabs.TabPages.AddRange(new[] { menuTab, appearanceTab, bandizipTab }); Controls.Add(Tabs); Controls.Add(footer);
            FontSize.ValueChanged += delegate {
                Font oldFont = Font; Font = new Font("맑은 고딕", (float)FontSize.Value); oldFont.Dispose();
                ResizeCommandList(); UpdateDirty();
            };
            FontChanged += delegate {
                float scale = Font.Size / 9F;
                orderPanel.MinimumSize = new Size(0, (int)(340 * scale));
                orderGroup.MinimumSize = new Size(0, (int)(380 * scale));
                orderGroup.Height = orderGroup.MinimumSize.Height;
            };
            orderPanel.MinimumSize = new Size(0, (int)(340 * Font.Size / 9F));
            orderGroup.MinimumSize = new Size(0, (int)(380 * Font.Size / 9F)); orderGroup.Height = orderGroup.MinimumSize.Height;
            saved = OptionsStore.Read(); savedOrder = OptionsStore.ReadOrder(); LoadControls(saved); LoadOrder(savedOrder);
            CommandList.ItemCheck += delegate(object sender, ItemCheckEventArgs e) {
                if (loading) return;
                uint id = (uint)CommandList.Items[e.Index].Tag;
                if (e.NewValue == CheckState.Checked) enabledCommands |= id;
                else enabledCommands &= ~id;
                UpdateDirty();
            };
            CommandList.SelectedIndexChanged += delegate { UpdateMoveButtons(); };
            CommandList.SizeChanged += delegate { ResizeCommandList(); };
            MoveUp.Click += delegate { MoveSelected(-1); };
            MoveDown.Click += delegate { MoveSelected(1); };
            ResetOrder.Click += delegate { LoadOrder(OptionsStore.DefaultOrder); };
            Direct.CheckedChanged += delegate { UpdateDirty(); };
            Cascaded.CheckedChanged += delegate { UpdateDirty(); };
            Icons.CheckedChanged += delegate { UpdateDirty(); };
            ShowSettings.CheckedChanged += delegate { UpdateDirty(); };
            Apply.Click += delegate { TrySave(); };
            Ok.Click += delegate { if (TrySave()) { DialogResult = DialogResult.OK; Close(); } };
            Reset.Click += delegate {
                LoadControls(OptionsStore.Defaults); LoadOrder(OptionsStore.DefaultOrder); FontSize.Value = OptionsStore.DefaultFontSize;
                UseBandizip.Checked = false; BandizipAlz.Checked = true; BandizipEgg.Checked = true; BandizipPath.Text = "";
            };
            AcceptButton = Ok; CancelButton = Cancel;
            UpdateMoveButtons();
        }
        private void UpdateBandizipControls()
        {
            BandizipAlz.Enabled = UseBandizip.Checked; BandizipEgg.Enabled = UseBandizip.Checked;
        }
        private BandizipOptions ReadBandizipControls()
        {
            return new BandizipOptions { Enabled = UseBandizip.Checked, Formats = (BandizipAlz.Checked ? 1 : 0) | (BandizipEgg.Checked ? 2 : 0),
                Executable = BandizipPath.Text.Trim() };
        }
        private void ResizeCommandList()
        {
            CommandList.Columns[1].Width = TextRenderer.MeasureText("7-Zip 기본", Font).Width + 24;
            CommandList.Columns[0].Width = Math.Max(250, CommandList.ClientSize.Width - CommandList.Columns[1].Width - 24);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel) loading = true; // ListView sends checkbox events while tearing down its items.
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) loading = true;
            base.Dispose(disposing);
        }
        private static GroupBox Group(string title, Control content)
        {
            var group = new GroupBox { Text = title, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(10, 20, 10, 10), Margin = new Padding(0, 0, 0, 8) };
            group.Controls.Add(content); return group;
        }
        private void Add(uint option, string title, bool extension)
        {
            var item = new ListViewItem(title) { Tag = option, ToolTipText = title };
            item.SubItems.Add(extension ? "확장" : "7-Zip 기본");
            Commands.Add(option, item); CommandList.Items.Add(item);
        }
        internal uint[] ReadOrder() { return (uint[])currentOrder.Clone(); }
        internal void LoadOrder(uint[] order)
        {
            uint? selected = CommandList.SelectedItems.Count == 0 ? (uint?)null : (uint)CommandList.SelectedItems[0].Tag;
            loading = true; CommandList.BeginUpdate();
            currentOrder = (uint[])order.Clone();
            CommandList.Items.Clear();
            foreach (uint id in order) CommandList.Items.Add(Commands[id]);
            if (selected.HasValue) { Commands[selected.Value].Selected = true; Commands[selected.Value].Focused = true; }
            CommandList.EndUpdate(); loading = false; UpdateDirty(); UpdateMoveButtons();
        }
        internal void MoveSelected(int direction)
        {
            if (CommandList.SelectedItems.Count == 0) return;
            var item = CommandList.SelectedItems[0]; int target = item.Index + direction;
            if (target < 0 || target >= CommandList.Items.Count) return;
            loading = true; CommandList.BeginUpdate();
            int original = item.Index;
            uint moved = currentOrder[original]; currentOrder[original] = currentOrder[target]; currentOrder[target] = moved;
            CommandList.Items.Remove(item); CommandList.Items.Insert(target, item);
            item.Selected = true; item.Focused = true; item.EnsureVisible();
            CommandList.EndUpdate(); loading = false; CommandList.Focus(); UpdateDirty(); UpdateMoveButtons();
        }
        private void UpdateMoveButtons()
        {
            int index = CommandList.SelectedItems.Count == 0 ? -1 : CommandList.SelectedItems[0].Index;
            MoveUp.Enabled = index > 0; MoveDown.Enabled = index >= 0 && index < CommandList.Items.Count - 1;
        }
        internal uint ReadControls()
        {
            uint value = Direct.Checked ? OptionsStore.Direct : 0;
            if (Icons.Checked) value |= OptionsStore.Icons;
            if (!ShowSettings.Checked) value |= OptionsStore.HideSettings;
            value |= enabledCommands;
            return value;
        }
        internal void LoadControls(uint value)
        {
            loading = true;
            enabledCommands = value & 0x1fff;
            foreach (var pair in Commands) pair.Value.Checked = (value & pair.Key) != 0;
            Direct.Checked = (value & OptionsStore.Direct) != 0; Cascaded.Checked = !Direct.Checked;
            Icons.Checked = (value & OptionsStore.Icons) != 0;
            ShowSettings.Checked = (value & OptionsStore.HideSettings) == 0;
            loading = false; UpdateDirty();
        }
        private void UpdateDirty()
        {
            if (loading || savedBandizip == null) return;
            BandizipOptions bandizip = ReadBandizipControls();
            Apply.Enabled = ReadControls() != saved || !ReadOrder().SequenceEqual(savedOrder) || FontSize.Value != savedFontSize ||
                bandizip.Enabled != savedBandizip.Enabled || bandizip.Formats != savedBandizip.Formats || bandizip.Executable != savedBandizip.Executable;
        }
        private bool TrySave()
        {
            try { Save(); Apply.Enabled = false; return true; }
            catch (Exception error) {
                MessageBox.Show(this, "설정을 저장하지 못했습니다.\r\n" + error.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return false;
            }
        }
        internal uint Save() {
            uint value = ReadControls(); uint[] order = ReadOrder(); int fontSize = (int)FontSize.Value;
            BandizipOptions bandizip = ReadBandizipControls();
            if (UseBandizip.Checked && bandizip.Formats == 0) throw new InvalidDataException("반디집으로 처리할 형식을 하나 이상 선택해 주세요.");
            if (bandizip.Enabled && (!savedBandizip.Enabled || bandizip.Formats != savedBandizip.Formats || bandizip.Executable != savedBandizip.Executable))
                BandizipOptions.Validate(bandizip.Executable);
            OptionsStore.Write(value, order, fontSize, bandizip);
            saved = value; savedOrder = order; savedFontSize = fontSize; savedBandizip = bandizip; return value;
        }
    }
}
