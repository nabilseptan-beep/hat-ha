using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hatha
{
    // yt-dlp, ffmpeg and deno live in %LOCALAPPDATA%\Hatha. They are not shipped inside the exe: the first run
    // downloads them from their GitHub releases, and yt-dlp updates itself once a week (sites keep changing).
    // deno is the JavaScript runtime yt-dlp needs for YouTube; it is found by sitting next to yt-dlp.exe.
    internal static class Tools
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hatha");
        public static readonly string YtDlp = Path.Combine(Folder, "yt-dlp.exe");
        public static readonly string Deno = Path.Combine(Folder, "deno.exe");
        public static readonly string FfmpegFolder = Path.Combine(Folder, "ffmpeg");
        public static readonly string Ffmpeg = Path.Combine(FfmpegFolder, "ffmpeg.exe");
        public static readonly string Ffprobe = Path.Combine(FfmpegFolder, "ffprobe.exe");

        const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
        // The yt-dlp project's own ffmpeg build. The shared variant is half the size of the static one.
        const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl-shared.zip";

        public static bool Installed
        {
            get { return File.Exists(YtDlp) && File.Exists(Deno) && File.Exists(Ffmpeg) && File.Exists(Ffprobe); }
        }

        // Downloads whatever is missing. Progress is (what is happening, percent of the current download).
        public static async Task Install(Action<string, int> progress)
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(YtDlp))
                await Download(YtDlpUrl, YtDlp, "بينزّل yt-dlp", progress);
            if (!File.Exists(Deno))
                await DownloadZip(DenoUrl, Folder, "deno", progress);
            if (!File.Exists(Ffmpeg) || !File.Exists(Ffprobe))
                await DownloadZip(FfmpegUrl, FfmpegFolder, "ffmpeg", progress);
        }

        static async Task DownloadZip(string url, string folder, string what, Action<string, int> progress)
        {
            string zip = Path.Combine(Folder, what + ".zip");
            await Download(url, zip, "بينزّل " + what, progress);
            progress("بيفك " + what, 100);
            await Task.Run(delegate { ExtractPrograms(zip, folder); });
            File.Delete(zip);
        }

        static async Task Download(string url, string path, string what, Action<string, int> progress)
        {
            string part = path + ".part";
            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                {
                    progress(what + " (" + e.BytesReceived / 1048576 + " ميجا)", e.ProgressPercentage);
                };
                await client.DownloadFileTaskAsync(new Uri(url), part);
            }
            File.Move(part, path);
        }

        // Keeps the programs and the DLLs they share, flat in one folder; docs and headers stay in the zip.
        // ffplay is not needed.
        static void ExtractPrograms(string zip, string folder)
        {
            Directory.CreateDirectory(folder);
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if ((extension != ".exe" && extension != ".dll") || entry.Name == "ffplay.exe") continue;
                    entry.ExtractToFile(Path.Combine(folder, entry.Name), true);
                }
            }
        }

        // yt-dlp's own updater: replaces yt-dlp.exe with the latest release, or exits 0 when it is current.
        public static Task<int> UpdateYtDlp()
        {
            return new ToolProcess(YtDlp, "-U", null).ExitCode;
        }
    }

    // A hidden console process whose output lines and exit are delivered on the thread that started it.
    internal sealed class ToolProcess
    {
        readonly Process process = new Process();
        readonly TaskCompletionSource<int> exited = new TaskCompletionSource<int>();
        readonly SynchronizationContext context = SynchronizationContext.Current;

        public Task<int> ExitCode { get { return exited.Task; } }

        public ToolProcess(string exe, string args, Action<string> onLine)
        {
            process.StartInfo = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            process.EnableRaisingEvents = true;

            DataReceivedEventHandler received = delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null && onLine != null) Post(delegate { onLine(e.Data); });
            };
            process.OutputDataReceived += received;
            process.ErrorDataReceived += received;
            process.Exited += delegate
            {
                // Without a timeout, this also waits for the last output lines to be delivered.
                process.WaitForExit();
                int code = process.ExitCode;
                Post(delegate { process.Dispose(); exited.TrySetResult(code); });
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        // yt-dlp.exe is a launcher whose child does the work (and runs ffmpeg), so the whole tree has to go.
        public void Kill()
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + process.Id) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (InvalidOperationException) { }   // already gone
        }

        void Post(SendOrPostCallback action)
        {
            if (context != null) context.Post(action, null);
            else action(null);
        }
    }
}
