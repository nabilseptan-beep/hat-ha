using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Hatha
{
    // Runs the real engines on a video made here by ffmpeg, so no internet is needed: every edit, the
    // preview frame, a download through yt-dlp from a tiny web server on this machine, and the window.
    //
    //   e2e.exe <path to Hatha.exe> <scratch folder>
    internal static class E2E
    {
        static int passed, failed;
        static string exe, scratch, video;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            exe = args[0];
            scratch = args[1];
            if (!Tools.Installed)
            {
                // On a fresh machine (a CI runner, say) the engines are fetched here the way the app would.
                Console.WriteLine("Installing the engines first...");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Tools.Install(delegate(string what, int percent) { }).Wait();
            }
            Directory.CreateDirectory(scratch);

            video = Path.Combine(scratch, "test.mp4");
            Run(Tools.Ffmpeg, "-v error -y -f lavfi -i testsrc=duration=10:size=320x240:rate=30 -f lavfi -i sine=frequency=440:duration=10"
                + " -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest " + Quote(video));

            foreach (var test in new Action[] { Probe, Cut, Gif, Mp3, Compress, Convert, Frame, Download, Window })
            {
                Console.WriteLine("=== " + test.Method.Name + " ===");
                try { test(); }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("ABORT " + ex.Message);
                }
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        static void Probe()
        {
            MediaInfo info = Ffmpeg.ParseProbe(Output(Tools.Ffprobe, Ffmpeg.ProbeArgs(video)));
            Near(info.Duration, 10, "duration of the test video");
            Equal(info.Width + "x" + info.Height, "320x240", "size of the test video");
        }

        static void Cut()
        {
            string output = Path.Combine(scratch, "cut.mp4");
            Expect(Run(Tools.Ffmpeg, Ffmpeg.Mp4Args(video, 2, 5, output)) == 0, "ffmpeg exits 0");
            Near(Duration(output), 3, "length of the cut");
            Equal(Codecs(output), "h264,aac", "codecs of the cut");
        }

        static void Gif()
        {
            string output = Path.Combine(scratch, "clip.gif");
            Expect(Run(Tools.Ffmpeg, Ffmpeg.GifArgs(video, 0, 2, 160, 10, output)) == 0, "ffmpeg exits 0");
            Equal(Codecs(output), "gif", "codec of the gif");
            Equal(Ffmpeg.ParseProbe(Output(Tools.Ffprobe, Ffmpeg.ProbeArgs(output))).Width.ToString(), "160", "width of the gif");
        }

        static void Mp3()
        {
            string output = Path.Combine(scratch, "sound.mp3");
            Expect(Run(Tools.Ffmpeg, Ffmpeg.Mp3Args(video, 0, 3, output)) == 0, "ffmpeg exits 0");
            Equal(Codecs(output), "mp3", "codec of the mp3");
            Near(Duration(output), 3, "length of the mp3");
        }

        static void Compress()
        {
            string output = Path.Combine(scratch, "small.mp4");
            string[] passes = Ffmpeg.CompressArgs(video, 0, 10, Ffmpeg.CompressVideoKbps(0.5, 10, 100000), Path.Combine(scratch, "2pass"), output);
            foreach (string pass in passes) Expect(Run(Tools.Ffmpeg, pass) == 0, "ffmpeg exits 0");
            long bytes = new FileInfo(output).Length;
            Expect(bytes <= 512 * 1024, "the result is under half a megabyte: " + bytes + " bytes");
            Expect(bytes > 100 * 1024, "the result is not trivially small: " + bytes + " bytes");
            Equal(Codecs(output), "h264,aac", "codecs of the compressed file");
        }

        static void Convert()
        {
            string output = Path.Combine(scratch, "converted.mp4");
            Expect(Run(Tools.Ffmpeg, Ffmpeg.Mp4Args(video, 0, 10, output)) == 0, "ffmpeg exits 0");
            Near(Duration(output), 10, "length of the converted file");
            Equal(Codecs(output), "h264,aac", "codecs of the converted file");
        }

        // The frame comes through stdout as a PNG; the app decodes it the same way.
        static void Frame()
        {
            var process = Process.Start(new ProcessStartInfo(Tools.Ffmpeg, Ffmpeg.FrameArgs(video, 1, 160))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            });
            var png = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(png);
            process.WaitForExit();
            Expect(png.Length > 0, "ffmpeg wrote something");
            png.Position = 0;
            using (Image frame = Image.FromStream(png))
                Equal(frame.Width + "x" + frame.Height, "160x120", "size of the frame");
        }

        // A direct link served from this machine goes through yt-dlp's generic extractor, which exercises
        // the same info and download command lines as any site.
        static void Download()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/test.mp4";
            var server = new Thread(delegate() { Serve(listener); }) { IsBackground = true };
            server.Start();

            List<string> lines = Output(Tools.YtDlp, YtDlp.InfoArgs(url, ""));
            VideoInfo info = YtDlp.ParseInfo(lines);
            Equal(info.Title, "test", "title from the file name");
            Equal(info.Heights.Count.ToString(), "0", "no heights for a plain file");

            string folder = Path.Combine(scratch, "downloads");
            var request = new DownloadRequest { Url = url, Height = 0, Folder = folder, Cookies = "" };
            string file = DownloadedFile(request);
            Expect(file != null && File.Exists(file), "the video landed: " + file);
            if (file != null) Near(Duration(file), 10, "length of the downloaded video");

            request.Mp3 = true;
            file = DownloadedFile(request);
            Expect(file != null && file.EndsWith(".mp3") && File.Exists(file), "the MP3 landed: " + file);
            if (file != null) Equal(Codecs(file), "mp3", "codec of the downloaded MP3");

            listener.Stop();
        }

        static string DownloadedFile(DownloadRequest request)
        {
            string file = null;
            int progressLines = 0;
            List<string> lines = Output(Tools.YtDlp, YtDlp.DownloadArgs(request, Tools.FfmpegFolder));
            foreach (string line in lines)
            {
                if (YtDlp.ParseProgress(line) != null) progressLines++;
                file = YtDlp.ParseFile(line) ?? file;
            }
            Expect(progressLines > 0, "progress lines were printed");
            if (file == null) Console.WriteLine("  " + YtDlp.LastError(lines));
            return file;
        }

        // Answers every request with the whole test video. yt-dlp asks with HEAD first, then GET.
        static void Serve(TcpListener listener)
        {
            byte[] body = File.ReadAllBytes(video);
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }   // stopped
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    string requestLine = reader.ReadLine() ?? "";
                    while ((reader.ReadLine() ?? "").Length > 0) { }
                    byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(head, 0, head.Length);
                    if (!requestLine.StartsWith("HEAD")) stream.Write(body, 0, body.Length);
                }
            }
        }

        // The exe starts, shows its window, and closes cleanly.
        static void Window()
        {
            if (Process.GetProcessesByName("Hatha").Length > 0)
            {
                Console.WriteLine("Close Hatha first: the test starts its own copy.");
                failed++;
                return;
            }
            using (Process app = Process.Start(exe))
            {
                string title = "";
                for (int i = 0; i < 50 && title != "هاتها"; i++)
                {
                    Thread.Sleep(200);
                    app.Refresh();
                    title = app.MainWindowTitle;
                }
                Equal(title, "هاتها", "window title");
                Expect(app.CloseMainWindow() && app.WaitForExit(5000), "the window closes on request");
            }
        }

        // Runs a tool to completion, echoing what it says, and gives back its exit code.
        static int Run(string tool, string args)
        {
            return new ToolProcess(tool, args, delegate(string line) { Console.WriteLine("  " + line); }).ExitCode.Result;
        }

        // Runs a tool to completion and gives back its output lines. Without a UI thread they arrive from
        // the pool, hence the lock.
        static List<string> Output(string tool, string args)
        {
            var lines = new List<string>();
            new ToolProcess(tool, args, delegate(string line) { lock (lines) lines.Add(line); }).ExitCode.Wait();
            return lines;
        }

        static double Duration(string file)
        {
            return Ffmpeg.ParseProbe(Output(Tools.Ffprobe, Ffmpeg.ProbeArgs(file))).Duration;
        }

        static string Codecs(string file)
        {
            return string.Join(",", Output(Tools.Ffprobe, "-v error -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 " + Quote(file)));
        }

        static void Near(double actual, double expected, string what)
        {
            Expect(Math.Abs(actual - expected) < 0.2, what + ": " + actual.ToString(CultureInfo.InvariantCulture) + " ~ " + expected);
        }

        static void Equal(string actual, string expected, string what)
        {
            Expect(actual == expected, what + ": [" + actual + "] expected [" + expected + "]");
        }

        static void Expect(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        }

        static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
