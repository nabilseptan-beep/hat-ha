using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hatha
{
    // The download screen: paste a link, see what it is, pick a quality, download to Downloads\هاتها.
    internal sealed class HathaTab : MirroredTabPage
    {
        static readonly string[] Browsers = { "", "firefox", "chrome", "edge", "brave" };
        static readonly Regex Link = new Regex(@"^https?://\S+$");
        const int FileItem = 5;        // the "cookies.txt file" entry of the cookies list
        const int TypingPauseMs = 400; // a link is looked up this long after it stops changing

        readonly Settings settings;
        readonly TextBox url;
        readonly Button fetch, download, cancel, edit, play, openFolder;
        readonly PictureBox thumb;
        readonly Label title, duration, hint, status;
        readonly LinkLabel report;
        readonly ComboBox quality, cookies;
        readonly ProgressBar progress;
        readonly Timer typing = new Timer { Interval = TypingPauseMs };
        readonly ToolTip tips = new ToolTip();

        List<int> heights = new List<int>();
        ToolProcess running;
        bool cancelled;
        bool choosingCookies;
        string fetched;   // the link whose details are on screen
        string file;      // the last file downloaded

        // Raised with the file's path when the user wants it in قصقوصة.
        public event Action<string> EditRequested = delegate { };

        public HathaTab(Settings settings)
        {
            this.settings = settings;
            Text = "هاتها";

            var urlLabel = new Label { Text = "اللينك:", Location = new Point(16, 20), Size = new Size(60, 24) };
            url = new TextBox { Location = new Point(80, 16), Size = new Size(500, 28), RightToLeft = RightToLeft.No, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            url.HandleCreated += delegate { Native.SetCueBanner(url, "الصق لينك الفيديو هنا"); };
            url.TextChanged += delegate { typing.Stop(); typing.Start(); };
            url.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Fetch(); }
            };
            typing.Tick += delegate { typing.Stop(); if (Link.IsMatch(url.Text.Trim())) Fetch(); };
            fetch = new Button { Text = "هات التفاصيل", Location = new Point(592, 14), Size = new Size(104, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            fetch.Click += delegate { Fetch(); };
            tips.SetToolTip(fetch, "العنوان والصورة والمدة والجودات المتاحة (Enter)");

            thumb = new PictureBox
            {
                Location = new Point(16, 60),
                Size = new Size(240, 135),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.ControlLight,
            };
            title = new Label { Location = new Point(272, 60), Size = new Size(424, 52), AutoEllipsis = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            duration = new Label { Location = new Point(272, 116), Size = new Size(424, 24), ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            var qualityLabel = new Label { Text = "الجودة:", Location = new Point(16, 212), Size = new Size(60, 24) };
            quality = new ComboBox { Location = new Point(80, 208), Size = new Size(200, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            FillQualities();
            tips.SetToolTip(quality, "الجودات اللي الموقع بيقدمها للفيديو ده، أو الصوت بس كـ MP3");

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
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            download = new Button { Text = "نزّل", Location = new Point(16, 304), Size = new Size(140, 40), Font = new Font("Segoe UI", 11f) };
            Theme.Primary(download);
            download.Click += delegate { Download(); };
            tips.SetToolTip(download, "ينزّل بالجودة المختارة في Downloads\\هاتها (Ctrl+Enter)");
            cancel = new Button { Text = "إلغاء", Location = new Point(164, 304), Size = new Size(100, 40), Visible = false };
            cancel.Click += delegate { Cancel(); };

            progress = new ProgressBar { Location = new Point(16, 356), Size = new Size(680, 22), Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            status = new Label { Location = new Point(16, 384), Size = new Size(560, 24), AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            report = new LinkLabel { Text = "بلّغ عن المشكلة", Location = new Point(584, 384), Size = new Size(112, 24), Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            report.LinkClicked += delegate { Process.Start(Report.Url("التحميل من " + url.Text.Trim(), tips.GetToolTip(status))); };

            edit = new Button { Text = "افتح في قصقوصة", Location = new Point(16, 424), Size = new Size(160, 36), Visible = false };
            edit.Click += delegate { EditRequested(file); };
            play = new Button { Text = "شغّل في المشغل", Location = new Point(184, 424), Size = new Size(160, 36), Visible = false };
            play.Click += delegate { Process.Start(file); };
            openFolder = new Button { Text = "افتح الفولدر", Location = new Point(352, 424), Size = new Size(160, 36), Visible = false };
            openFolder.Click += delegate { Process.Start("explorer.exe", "/select,\"" + file + "\""); };

            Controls.AddRange(new Control[]
            {
                urlLabel, url, fetch, thumb, title, duration, qualityLabel, quality, cookiesLabel, cookies, hint,
                download, cancel, progress, status, report, edit, play, openFolder,
            });
        }

        public static string OutputFolder()
        {
            return Path.Combine(Native.DownloadsFolder(), "هاتها");
        }

        // A link waiting on the clipboard goes straight into the box when the window comes up empty.
        public void OfferClipboard()
        {
            if (url.Text.Length > 0 || !Clipboard.ContainsText()) return;
            string text = Clipboard.GetText().Trim();
            if (Link.IsMatch(text)) url.Text = text;
        }

        // Ctrl+V anywhere on the page pastes a link; Ctrl+Enter downloads.
        public bool Shortcut(Keys keys)
        {
            if (keys == (Keys.Control | Keys.V) && !url.Focused && Clipboard.ContainsText())
            {
                url.Text = Clipboard.GetText().Trim();
                return true;
            }
            if (keys == (Keys.Control | Keys.Enter) && download.Enabled)
            {
                Download();
                return true;
            }
            return false;
        }

        async void Fetch()
        {
            string link = url.Text.Trim();
            if (link.Length == 0 || link == fetched || running != null || !Enabled) return;

            SetBusy(true);
            Say("بيجيب التفاصيل...", SystemColors.ControlText);
            var lines = new List<string>();
            running = new ToolProcess(Tools.YtDlp, YtDlp.InfoArgs(link, settings.Cookies), lines.Add);
            int code = await running.ExitCode;
            SetBusy(false);
            if (cancelled || code != 0)
            {
                fetched = null;
                ShowDetails(new VideoInfo());   // whatever was on screen belonged to another link
                if (cancelled) Say("اتلغى.", SystemColors.ControlText);
                else Fail(YtDlp.LastError(lines));
                return;
            }

            fetched = link;
            ShowDetails(YtDlp.ParseInfo(lines));
            Say("", SystemColors.ControlText);
        }

        void ShowDetails(VideoInfo info)
        {
            title.Text = info.Title;
            duration.Text = info.Duration > 0 ? "المدة: " + FormatDuration(info.Duration) : "";
            heights = info.Heights;
            FillQualities();
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
            if (link.Length == 0 || running != null) return;

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
            progress.Style = ProgressBarStyle.Marquee;
            progress.Visible = true;
            Say("بيبدأ...", SystemColors.ControlText);
            file = null;
            var lines = new List<string>();
            running = new ToolProcess(Tools.YtDlp, YtDlp.DownloadArgs(request, Tools.FfmpegFolder), delegate(string line)
            {
                lines.Add(line);
                Progress step = YtDlp.ParseProgress(line);
                if (step != null)
                {
                    progress.Style = step.Percent >= 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                    if (step.Percent >= 0) progress.Value = step.Percent;
                    status.Text = step.Percent == 100 ? "بيجهّز الملف..." : "بينزّل " + step;
                }
                string path = YtDlp.ParseFile(line);
                if (path != null) file = path;
            });
            int code = await running.ExitCode;
            SetBusy(false);
            progress.Visible = false;

            if (cancelled) Say("اتلغى.", SystemColors.ControlText);
            else if (code != 0) Fail(YtDlp.LastError(lines));
            else if (file == null) Fail("yt-dlp خلص من غير ما يقول فين الملف");
            else
            {
                Say("خلص: " + Path.GetFileName(file), Theme.Success);
                ShowDone(true);
            }
            Attention();
        }

        public void Cancel()
        {
            if (running == null) return;
            cancelled = true;
            running.Kill();
        }

        void Say(string message, Color color)
        {
            status.Text = message;
            status.ForeColor = color;
            tips.SetToolTip(status, "");
            report.Visible = false;
        }

        // The plain-words version on screen, the original behind it for the tooltip and the report.
        void Fail(string error)
        {
            Say(YtDlp.Explain(error), Theme.Failure);
            tips.SetToolTip(status, error);
            report.Visible = true;
        }

        // Blinks on the taskbar when the news arrives while the user is elsewhere.
        void Attention()
        {
            Form form = FindForm();
            if (form != null && Form.ActiveForm != form) Native.Flash(form.Handle);
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
