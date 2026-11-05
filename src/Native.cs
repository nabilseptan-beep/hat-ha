using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hatha
{
    internal static class Native
    {
        public const int WS_EX_RIGHT = 0x00001000;
        public const int WS_EX_RTLREADING = 0x00002000;
        public const int WS_EX_LEFTSCROLLBAR = 0x00004000;
        public const int WS_EX_NOINHERITLAYOUT = 0x00100000;
        public const int WS_EX_LAYOUTRTL = 0x00400000;

        const uint EM_SETCUEBANNER = 0x1501;
        const uint FLASHW_TRAY = 0x0002;
        const uint FLASHW_TIMERNOFG = 0x000C;

        static readonly Guid FOLDERID_Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO info);
        [DllImport("dwmapi.dll")] static extern int DwmGetColorizationColor(out uint color, out bool opaqueBlend);

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

        // Grey hint text shown inside an empty text box, also while it has the focus.
        public static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        // Blinks the taskbar button until the window is brought to the front.
        public static void Flash(IntPtr hwnd)
        {
            var info = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)), hwnd = hwnd, dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG, uCount = uint.MaxValue };
            FlashWindowEx(ref info);
        }

        // The accent colour the user picked in Windows settings, as 0xAARRGGBB; 0 when it can't be read.
        public static uint AccentColor()
        {
            uint color;
            bool opaque;
            return DwmGetColorizationColor(out color, out opaque) == 0 ? color : 0;
        }
    }
}
