using System;
using System.Runtime.InteropServices;

namespace Hatha
{
    internal static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    }
}
