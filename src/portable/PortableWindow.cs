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
        internal readonly Button Options = new Button { Text = "설정 열기", AutoSize = true, Enabled = false };
        internal readonly NumericUpDown FontSize = new NumericUpDown { Minimum = 9, Maximum = 18, Width = 64, Enabled = false };
        internal readonly Label Status = new Label { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(336, 0), Margin = new Padding(4, 8, 4, 2) };
        internal bool Busy { get; private set; }
        private readonly string appRoot;
        private readonly string testRegistryRoot;
        private bool loadingAppearance;
        private int savedFontSize;
        private string statusDetails = "";
        private string lastError;
        private readonly TableLayoutPanel layout;
        private readonly FlowLayoutPanel appearance;

        internal PortableWindow(string appRoot, string testRegistryRoot = null)
        {
            if (testRegistryRoot != null && !Regex.IsMatch(testRegistryRoot, @"^Software\\SevenZipContextMenu\\PortableTests\\[a-f0-9]{32}$"))
                throw new ArgumentException("Invalid isolated registry root.");
            this.appRoot = Path.GetFullPath(appRoot);
            this.testRegistryRoot = testRegistryRoot;
            Text = "7-Zip 확장 · 무설치";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            savedFontSize = AppearanceStore.ReadFontSize();
            Font = new Font("맑은 고딕", savedFontSize); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(360, 130);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; HelpButton = true;
            Padding = new Padding(12); StartPosition = FormStartPosition.CenterScreen;
            layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach (Button button in new[] { Register, Unregister, Options }) {
                button.Dock = DockStyle.Top; button.MinimumSize = new Size(150, 32); button.Margin = new Padding(4);
            }
            appearance = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(4), Padding = new Padding(0, 4, 0, 0) };
            appearance.Controls.Add(new Label { Text = "글자 크기", AutoSize = true, Margin = new Padding(0, 3, 4, 0) });
            FontSize.Value = savedFontSize; appearance.Controls.Add(FontSize);
            appearance.Controls.Add(new Label { Text = "pt", AutoSize = true, Margin = new Padding(2, 3, 0, 0) });
            layout.Controls.Add(Register, 0, 0); layout.Controls.Add(Unregister, 1, 0);
            layout.Controls.Add(Options, 0, 1); layout.Controls.Add(appearance, 1, 1);
            layout.Controls.Add(Status, 0, 2); layout.SetColumnSpan(Status, 2); Controls.Add(layout);
            layout.SizeChanged += delegate { FitHeight(); };
            Shown += delegate { FitLauncher(); };
            HelpButtonClicked += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = true; ShowHelp(); };
            HelpRequested += delegate(object sender, HelpEventArgs e) { e.Handled = true; ShowHelp(); };
            FontSize.ValueChanged += delegate { SaveFontSize(); };
            FontSize.TextChanged += delegate {
                int points;
                if (Int32.TryParse(FontSize.Text, out points) && points >= FontSize.Minimum && points <= FontSize.Maximum && points != FontSize.Value)
                    FontSize.Value = points;
            };
            Register.Click += async delegate { await ChangeRegistration("Register"); };
            Unregister.Click += async delegate { await ChangeRegistration("Unregister"); };
            Options.Click += delegate {
                try {
                    RuntimeSafety.EnsureStandardUser();
                    Process.Start(new ProcessStartInfo(Path.Combine(this.appRoot, "SevenZipContextWorker.exe"), "--settings") {
                        UseShellExecute = false, WorkingDirectory = this.appRoot });
                } catch (Exception error) { ShowError(error.Message); }
            };
            Activated += async delegate { if (!Busy) await RefreshStatus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (Busy) e.Cancel = true; };
            FitLauncher();
        }

        private void FitHeight()
        {
            int height = layout.Height + Padding.Vertical;
            if (ClientSize.Height != height) ClientSize = new Size(ClientSize.Width, height);
        }
        private void FitLauncher()
        {
            int columnWidth = Math.Max(Register.GetPreferredSize(Size.Empty).Width + Register.Margin.Horizontal,
                appearance.GetPreferredSize(Size.Empty).Width + appearance.Margin.Horizontal);
            int width = Math.Max((int)Math.Ceiling(360 * DeviceDpi / 96F), columnWidth * 2 + Padding.Horizontal);
            ClientSize = new Size(width, ClientSize.Height);
            Status.MaximumSize = new Size(width - Padding.Horizontal - Status.Margin.Horizontal, 0);
            layout.PerformLayout(); FitHeight();
        }
        private void ApplyFontSize(int points)
        {
            if (Font.Size == points) return;
            Font oldFont = Font; Font = new Font("맑은 고딕", points); oldFont.Dispose();
            FitLauncher();
        }
        private void SaveFontSize()
        {
            if (loadingAppearance || !FontSize.Enabled) return;
            try {
                int points = (int)FontSize.Value;
                AppearanceStore.WriteFontSize(points); savedFontSize = points; ApplyFontSize(points); lastError = null;
            } catch (Exception error) {
                loadingAppearance = true; FontSize.Value = savedFontSize; loadingAppearance = false;
                ShowError(error.Message);
            }
        }
        private void ShowError(string message)
        {
            lastError = message;
            Status.Text = "작업을 완료하지 못했습니다. ? 도움말을 확인하세요.";
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private void ShowHelp()
        {
            string help = "메뉴 등록\r\n현재 사용자에게 메뉴를 등록합니다. 등록·갱신 후에는 기존 파일관리자를 완전히 종료한 뒤 다시 실행하세요.\r\n\r\n" +
                "메뉴 해제\r\n포터블이 소유한 메뉴와 설정 바로 가기를 정리합니다. 개인 옵션은 보관합니다.\r\n\r\n" +
                "설정 열기\r\n기존 옵션 페이지에서 메뉴 항목·순서와 반디집 연동을 설정합니다.\r\n\r\n" +
                "글자 크기\r\n등록 후 9~18pt로 바꾸면 바로 저장하고 이 창에 반영합니다. 설정 창에도 열거나 다시 선택할 때 반영합니다.\r\n\r\n" +
                "다른 위치의 포터블은 메뉴 등록으로 다시 연결할 수 있습니다. 설치형과 포터블의 등록이 함께 있으면 기존 등록을 먼저 확인하세요.\r\n\r\n현재 상태\r\n" + statusDetails;
            if (lastError != null) help += "\r\n\r\n최근 오류\r\n" + lastError;
            MessageBox.Show(this, help, Text + " 도움말", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DisableActions()
        {
            Register.Enabled = false; Unregister.Enabled = false; Options.Enabled = false; FontSize.Enabled = false;
        }
        private void StatusUnavailable(Exception error)
        {
            DisableActions(); lastError = error.Message;
            statusDetails = "현재 등록 상태를 확인할 수 없습니다.";
            Status.Text = "등록 상태를 확인하지 못했습니다. ? 도움말을 확인하세요.";
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
                FontSize.Enabled = state.CanOptions;
                loadingAppearance = true; savedFontSize = AppearanceStore.ReadFontSize(); FontSize.Value = savedFontSize; loadingAppearance = false;
                ApplyFontSize(savedFontSize); statusDetails = state.Message;
                if (state.CanOptions) Status.Text = "등록 완료 · 설정과 글자 크기를 변경할 수 있습니다.\r\n기존 파일관리자는 완전히 종료한 뒤 다시 실행하세요.";
                else if (state.CanRegister && state.CanUnregister) Status.Text = "메뉴를 다시 등록하거나 남은 항목을 해제하세요.";
                else if (state.CanRegister) Status.Text = "메뉴 등록을 눌러 시작하세요.";
                else if (state.CanUnregister) Status.Text = "남은 포터블 항목을 메뉴 해제로 정리할 수 있습니다.";
                else Status.Text = "기존 등록을 확인하세요. 자세한 내용은 ? 도움말에서 확인할 수 있습니다.";
            }
        }
        internal async Task RefreshStatus()
        {
            if (Busy) return;
            Busy = true; DisableActions(); Status.Text = "메뉴 등록 상태를 확인하고 있습니다…";
            statusDetails = Status.Text;
            try { await ReadStatus(); }
            catch (Exception error) { StatusUnavailable(error); }
            finally { Busy = false; }
        }
        private async Task ChangeRegistration(string action)
        {
            if (Busy) return;
            Busy = true; lastError = null; DisableActions(); Status.Text = action == "Register" ? "메뉴를 등록하고 있습니다…" : "메뉴를 해제하고 있습니다…";
            statusDetails = Status.Text;
            string failure = null;
            try { await RunScript(action); }
            catch (Exception error) { failure = error.Message; }
            try { await ReadStatus(); }
            catch (Exception error) { StatusUnavailable(error); }
            Busy = false;
            if (failure != null) ShowError(failure);
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
