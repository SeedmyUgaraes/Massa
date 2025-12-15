using System;
using System.Text;
using System.Windows.Forms;

namespace MassaKWin
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Нужен для CP1251 / windows-1251 в .NET Core/5+/6
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }
    }
}