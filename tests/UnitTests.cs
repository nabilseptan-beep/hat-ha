using System;
using System.Collections.Generic;
using System.Text;

namespace Hatha
{
    // Console test runner: no framework, so the in-box compiler can build it.
    internal static class UnitTests
    {
        static int failures;

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            YtDlpInfo();
            YtDlpDownload();
            YtDlpOutput();
            FfmpegArgs();
            FfmpegOutput();
            Updates();
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        static void YtDlpInfo()
        {
            string args = YtDlp.InfoArgs("https://youtu.be/abc?list=xyz", "");
            Contains(args, "--no-playlist");
            Contains(args, "--encoding utf-8");
            Contains(args, "--print \"HATHA_TITLE %(title)s\"");
            Contains(args, "--print \"HATHA_HEIGHTS %(formats.:.{height,vcodec})j\"");
            EndsWith(args, " \"https://youtu.be/abc?list=xyz\"");
            NotContains(args, "--cookies");

            // Lines arrive in any order, mixed with warnings on stderr.
            VideoInfo info = YtDlp.ParseInfo(new[]
            {
                "WARNING: [youtube] something",
                // Storyboards, audio-only formats, a format without a codec field, and duplicates.
                "HATHA_HEIGHTS [{\"height\": 90, \"vcodec\": \"none\"}, {\"vcodec\": \"none\"}, {\"height\": 360, \"vcodec\": \"avc1.4D401E\"},"
                    + " {\"height\": 720, \"vcodec\": \"vp9\"}, {\"height\": 720, \"vcodec\": \"avc1.4D401F\"}, {\"height\": 1080},"
                    + " {\"height\": 144, \"vcodec\": \"av01.0.00M.08\"}]",
                "HATHA_TITLE حلقة 3 - الجزء الأول",
                "HATHA_THUMBS [\"https://i/1.jpg\", \"https://i/2.webp\", \"https://i/3.jpg\", \"https://i/4.webp\"]",
                "HATHA_DURATION 213.5",
            });
            Equal(info.Title, "حلقة 3 - الجزء الأول", "title");
            Equal(info.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture), "213.5", "duration");
            Equal(string.Join(",", info.Heights), "1080,720,360,144", "heights");
            Equal(string.Join(",", info.Thumbnails), "https://i/3.jpg,https://i/1.jpg", "thumbnails skip webp, best first");

            // A direct file link: no duration, no thumbnails, no heights.
            info = YtDlp.ParseInfo(new[] { "HATHA_TITLE clip", "HATHA_DURATION NA", "HATHA_THUMBS NA", "HATHA_HEIGHTS [{}]" });
            Equal(info.Duration.ToString(), "0", "unknown duration");
            Equal(info.Thumbnails.Count.ToString(), "0", "no thumbnails");
            Equal(info.Heights.Count.ToString(), "0", "no heights");
        }

        static void YtDlpDownload()
        {
            var request = new DownloadRequest { Url = "https://x/v", Folder = @"C:\Users\me\Downloads\هاتها", Cookies = "" };
            string args = YtDlp.DownloadArgs(request, @"C:\Users\me\AppData\Local\Hatha\ffmpeg");
            Contains(args, "--ffmpeg-location \"C:\\Users\\me\\AppData\\Local\\Hatha\\ffmpeg\"");
            Contains(args, "-o \"C:\\Users\\me\\Downloads\\هاتها\\%(title)s%(height& [{}p]|)s.%(ext)s\"");
            Contains(args, "--progress-template \"download:HATHA_PROGRESS %(progress.downloaded_bytes|0)d"
                + " %(progress.total_bytes,progress.total_bytes_estimate|0)d %(progress.speed|0)d %(progress.eta|0)d\"");
            Contains(args, "--print \"after_move:HATHA_FILE %(filepath)s\"");
            Contains(args, "--no-simulate");
            Contains(args, "-S \"res,+codec:avc:m4a,ext\" --merge-output-format mp4");
            NotContains(args, "--audio-format");
            EndsWith(args, " \"https://x/v\"");

            request.Height = 720;
            Contains(YtDlp.DownloadArgs(request, "f"), "-S \"res:720,+codec:avc:m4a,ext\"");

            request.Mp3 = true;
            args = YtDlp.DownloadArgs(request, "f");
            Contains(args, "-f ba/b -x --audio-format mp3");
            NotContains(args, "-S ");

            request.Cookies = "firefox";
            Contains(YtDlp.DownloadArgs(request, "f"), " --cookies-from-browser firefox ");
            Contains(YtDlp.InfoArgs("u", "firefox"), " --cookies-from-browser firefox ");

            request.Cookies = @"C:\my cookies\cookies.txt";
            Contains(YtDlp.DownloadArgs(request, "f"), " --cookies \"C:\\my cookies\\cookies.txt\" ");
            NotContains(YtDlp.DownloadArgs(request, "f"), "--cookies-from-browser");
        }

