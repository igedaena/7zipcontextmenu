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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SevenZipContextMenu;

internal static class NativeProgressTests
{
    private static int checks;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
    private static string Cli(string directory, params string[] arguments) {
        using (var process = Process.Start(new ProcessStartInfo(Engine.FindSevenZip(), String.Join(" ", arguments.Select(Engine.QuoteArgument))) {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })) {
            string text = process.StandardOutput.ReadToEnd(); string errors = process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("Fixture CLI failed: " + errors);
            return text;
        }
    }
    private sealed class DialogMonitor : IDisposable
    {
        internal bool SawProgress, SawPassword, CancelNative, CancelSent;
        internal string Password, Screenshot;
        internal string Error;
        internal readonly StringBuilder Diagnostics=new StringBuilder();
        private readonly HashSet<IntPtr> diagnosed=new HashSet<IntPtr>();
        private volatile bool stopped;
        private readonly Thread thread;
        private readonly HashSet<int> owned = new HashSet<int>();
        private readonly HashSet<int> ignored = new HashSet<int>();
        private readonly int parent = Process.GetCurrentProcess().Id;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly Stopwatch progressElapsed=new Stopwatch();
        private delegate bool EnumProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr word, IntPtr value);
        [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] private static extern IntPtr SendText(IntPtr window, uint message, IntPtr parameter, string text);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
        internal DialogMonitor() { thread = new Thread(Watch); thread.IsBackground = true; thread.Start(); }
        private static string Text(IntPtr window) { var text = new StringBuilder(1024); GetWindowText(window, text, text.Capacity); return text.ToString(); }
        private bool Owns(int id) {
            if (owned.Contains(id)) return true;
            if (ignored.Contains(id)) return false;
            try {
                using (var query = new ManagementObjectSearcher("SELECT ParentProcessId FROM Win32_Process WHERE ProcessId=" + id))
                using (var results = query.Get()) foreach (ManagementObject result in results) {
                    using (result) if (Convert.ToInt32(result["ParentProcessId"]) == parent) { owned.Add(id); return true; }
                }
            } catch (ManagementException) { }
            ignored.Add(id); return false;
        }
        private void Watch() {
            try {
                while (!stopped) {
                    var candidates = new HashSet<int>();
                    foreach (var process in Process.GetProcessesByName("7zG")) using (process) if (Owns(process.Id)) candidates.Add(process.Id);
                    EnumWindows(delegate(IntPtr window, IntPtr unused) {
                        uint pid; GetWindowThreadProcessId(window, out pid);
                        if (!candidates.Contains((int)pid)) return true;
                        bool diagnose=Password!=null && IsWindowVisible(window) && diagnosed.Add(window);
                        if(diagnose)Diagnostics.AppendLine("WINDOW: "+Text(window));
                        bool progress = false; IntPtr password = IntPtr.Zero, edit = IntPtr.Zero;
                        EnumChildWindows(window, delegate(IntPtr child, IntPtr state) {
                            var name = new StringBuilder(128); GetClassName(child, name, name.Capacity);
                            if(diagnose)Diagnostics.AppendLine(name+" id="+GetDlgCtrlID(child)+" visible="+IsWindowVisible(child)+" style="+GetWindowLong(child,-16)+" text="+(name.ToString()=="Edit"?"[omitted]":Text(child)));
                            if (name.ToString() == "msctls_progress32") progress = true;
                            if (name.ToString() == "Edit" && IsWindowVisible(child) && (GetWindowLong(child,-16)&0x800)==0) {
                                if (edit == IntPtr.Zero) edit=child;
                                if ((GetWindowLong(child,-16)&0x20)!=0) password=child;
                            }
                            return true;
                        }, IntPtr.Zero);
                        // 7-Zip remembers the user's Show password option; an unmasked
                        // password field is still the password input in this dialog.
                        if (!progress && Password != null && password == IntPtr.Zero) password = edit;
                        if (password != IntPtr.Zero && Password != null) {
                            SawPassword = true; SendText(password, 0x000c, IntPtr.Zero, Password); PostMessage(window, 0x111, new IntPtr(1), IntPtr.Zero);
                        } else if (progress) {
                            SawProgress = true;
                            if(!progressElapsed.IsRunning)progressElapsed.Start();
                            if (Screenshot != null && progressElapsed.ElapsedMilliseconds>=700) {
                                Rect rect; if (GetWindowRect(window, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top) {
                                    using (var bitmap = new Bitmap(rect.Right-rect.Left, rect.Bottom-rect.Top)) {
                                        using (var graphics = Graphics.FromImage(bitmap)) { IntPtr dc=graphics.GetHdc(); try { PrintWindow(window,dc,2); } finally { graphics.ReleaseHdc(dc); } }
                                        bitmap.Save(Screenshot);
                                    }
                                }
                                Screenshot = null;
                            }
                            if (CancelNative && !CancelSent) { CancelSent = true; PostMessage(window,0x111,new IntPtr(2),IntPtr.Zero); }
                            string close = Text(GetDlgItem(window,2));
                            if (close.StartsWith("닫기") || close.IndexOf("Close",StringComparison.OrdinalIgnoreCase)>=0)
                                PostMessage(window,0x111,new IntPtr(2),IntPtr.Zero);
                        } else if (CancelSent && GetDlgItem(window,6)!=IntPtr.Zero) {
                            PostMessage(window,0x111,new IntPtr(6),IntPtr.Zero);
                        } else if (password == IntPtr.Zero && GetDlgItem(window,1)!=IntPtr.Zero) {
                            PostMessage(window,0x111,new IntPtr(1),IntPtr.Zero);
                        }
                        return true;
                    }, IntPtr.Zero);
                    if (elapsed.Elapsed.TotalSeconds > 45) {
                        Error="Native dialog timed out";
                        foreach(int id in owned) try { using(var process=Process.GetProcessById(id)) if(!process.HasExited)process.Kill(); } catch(ArgumentException) { }
                        return;
                    }
                    Thread.Sleep(25);
                }
            } catch(Exception error) {
                Error=error.ToString();
                foreach(int id in owned) try { using(var process=Process.GetProcessById(id)) if(!process.HasExited)process.Kill(); } catch(ArgumentException) { }
            }
        }
        public void Dispose() { stopped=true; thread.Join(5000); }
    }
    private static JobResult Gui(Operation operation, string[] paths, DialogMonitor monitor) {
        JobResult result;
        using(monitor) result=new Engine(CancellationToken.None,null,delegate {},true).Run(new Request {Operation=operation,Paths=paths});
        Check(monitor.Error==null,"Native GUI completed without test timeout");
        return result;
    }
    [STAThread] private static int Main(string[] args) {
        try {
            RuntimeSafety.EnsureStandardUser(); string root=args[0]; Directory.CreateDirectory(root);
            string input=Path.Combine(root,"payload.bin"); var random=new Random(7331); var block=new byte[1024*1024];
            using(var file=File.Create(input)) for(int i=0;i<48;i++){random.NextBytes(block);file.Write(block,0,block.Length);}
            var plan=NativeCommands.BuildProgressPlan(Engine.FindSevenZip(),root,new[]{"x","-bsp0","-sccUTF-8","-snz","--",input});
            Check(plan.Executable.EndsWith("7zG.exe") && !plan.Arguments.Contains("-bsp0") && !plan.Arguments.Contains("-sccUTF-8"),"GUI mode launches 7zG without console progress suppression");
            string other=Path.Combine(root,"other.txt");File.WriteAllText(other,"other file");
            var monitor=new DialogMonitor();
            var grouped=Gui(Operation.QuickZip,new[]{input,other},monitor);
            Check(grouped.Success && grouped.Items.Count==1,"Native progress creates one ZIP for multiple inputs");
            Check(monitor.SawProgress,"Observed real 7-Zip progress control");
            Check(Cli(root,"l","--",grouped.Items[0].Output).Contains("other.txt"),"Grouped ZIP contains both selected files");
            Cli(root,"a","-tzip","--","single.zip","payload.bin");
            var smart=Gui(Operation.SmartExtract,new[]{Path.Combine(root,"single.zip")},new DialogMonitor());
            Check(smart.Success && Path.GetFileName(smart.Items[0].Output)=="payload (2).bin","Smart extract keeps single-item layout and collision naming");
            var folder=Gui(Operation.ExtractToFolder,new[]{Path.Combine(root,"single.zip"),Path.Combine(root,"single.zip")},new DialogMonitor());
            Check(folder.Success && folder.Items.Count==1 && File.Exists(Path.Combine(folder.Items[0].Output,"payload.bin")),"File-name folder extraction keeps duplicate selection handling");
            var folderAgain=Gui(Operation.ExtractToFolder,new[]{Path.Combine(root,"single.zip")},new DialogMonitor());
            Check(folderAgain.Success && Path.GetFileName(folderAgain.Items[0].Output)=="single (2)","Existing destination folder is preserved");
            string preview=Engine.QuickArchivePath(new[]{Path.Combine(root,"single.zip")},"zip");
            var wrapped=Gui(Operation.QuickZip,new[]{Path.Combine(root,"single.zip")},new DialogMonitor());
            Check(wrapped.Success && wrapped.Items[0].Output==preview,"Archive wrapping uses the displayed new output name");
            Check(Cli(root,"l","--",wrapped.Items[0].Output).Contains("single.zip"),"Wrapping stores the original archive rather than converting it");
            var sevenMonitor=new DialogMonitor {Screenshot=Path.Combine(root,"native-progress.png")};
            var seven=Gui(Operation.Quick7z,new[]{input},sevenMonitor);
            Check(seven.Success && seven.Items[0].Output.EndsWith(".7z") && sevenMonitor.SawProgress,"Native 7z compression completes with native progress");
            Cli(root,"a","-ttar","--","bundle.tar","other.txt");
            Cli(root,"a","-tgzip","--","bundle.tar.gz","bundle.tar");
            var tarMonitor=new DialogMonitor();
            var tar=Gui(Operation.ExtractToFolder,new[]{Path.Combine(root,"bundle.tar.gz")},tarMonitor);
            Check(tar.Success && File.ReadAllText(Path.Combine(tar.Items[0].Output,"other.txt"))=="other file" && !File.Exists(Path.Combine(tar.Items[0].Output,"bundle.tar")),"Native TAR.GZ extraction unwraps both stages");
            Cli(root,"a","-t7z","-pfixture-pass","-mhe=on","--","encrypted.7z","other.txt");
            var passwordMonitor=new DialogMonitor {Password="fixture-pass"};
            var encrypted=Gui(Operation.ExtractToFolder,new[]{Path.Combine(root,"encrypted.7z")},passwordMonitor);
            if(!encrypted.Success || !passwordMonitor.SawPassword)
                throw new Exception("Native password input failed: "+encrypted.Error+String.Join(";",encrypted.Items.Select(i=>i.Error))+passwordMonitor.Diagnostics);
            Check(true,"7-Zip owns and accepts the archive password dialog");
            var cancelMonitor=new DialogMonitor {CancelNative=true};
            var cancelled=Gui(Operation.Compress7z,new[]{input},cancelMonitor);
            Check(cancelMonitor.CancelSent && cancelled.Cancelled && !File.Exists(Path.Combine(root,"payload (2).7z")) && File.Exists(seven.Items[0].Output),"Native Cancel stops publication and preserves completed output");
            Check(!Directory.GetDirectories(root,".7ze-work-*").Any(),"Success and native cancellation clean staging folders");
            File.WriteAllText(Path.Combine(root,"broken.zip"),"broken archive");
            var broken=Gui(Operation.ExtractToFolder,new[]{Path.Combine(root,"broken.zip")},new DialogMonitor());
            Check(!broken.Success && !Directory.Exists(Path.Combine(root,"broken")) && !Directory.GetDirectories(root,".7ze-work-*").Any(),"Native errors do not publish partial extraction results");
            Console.WriteLine("NATIVE PROGRESS TESTS PASSED: "+checks);return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
