using System;
using System.Runtime.InteropServices;

namespace Hatha
{
    internal static class Native
    {
        public const int WS_EX_RIGHT = 0x00001000;
        public const int WS_EX_RTLREADING = 0x00002000;
        public const int WS_EX_LEFTSCROLLBAR = 0x00004000;
        public const int WS_EX_NOINHERITLAYOUT = 0x00100000;
        public const int WS_EX_LAYOUTRTL = 0x00400000;

        static readonly Guid FOLDERID_Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        // .NET Framework has no SpecialFolder for Downloads.
        public static string DownloadsFolder()
        {
            IntPtr path;
            SHGetKnownFolderPath(FOLDERID_Downloads, 0, IntPtr.Zero, out path);
            string folder = Marshal.PtrToStringUni(path);
            Marshal.FreeCoTaskMem(path);
            return folder;
        }
    }
}
