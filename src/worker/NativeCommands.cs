using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SevenZipContextMenu
{
    // The installed 7-Zip owns its dialogs and their preferences.
    internal sealed class LaunchPlan
    {
        internal string Executable;
        internal string WorkingDirectory;
        internal string[] Arguments;
    }
    internal static class NativeCommands
    {
        internal static bool Handles(Operation operation)
        {
            return (operation >= Operation.Open && operation <= Operation.AddDialog) || operation >= Operation.HashCrc32;
        }
        internal static string UnusedName(string directory, string stem, string extension)
        {
            for (int i = 1; i <= 10000; i++) {
                string path = Path.Combine(directory, stem + (i == 1 ? "" : " (" + i + ")") + extension);
                if (!File.Exists(path) && !Directory.Exists(path)) return path;
            }
            throw new IOException("사용 가능한 출력 이름을 찾지 못했습니다.");
        }
        internal static LaunchPlan BuildPlan(Operation operation, string[] paths, string sevenZipDirectory, string listFile)
        {
            if (!Handles(operation) || paths.Length == 0) throw new ArgumentException("지원하지 않는 명령입니다.");
            string parent = Path.GetDirectoryName(paths[0]);
            var args = new List<string>();
            string executable = "7zG.exe";
            if (operation >= Operation.Open && operation <= Operation.OpenRar) {
                if (paths.Length != 1 || !File.Exists(paths[0])) throw new ArgumentException("열기는 파일 하나를 선택해 주세요.");
                executable = "7zFM.exe"; args.Add(paths[0]);
                string[] types = { null, null, "*", "#", "#:e", "7z", "zip", "cab", "rar" };
                string type = types[(int)operation - (int)Operation.Open];
                if (type != null) args.Add("-t" + type);
            } else {
                string stem = paths.Length == 1
                    ? (Directory.Exists(paths[0]) ? Path.GetFileName(paths[0]) : Path.GetFileNameWithoutExtension(paths[0]))
                    : Path.GetFileName(parent);
                if (String.IsNullOrEmpty(stem)) stem = "archive";
                if (operation == Operation.AddDialog) {
                    if (paths.Any(p => !String.Equals(Path.GetDirectoryName(p), parent, StringComparison.OrdinalIgnoreCase)))
                        throw new ArgumentException("같은 폴더의 항목을 선택해 주세요.");
                    // -saa lets the dialog append the chosen type's extension.
                    string outputBase = Path.ChangeExtension(UnusedName(parent, stem, ".7z"), null);
                    args.AddRange(new[] { "a", "-ad", "-saa", "-i@" + listFile, "--", outputBase });
                } else if (operation == Operation.ExtractDialog || operation == Operation.ExtractHere) {
                    args.AddRange(new[] { "x", "-an", "-ai@" + listFile, "-aou", "-snz", "-o" +
                        (operation == Operation.ExtractDialog ? Path.Combine(parent, Engine.ArchiveStem(paths[0])) : parent) });
                    if (operation == Operation.ExtractDialog) args.Add("-ad");
                } else if (operation == Operation.Test || operation == Operation.HashTest) {
                    args.AddRange(new[] { "t", "-an", "-ai@" + listFile });
                    if (operation == Operation.HashTest) args.Add("-thash");
                } else if (operation == Operation.HashGenerate) {
                    args.AddRange(new[] { "a", "-thash", "-scrcSHA256", "-sae", "-i@" + listFile, "--", UnusedName(parent, stem, ".sha256") });
                } else {
                    string[] methods = { "CRC32", "CRC64", "XXH64", "MD5", "SHA1", "SHA256", "SHA384", "SHA512", "SHA3-256", "BLAKE2sp", "*" };
                    args.AddRange(new[] { "h", "-scrc" + methods[(int)operation - (int)Operation.HashCrc32], "-i@" + listFile });
                }
                int stop = args.IndexOf("--");
                args.InsertRange(stop < 0 ? args.Count : stop, new[] { "-scsUTF-8", "-spd" });
            }
            return new LaunchPlan { Executable = Path.Combine(sevenZipDirectory, executable), WorkingDirectory = parent, Arguments = args.ToArray() };
        }
        internal static void Run(Request request)
        {
            RuntimeSafety.EnsureStandardUser();
            string[] paths = Engine.NormalizeInputs(request);
            string list = null;
            try {
                if (request.Operation > Operation.OpenRar) {
                    Directory.CreateDirectory(Request.DirectoryPath);
                    list = Path.Combine(Request.DirectoryPath, Guid.NewGuid().ToString("N") + ".txt");
                    using (var stream = new FileStream(list, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        foreach (string path in paths) writer.WriteLine(request.Operation == Operation.AddDialog ? Path.GetFileName(path) : path);
                }
                LaunchPlan plan = BuildPlan(request.Operation, paths, Path.GetDirectoryName(Engine.FindSevenZip()), list);
                RuntimeSafety.RequireSupportedSevenZip(plan.Executable);
                using (var process = Process.Start(new ProcessStartInfo(plan.Executable, String.Join(" ", plan.Arguments.Select(Engine.QuoteArgument))) {
                    UseShellExecute = false, WorkingDirectory = plan.WorkingDirectory })) {
                    process.WaitForExit();
                    if (process.ExitCode != 0 && process.ExitCode != 255)
                        throw new IOException("7-Zip 작업이 완료되지 않았습니다 (코드 " + process.ExitCode + ").");
                }
            } finally { if (list != null) try { File.Delete(list); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
