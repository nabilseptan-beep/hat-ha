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
            Contains(args, "--progress-template \"download:HATHA_PROGRESS %(progress._percent_str)s\"");
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
            Equal(YtDlp.ParseProgress("HATHA_PROGRESS   5.3%").ToString(), "5", "progress 5.3%");
            Equal(YtDlp.ParseProgress("HATHA_PROGRESS 100.0%").ToString(), "100", "progress 100%");
            Equal(YtDlp.ParseProgress("[download] 45.0% of 10MiB").ToString(), "-1", "unmarked progress line");
            Equal(YtDlp.ParseProgress("HATHA_FILE C:\\5%.mp4").ToString(), "-1", "file line is not progress");

            Equal(YtDlp.ParseFile(@"HATHA_FILE C:\Users\me\Downloads\هاتها\حلقة 3.mp4"), @"C:\Users\me\Downloads\هاتها\حلقة 3.mp4", "file");
            Equal(YtDlp.ParseFile("HATHA_PROGRESS 1%") ?? "null", "null", "progress line is not a file");

            Equal(YtDlp.LastError(new[] { "WARNING: x", "ERROR: [youtube] abc: Video unavailable", "ERROR: second" }), "second", "last error");
            Equal(YtDlp.LastError(new[] { "WARNING: x" }), "yt-dlp فشل من غير ما يقول ليه", "no error line");
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