        static void YtDlpOutput()
        {
            Progress step = YtDlp.ParseProgress("HATHA_PROGRESS 14680064 29360128 3355443 12");
            Equal(step.Percent.ToString(), "50", "percent from bytes");
            Equal(step.ToString(), "50% · 3.2 ميجا/ث · فاضل 0:12 · من 28 ميجا", "progress in words");
            step = YtDlp.ParseProgress("HATHA_PROGRESS 524288 0 0 0");
            Equal(step.Percent.ToString(), "-1", "no total, no percent");
            Equal(step.ToString(), "512 كيلو", "only what is known");
            Equal(YtDlp.ParseProgress("HATHA_PROGRESS 100 100 0 0").Percent.ToString(), "100", "done");
            Equal(YtDlp.ParseProgress("HATHA_PROGRESS 100 100 0 3700").ToString(), "100% · فاضل 1:01:40 · من 100 بايت", "an hour or more");
            Equal((YtDlp.ParseProgress("[download] 45.0% of 10MiB") == null).ToString(), "True", "unmarked progress line");
            Equal((YtDlp.ParseProgress("HATHA_FILE C:\\5%.mp4") == null).ToString(), "True", "file line is not progress");
            Equal(Progress.Size(999), "999 بايت", "bytes");
            Equal(Progress.Size(1536), "1.5 كيلو", "kilobytes with a decimal");
            Equal(Progress.Size(1572864000), "1.5 جيجا", "gigabytes");

            Equal(YtDlp.ParseFile(@"HATHA_FILE C:\Users\me\Downloads\هاتها\حلقة 3.mp4"), @"C:\Users\me\Downloads\هاتها\حلقة 3.mp4", "file");
            Equal(YtDlp.ParseFile("HATHA_PROGRESS 1%") ?? "null", "null", "progress line is not a file");

            Equal(YtDlp.LastError(new[] { "WARNING: x", "ERROR: [youtube] abc: Video unavailable", "ERROR: second" }), "second", "last error");
            Equal(YtDlp.LastError(new[] { "WARNING: x" }), "yt-dlp فشل من غير ما يقول ليه", "no error line");

            Equal(YtDlp.Explain("[youtube] abc: Private video. Sign in if you've been granted access to this video"), "الفيديو ده محتاج تسجيل دخول: اختار الكوكيز من القايمة وجرّب تاني.", "private video");
            Equal(YtDlp.Explain("[generic] Unsupported URL: https://example.com/page"), "اللينك ده مش لينك فيديو، أو الموقع مش مدعوم.", "unsupported");
            Equal(YtDlp.Explain("[youtube] abc: Video unavailable"), "الفيديو مش موجود، يمكن اتمسح أو اللينك غلط.", "unavailable");
            Equal(YtDlp.Explain("Unable to download webpage: <urlopen error [Errno 11001] getaddrinfo failed>"), "مش عارف أوصل للموقع: اتأكد من النت.", "offline");
            Equal(YtDlp.Explain("[youtube] abc: This video has DRM protection"), "المحتوى ده مشفّر، وده خارج نطاق البرنامج.", "drm");
            Equal(YtDlp.Explain("something new"), "something new", "unknown errors pass through");
        }

        static void FfmpegArgs()
        {
            const string input = @"C:\Users\me\Downloads\هاتها\حلقة 3 [720p].mp4";
            Equal(Ffmpeg.ProbeArgs(input),
                "-v error -show_entries format=duration:stream=width,height -of default=noprint_wrappers=1 \"" + input + "\"", "probe");
            Equal(Ffmpeg.FrameArgs(input, 12.5, 480),
                "-v error -ss 12.5 -i \"" + input + "\" -frames:v 1 -vf scale=480:-1 -f image2pipe -vcodec png -", "frame");

            // The piece is always "-ss from -t length", decimals with a dot whatever the Windows locale.
            string mp4 = Ffmpeg.Mp4Args(input, 2.25, 5.5, @"C:\out\x - قص.mp4");
            Contains(mp4, "-progress pipe:1 -nostats -y -ss 2.25 -t 3.25 -i \"" + input + "\"");
            Contains(mp4, " -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"C:\\out\\x - قص.mp4\"");
            Contains(Ffmpeg.Mp4Args(input, 0, 10, "o"), " -ss 0 -t 10 -i ");
            Contains(Ffmpeg.Mp4Args(input, 1.0 / 3, 1, "o"), " -ss 0.333 -t 0.667 -i ");

            string gif = Ffmpeg.GifArgs(input, 0, 3, 320, 15, @"C:\out\x.gif");
            Contains(gif, " -ss 0 -t 3 -i ");
            Contains(gif, " -vf \"fps=15,scale=320:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" \"C:\\out\\x.gif\"");

            string mp3 = Ffmpeg.Mp3Args(input, 0, 3, @"C:\out\x.mp3");
            Contains(mp3, " -vn -c:a libmp3lame -q:a 2 \"C:\\out\\x.mp3\"");

            // 16 MB over 60 s is 2184 kbps; 5% is kept back and the audio takes its share.
            Equal(Ffmpeg.CompressVideoKbps(16, 60, 100000).ToString(), "1979", "compress bitrate");
            Equal(Ffmpeg.CompressVideoKbps(16, 60, 500).ToString(), "404", "a small source is not inflated");
            Equal(Ffmpeg.CompressVideoKbps(1, 3600, 100000).ToString(), "50", "compress bitrate floor");
            string[] passes = Ffmpeg.CompressArgs(input, 0, 60, 1979, @"C:\tmp\log", @"C:\out\x - 16MB.mp4");
            Equal(passes.Length.ToString(), "2", "two passes");
            Contains(passes[0], " -c:v libx264 -preset veryfast -b:v 1979k -passlogfile \"C:\\tmp\\log\" -pass:v 1 -an -f mp4 NUL");
            Contains(passes[1], " -b:v 1979k -passlogfile \"C:\\tmp\\log\" -pass:v 2 -c:a aac -b:a 96k -movflags +faststart \"C:\\out\\x - 16MB.mp4\"");
        }

