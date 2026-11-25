// Copyright (C) 2026 igedaena
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using SevenZipContextMenu;

internal static class BandizipTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    private static uint Crc(byte[] data)
    {
        uint crc = UInt32.MaxValue;
        foreach (byte value in data) {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0U : 0xedb88320U);
        }
        return ~crc;
    }
    // Original stored-data fixtures, containing only the test's own text.
    private static void Fixture(string file, bool egg, byte[] data, uint id = 1, uint previous = 0, uint next = 0)
    {
        byte[] name = Encoding.ASCII.GetBytes("result.txt");
        using (var writer = new BinaryWriter(File.Create(file))) {
            if (egg) {
                writer.Write(0x41474745U); writer.Write((ushort)256); writer.Write(id); writer.Write(0U);
                if (previous != 0 || next != 0) {
                    writer.Write(0x24f5a262U); writer.Write((byte)0); writer.Write((ushort)8); writer.Write(previous); writer.Write(next);
                }
                writer.Write(0x08e28222U);
                writer.Write(0x0a8590e3U); writer.Write(0U); writer.Write((long)data.Length);
                writer.Write(0x0a8591acU); writer.Write((byte)0); writer.Write((ushort)name.Length); writer.Write(name);
                writer.Write(0x2c86950bU); writer.Write((byte)0); writer.Write((ushort)9); writer.Write(DateTime.UtcNow.ToFileTimeUtc()); writer.Write((byte)0); writer.Write(0x08e28222U);
                writer.Write(0x02b50c13U); writer.Write((byte)0); writer.Write((byte)0); writer.Write((uint)data.Length); writer.Write((uint)data.Length); writer.Write(Crc(data)); writer.Write(0x08e28222U);
                writer.Write(data); writer.Write(0x08e28222U);
            } else {
                writer.Write(0x015a4c41U); writer.Write(10U);
                writer.Write(0x015a4c42U); writer.Write((ushort)name.Length); writer.Write((byte)32); writer.Write(0x58210000U);
                writer.Write((byte)0x40); writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write(Crc(data)); writer.Write((uint)data.Length); writer.Write((uint)data.Length); writer.Write(name); writer.Write(data);
                writer.Write(0x015a4c43U); writer.Write(0L); writer.Write(0x025a4c43U);
            }
        }
    }
    private static JobResult Run(Operation operation, params string[] paths)
    {
        return new Engine(CancellationToken.None, delegate { return null; }, delegate { }).Run(new Request { Operation = operation, Paths = paths });
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try {
            string root = args[0]; string installed = BandizipOptions.FindInstalled();
            BandizipOptions.Validate(installed); Directory.CreateDirectory(root);
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(OptionsStore.KeyPath)) {
                key.SetValue("BandizipPathV1", installed); key.SetValue("BandizipFormatsV1", 3);
                key.SetValue("BandizipEnabledV1", 1);
                byte[] content = Encoding.UTF8.GetBytes("original Bandizip fallback fixture\r\n");
                foreach (bool egg in new[] { false, true }) {
                    string file = Path.Combine(root, egg ? "한글 & EGG.egg" : "한글 & ALZ.alz"); Fixture(file, egg, content);
                    var folder = Run(Operation.ExtractToFolder, file);
                    Check(folder.Success, "Extract " + Path.GetExtension(file) + ": " + folder.Error + String.Join(";", folder.Items.Select(i => i.Error)));
                    Check(File.ReadAllBytes(Path.Combine(folder.Items[0].Output, "result.txt")).SequenceEqual(content), "Real Bandizip decodes original fixture bytes");
                    var smart = Run(Operation.SmartExtract, file);
                    Check(smart.Success && File.ReadAllBytes(smart.Items[0].Output).SequenceEqual(content), "Smart extraction publishes a single item");
                    var again = Run(Operation.SmartExtract, file);
                    Check(again.Success && again.Items[0].Output != smart.Items[0].Output && File.Exists(smart.Items[0].Output), "Repeated extraction preserves existing output");
                }
                string bad = Path.Combine(root, "broken.egg"); File.WriteAllText(bad, "broken archive");
                var broken = Run(Operation.ExtractToFolder, bad);
                Check(broken.HasErrors && !Directory.Exists(Path.Combine(root, "broken")), "Corrupt external archive does not publish a folder");
                key.SetValue("BandizipPathV1", Path.Combine(root, "missing", "Bandizip.exe"));
                var missing = Run(Operation.ExtractToFolder, Path.Combine(root, "한글 & EGG.egg"));
                Check(missing.HasErrors, "Missing configured Bandizip reports an error");
                key.SetValue("BandizipPathV1", installed);
                using (var monitor = new NativeProgressTests.DialogMonitor { ProcessName = "Bandizip" }) {
                    var gui = new Engine(CancellationToken.None, null, delegate { }, true).Run(new Request {
                        Operation = Operation.ExtractToFolder, Paths = new[] { Path.Combine(root, "한글 & EGG.egg") } });
                    Check(monitor.Error == null && gui.Success, "Real Bandizip GUI publishes completed extraction");
                }
                using (var monitor = new NativeProgressTests.DialogMonitor { ProcessName = "Bandizip", CloseExternalDialogs = true }) {
                    var gui = new Engine(CancellationToken.None, null, delegate { }, true).Run(new Request { Operation = Operation.ExtractToFolder, Paths = new[] { bad } });
                    Check(monitor.Error == null && gui.HasErrors && !gui.Cancelled && !Directory.Exists(Path.Combine(root, "broken")), "Real Bandizip GUI errors keep incomplete output unpublished");
                }
                string encrypted = Path.Combine(root, "encrypted.zip");
                using (var process = Process.Start(new ProcessStartInfo(Engine.FindSevenZip(), String.Join(" ", new[] {
                    "a", "-tzip", "-p:fixture-password", "-mem=AES256", "--", encrypted, Path.Combine(root, "result.txt") }.Select(Engine.QuoteArgument))) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })) {
                    process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit();
                    Check(process.ExitCode == 0, "Encrypted cancellation fixture created");
                }
                string encryptedEgg = Path.Combine(root, "encrypted.egg"); File.Copy(encrypted, encryptedEgg);
                using (var monitor = new NativeProgressTests.DialogMonitor { ProcessName = "Bandizip", CancelNative = true }) {
                    var gui = new Engine(CancellationToken.None, null, delegate { }, true).Run(new Request { Operation = Operation.ExtractToFolder, Paths = new[] { encryptedEgg } });
                    Check(monitor.Error == null && monitor.CancelSent && gui.Cancelled && !gui.HasErrors && !Directory.Exists(Path.Combine(root, "encrypted")), "Bandizip native cancellation cleans output without a second error popup");
                }
                string volume = Path.Combine(root, "split.vol1.egg"); Fixture(volume, true, content, 1, 0, 2);
                string later = Path.Combine(root, "split.vol2.egg"); Fixture(later, true, content, 2, 1, 0);
                var normalized = Engine.NormalizeInputs(new Request { Operation = Operation.ExtractToFolder, Paths = new[] { volume, later } });
                Check(normalized.Length == 1 && normalized[0] == volume && Engine.ArchiveStem(volume) == "split", "EGG volumes normalize to one input and output name");
                string standalone = Path.Combine(root, "standalone.vol2.egg"); Fixture(standalone, true, content);
                Fixture(Path.Combine(root, "standalone.vol1.egg"), true, Encoding.UTF8.GetBytes("different archive"));
                normalized = Engine.NormalizeInputs(new Request { Operation = Operation.ExtractToFolder, Paths = new[] { standalone } });
                Check(normalized.Single() == standalone && Engine.ArchiveStem(standalone) == "standalone.vol2", "A standalone archive with a volume-like name stays selected");
                Fixture(Path.Combine(root, "mismatch.vol1.egg"), true, content, 10, 0, 20);
                string mismatch = Path.Combine(root, "mismatch.vol2.egg"); Fixture(mismatch, true, content, 22, 11, 0);
                bool rejected = false;
                try { Engine.NormalizeInputs(new Request { Operation = Operation.SmartExtract, Paths = new[] { mismatch } }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Unrelated first volumes cannot replace the selected EGG archive");
                for (int i = 1; i <= 10; i++) Fixture(Path.Combine(root, "ten.vol" + i + ".egg"), true, content, (uint)i, (uint)(i - 1), i == 10 ? 0U : (uint)(i + 1));
                normalized = Engine.NormalizeInputs(new Request { Operation = Operation.SmartExtract, Paths = new[] { Path.Combine(root, "ten.vol10.egg") } });
                Check(normalized.Single() == Path.Combine(root, "ten.vol1.egg"), "Two-digit EGG volumes preserve unpadded numbering");
                for (int i = 1; i <= 10; i++) Fixture(Path.Combine(root, "padded.vol" + i.ToString("D2") + ".egg"), true, content, (uint)i, (uint)(i - 1), i == 10 ? 0U : (uint)(i + 1));
                normalized = Engine.NormalizeInputs(new Request { Operation = Operation.SmartExtract, Paths = new[] { Path.Combine(root, "padded.vol10.egg") } });
                Check(normalized.Single() == Path.Combine(root, "padded.vol01.egg"), "A padded EGG set still resolves correctly at the ten-volume boundary");
                string[] bulkVolumes = Enumerable.Range(1, 100).Select(i => Path.Combine(root, "bulk.vol" + i + ".egg")).ToArray();
                for (int i = 0; i < bulkVolumes.Length; i++) Fixture(bulkVolumes[i], true, content, (uint)(i + 1), (uint)i, i + 1 == bulkVolumes.Length ? 0U : (uint)(i + 2));
                normalized = Engine.NormalizeInputs(new Request { Operation = Operation.SmartExtract, Paths = bulkVolumes.Reverse().ToArray() });
                Check(normalized.Length == 1 && normalized[0] == bulkVolumes[0], "A bulk selection reuses validated volume mappings");
                string alz = Path.Combine(root, "split.alz"); Fixture(alz, false, content);
                string a00 = Path.Combine(root, "split.a00"); File.WriteAllText(a00, "volume placeholder");
                rejected = false;
                try { Engine.NormalizeInputs(new Request { Operation = Operation.SmartExtract, Paths = new[] { a00 } }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "A raw ALZ continuation name cannot redirect selection to another archive");
                using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel(); var result = new Engine(cancel.Token, null, delegate { }).Run(new Request { Operation = Operation.SmartExtract, Paths = new[] { alz } });
                    Check(result.Cancelled && !result.HasErrors, "Cancellation does not start the external program or request an error dialog");
                }
                key.SetValue("BandizipEnabledV1", 0);
                Check(Run(Operation.SmartExtract, alz).HasErrors, "Disabling integration rejects external formats");
                Check(!Directory.GetDirectories(root, ".7ze-work-*").Any(), "All external extraction paths clean staging folders");
            }
            Console.WriteLine("BANDIZIP TESTS PASSED"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
