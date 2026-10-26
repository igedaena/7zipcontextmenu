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
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            OptionsStore.Write(OptionsStore.Defaults);
            using (var form = new SettingsWindow()) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                form.Show(); Application.DoEvents();
                Check(form.ReadControls() == OptionsStore.Defaults && !form.Apply.Enabled && form.ShowSettings.Checked, "Existing settings keep the settings entry visible");
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
            string sevenZip = Path.GetDirectoryName(Engine.FindSevenZip());
            foreach (string name in new[] { "7z.exe", "7zG.exe", "7zFM.exe" }) {
                RuntimeSafety.RequireSupportedSevenZip(Path.Combine(sevenZip, name));
                Check(true, "Installed tool and engine meet minimum: " + name);
            }
            string source = Path.Combine(root, "한글 [1] & @문서.txt"); File.WriteAllText(source, "original");
            string other = Path.Combine(root, "-추가.txt"); File.WriteAllText(other, "other");
            var engine = new Engine(CancellationToken.None, delegate { return null; }, delegate { });
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
