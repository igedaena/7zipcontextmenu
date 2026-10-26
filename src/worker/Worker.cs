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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("7-Zip 확장")]
[assembly: System.Reflection.AssemblyVersion("0.4.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.4.1.0")]

namespace SevenZipContextMenu
{
    internal enum Operation : uint {
        Diagnose, SmartExtract, ExtractToFolder, CompressZip, Compress7z, Settings,
        Open, OpenAuto, OpenStar, OpenHash, OpenHashE, Open7z, OpenZip, OpenCab, OpenRar,
        ExtractDialog, ExtractHere, Test, AddDialog, QuickZip, Quick7z,
        HashCrc32, HashCrc64, HashXXH64, HashMD5, HashSHA1, HashSHA256,
        HashSHA384, HashSHA512, HashSHA3, HashBLAKE2, HashAll, HashGenerate, HashTest
    }
    internal sealed class Request
    {
        internal static string DirectoryPath {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SevenZipContextMenu", "Requests"); }
        }
        internal Operation Operation;
        internal string[] Paths;
        internal static Request Read(string filename)
        {
            string expected = DirectoryPath + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(filename);
            if (!full.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !String.Equals(Path.GetDirectoryName(full) + Path.DirectorySeparatorChar, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("잘못된 작업 목록 위치입니다.");
            using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var reader = new BinaryReader(stream, Encoding.Unicode))
            {
                if (stream.Length > 64 * 1024 * 1024 || reader.ReadUInt32() != 0x31455a37)
                    throw new InvalidDataException("잘못된 작업 목록입니다.");
                uint operation = reader.ReadUInt32();
                uint count = reader.ReadUInt32();
                if (!Enum.IsDefined(typeof(Operation), operation) || count == 0 || count > 100000) throw new InvalidDataException("잘못된 작업 종류 또는 항목 수입니다.");
                var paths = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    uint length = reader.ReadUInt32();
                    if (length == 0 || length > 32767 || stream.Length - stream.Position < length * 2L)
                        throw new InvalidDataException("잘못된 파일 경로입니다.");
                    string path = Encoding.Unicode.GetString(reader.ReadBytes((int)length * 2));
                    if (path.IndexOf('\0') >= 0 || !Path.IsPathRooted(path)) throw new InvalidDataException("절대 경로가 필요합니다.");
                    paths.Add(Path.GetFullPath(path));
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("작업 목록에 알 수 없는 데이터가 있습니다.");
                return new Request { Operation = (Operation)operation, Paths = paths.ToArray() };
            }
        }
    }

    [DataContract]
    internal sealed class ItemResult
    {
        [DataMember] public string Input;
        [DataMember] public string Output;
        [DataMember] public bool Success;
        [DataMember] public string Error;
    }
    [DataContract]
    internal sealed class JobResult
    {
        [DataMember] public string Operation;
        [DataMember] public int SelectedCount;
        [DataMember] public string[] SelectedPaths;
        [DataMember] public bool Cancelled;
        [DataMember] public string Error;
        [DataMember] public List<ItemResult> Items = new List<ItemResult>();
        internal bool Success { get { return !Cancelled && Error == null && Items.All(i => i.Success); } }
    }
    internal sealed class ProcessResult { internal int ExitCode; internal string Text; }

    internal sealed class Engine
    {
        private readonly CancellationToken cancellation;
        private readonly Func<string, string> askPassword;
        private readonly Action<string> status;
        private string sevenZip;
        internal Engine(CancellationToken cancellation, Func<string, string> askPassword, Action<string> status)
        { this.cancellation = cancellation; this.askPassword = askPassword; this.status = status; }

