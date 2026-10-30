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
    internal static class OptionsStore
    {
        internal const string KeyPath = @"Software\SevenZipContextMenu";
        internal const uint Defaults = 0x2000f;
        internal const uint Direct = 0x10000;
        internal const uint Icons = 0x20000;
        internal const uint HideSettings = 0x40000;
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
        private uint saved;
        private uint enabledCommands;
        private uint[] savedOrder;
        private uint[] currentOrder = (uint[])OptionsStore.DefaultOrder.Clone();
        private bool loading;

        internal SettingsWindow()
        {
            Text = "7-Zip 확장 설정";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("맑은 고딕", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 780); MinimumSize = new Size(650, 620);
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
            var orderButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(0, 6, 0, 0) };
            orderButtons.Controls.AddRange(new Control[] { MoveUp, MoveDown, ResetOrder });
            orderPanel.Controls.Add(CommandList); orderPanel.Controls.Add(orderButtons);
            var orderGroup = Group("표시할 기능과 순서", orderPanel);
            // Docked panels do not contribute their height to GroupBox.PreferredSize.
            orderGroup.AutoSize = false; orderGroup.Height = 380; orderGroup.MinimumSize = new Size(0, 380);
            layout.Controls.Add(orderGroup);
            layout.Controls.Add(new Label { Text = "항목을 선택하고 위로·아래로 이동하면 실제 메뉴에도 같은 순서로 적용됩니다.\r\n체크를 꺼도 위치는 유지하며, 선택한 파일·폴더에 맞는 명령만 표시합니다.\r\n‘7-Zip 기본’은 기본 기능 연결, ‘확장’은 추가 처리 기능입니다.\r\n형식 선택과 CRC / SHA는 하위 메뉴 단위로 이동합니다. 설정 항목은 맨 아래에 표시합니다.\r\n적용 후 우클릭 메뉴를 다시 열면 반영됩니다. 설정은 시작 메뉴에서도 열 수 있습니다.", AutoSize = true, Margin = new Padding(0, 10, 0, 6) });
            scroll.Controls.Add(layout); Controls.Add(scroll); Controls.Add(footer);
            saved = OptionsStore.Read(); savedOrder = OptionsStore.ReadOrder(); LoadControls(saved); LoadOrder(savedOrder);
            CommandList.ItemCheck += delegate(object sender, ItemCheckEventArgs e) {
                if (loading) return;
                uint id = (uint)CommandList.Items[e.Index].Tag;
                if (e.NewValue == CheckState.Checked) enabledCommands |= id;
                else enabledCommands &= ~id;
                UpdateDirty();
            };
            CommandList.SelectedIndexChanged += delegate { UpdateMoveButtons(); };
            CommandList.SizeChanged += delegate {
                CommandList.Columns[1].Width = TextRenderer.MeasureText("7-Zip 기본", Font).Width + 24;
                CommandList.Columns[0].Width = Math.Max(250, CommandList.ClientSize.Width - CommandList.Columns[1].Width - 24);
            };
            MoveUp.Click += delegate { MoveSelected(-1); };
            MoveDown.Click += delegate { MoveSelected(1); };
            ResetOrder.Click += delegate { LoadOrder(OptionsStore.DefaultOrder); };
            Direct.CheckedChanged += delegate { UpdateDirty(); };
            Cascaded.CheckedChanged += delegate { UpdateDirty(); };
            Icons.CheckedChanged += delegate { UpdateDirty(); };
            ShowSettings.CheckedChanged += delegate { UpdateDirty(); };
            Apply.Click += delegate { TrySave(); };
            Ok.Click += delegate { if (TrySave()) { DialogResult = DialogResult.OK; Close(); } };
            Reset.Click += delegate { LoadControls(OptionsStore.Defaults); LoadOrder(OptionsStore.DefaultOrder); };
            AcceptButton = Ok; CancelButton = Cancel;
            UpdateMoveButtons();
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
        private void UpdateDirty() { if (!loading) Apply.Enabled = ReadControls() != saved || !ReadOrder().SequenceEqual(savedOrder); }
        private bool TrySave()
        {
            try { Save(); Apply.Enabled = false; return true; }
            catch (Exception error) {
                MessageBox.Show(this, "설정을 저장하지 못했습니다.\r\n" + error.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return false;
            }
        }
        internal uint Save() {
            uint value = ReadControls(); uint[] order = ReadOrder(); OptionsStore.Write(value, order);
            saved = value; savedOrder = order; return value;
        }
    }
}
