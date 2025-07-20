using System;
using System.Windows.Forms;

namespace AQtionDemoLauncher
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Check for minimum .NET version (8.0)
            if (!IsDotNet8OrGreater())
            {
                MessageBox.Show(
                    ".NET 8.0 or newer is required to run AQtion Demo Launcher.\nPlease install the latest .NET Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/8.0/runtime",
                    "Missing .NET Runtime",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }

        private static bool IsDotNet8OrGreater()
        {
            Version v = Environment.Version;
            return v.Major >= 8;
        }
    }
}
