using System;
using Microsoft.Win32;

namespace Hatha
{
    // A GitHub issue with the facts already filled in: what was being done, what the tool said, and
    // which versions were involved. The user still reads and sends it; nothing leaves the machine on its own.
    internal static class Report
    {
        const string Issues = "https://github.com/nabilseptan-beep/hat-ha/issues/new";

        public static string Url(string doing, string error)
        {
            string title = error.Length > 80 ? error.Substring(0, 80) + "…" : error;
            string body = "**اللي كنت بعمله:** " + doing + "\n"
                + "_(اللينك موجود عشان نقدر نجرّب نفس الفيديو؛ لو مش حابب تشاركه امسحه من هنا قبل الإرسال.)_\n\n"
                + "**الخطأ:**\n```\n" + error + "\n```\n\n"
                + "**الإصدارات:** هاتها " + Program.Version + " · " + Tools.Versions + "\n**ويندوز:** " + WindowsVersion();
            return Issues + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
        }

        static string WindowsVersion()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                return key == null ? "?" : key.GetValue("DisplayVersion", "?") + " (بناء " + key.GetValue("CurrentBuild", "?") + ")";
        }
    }
}
