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
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SevenZipContextMenu;

internal static class OptionsTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static string Cli(LaunchPlan plan)
    {
        string exe = Path.Combine(Path.GetDirectoryName(plan.Executable), "7z.exe");
        string[] args = plan.Arguments.Where(a => a != "-ad").ToArray();
        using (var process = Process.Start(new ProcessStartInfo(exe, String.Join(" ", args.Select(Engine.QuoteArgument))) {
            WorkingDirectory = plan.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 })) {
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Check(process.ExitCode == 0, "Installed 7-Zip accepts: " + String.Join(" ", args.Take(3)));
            return output;
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try {
            string root = args[0]; Directory.CreateDirectory(root);
            RuntimeSafety.EnsureStandardUser();
            foreach (string fixture in new[] { "old", "minimum", "future", "old-engine", "missing-engine", "unversioned" }) {
                string tool = Path.Combine(root, "versions", fixture, "7z.exe");
                bool accepted = true;
                try { RuntimeSafety.RequireSupportedSevenZip(tool); }
                catch (InvalidDataException) { accepted = false; }
                catch (FileNotFoundException) { accepted = false; }
                Check(accepted == (fixture == "minimum" || fixture == "future"), "Version policy with real PE resources: " + fixture);
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
                Console.Error.WriteLine(e.Exception.GetType().FullName + ": " + e.Exception.Message);
                Console.Error.WriteLine(e.Exception.StackTrace); Environment.Exit(1);
            };
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            OptionsStore.Write(OptionsStore.Defaults);
            using (var form = new SettingsWindow()) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                form.Show(); Application.DoEvents();
                Check(form.ReadControls() == OptionsStore.Defaults && !form.Apply.Enabled && form.ShowSettings.Checked, "Existing settings keep the settings entry visible");
                Check(form.CommandList.Height >= 250 && form.MoveUp.Visible && form.MoveDown.Visible && form.ResetOrder.Visible,
                    "Order list has room for all commands and visible move buttons");
                Check(form.CommandList.Parent.Parent.ClientSize.Height >= form.CommandList.Parent.Height,
                    "Order list is contained within its visible group instead of clipped");
                using (var bitmap = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, form.ClientRectangle.IsEmpty ? Rectangle.Empty : new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(root, "settings.png"));
                }
                form.LoadControls(OptionsStore.Direct);
                Check(OptionsStore.Read() == OptionsStore.Defaults && form.Apply.Enabled, "Editing does not save");
                form.Apply.PerformClick();
                Check(OptionsStore.Read() == OptionsStore.Direct && !form.Apply.Enabled, "Apply saves all-off and layout");
                form.Reset.PerformClick();
                Check(form.ReadControls() == OptionsStore.Defaults && OptionsStore.Read() == OptionsStore.Direct, "Restore waits for apply");
                form.Cancel.PerformClick();
                Check(OptionsStore.Read() == OptionsStore.Direct, "Cancel discards edits after apply");
            }
            using (var form = new SettingsWindow()) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false; form.Show();
                Check(form.Direct.Checked && !form.Icons.Checked && form.Commands.All(p => !p.Value.Checked), "Saved settings reload");
                form.LoadControls(0x31fff); form.Ok.PerformClick();
                Check(OptionsStore.Read() == 0x31fff && form.DialogResult == DialogResult.OK, "OK saves and closes");
            }
            using (var form = new SettingsWindow()) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false; form.Show();
                form.ShowSettings.Checked = false;
                Check(form.Apply.Enabled && OptionsStore.Read() == 0x31fff, "Hide settings is not saved before apply");
                form.Apply.PerformClick();
                Check(OptionsStore.Read() == 0x71fff, "Hide settings saves with existing options");
                form.Reset.PerformClick();
                Check(form.ShowSettings.Checked && OptionsStore.Read() == 0x71fff, "Restore shows settings without saving");
                form.Cancel.PerformClick();
            }
            using (var form = new SettingsWindow()) {
                Check(!form.ShowSettings.Checked, "Hidden setting reloads in independently opened settings window");
                form.LoadControls(OptionsStore.Defaults); form.Save();
                Check((OptionsStore.Read() & OptionsStore.HideSettings) == 0, "Settings window can restore the context entry");
            }
            uint[] defaultOrder = { 16, 32, 64, 128, 1, 2, 256, 512, 1024, 2048, 4, 8, 4096 };
            Check(OptionsStore.ReadOrder().SequenceEqual(defaultOrder), "Default order follows the settings list");
            Check(OptionsStore.NormalizeOrder(new[] { "4096", "4096", "unknown", "2", "00016", "-1" })
                .SequenceEqual(new uint[] { 4096, 2 }.Concat(defaultOrder.Where(id => id != 4096 && id != 2))), "Partial order ignores unknown/duplicate IDs and appends missing commands");
            using (var form = new SettingsWindow()) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                Check(form.ReadOrder().SequenceEqual(defaultOrder) && !form.Apply.Enabled, "UI and stored default order agree");
                Check(form.Commands[16].SubItems[1].Text == "7-Zip 기본" && form.Commands[1].SubItems[1].Text == "확장" && form.Commands[2].SubItems[1].Text == "확장", "Origin column distinguishes built-in connections and extension processing");
                Check(!form.MoveUp.Enabled && !form.MoveDown.Enabled, "Move buttons require a selection");
                form.Commands[16].Selected = true; Application.DoEvents();
                Check(!form.MoveUp.Enabled && form.MoveDown.Enabled, "First row cannot move upward");
                bool checkedBefore = form.Commands[16].Checked;
                form.MoveDown.PerformClick(); Application.DoEvents();
                Check(form.ReadOrder()[0] == 32 && form.ReadOrder()[1] == 16 && form.Commands[16].Checked == checkedBefore, "Moving a row preserves its checkbox and identity");
                Check(form.Apply.Enabled && OptionsStore.ReadOrder().SequenceEqual(defaultOrder), "Order-only editing does not save before apply");
                form.Apply.PerformClick();
                Check(OptionsStore.ReadOrder()[0] == 32 && !form.Apply.Enabled, "Apply saves order even when checkboxes do not change");
                form.Commands[16].Checked = true; form.Save();
                form.Commands[16].Checked = false; form.Save();
                Check(OptionsStore.ReadOrder()[1] == 16, "Toggling a command preserves its saved position");
                form.ResetOrder.PerformClick();
                Check(form.ReadOrder().SequenceEqual(defaultOrder) && form.Apply.Enabled && OptionsStore.ReadOrder()[0] == 32, "Restore order waits for apply and preserves checkboxes");
                form.Cancel.PerformClick();
            }
            using (var form = new SettingsWindow()) {
                Check(form.ReadOrder()[0] == 32, "Cancel discards order edits and reopening restores applied order");
                form.LoadOrder(defaultOrder.Reverse().ToArray()); form.Save();
            }
            using (var form = new SettingsWindow()) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                Check(form.ReadOrder().SequenceEqual(defaultOrder.Reverse()), "Full custom order reloads");
                form.Commands[16].Selected = true; Application.DoEvents();
                Check(form.MoveUp.Enabled && !form.MoveDown.Enabled, "Last row cannot move downward");
                using (var bitmap = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(root, "settings-custom-order.png"));
                }
                form.Reset.PerformClick();
                Check(form.ReadControls() == OptionsStore.Defaults && form.ReadOrder().SequenceEqual(defaultOrder), "Full reset restores both flags and order");
                form.Ok.PerformClick();
                Check(OptionsStore.ReadOrder().SequenceEqual(defaultOrder) && form.DialogResult == DialogResult.OK, "OK saves restored order");
            }
            string sevenZip = Path.GetDirectoryName(Engine.FindSevenZip());
            foreach (string name in new[] { "7z.exe", "7zG.exe", "7zFM.exe" }) {
                RuntimeSafety.RequireSupportedSevenZip(Path.Combine(sevenZip, name));
                Check(true, "Installed tool and engine meet minimum: " + name);
            }
            string source = Path.Combine(root, "한글 [1] & @문서.txt"); File.WriteAllText(source, "original");
            string longStem = new String('앞', 33) + Char.ConvertFromUtf32(0x1f680) + Char.ConvertFromUtf32(0x1f600)
                + String.Concat(Enumerable.Repeat("긴&이름", 12)) + " 실제출력";
            string longSource = Path.Combine(root, longStem + ".txt"); File.WriteAllText(longSource, "literal long name");
            string other = Path.Combine(root, "-추가.txt"); File.WriteAllText(other, "other");
            var engine = new Engine(CancellationToken.None, delegate { return null; }, delegate { });
            foreach (var operation in new[] { Operation.QuickZip, Operation.Quick7z }) {
                string suffix = operation == Operation.QuickZip ? ".zip" : ".7z";
                string existing = Path.Combine(root, longStem + suffix); File.WriteAllText(existing, "preserve");
                JobResult longJob = engine.Run(new Request { Operation = operation, Paths = new[] { longSource } });
                Check(longJob.Success && longJob.Items[0].Output == Path.Combine(root, longStem + " (2)" + suffix)
                    && File.Exists(longJob.Items[0].Output) && File.ReadAllText(existing) == "preserve", "Long " + suffix + " output keeps full filename and existing result despite abbreviated menu");
            }
            JobResult zip = engine.Run(new Request { Operation = Operation.QuickZip, Paths = new[] { source, other } });
            Check(zip.Success && zip.Items.Count == 1 && File.Exists(zip.Items[0].Output), "Quick ZIP groups selected files");
            JobResult again = engine.Run(new Request { Operation = Operation.QuickZip, Paths = new[] { source, other } });
            Check(again.Success && again.Items[0].Output != zip.Items[0].Output, "Quick ZIP preserves existing output");
            JobResult z7 = engine.Run(new Request { Operation = Operation.Quick7z, Paths = new[] { source, other } });
            Check(z7.Success && z7.Items[0].Output.EndsWith(".7z"), "Quick 7z groups selected files");
            string list = Path.Combine(root, "파일 목록.txt");
            File.WriteAllLines(list, new[] { source, other }, new UTF8Encoding(false));
            foreach (Operation operation in Enum.GetValues(typeof(Operation))) {
                if (!NativeCommands.Handles(operation)) continue;
                string[] paths = operation <= Operation.OpenRar ? new[] { zip.Items[0].Output } : new[] { source, other };
                LaunchPlan plan = NativeCommands.BuildPlan(operation, paths, sevenZip, list);
                Check(plan.Executable.EndsWith(operation <= Operation.OpenRar ? "7zFM.exe" : "7zG.exe"), "Dialog target: " + operation);
                if (operation >= Operation.HashCrc32 && operation <= Operation.HashAll) Cli(plan);
            }
            var generate = NativeCommands.BuildPlan(Operation.HashGenerate, new[] { source, other }, sevenZip, list);
            Cli(generate);
            string checksum = generate.Arguments.Last(); Check(File.Exists(checksum), "SHA256 manifest created");
            File.WriteAllLines(list, new[] { checksum }, new UTF8Encoding(false));
            Cli(NativeCommands.BuildPlan(Operation.HashTest, new[] { checksum }, sevenZip, list));
            File.WriteAllLines(list, new[] { zip.Items[0].Output }, new UTF8Encoding(false));
            Cli(NativeCommands.BuildPlan(Operation.Test, new[] { zip.Items[0].Output }, sevenZip, list));
            // The source already exists: ExtractHere must keep it rather than overwrite.
            File.WriteAllText(source, "keep this existing file");
            Cli(NativeCommands.BuildPlan(Operation.ExtractHere, new[] { zip.Items[0].Output }, sevenZip, list));
            Check(File.ReadAllText(source) == "keep this existing file", "ExtractHere preserves existing files");
            Cli(NativeCommands.BuildPlan(Operation.ExtractDialog, new[] { zip.Items[0].Output }, sevenZip, list));
            string contents = Cli(new LaunchPlan { Executable = Path.Combine(sevenZip, "7zG.exe"), WorkingDirectory = root, Arguments = new[] { "l", "-slt", "-sccUTF-8", "--", zip.Items[0].Output } });
            Check(contents.Contains(Path.GetFileName(source)) && contents.Contains(Path.GetFileName(other)), "Combined ZIP has both literal names");
            File.WriteAllLines(list, new[] { Path.GetFileName(source), Path.GetFileName(other) }, new UTF8Encoding(false));
            LaunchPlan add = NativeCommands.BuildPlan(Operation.AddDialog, new[] { source, other }, sevenZip, list);
            Cli(add);
            Check(File.Exists(add.Arguments.Last() + ".7z") && !File.Exists(add.Arguments.Last() + ".7z.7z"), "Add dialog appends the extension exactly once");
            Console.WriteLine("ALL OPTIONS TESTS PASSED"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
