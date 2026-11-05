using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("هاتها")]
[assembly: AssemblyProduct("هاتها")]
[assembly: AssemblyDescription("Downloads videos from any site and trims, converts and compresses them")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Hatha
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
