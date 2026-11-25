// Copyright (C) 2026 igedaena
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using SevenZipContextMenu;

internal static class PortableUiTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Wait(PortableWindow form)
    {
        var timer = Stopwatch.StartNew();
        while (form.Busy) {
            if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException(form.Status.Text);
            Application.DoEvents(); Thread.Sleep(20);
        }
        Application.DoEvents();
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try {
            RuntimeSafety.EnsureStandardUser();
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(AppearanceStore.KeyPath)) {
                object savedFont = key.GetValue("FontSizeV1");
                RegistryValueKind savedKind = savedFont == null ? RegistryValueKind.None : key.GetValueKind("FontSizeV1");
                try {
                    AppearanceStore.WriteFontSize(9);
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using (var form = new PortableWindow(args[0], args[1])) {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                        form.Show(); Wait(form);
                        Check(form.Register.Enabled && !form.Unregister.Enabled && !form.Options.Enabled && !form.FontSize.Enabled, "Unregistered UI enables registration only: " + form.Status.Text);
                        Check(form.Width >= 320 && form.Width <= 430 && form.Height <= 240 && form.HelpButton && !form.MaximizeBox && !form.MinimizeBox,
                            "Launcher stays compact and exposes title-bar help");
                        Check(form.Register.Top == form.Unregister.Top && form.Options.Top > form.Register.Top,
                            "Register and unregister share the first row, settings uses the second row");
                        Check(form.Register.Height <= 40 && form.Options.Height <= 40, "Default-size buttons keep compact row heights");
                        form.Register.PerformClick();
                        Check(form.Busy && !form.Register.Enabled && !form.Unregister.Enabled && !form.Options.Enabled && !form.FontSize.Enabled, "Registration disables concurrent button actions");
                        Wait(form);
                        Check(!form.Register.Enabled && form.Unregister.Enabled && form.Options.Enabled && form.FontSize.Enabled, "Successful registration enables unregister, settings and font: " + form.Status.Text);
                        using (var bitmap = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(args[2], "portable-registered.png"));
                        }
                        object menuOptions = key.GetValue("OptionsV1");
                        form.FontSize.Text = "12"; Application.DoEvents();
                        Check(AppearanceStore.ReadFontSize() == 12 && form.Font.Size == 12 && Object.Equals(menuOptions, key.GetValue("OptionsV1")),
                            "Typed font size immediately saves and updates the launcher without changing menu options");
                        form.FontSize.Value = 18; Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(args[2], "portable-font-18.png"));
                        }
                        Check(form.FontSize.Right <= form.FontSize.Parent.ClientSize.Width && form.Options.Right <= form.Options.Parent.ClientSize.Width,
                            "Large-font controls fit the compact layout, numeric right=" + form.FontSize.Right + "/" + form.FontSize.Parent.ClientSize.Width + ", settings right=" + form.Options.Right + "/" + form.Options.Parent.ClientSize.Width);
                        form.FontSize.Value = 12;
                        form.Unregister.PerformClick(); Wait(form);
                        Check(form.Register.Enabled && !form.Unregister.Enabled && !form.Options.Enabled && !form.FontSize.Enabled && AppearanceStore.ReadFontSize() == 12,
                            "Unregister disables settings and font while preserving the saved size: " + form.Status.Text);
                    }
                    using (var form = new PortableWindow(args[0], args[1])) {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                        form.Show(); Wait(form);
                        Check(form.Register.Enabled && !form.Options.Enabled && !form.FontSize.Enabled && form.FontSize.Value == 12, "Reopening reads actual state and saved font");
                    }
                    return 0;
                } finally {
                        if (savedFont == null) key.DeleteValue("FontSizeV1", false);
                        else key.SetValue("FontSizeV1", savedFont, savedKind);
                }
            }
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
