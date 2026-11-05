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

    internal sealed class Progress
    {
        public long Downloaded, Total, Speed;   // bytes, bytes, bytes per second
        public int Eta;                         // seconds

        public int Percent { get { return Total > 0 ? (int)Math.Min(100, Downloaded * 100 / Total) : -1; } }

        // "45% · 3.2 ميجا/ث · فاضل 0:12 · من 28 ميجا", leaving out whatever isn't known yet.
        public override string ToString()
        {
            var parts = new List<string>();
            if (Percent >= 0) parts.Add(Percent + "%");
            else if (Downloaded > 0) parts.Add(Size(Downloaded));
            if (Speed > 0) parts.Add(Size(Speed) + "/ث");
            if (Eta > 0) parts.Add("فاضل " + TimeSpan.FromSeconds(Eta).ToString(Eta >= 3600 ? @"h\:mm\:ss" : @"m\:ss"));
            if (Total > 0) parts.Add("من " + Size(Total));
            return string.Join(" · ", parts);
        }

        public static string Size(long bytes)
        {
            double value = bytes;
            string unit = "بايت";
            foreach (string next in new[] { "كيلو", "ميجا", "جيجا" })
            {
                if (value < 1000) break;
                value /= 1024;
                unit = next;
            }
            return value.ToString(value < 10 && unit != "بايت" ? "0.#" : "0", CultureInfo.InvariantCulture) + " " + unit;
        }
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
            args.Append(" --progress --newline --progress-template \"download:HATHA_PROGRESS %(progress.downloaded_bytes|0)d"
                + " %(progress.total_bytes,progress.total_bytes_estimate|0)d %(progress.speed|0)d %(progress.eta|0)d\"");
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

        // The four numbers of a progress line (bytes so far, total bytes, bytes per second, seconds left),
        // or null for any other line. A number yt-dlp doesn't know yet is 0.
        public static Progress ParseProgress(string line)
        {
            if (!line.StartsWith("HATHA_PROGRESS ")) return null;
            string[] parts = line.Substring(15).Split(' ');
            if (parts.Length != 4) return null;
            var progress = new Progress();
            long.TryParse(parts[0], out progress.Downloaded);
            long.TryParse(parts[1], out progress.Total);
            long.TryParse(parts[2], out progress.Speed);
            int.TryParse(parts[3], out progress.Eta);
            return progress;
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

        // What the usual errors mean in plain words, or the error itself when it is not a usual one.
        public static string Explain(string error)
        {
            string lower = error.ToLowerInvariant();
            if (lower.Contains("drm")) return "المحتوى ده مشفّر، وده خارج نطاق البرنامج.";
            if (lower.Contains("private") || lower.Contains("sign in") || lower.Contains("login") || lower.Contains("log in")
                || lower.Contains("registered users") || lower.Contains("cookies") || lower.Contains("age-restricted") || lower.Contains("confirm your age"))
                return "الفيديو ده محتاج تسجيل دخول: اختار الكوكيز من القايمة وجرّب تاني.";
            if (lower.Contains("unsupported url") || lower.Contains("not a valid url") || lower.Contains("no video"))
                return "اللينك ده مش لينك فيديو، أو الموقع مش مدعوم.";
            if (lower.Contains("unavailable") || lower.Contains("removed") || lower.Contains("does not exist") || lower.Contains("not found") || lower.Contains("404"))
                return "الفيديو مش موجود، يمكن اتمسح أو اللينك غلط.";
            if (lower.Contains("country") || lower.Contains("geo") || lower.Contains("region"))
                return "الفيديو محجوب في بلدك.";
            if (lower.Contains("403") || lower.Contains("429") || lower.Contains("too many requests"))
                return "الموقع رفض الطلب دلوقتي. استنى شوية وجرّب تاني.";
            if (lower.Contains("unable to download webpage") || lower.Contains("getaddrinfo") || lower.Contains("timed out") || lower.Contains("connection"))
                return "مش عارف أوصل للموقع: اتأكد من النت.";
            return error;
        }

        static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
