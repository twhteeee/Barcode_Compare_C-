using System;
using System.Windows.Forms;

namespace PLCCompare
{
    internal static class Program
    {
        // [STAThread] is required for WinForms — it tells .NET this thread
        // uses the Single-Threaded Apartment model, needed for UI components to work correctly.
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var mainForm = new MainForm();
            var controller = new FrameController(mainForm);

            // Application.Run shows the form and starts the message loop (WinForms' equivalent
            // of the EDT). Since MainForm's constructor only builds controls in memory (no blocking
            // I/O), the window appears immediately and fully drawn, with no blank/white flash.
            Application.Run(mainForm);
        }
    }
}