// Copyright (C) 2026 igedaena
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
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
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new PortableWindow(args[0], args[1])) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                form.Show(); Wait(form);
                Check(form.Register.Enabled && !form.Unregister.Enabled && !form.Options.Enabled, "Unregistered UI enables registration only: " + form.Status.Text);
                form.Register.PerformClick();
                Check(form.Busy && !form.Register.Enabled && !form.Unregister.Enabled && !form.Options.Enabled, "Registration disables concurrent button actions");
                Wait(form);
                Check(!form.Register.Enabled && form.Unregister.Enabled && form.Options.Enabled, "Successful registration enables unregister and options: " + form.Status.Text);
                using (var bitmap = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(args[2], "portable-registered.png"));
                }
                form.Unregister.PerformClick(); Wait(form);
                Check(form.Register.Enabled && !form.Unregister.Enabled && !form.Options.Enabled, "Unregister returns to registration only: " + form.Status.Text);
            }
            using (var form = new PortableWindow(args[0], args[1])) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                form.Show(); Wait(form);
                Check(form.Register.Enabled && !form.Options.Enabled, "Reopening reads actual unregistered state");
            }
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
