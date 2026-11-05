using System;
using System.Net;
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
        public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        [STAThread]
        static void Main()
        {
            // Without a target framework attribute the runtime would default to TLS 1.0, which GitHub refuses.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
