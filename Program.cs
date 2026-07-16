using System;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (Array.Exists(args, a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                // WinExe has no console, so tee results to a log file that the
                // test harness can read back.
                string logPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "pdfeditor_selftest", "result.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                using var writer = new System.IO.StreamWriter(logPath) { AutoFlush = true };
                Console.SetOut(writer);
                try
                {
                    return SelfTest.Run();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SELFTEST CRASHED: " + ex);
                    return 2;
                }
            }

            if (Array.Exists(args, a => string.Equals(a, "--guitest", StringComparison.OrdinalIgnoreCase)))
            {
                string logPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "pdfeditor_guitest_log", "result.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                using var writer = new System.IO.StreamWriter(logPath) { AutoFlush = true };
                Console.SetOut(writer);
                Application.EnableVisualStyles();
                try
                {
                    return UI.GuiTest.Run();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("GUITEST CRASHED: " + ex);
                    return 2;
                }
            }

            WinFontResolver.Register();
            ApplicationConfiguration.Initialize();
            Application.Run(new UI.MainForm(args));
            return 0;
        }
    }
}