        internal JobResult Run(Request request)
        {
            RuntimeSafety.EnsureStandardUser();
            var result = new JobResult { Operation = request.Operation.ToString(), SelectedCount = request.Paths.Length, SelectedPaths = request.Paths };
            if (request.Operation == Operation.Diagnose) return result;
            try
            {
                sevenZip = FindSevenZip();
                string[] inputs = NormalizeInputs(request);
                if (request.Operation == Operation.QuickZip || request.Operation == Operation.Quick7z) {
                    var item = new ItemResult { Input = String.Join("\r\n", inputs) };
                    result.Items.Add(item);
                    status("선택한 " + inputs.Length + "개 항목을 하나의 압축 파일로 만듭니다.");
                    item.Output = CompressTogether(inputs, request.Operation == Operation.QuickZip ? "zip" : "7z");
                    item.Success = true;
                    return result;
                }
                for (int i = 0; i < inputs.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string input = inputs[i];
                    status(String.Format("{0} / {1}  ·  {2}", i + 1, inputs.Length, Path.GetFileName(input)));
                    var item = new ItemResult { Input = input };
                    try
                    {
                        if (request.Operation == Operation.CompressZip || request.Operation == Operation.Compress7z)
                            item.Output = Compress(input, request.Operation == Operation.CompressZip ? "zip" : "7z");
                        else if (request.Operation == Operation.SmartExtract || request.Operation == Operation.ExtractToFolder)
                            item.Output = Extract(input, request.Operation == Operation.SmartExtract);
                        else throw new InvalidDataException("이 작업은 7-Zip 창에서 실행해야 합니다.");
                        item.Success = true;
                    }
                    catch (OperationCanceledException) { result.Cancelled = true; item.Error = "취소됨"; result.Items.Add(item); break; }
                    catch (Exception error) { item.Error = error.Message; }
                    result.Items.Add(item);
                }
            }
            catch (OperationCanceledException) { result.Cancelled = true; }
            catch (Exception error) { result.Error = error.Message; }
            return result;
        }

        internal static string FindSevenZip()
        {
            var candidates = new List<string>();
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (var key = hive.OpenSubKey(@"SOFTWARE\7-Zip"))
            {
                if (key == null) continue;
                foreach (string name in new[] { "Path64", "Path" })
                {
                    string directory = key.GetValue(name) as string;
                    if (!String.IsNullOrWhiteSpace(directory)) candidates.Add(Path.Combine(directory, "7z.exe"));
                }
            }
            foreach (string variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
            {
                string directory = Environment.GetEnvironmentVariable(variable);
                if (!String.IsNullOrEmpty(directory)) candidates.Add(Path.Combine(directory, "7-Zip", "7z.exe"));
            }
            string firstError = null;
            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (!File.Exists(candidate)) continue;
                try { RuntimeSafety.RequireSupportedSevenZip(candidate); return candidate; }
                catch (InvalidDataException error) { if (firstError == null) firstError = error.Message; }
                catch (FileNotFoundException error) { if (firstError == null) firstError = error.Message; }
            }
            if (firstError != null) throw new InvalidDataException(firstError);
            throw new FileNotFoundException("설치된 7-Zip " + RuntimeSafety.MinimumSevenZipText + " 이상의 7z.exe를 찾을 수 없습니다.");
        }

        private static string FirstVolume(string input)
        {
            string filename = Path.GetFileName(input);
            Match rar = Regex.Match(filename, @"^(.*\.part)(\d+)(\.rar)$", RegexOptions.IgnoreCase);
            if (rar.Success)
            {
                string first = Path.Combine(Path.GetDirectoryName(input), rar.Groups[1].Value + "1".PadLeft(rar.Groups[2].Length, '0') + rar.Groups[3].Value);
                if (!File.Exists(first)) throw new FileNotFoundException("분할 압축의 첫 볼륨을 찾을 수 없습니다.", first);
                return first;
            }
            Match split = Regex.Match(filename, @"^(.*\.(?:7z|zip))\.(\d{3})$", RegexOptions.IgnoreCase);
            if (split.Success)
            {
                string first = Path.Combine(Path.GetDirectoryName(input), split.Groups[1].Value + ".001");
                if (!File.Exists(first)) throw new FileNotFoundException("분할 압축의 첫 볼륨을 찾을 수 없습니다.", first);
                return first;
            }
            return input;
        }

