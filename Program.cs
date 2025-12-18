using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MassaKWin.Ui;

namespace MassaKWin
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Нужен для CP1251 / windows-1251 в .NET Core/5+/6
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetDefaultFont(new Font("Segoe UI", 9f));

            ThemeManager.Initialize();

            Application.Run(new MainForm());
        }
    }
}
