using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hatha
{
    internal sealed class VideoInfo
    {
        public string Title = "";
        public List<string> Thumbnails = new List<string>();   // best first; not all of them necessarily exist
        public double Duration;                                 // seconds, 0 when the site doesn't say
        public List<int> Heights = new List<int>();             // distinct video heights, largest first
    }

    internal sealed class DownloadRequest
    {
        public string Url;
        public int Height;        // largest height to take, 0 for the best quality
        public bool Mp3;
        public string Folder;
        public string Cookies;    // "", a browser name, or the path of a cookies.txt file
    }

    // Builds yt-dlp command lines and reads what it prints. Every line we rely on carries a HATHA_ marker,
    // so warnings and other output can be interleaved with it.
    internal static class YtDlp
    {
        const string Common = "--no-playlist --encoding utf-8";

        public static string InfoArgs(string url, string cookies)
        {
            return Common + CookiesArgs(cookies)
                + " --print \"HATHA_TITLE %(title)s\" --print \"HATHA_DURATION %(duration)s\""
                + " --print \"HATHA_THUMBS %(thumbnails.:.url)j\" --print \"HATHA_HEIGHTS %(formats.:.{height,vcodec})j\" "
                + Quote(url);
        }

        public static VideoInfo ParseInfo(IEnumerable<string> lines)
        {
            var info = new VideoInfo();
            foreach (string line in lines)
            {
                if (line.StartsWith("HATHA_TITLE ")) info.Title = line.Substring(12);
                else if (line.StartsWith("HATHA_DURATION ")) double.TryParse(line.Substring(15), NumberStyles.Float, CultureInfo.InvariantCulture, out info.Duration);
                else if (line.StartsWith("HATHA_THUMBS ")) info.Thumbnails = ParseThumbnails(line);
                else if (line.StartsWith("HATHA_HEIGHTS ")) info.Heights = ParseHeights(line);
            }
            return info;
        }

        // yt-dlp lists thumbnails worst to best, and GDI+ can't show WebP.
        static List<string> ParseThumbnails(string line)
        {
            var urls = new List<string>();
            foreach (Match match in Regex.Matches(line, "\"([^\"]+)\""))
                if (!match.Groups[1].Value.EndsWith(".webp")) urls.Insert(0, match.Groups[1].Value);
            return urls;
        }

        // One {"height": 720, "vcodec": "avc1..."} object per format. Storyboards (image strips) carry a
        // height but no video codec, and audio-only formats carry no height.
        static List<int> ParseHeights(string line)
        {
            var heights = new SortedSet<int>();
            foreach (Match format in Regex.Matches(line, @"\{[^}]*\}"))
            {
                Match height = Regex.Match(format.Value, "\"height\": (\\d+)");
                if (height.Success && !format.Value.Contains("\"vcodec\": \"none\"")) heights.Add(int.Parse(height.Groups[1].Value));
            }
            var list = new List<int>(heights);
            list.Reverse();
            return list;
        }

        public static string DownloadArgs(DownloadRequest request, string ffmpegFolder)
        {
            var args = new StringBuilder(Common);
            args.Append(CookiesArgs(request.Cookies));
            args.Append(" --ffmpeg-location ").Append(Quote(ffmpegFolder));
            args.Append(" --progress --newline --progress-template \"download:HATHA_PROGRESS %(progress._percent_str)s\"");
            args.Append(" --no-simulate --print \"after_move:HATHA_FILE %(filepath)s\"");
            // The height goes into the name, so other qualities of the same video don't count as already downloaded.
            args.Append(" -o ").Append(Quote(request.Folder + @"\%(title)s%(height& [{}p]|)s.%(ext)s"));
            if (request.Mp3)
                // Audio-only when the site has it, so the video isn't fetched just to be thrown away.
                args.Append(" -f ba/b -x --audio-format mp3");
            else
                // Largest resolution first, then H.264/AAC and MP4 for compatibility among the same size.
                args.Append(" -S \"res").Append(request.Height > 0 ? ":" + request.Height : "").Append(",+codec:avc:m4a,ext\" --merge-output-format mp4");
            args.Append(' ').Append(Quote(request.Url));
            return args.ToString();
        }

        static string CookiesArgs(string cookies)
        {
            if (string.IsNullOrEmpty(cookies)) return "";
            if (cookies.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return " --cookies " + Quote(cookies);
            return " --cookies-from-browser " + cookies;
        }

        // Percent from a progress line, or -1 for any other line.
        public static int ParseProgress(string line)
        {
            if (!line.StartsWith("HATHA_PROGRESS ")) return -1;
            Match match = Regex.Match(line, @"(\d+(\.\d+)?)%");
            if (!match.Success) return -1;
            return (int)double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // The finished file's path, or null for any other line.
        public static string ParseFile(string line)
        {
            return line.StartsWith("HATHA_FILE ") ? line.Substring(11) : null;
        }

        // yt-dlp's last error message, in its own words.
        public static string LastError(IEnumerable<string> lines)
        {
            string error = "yt-dlp فشل من غير ما يقول ليه";
            foreach (string line in lines)
                if (line.StartsWith("ERROR: ")) error = line.Substring(7);
            return error;
        }

        static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