        internal static string[] NormalizeInputs(Request request)
        {
            bool extracting = request.Operation == Operation.SmartExtract || request.Operation == Operation.ExtractToFolder ||
                request.Operation == Operation.ExtractDialog || request.Operation == Operation.ExtractHere || request.Operation == Operation.Test;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (string original in request.Paths)
            {
                string path = Path.GetFullPath(original);
                if (extracting) path = FirstVolume(path);
                if (!unique.Add(path)) continue;
                if (extracting && (!File.Exists(path) || !IsArchive(path)))
                    throw new InvalidDataException("선택한 항목 전체가 지원하는 압축 파일이어야 합니다: " + path);
                if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("선택한 항목이 없습니다.", path);
                if (String.Equals(path.TrimEnd('\\'), Path.GetPathRoot(path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("드라이브 전체는 이 메뉴의 대상이 아닙니다.");
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("링크나 연결 지점은 이 버전에서 지원하지 않습니다: " + path);
                result.Add(path.TrimEnd(Path.DirectorySeparatorChar));
            }
            return result.ToArray();
        }

        private static bool IsArchive(string input)
        {
            string lower = input.ToLowerInvariant();
            return new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".7z.001", ".zip.001" }.Any(lower.EndsWith);
        }

        internal static string ArchiveStem(string input)
        {
            string name = Path.GetFileName(input);
            Match rar = Regex.Match(name, @"^(.*)\.part\d+\.rar$", RegexOptions.IgnoreCase);
            if (rar.Success) return ValidStem(rar.Groups[1].Value);
            foreach (string suffix in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".7z.001", ".zip.001", ".tgz", ".tbz2", ".txz" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return ValidStem(name.Substring(0, name.Length - suffix.Length));
            return ValidStem(Path.GetFileNameWithoutExtension(name));
        }

        private static string ValidStem(string value) { return String.IsNullOrEmpty(value) ? "archive" : value; }
        private static string CreateStage(string parent)
        {
            string stage = Path.Combine(parent, ".7ze-work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            File.SetAttributes(stage, File.GetAttributes(stage) | FileAttributes.Hidden);
            return stage;
        }

        private static void CheckExtractedTree(string stage)
        {
            var pending = new Stack<string>(); pending.Push(stage);
            while (pending.Count != 0)
            {
                string path = pending.Pop();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("압축 파일 안의 링크·연결 지점은 지원하지 않습니다.");
                if ((attributes & FileAttributes.Directory) != 0)
                    foreach (string child in Directory.GetFileSystemEntries(path)) pending.Push(child);
            }
        }

        private static void DeleteTreeWithoutFollowingLinks(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                    foreach (string child in Directory.GetFileSystemEntries(path)) DeleteTreeWithoutFollowingLinks(child);
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                Directory.Delete(path);
            }
            else { File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly); File.Delete(path); }
        }
        private static void Cleanup(string stage)
        {
            if (stage == null || !Path.GetFileName(stage).StartsWith(".7ze-work-", StringComparison.Ordinal) || !Directory.Exists(stage)) return;
            try { DeleteTreeWithoutFollowingLinks(stage); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private string Compress(string input, string format)
        {
            if (Directory.Exists(input)) CheckExtractedTree(input);
            string parent = Path.GetDirectoryName(input);
            string stage = CreateStage(parent);
            try
            {
                string temporary = Path.Combine(stage, "payload." + format);
                ProcessResult process = RunSevenZip(parent, new[] { "a", "-t" + format, "-sse", "-spd", "-sccUTF-8", "-bsp0", "--", temporary, Path.GetFileName(input) }, null);
                RequireSuccess(process);
                cancellation.ThrowIfCancellationRequested();
                string name = Directory.Exists(input) ? Path.GetFileName(input) : Path.GetFileNameWithoutExtension(input);
                return Publish(temporary, parent, ValidStem(name) + "." + format, false);
            }
            finally { Cleanup(stage); }
        }

        private string CompressTogether(string[] inputs, string format)
        {
            foreach (string input in inputs) if (Directory.Exists(input)) CheckExtractedTree(input);
            string parent = Path.GetDirectoryName(inputs[0]);
            if (inputs.Any(p => !String.Equals(Path.GetDirectoryName(p), parent, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("하나로 압축할 항목은 같은 폴더에서 선택해 주세요.");
            string stage = CreateStage(parent);
            try {
                string list = Path.Combine(stage, "inputs.txt");
                File.WriteAllLines(list, inputs.Select(Path.GetFileName), new UTF8Encoding(false));
                string temporary = Path.Combine(stage, "payload." + format);
                RequireSuccess(RunSevenZip(parent, new[] { "a", "-t" + format, "-sse", "-spd", "-scsUTF-8", "-sccUTF-8", "-bsp0", "-i@" + list, "--", temporary }, null));
                cancellation.ThrowIfCancellationRequested();
                string stem = inputs.Length == 1 ? (Directory.Exists(inputs[0]) ? Path.GetFileName(inputs[0]) : Path.GetFileNameWithoutExtension(inputs[0])) : Path.GetFileName(parent);
                return Publish(temporary, parent, ValidStem(stem) + "." + format, false);
            } finally { Cleanup(stage); }
        }

        private string ExtractToStage(string input, string parent)
        {
            string password = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                string stage = CreateStage(parent);
                bool keep = false;
                try
                {
                    ProcessResult process = RunSevenZip(parent, new[] { "x", "-y", "-snz", "-sccUTF-8", "-bsp0", "-o" + stage, "--", input }, password);
                    if (process.ExitCode == 0) { CheckExtractedTree(stage); keep = true; return stage; }
                    if (process.Text.IndexOf("password", StringComparison.OrdinalIgnoreCase) < 0 || attempt == 3)
                        RequireSuccess(process);
                    password = askPassword(input);
                    if (password == null) throw new OperationCanceledException();
                }
                finally { if (!keep) Cleanup(stage); }
            }
            throw new InvalidDataException("암호를 확인할 수 없습니다.");
        }

        private string Extract(string input, bool smart)
        {
            string parent = Path.GetDirectoryName(input);
            string stage = null;
            try
            {
                stage = ExtractToStage(input, parent);
                string[] contents = Directory.GetFileSystemEntries(stage);
                // Unwrap the TAR layer of tar.gz/tgz/tar.bz2/tar.xz without unpacking arbitrary nested archives.
                bool compressedTar = new[] { ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".tar.xz", ".txz" }.Any(s => input.EndsWith(s, StringComparison.OrdinalIgnoreCase));
                if (compressedTar && contents.Length == 1 && File.Exists(contents[0]) && contents[0].EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                {
                    string inner = ExtractToStage(contents[0], parent);
                    Cleanup(stage); stage = inner;
                    contents = Directory.GetFileSystemEntries(stage);
                }
                cancellation.ThrowIfCancellationRequested();
                if (smart && contents.Length == 1)
                    return Publish(contents[0], parent, Path.GetFileName(contents[0]), Directory.Exists(contents[0]));
                File.SetAttributes(stage, File.GetAttributes(stage) & ~FileAttributes.Hidden);
                string output = Publish(stage, parent, ArchiveStem(input), true);
                stage = null;
                return output;
            }
            finally { Cleanup(stage); }
        }

        private static string Publish(string source, string parent, string name, bool directory)
        {
            string stem = directory ? name : Path.GetFileNameWithoutExtension(name);
            string extension = directory ? "" : Path.GetExtension(name);
            for (int number = 1; number <= 10000; number++)
            {
                string candidate = Path.Combine(parent, number == 1 ? name : stem + " (" + number + ")" + extension);
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                try
                {
                    if (directory) Directory.Move(source, candidate); else File.Move(source, candidate);
                    return candidate;
                }
                catch (IOException) { if (File.Exists(candidate) || Directory.Exists(candidate)) continue; throw; }
            }
            throw new IOException("사용 가능한 출력 이름을 찾지 못했습니다.");
        }

        internal static string QuoteArgument(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
                else result.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private ProcessResult RunSevenZip(string workingDirectory, IEnumerable<string> arguments, string password)
        {
            cancellation.ThrowIfCancellationRequested();
            RuntimeSafety.EnsureStandardUser();
            RuntimeSafety.RequireSupportedSevenZip(sevenZip);
            var start = new ProcessStartInfo(sevenZip, String.Join(" ", arguments.Select(QuoteArgument))) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var output = new StringBuilder();
            object gate = new object();
            DataReceivedEventHandler receive = delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data == null) return;
                lock (gate) { output.AppendLine(e.Data); if (output.Length > 24000) output.Remove(0, output.Length - 16000); }
            };
            using (var process = new Process { StartInfo = start })
            {
                process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                try
                {
                    try {
                        if (password != null) {
                            byte[] bytes = Encoding.UTF8.GetBytes(password + "\n");
                            process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                            process.StandardInput.BaseStream.Flush();
                            Array.Clear(bytes, 0, bytes.Length);
                        }
                        process.StandardInput.Close();
                    } catch (IOException) { }
                    while (!process.WaitForExit(100)) cancellation.ThrowIfCancellationRequested();
                    process.WaitForExit();
                    cancellation.ThrowIfCancellationRequested();
                    lock (gate) return new ProcessResult { ExitCode = process.ExitCode, Text = output.ToString() };
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            }
        }
        private static void RequireSuccess(ProcessResult process)
        {
            if (process.ExitCode != 0)
                throw new IOException("7-Zip 작업이 완료되지 않았습니다 (코드 " + process.ExitCode + ").\r\n" + process.Text);
        }
    }

    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox password = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Top };
        internal string Password { get { return password.Text; } }
        internal PasswordDialog(string filename)
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Text = "압축 파일 암호"; Width = 460; Height = 180; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var label = new Label { Text = Path.GetFileName(filename) + "\r\n암호를 입력하세요.", Dock = DockStyle.Top, Height = 45 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "확인", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            Controls.Add(password); Controls.Add(label); Controls.Add(buttons); Padding = new Padding(12);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    internal sealed class JobWindow : Form
    {
        private readonly Request request;
        private readonly string resultPath;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Label current = new Label { Dock = DockStyle.Top, Height = 48, AutoEllipsis = true };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Height = 20 };
        private readonly TextBox details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        private readonly Button button = new Button { Text = "취소", Dock = DockStyle.Bottom, Height = 32 };
        private bool running;
        internal JobWindow(Request request, string resultPath)
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.request = request; this.resultPath = resultPath;
            Text = "7-Zip 확장"; Width = 620; Height = 320; Padding = new Padding(12);
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(details); Controls.Add(progress); Controls.Add(current); Controls.Add(button);
            button.Click += delegate { if (running) { cancellation.Cancel(); button.Enabled = false; current.Text = "취소하는 중…"; } else Close(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; cancellation.Cancel(); button.Enabled = false; } };
            Shown += async delegate { await RunJob(); };
        }
        private async Task RunJob()
        {
            running = true;
            var engine = new Engine(cancellation.Token,
                delegate(string file) { return (string)Invoke(new Func<string>(delegate {
                    if (cancellation.IsCancellationRequested) return null;
                    using (var dialog = new PasswordDialog(file)) return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Password : null;
                })); },
                delegate(string text) { BeginInvoke(new Action(delegate { if (!cancellation.IsCancellationRequested) current.Text = text; })); });
            JobResult result = await Task.Run(() => engine.Run(request));
            running = false; progress.Style = ProgressBarStyle.Blocks; progress.Value = 100; button.Enabled = true; button.Text = "닫기";
            try { Program.WriteResult(resultPath, result); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Program.ExitCode = result.Success ? 0 : result.Cancelled ? 2 : 1;
            if (result.Success) { Close(); return; }
            current.Text = result.Cancelled ? "작업을 취소했습니다. 완료된 항목은 유지됩니다." : "일부 작업을 완료하지 못했습니다.";
            details.Text = (result.Error ?? "") + String.Join("\r\n\r\n", result.Items.Select(i =>
                i.Input + "\r\n" + (i.Success ? "완료: " + i.Output : i.Error)));
        }
    }

    internal static class Program
    {
        internal static int ExitCode;
        [STAThread]
        private static int Main(string[] args)
        {
            bool headless = args.Contains("--headless");
            string requestFile = null;
            string resultFile = null;
            bool standardUserVerified = false;
            try
            {
                // Reject an inherited administrator token before opening or deleting requests,
                // changing settings, launching 7-Zip, or writing even an error result.
                RuntimeSafety.EnsureStandardUser();
                standardUserVerified = true;
                if (args.Length == 1 && args[0] == "--settings") {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new SettingsWindow()); return 0;
                }
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--request" && i + 1 < args.Length) requestFile = Path.GetFullPath(args[++i]);
                    else if (args[i] == "--result" && i + 1 < args.Length) resultFile = Path.GetFullPath(args[++i]);
                    else if (args[i] != "--headless" && args[i] != "--password-stdin") throw new ArgumentException("알 수 없는 실행 옵션입니다.");
                }
                if (requestFile == null) throw new ArgumentException("작업 목록이 필요합니다.");
                Request request = Request.Read(requestFile);
                File.Delete(requestFile);
                if (request.Operation == Operation.Settings) {
                    if (headless) throw new ArgumentException("설정은 설정 창에서 변경해 주세요.");
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new SettingsWindow()); return 0;
                }
                if (request.Operation == Operation.Diagnose)
                {
                    WriteResult(resultFile ?? Path.ChangeExtension(requestFile, ".json"), new Engine(CancellationToken.None, null, null).Run(request));
                    return 0;
                }
                if (NativeCommands.Handles(request.Operation)) {
                    if (headless) throw new ArgumentException("이 명령은 7-Zip 창을 사용합니다.");
                    NativeCommands.Run(request);
                    return 0;
                }
                if (headless)
                {
                    bool passwordStdin = args.Contains("--password-stdin");
                    if (passwordStdin) Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
                    JobResult result = new Engine(CancellationToken.None,
                        delegate(string unused) { return passwordStdin ? Console.ReadLine() : null; }, delegate(string unused) { }).Run(request);
                    WriteResult(resultFile ?? Path.ChangeExtension(requestFile, ".json"), result);
                    return result.Success ? 0 : result.Cancelled ? 2 : 1;
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new JobWindow(request, resultFile));
                return ExitCode;
            }
            catch (Exception error)
            {
                if (headless && standardUserVerified && resultFile != null) { try { WriteResult(resultFile, new JobResult { Error = error.Message }); } catch { } }
                else if (!headless) MessageBox.Show(error.Message, "7-Zip 확장", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        internal static void WriteResult(string path, JobResult result)
        {
            if (path == null) return;
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                new DataContractJsonSerializer(typeof(JobResult)).WriteObject(stream, result);
        }
    }
}
