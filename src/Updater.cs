using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hatha
{
    // Keeps the app current from its GitHub releases: once a day it asks which release is the latest,
    // and when that is newer it fetches the new Hatha.exe, swaps it in and starts it.
    internal static class Updater
    {
        public const string Releases = "https://github.com/nabilseptan-beep/hat-ha/releases";

        // The latest release's version, read from where /releases/latest redirects to; null when offline
        // or when nothing has been released yet.
        public static async Task<Version> Latest()
        {
            var request = (HttpWebRequest)WebRequest.Create(Releases + "/latest");
            request.Method = "HEAD";
            request.AllowAutoRedirect = false;
            request.UserAgent = "Hatha";
            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                    return ParseTag(response.Headers["Location"]);
            }
            catch (WebException) { return null; }
        }

        // ".../releases/tag/v1.2.0" (or V1.2.0) gives 1.2.0; anything else, like ".../releases" when there
        // are none, gives null.
        public static Version ParseTag(string location)
        {
            Match match = Regex.Match(location ?? "", @"/tag/[vV]?(\d+\.\d+\.\d+)$");
            return match.Success ? new Version(match.Groups[1].Value) : null;
        }

        public static bool IsNewer(Version latest, string current)
        {
            return latest != null && latest > new Version(current);
        }

        // Downloads the latest release's exe next to the running one, as Hatha.exe.new. GitHub resolves
        // "latest" itself, so the tag's spelling doesn't matter.
        public static async Task<string> Fetch(string exe, Action<int> progress)
        {
            string fresh = exe + ".new";
            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e) { progress(e.ProgressPercentage); };
                await client.DownloadFileTaskAsync(new Uri(Releases + "/latest/download/Hatha.exe"), fresh);
            }
            return fresh;
        }

        // Hands over to a PowerShell one-liner that waits for this process to end, swaps the files and
        // starts the new exe. The caller exits right after. Paths travel on the command line, so any
        // characters in them survive.
        public static void Apply(string exe, string fresh, int pid)
        {
            string script = "Wait-Process -Id " + pid + " -ErrorAction SilentlyContinue;"
                + " for ($i = 0; $i -lt 20; $i++) { try { Move-Item -LiteralPath " + Quote(fresh) + " -Destination " + Quote(exe) + " -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 500 } };"
                + " Start-Process -FilePath " + Quote(exe);
            Process.Start(new ProcessStartInfo("powershell", "-NoProfile -WindowStyle Hidden -Command \"" + script + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        // A PowerShell single-quoted string: only the quote itself needs doubling.
        static string Quote(string path)
        {
            return "'" + path.Replace("'", "''") + "'";
        }
    }
}
