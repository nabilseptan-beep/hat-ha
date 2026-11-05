using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hatha
{
    // The download screen: paste a link, see what it is, pick a quality, download to Downloads\هاتها.
    internal sealed class HathaTab : MirroredTabPage
    {
        static readonly string[] Browsers = { "", "firefox", "chrome", "edge", "brave" };
        const int FileItem = 5;   // the "cookies.txt file" entry of the cookies list

        readonly Settings settings;
        readonly TextBox url;
        readonly Button fetch, download, cancel, edit, play, openFolder;
        readonly PictureBox thumb;
        readonly Label title, duration, hint, status;
        readonly ComboBox quality, cookies;
        readonly ProgressBar progress;

        List<int> heights = new List<int>();
        ToolProcess running;
        bool cancelled;
        bool choosingCookies;
        string file;   // the last file downloaded

        // Raised with the file's path when the user wants it in قصقوصة.
        public event Action<string> EditRequested = delegate { };

        public HathaTab(Settings settings)
        {
            this.settings = settings;
            Text = "هاتها";

            var urlLabel = new Label { Text = "اللينك:", Location = new Point(16, 20), Size = new Size(60, 24) };
            url = new TextBox { Location = new Point(80, 16), Size = new Size(500, 28), RightToLeft = RightToLeft.No };
            url.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Fetch(); }
            };
            fetch = new Button { Text = "هات التفاصيل", Location = new Point(592, 14), Size = new Size(104, 32) };
            fetch.Click += delegate { Fetch(); };

            thumb = new PictureBox
            {
                Location = new Point(16, 60),
                Size = new Size(240, 135),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.ControlLight,
            };
            title = new Label { Location = new Point(272, 60), Size = new Size(424, 52), AutoEllipsis = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
            duration = new Label { Location = new Point(272, 116), Size = new Size(424, 24), ForeColor = SystemColors.GrayText };

            var qualityLabel = new Label { Text = "الجودة:", Location = new Point(16, 212), Size = new Size(60, 24) };
            quality = new ComboBox { Location = new Point(80, 208), Size = new Size(200, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            FillQualities();

            var cookiesLabel = new Label { Text = "الكوكيز:", Location = new Point(16, 252), Size = new Size(60, 24) };
            cookies = new ComboBox { Location = new Point(80, 248), Size = new Size(200, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            cookies.Items.AddRange(new object[] { "بدون", "Firefox", "Chrome", "Edge", "Brave", "ملف cookies.txt..." });
            ShowCookiesChoice();
            cookies.SelectedIndexChanged += delegate { CookiesChanged(); };
            hint = new Label
            {
                Text = "للمواقع اللي بتطلب تسجيل دخول. Chrome وEdge غالباً بيرفضوا على ويندوز، فاستخدم Firefox أو ملف cookies.txt.",
                Location = new Point(296, 248),
                Size = new Size(400, 44),
                ForeColor = SystemColors.GrayText,
            };

            download = new Button { Text = "نزّل", Location = new Point(16, 304), Size = new Size(140, 40), Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
            download.Click += delegate { Download(); };
            cancel = new Button { Text = "إلغاء", Location = new Point(164, 304), Size = new Size(100, 40), Visible = false };
            cancel.Click += delegate { Cancel(); };

            progress = new ProgressBar { Location = new Point(16, 356), Size = new Size(680, 22), Visible = false };
            status = new Label { Location = new Point(16, 384), Size = new Size(680, 24), AutoEllipsis = true };

            edit = new Button { Text = "افتح في قصقوصة", Location = new Point(16, 424), Size = new Size(160, 36), Visible = false };
            edit.Click += delegate { EditRequested(file); };
            play = new Button { Text = "شغّل في المشغل", Location = new Point(184, 424), Size = new Size(160, 36), Visible = false };
            play.Click += delegate { Process.Start(file); };
            openFolder = new Button { Text = "افتح الفولدر", Location = new Point(352, 424), Size = new Size(160, 36), Visible = false };
            openFolder.Click += delegate { Process.Start("explorer.exe", "/select,\"" + file + "\""); };

            Controls.AddRange(new Control[]
            {
                urlLabel, url, fetch, thumb, title, duration, qualityLabel, quality, cookiesLabel, cookies, hint,
                download, cancel, progress, status, edit, play, openFolder,
            });
        }

        public static string OutputFolder()
        {
            return Path.Combine(Native.DownloadsFolder(), "هاتها");
        }

        async void Fetch()
        {
            string link = url.Text.Trim();
            if (link.Length == 0) return;

            SetBusy(true);
            status.Text = "بيجيب التفاصيل...";
            var lines = new List<string>();
            running = new ToolProcess(Tools.YtDlp, YtDlp.InfoArgs(link, settings.Cookies), lines.Add);
            int code = await running.ExitCode;
            SetBusy(false);
            if (cancelled || code != 0)
            {
                status.Text = cancelled ? "اتلغى." : YtDlp.LastError(lines);
                return;
            }

            VideoInfo info = YtDlp.ParseInfo(lines);
            title.Text = info.Title;
            duration.Text = info.Duration > 0 ? "المدة: " + FormatDuration(info.Duration) : "";
            heights = info.Heights;
            FillQualities();
            status.Text = "";
            LoadThumbnail(info.Thumbnails);
        }

        // The first candidate that actually exists wins; a missing picture is no reason to bother the user.
        async void LoadThumbnail(List<string> links)
        {
            thumb.Image = null;
            foreach (string link in links)
            {
                try
                {
                    byte[] bytes;
                    using (var client = new WebClient()) bytes = await client.DownloadDataTaskAsync(link);
                    thumb.Image = Image.FromStream(new MemoryStream(bytes));
                    return;
                }
                catch (Exception) { }
            }
        }

        async void Download()
        {
            string link = url.Text.Trim();
            if (link.Length == 0) return;

            var request = new DownloadRequest
            {
                Url = link,
                Height = quality.SelectedIndex > 0 && quality.SelectedIndex <= heights.Count ? heights[quality.SelectedIndex - 1] : 0,
                Mp3 = quality.SelectedIndex == quality.Items.Count - 1,
                Folder = OutputFolder(),
                Cookies = settings.Cookies,
            };
            Directory.CreateDirectory(request.Folder);

            SetBusy(true);
            ShowDone(false);
            progress.Value = 0;
            progress.Visible = true;
            status.Text = "بيبدأ...";
            file = null;
            var lines = new List<string>();
            running = new ToolProcess(Tools.YtDlp, YtDlp.DownloadArgs(request, Tools.FfmpegFolder), delegate(string line)
            {
                lines.Add(line);
                int percent = YtDlp.ParseProgress(line);
                if (percent >= 0)
                {
                    progress.Value = percent;
                    status.Text = percent < 100 ? "بينزّل " + percent + "%" : "بيجهّز الملف...";
                }
                string path = YtDlp.ParseFile(line);
                if (path != null) file = path;
            });
            int code = await running.ExitCode;
            SetBusy(false);
            progress.Visible = false;

            if (cancelled) status.Text = "اتلغى.";
            else if (code != 0) status.Text = YtDlp.LastError(lines);
            else if (file == null) status.Text = "الملف موجود من قبل في " + request.Folder;
            else
            {
                status.Text = "خلص: " + Path.GetFileName(file);
                ShowDone(true);
            }
        }

        public void Cancel()
        {
            if (running == null) return;
            cancelled = true;
            running.Kill();
        }

        void SetBusy(bool busy)
        {
            if (busy) cancelled = false;
            else running = null;
            foreach (Control control in new Control[] { url, fetch, download, quality, cookies }) control.Enabled = !busy;
            cancel.Visible = busy;
        }

        void ShowDone(bool done)
        {
            edit.Visible = play.Visible = openFolder.Visible = done;
        }

        // Best, then whatever heights the site offers, then MP3.
        void FillQualities()
        {
            quality.Items.Clear();
            quality.Items.Add("أعلى جودة");
            foreach (int height in heights) quality.Items.Add(height + "p");
            quality.Items.Add("صوت MP3");
            quality.SelectedIndex = 0;
        }

        void ShowCookiesChoice()
        {
            int browser = Array.IndexOf(Browsers, settings.Cookies);
            if (browser >= 0)
            {
                cookies.SelectedIndex = browser;
                return;
            }
            cookies.Items[FileItem] = "ملف: " + Path.GetFileName(settings.Cookies);
            cookies.SelectedIndex = FileItem;
        }

        void CookiesChanged()
        {
            // Showing the choice re-selects an item, which would come back here.
            if (choosingCookies) return;
            choosingCookies = true;
            if (cookies.SelectedIndex < FileItem)
            {
                settings.Cookies = Browsers[cookies.SelectedIndex];
            }
            else
            {
                using (var dialog = new OpenFileDialog { Title = "ملف الكوكيز", Filter = "cookies.txt|*.txt" })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) settings.Cookies = dialog.FileName;
                }
                ShowCookiesChoice();
            }
            settings.Save();
            choosingCookies = false;
        }

        static string FormatDuration(double seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(Math.Round(seconds));
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }
    }
}
