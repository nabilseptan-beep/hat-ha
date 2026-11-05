using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hatha
{
    internal sealed class MediaInfo
    {
        public double Duration;   // seconds
        public int Width, Height; // 0 for audio-only files
    }

    // Builds ffmpeg and ffprobe command lines and reads what they print. Every edit works on [from, to]
    // seconds of the input; video comes out as H.264/AAC MP4, which plays everywhere.
    internal static class Ffmpeg
    {
        const string Quiet = "-v error -progress pipe:1 -nostats -y";
        const string Mp4Codecs = "-c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart";

        public static string ProbeArgs(string file)
        {
            return "-v error -show_entries format=duration:stream=width,height -of default=noprint_wrappers=1 " + Quote(file);
        }

        // ffprobe prints key=value lines; a file with several streams repeats width and height.
        public static MediaInfo ParseProbe(IEnumerable<string> lines)
        {
            var info = new MediaInfo();
            foreach (string line in lines)
            {
                int equals = line.IndexOf('=');
                if (equals < 0) continue;
                string key = line.Substring(0, equals), value = line.Substring(equals + 1);
                if (key == "duration") double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out info.Duration);
                else if (key == "width" && info.Width == 0) int.TryParse(value, out info.Width);
                else if (key == "height" && info.Height == 0) int.TryParse(value, out info.Height);
            }
            return info;
        }

        // One PNG frame at the given second, scaled to the width, written to stdout.
        public static string FrameArgs(string file, double second, int width)
        {
            return "-v error -ss " + Seconds(second) + " -i " + Quote(file) + " -frames:v 1 -vf scale=" + width + ":-1 -f image2pipe -vcodec png -";
        }

        // Re-encodes the piece, so it starts on the exact frame rather than the nearest keyframe.
        public static string Mp4Args(string input, double from, double to, string output)
        {
            return Quiet + Piece(input, from, to) + " " + Mp4Codecs + " " + Quote(output);
        }

        // The usual two steps in one filter graph: a palette from the piece, then the piece drawn with it.
        public static string GifArgs(string input, double from, double to, int width, int fps, string output)
        {
            return Quiet + Piece(input, from, to)
                + " -vf \"fps=" + fps + ",scale=" + width + ":-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" "
                + Quote(output);
        }

        public static string Mp3Args(string input, double from, double to, string output)
        {
            return Quiet + Piece(input, from, to) + " -vn -c:a libmp3lame -q:a 2 " + Quote(output);
        }

        public const int CompressAudioKbps = 96;

        // The video bitrate that lands the piece under the target size: 5% is kept back for the container
        // and for the encoders landing a little above what they were asked for. A source that is already
        // smaller is not inflated to fill the target.
        public static int CompressVideoKbps(double targetMegabytes, double seconds, int sourceKbps)
        {
            int kbps = (int)Math.Min(targetMegabytes * 8192 * 0.95 / seconds, sourceKbps) - CompressAudioKbps;
            return Math.Max(kbps, 50);
        }

        // Two passes hit a size target far more accurately than one. The log file is shared between them,
        // and only the video is two-pass: the audio has no log to read.
        public static string[] CompressArgs(string input, double from, double to, int videoKbps, string log, string output)
        {
            string common = Quiet + Piece(input, from, to) + " -c:v libx264 -preset veryfast -b:v " + videoKbps + "k -passlogfile " + Quote(log);
            return new[]
            {
                common + " -pass:v 1 -an -f mp4 NUL",
                common + " -pass:v 2 -c:a aac -b:a " + CompressAudioKbps + "k -movflags +faststart " + Quote(output),
            };
        }

        // -ss before -i seeks fast, and -t keeps the piece's length independent of where it starts.
        static string Piece(string input, double from, double to)
        {
            return " -ss " + Seconds(from) + " -t " + Seconds(to - from) + " -i " + Quote(input);
        }

        // Seconds done so far from a "-progress" line, or -1 for any other line.
        public static double ParseProgress(string line)
        {
            if (!line.StartsWith("out_time_us=")) return -1;
            long micros;
            return long.TryParse(line.Substring(12), out micros) ? micros / 1e6 : -1;
        }

        // How many times faster than real time ffmpeg is going, from a "speed=1.2x" line; -1 for any other.
        public static double ParseSpeed(string line)
        {
            if (!line.StartsWith("speed=")) return -1;
            double speed;
            return double.TryParse(line.Substring(6).Trim().TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out speed) ? speed : -1;
        }

        // Lines that are not progress bookkeeping are ffmpeg complaining.
        public static bool IsError(string line)
        {
            return !Regex.IsMatch(line, @"^(frame|fps|stream_\d+_\d+_q|bitrate|total_size|out_time_us|out_time_ms|out_time|dup_frames|drop_frames|speed|progress)=");
        }

        static string Seconds(double seconds)
        {
            return seconds.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