        static void FfmpegOutput()
        {
            MediaInfo info = Ffmpeg.ParseProbe(new[] { "width=1280", "height=720", "duration=63.4", "width=0", "height=0" });
            Equal(info.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture), "63.4", "probe duration");
            Equal(info.Width + "x" + info.Height, "1280x720", "probe size keeps the first stream");
            info = Ffmpeg.ParseProbe(new[] { "duration=5" });
            Equal(info.Width + "x" + info.Height, "0x0", "audio-only probe");

            Equal(Ffmpeg.ParseProgress("out_time_us=2500000").ToString(System.Globalization.CultureInfo.InvariantCulture), "2.5", "progress seconds");
            Equal(Ffmpeg.ParseProgress("out_time_ms=2500000").ToString(), "-1", "only the us line counts");
            Equal(Ffmpeg.ParseProgress("progress=end").ToString(), "-1", "end line");
            Equal(Ffmpeg.ParseSpeed("speed=  44x").ToString(System.Globalization.CultureInfo.InvariantCulture), "44", "speed");
            Equal(Ffmpeg.ParseSpeed("speed=1.25x").ToString(System.Globalization.CultureInfo.InvariantCulture), "1.25", "fractional speed");
            Equal(Ffmpeg.ParseSpeed("speed=N/A").ToString(), "-1", "unknown speed");
            Equal(Ffmpeg.ParseSpeed("fps=30").ToString(), "-1", "not a speed line");
            foreach (string line in new[] { "frame=12", "fps=0.00", "stream_0_0_q=28.0", "bitrate=N/A", "total_size=4096", "out_time_us=1", "out_time_ms=1", "out_time=00:00:00.000001", "dup_frames=0", "drop_frames=0", "speed=1.2x", "progress=continue" })
                if (Ffmpeg.IsError(line)) Fail("[" + line + "] is progress, not an error");
            if (!Ffmpeg.IsError("C:\\x.mp4: No such file or directory")) Fail("a complaint should count as an error");
        }

        static void Updates()
        {
            Equal(Updater.ParseTag("https://github.com/nabilseptan-beep/hat-ha/releases/tag/v1.2.0").ToString(), "1.2.0", "tag with v");
            Equal(Updater.ParseTag("https://github.com/nabilseptan-beep/hat-ha/releases/tag/1.2.10").ToString(), "1.2.10", "tag without v");
            Equal((Updater.ParseTag("https://github.com/nabilseptan-beep/hat-ha/releases") == null).ToString(), "True", "no release yet");
            Equal((Updater.ParseTag(null) == null).ToString(), "True", "no location");
            Equal(Updater.IsNewer(new Version("1.2.0"), "1.1.9").ToString(), "True", "newer");
            Equal(Updater.IsNewer(new Version("1.2.0"), "1.2.0").ToString(), "False", "same");
            Equal(Updater.IsNewer(new Version("1.2.0"), "1.10.0").ToString(), "False", "numeric, not textual, comparison");
            Equal(Updater.IsNewer(null, "1.0.0").ToString(), "False", "nothing found");
        }

        static void Contains(string text, string part)
        {
            if (!text.Contains(part)) Fail(string.Format("[{0}] should contain [{1}]", text, part));
        }

        static void NotContains(string text, string part)
        {
            if (text.Contains(part)) Fail(string.Format("[{0}] should not contain [{1}]", text, part));
        }

        static void EndsWith(string text, string end)
        {
            if (!text.EndsWith(end)) Fail(string.Format("[{0}] should end with [{1}]", text, end));
        }

        static void Equal(string actual, string expected, string what)
        {
            if (actual != expected) Fail(string.Format("{0}: got [{1}], expected [{2}]", what, actual, expected));
        }

        static void Fail(string message)
        {
            failures++;
            Console.WriteLine("FAIL " + message);
        }
    }
}
