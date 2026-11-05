using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Hatha
{
    // Stored in %APPDATA%\Hatha\settings.ini as key=value lines.
    internal sealed class Settings
    {
        static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hatha");
        static readonly string FilePath = Path.Combine(Folder, "settings.ini");
        const string DateFormat = "yyyy-MM-dd";

        // The day yt-dlp was last installed or updated, and the day GitHub was last asked for a new Hatha.
        public DateTime YtDlpUpdated = DateTime.MinValue;
        public DateTime UpdateChecked = DateTime.MinValue;
        // "", a browser name for --cookies-from-browser, or the path of a cookies.txt file.
        public string Cookies = "";

        public static Settings Load()
        {
            var settings = new Settings();
            if (!File.Exists(FilePath)) return settings;

            var values = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(FilePath))
            {
                int equals = line.IndexOf('=');
                if (equals > 0) values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }

            string text;
            DateTime date;
            if (values.TryGetValue("yt_dlp_updated", out text)
                && DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                settings.YtDlpUpdated = date;
            }
            if (values.TryGetValue("update_checked", out text)
                && DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                settings.UpdateChecked = date;
            }
            if (values.TryGetValue("cookies", out text)) settings.Cookies = text;
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(FilePath, new[]
            {
                "yt_dlp_updated=" + YtDlpUpdated.ToString(DateFormat, CultureInfo.InvariantCulture),
                "update_checked=" + UpdateChecked.ToString(DateFormat, CultureInfo.InvariantCulture),
                "cookies=" + Cookies,
            });
        }
    }
}
