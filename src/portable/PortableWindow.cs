// Copyright (C) 2026 igedaena
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SevenZipContextMenu
{
    [DataContract]
    internal sealed class PortableStatus
    {
        [DataMember] public bool CanRegister { get; set; }
        [DataMember] public bool CanUnregister { get; set; }
        [DataMember] public bool CanOptions { get; set; }
        [DataMember] public string Message { get; set; }
    }

    internal sealed class PortableWindow : Form
    {
        internal readonly Button Register = new Button { Text = "메뉴 등록", AutoSize = true, Enabled = false };
        internal readonly Button Unregister = new Button { Text = "메뉴 해제", AutoSize = true, Enabled = false };
        internal readonly Button Options = new Button { Text = "옵션", AutoSize = true, Enabled = false };
        internal readonly TextBox Status = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
        internal bool Busy { get; private set; }
        private readonly string appRoot;
        private readonly string testRegistryRoot;

        internal PortableWindow(string appRoot, string testRegistryRoot = null)
        {
            if (testRegistryRoot != null && !Regex.IsMatch(testRegistryRoot, @"^Software\\SevenZipContextMenu\\PortableTests\\[a-f0-9]{32}$"))
                throw new ArgumentException("Invalid isolated registry root.");
            this.appRoot = Path.GetFullPath(appRoot);
            this.testRegistryRoot = testRegistryRoot;
            Text = "7-Zip 확장 · 무설치";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("맑은 고딕", 10F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(510, 210); MinimumSize = new Size(460, 240);
            Padding = new Padding(18); StartPosition = FormStartPosition.CenterScreen;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
            buttons.Controls.AddRange(new Control[] { Register, Unregister, Options });
            Controls.Add(Status); Controls.Add(buttons);
            Register.Click += async delegate { await ChangeRegistration("Register"); };
            Unregister.Click += async delegate { await ChangeRegistration("Unregister"); };
            Options.Click += delegate {
                try {
                    RuntimeSafety.EnsureStandardUser();
                    Process.Start(new ProcessStartInfo(Path.Combine(this.appRoot, "SevenZipContextWorker.exe"), "--settings") {
                        UseShellExecute = false, WorkingDirectory = this.appRoot });
                } catch (Exception error) { Status.Text = error.Message; }
            };
            Activated += async delegate { if (!Busy) await RefreshStatus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (Busy) e.Cancel = true; };
        }

        private void DisableActions()
        {
            Register.Enabled = false; Unregister.Enabled = false; Options.Enabled = false;
        }
        private async Task<string> RunScript(string action)
        {
            RuntimeSafety.EnsureStandardUser();
            string script = Path.Combine(appRoot, "scripts", "Portable-Menu.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("무설치 폴더의 scripts 파일이 필요합니다.", script);
            string arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -Action " + action + " -AsJson";
            if (testRegistryRoot != null) arguments += " -TestRegistryRoot " + testRegistryRoot;
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            using (var process = new Process { StartInfo = new ProcessStartInfo(powershell, arguments) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = appRoot,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } }) {
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                await Task.Run((Action)process.WaitForExit);
                string text = await output; string error = await errors;
                if (process.ExitCode != 0) throw new IOException(String.IsNullOrWhiteSpace(error) ? text : error);
                return text;
            }
        }
        private async Task ReadStatus()
        {
            string json = await RunScript("Status");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json))) {
                var state = (PortableStatus)new DataContractJsonSerializer(typeof(PortableStatus)).ReadObject(stream);
                Register.Enabled = state.CanRegister; Unregister.Enabled = state.CanUnregister; Options.Enabled = state.CanOptions;
                Status.Text = state.Message;
            }
        }
        internal async Task RefreshStatus()
        {
            if (Busy) return;
            Busy = true; DisableActions(); Status.Text = "메뉴 등록 상태를 확인하고 있습니다…";
            try { await ReadStatus(); }
            catch (Exception error) { DisableActions(); Status.Text = error.Message; }
            finally { Busy = false; }
        }
        private async Task ChangeRegistration(string action)
        {
            if (Busy) return;
            Busy = true; DisableActions(); Status.Text = action == "Register" ? "메뉴를 등록하고 있습니다…" : "메뉴를 해제하고 있습니다…";
            string failure = null;
            try { await RunScript(action); }
            catch (Exception error) { failure = error.Message; }
            try { await ReadStatus(); }
            catch (Exception error) { DisableActions(); Status.Text = error.Message; }
            if (failure != null) Status.Text = "작업을 완료하지 못했습니다.\r\n" + failure + "\r\n\r\n" + Status.Text;
            Busy = false;
        }
    }

    internal static class PortableProgram
    {
        [STAThread]
        private static int Main()
        {
            try {
                RuntimeSafety.EnsureStandardUser();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PortableWindow(AppDomain.CurrentDomain.BaseDirectory)); return 0;
            } catch (Exception error) {
                MessageBox.Show(error.Message, "7-Zip 확장 · 무설치", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
    }
}
