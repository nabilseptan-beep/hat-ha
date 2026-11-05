using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hatha
{
    // The edit screen: pick a video, mark a piece of it on the slider, then cut it, make a GIF or an MP3,
    // squeeze it under a size, or convert it to MP4. Results go to Downloads\هاتها.
    internal sealed class QasqousaTab : MirroredTabPage
    {
        enum Operation { Cut, Gif, Mp3, Compress, Convert }

        const int PreviewWidth = 480;
        const int PreviewDelayMs = 150;

        readonly TextBox path;
        readonly Button choose, playSource, markFrom, markTo, run, cancel, playResult, openFolder;
        readonly PictureBox preview;
        readonly TrackBar slider;
        readonly Label position, range, gifWidthLabel, gifFpsLabel, sizeLabel, status;
        readonly LinkLabel report;
        readonly ComboBox operation;
        readonly NumericUpDown gifWidth, gifFps, size;
        readonly ProgressBar progress;
        readonly Timer previewTimer = new Timer { Interval = PreviewDelayMs };
        readonly ToolTip tips = new ToolTip();

        MediaInfo media;
        double from, to;
        int previewRequest;   // so a slow frame doesn't overwrite a newer one
        ToolProcess running;
        bool cancelled;
        string result;        // the last file made

        public QasqousaTab()
        {
            Text = "قصقوصة";

            var pathLabel = new Label { Text = "الفيديو:", Location = new Point(16, 20), Size = new Size(60, 24) };
            path = new TextBox { Location = new Point(80, 16), Size = new Size(360, 28), ReadOnly = true, RightToLeft = RightToLeft.No, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            path.HandleCreated += delegate { Native.SetCueBanner(path, "اختار فيديو أو اسحبه هنا"); };
            choose = new Button { Text = "اختار...", Location = new Point(450, 14), Size = new Size(90, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            choose.Click += delegate { Choose(); };
            playSource = new Button { Text = "شغّل في المشغل", Location = new Point(550, 14), Size = new Size(146, 32), Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            playSource.Click += delegate { Process.Start(path.Text); };
            tips.SetToolTip(playSource, "يفتح الفيديو في المشغل بتاعك؛ المعاينة هنا صورة ثابتة بس");

            preview = new PictureBox
            {
                Location = new Point(16, 60),
                Size = new Size(680, 270),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };

            // Tenths of a second. Left-to-right like every video timeline, whatever the page's direction.
            slider = new TrackBar { Location = new Point(16, 336), Size = new Size(680, 45), TickStyle = TickStyle.None, RightToLeft = RightToLeft.No, Enabled = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            tips.SetToolTip(slider, "حرّك عشان تشوف الإطار؛ الأسهم بتحرك عُشر ثانية وPage Up/Down نص ثانية");
            slider.ValueChanged += delegate { SliderMoved(); };
            previewTimer.Tick += delegate { previewTimer.Stop(); ShowFrame(); };

            position = new Label { Location = new Point(16, 392), Size = new Size(134, 24), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            markFrom = new Button { Text = "ابدأ من هنا", Location = new Point(160, 388), Size = new Size(110, 30), Enabled = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            markFrom.Click += delegate { from = Seconds(); if (to < from) to = media.Duration; ShowRange(); };
            tips.SetToolTip(markFrom, "القطعة تبدأ من موضع الشريط");
            markTo = new Button { Text = "اقطع لحد هنا", Location = new Point(278, 388), Size = new Size(110, 30), Enabled = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            markTo.Click += delegate { to = Seconds(); if (from > to) from = 0; ShowRange(); };
            tips.SetToolTip(markTo, "القطعة تنتهي عند موضع الشريط");
            range = new Label { Location = new Point(396, 392), Size = new Size(300, 24), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

            operation = new ComboBox { Location = new Point(16, 428), Size = new Size(200, 28), DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            operation.Items.AddRange(new object[] { "قص", "تحويل GIF", "استخراج MP3", "ضغط لحجم معين", "تحويل لـ MP4" });
            operation.SelectedIndexChanged += delegate { ShowOptions(); };
            gifWidthLabel = new Label { Text = "العرض:", Location = new Point(232, 432), Size = new Size(60, 24), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            gifWidth = new NumericUpDown { Location = new Point(296, 428), Size = new Size(80, 28), Minimum = 32, Maximum = 1920, Increment = 40, Value = 480, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            tips.SetToolTip(gifWidth, "عرض الـGIF بالبكسل؛ مش هيكبر عن عرض الفيديو");
            gifFpsLabel = new Label { Text = "إطار/ثانية:", Location = new Point(392, 432), Size = new Size(90, 24), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            gifFps = new NumericUpDown { Location = new Point(486, 428), Size = new Size(70, 28), Minimum = 1, Maximum = 60, Value = 12, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            tips.SetToolTip(gifFps, "إطارات أكتر = حركة أنعم وحجم أكبر");
            sizeLabel = new Label { Text = "الحجم بالميجا:", Location = new Point(232, 432), Size = new Size(110, 24), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            size = new NumericUpDown { Location = new Point(346, 428), Size = new Size(80, 28), Minimum = 1, Maximum = 4000, Value = 16, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            tips.SetToolTip(size, "الناتج هيبقى تحت الحجم ده؛ واتساب بيقبل لحد 16");
            operation.SelectedIndex = 0;

            run = new Button { Text = "نفّذ", Location = new Point(16, 472), Size = new Size(140, 40), Font = new Font("Segoe UI", 11f), Enabled = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            Theme.Primary(run);
            run.Click += delegate { Run(); };
            tips.SetToolTip(run, "ينفّذ العملية على القطعة المحددة ويحفظ الناتج في Downloads\\هاتها");
            cancel = new Button { Text = "إلغاء", Location = new Point(164, 472), Size = new Size(100, 40), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            cancel.Click += delegate { Cancel(); };
            progress = new ProgressBar { Location = new Point(280, 480), Size = new Size(416, 22), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

            status = new Label { Location = new Point(16, 524), Size = new Size(300, 24), AutoEllipsis = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            report = new LinkLabel { Text = "بلّغ عن المشكلة", Location = new Point(324, 524), Size = new Size(100, 24), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            report.LinkClicked += delegate { Process.Start(Report.Url(operation.Text + " لملف " + Path.GetFileName(path.Text), tips.GetToolTip(status))); };
            playResult = new Button { Text = "شغّل الناتج", Location = new Point(430, 518), Size = new Size(130, 34), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            playResult.Click += delegate { Process.Start(result); };
            openFolder = new Button { Text = "افتح الفولدر", Location = new Point(566, 518), Size = new Size(130, 34), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            openFolder.Click += delegate { Process.Start("explorer.exe", "/select,\"" + result + "\""); };

            Controls.AddRange(new Control[]
            {
                pathLabel, path, choose, playSource, preview, slider, position, markFrom, markTo, range,
                operation, gifWidthLabel, gifWidth, gifFpsLabel, gifFps, sizeLabel, size,
                run, cancel, progress, status, report, playResult, openFolder,
            });

            // A file dropped anywhere on the page, the big picture included, is opened.
            foreach (Control target in new Control[] { this, preview })
            {
                target.AllowDrop = true;
                target.DragEnter += delegate(object sender, DragEventArgs e)
                {
                    if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
                };
                target.DragDrop += delegate(object sender, DragEventArgs e) { Load(((string[])e.Data.GetData(DataFormats.FileDrop))[0]); };
            }
        }

        void Choose()
        {
            using (var dialog = new OpenFileDialog { Title = "اختار فيديو", Filter = "فيديو|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.ts;*.flv;*.3gp;*.gif|كل الملفات|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) Load(dialog.FileName);
            }
        }

        // Reads the file's length and size, then shows its first frame and the whole of it as the piece.
        public async void Load(string file)
        {
            path.Text = file;
            playSource.Enabled = true;
            Say("بيقرأ الفيديو...", SystemColors.ControlText);
            ShowDone(false);

            var lines = new List<string>();
            int code = await new ToolProcess(Tools.Ffprobe, Ffmpeg.ProbeArgs(file), lines.Add).ExitCode;
            media = Ffmpeg.ParseProbe(lines);
            if (code != 0 || media.Duration <= 0)
            {
                Fail("مش عارف أقرأ الملف ده.", string.Join("\n", lines));
                slider.Enabled = markFrom.Enabled = markTo.Enabled = run.Enabled = false;
                preview.Image = null;
                return;
            }

            Say("", SystemColors.ControlText);
            slider.Enabled = markFrom.Enabled = markTo.Enabled = run.Enabled = true;
            slider.Maximum = (int)(media.Duration * 10);
            slider.Value = 0;
            from = 0;
            to = media.Duration;
            ShowRange();
            SliderMoved();
        }

        double Seconds()
        {
            return slider.Value / 10.0;
        }

        void SliderMoved()
        {
            position.Text = "الموضع: " + Clock(Seconds());
            previewTimer.Stop();
            previewTimer.Start();
        }

        void ShowRange()
        {
            range.Text = "القطعة: من " + Clock(from) + " إلى " + Clock(to) + " (" + Clock(to - from) + ")";
        }

        async void ShowFrame()
        {
            if (media == null || media.Width == 0) return;
            int request = ++previewRequest;
            Image frame = await GrabFrame(path.Text, Seconds());
            if (request != previewRequest || frame == null) return;
            Image previous = preview.Image;
            preview.Image = frame;
            if (previous != null) previous.Dispose();
        }

        // ffmpeg writes the PNG to its stdout, so no temporary files pile up.
        static async Task<Image> GrabFrame(string file, double second)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(Tools.Ffmpeg, Ffmpeg.FrameArgs(file, second, PreviewWidth))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                },
            };
            process.Start();
            var png = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(png);
            process.WaitForExit();
            process.Dispose();
            if (png.Length == 0) return null;
            png.Position = 0;
            return Image.FromStream(png);
        }

        void ShowOptions()
        {
            var op = (Operation)operation.SelectedIndex;
            gifWidthLabel.Visible = gifWidth.Visible = gifFpsLabel.Visible = gifFps.Visible = op == Operation.Gif;
            sizeLabel.Visible = size.Visible = op == Operation.Compress;
        }

        async void Run()
        {
            string input = path.Text;
            var op = (Operation)operation.SelectedIndex;
            string output;
            string[] passes;
            switch (op)
            {
                case Operation.Gif:
                    output = OutputPath("", ".gif");
                    // No wider than the source: an upscaled GIF is only bigger, not better.
                    passes = new[] { Ffmpeg.GifArgs(input, from, to, Math.Min((int)gifWidth.Value, media.Width), (int)gifFps.Value, output) };
                    break;
                case Operation.Mp3:
                    output = OutputPath("", ".mp3");
                    passes = new[] { Ffmpeg.Mp3Args(input, from, to, output) };
                    break;
                case Operation.Compress:
                    output = OutputPath(" - " + size.Value + "MB", ".mp4");
                    int sourceKbps = (int)(new FileInfo(input).Length * 8 / 1000 / media.Duration);
                    int videoKbps = Ffmpeg.CompressVideoKbps((double)size.Value, to - from, sourceKbps);
                    passes = Ffmpeg.CompressArgs(input, from, to, videoKbps, Path.Combine(Path.GetTempPath(), "hatha-2pass"), output);
                    break;
                default:
                    output = OutputPath(op == Operation.Cut ? " - قص" : "", ".mp4");
                    passes = new[] { Ffmpeg.Mp4Args(input, from, to, output) };
                    break;
            }

            SetBusy(true);
            ShowDone(false);
            progress.Value = 0;
            progress.Visible = true;
            string error = null;
            int code = 0;
            for (int pass = 0; pass < passes.Length && code == 0 && !cancelled; pass++)
            {
                string stage = passes.Length > 1 ? "المرور " + (pass + 1) + " من " + passes.Length + ": " : "";
                Say(stage + "بيشتغل...", SystemColors.ControlText);
                double done = 0, length = Math.Max(to - from, 0.1);
                running = new ToolProcess(Tools.Ffmpeg, passes[pass], delegate(string line)
                {
                    double seconds = Ffmpeg.ParseProgress(line), speed = Ffmpeg.ParseSpeed(line);
                    if (seconds >= 0)
                    {
                        done = seconds;
                        progress.Value = (int)Math.Min(100, done * 100 / length);
                        status.Text = stage + progress.Value + "%";
                    }
                    else if (speed > 0)
                        status.Text += " · فاضل " + TimeSpan.FromSeconds((length - done) / speed).ToString(@"m\:ss");
                    else if (Ffmpeg.IsError(line)) error = line;
                });
                code = await running.ExitCode;
            }
            SetBusy(false);
            progress.Visible = false;

            if (cancelled) Say("اتلغى.", SystemColors.ControlText);
            else if (code != 0) Fail("فشل: " + (error ?? "ffmpeg رجع " + code), error ?? "ffmpeg exit code " + code);
            else
            {
                result = output;
                Say("خلص: " + Path.GetFileName(output), Theme.Success);
                ShowDone(true);
            }
            Form form = FindForm();
            if (form != null && Form.ActiveForm != form) Native.Flash(form.Handle);
        }

        void Say(string message, Color color)
        {
            status.Text = message;
            status.ForeColor = color;
            tips.SetToolTip(status, "");
            report.Visible = false;
        }

        // The short version on screen, the tool's own words behind it for the tooltip and the report.
        void Fail(string message, string detail)
        {
            Say(message, Theme.Failure);
            tips.SetToolTip(status, detail);
            report.Visible = true;
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
            foreach (Control control in new Control[] { choose, slider, markFrom, markTo, operation, gifWidth, gifFps, size, run })
                control.Enabled = !busy;
            cancel.Visible = busy;
        }

        void ShowDone(bool done)
        {
            playResult.Visible = openFolder.Visible = done;
        }

        // Next to the downloads, named after the source; never over an existing file.
        string OutputPath(string suffix, string extension)
        {
            string folder = HathaTab.OutputFolder();
            Directory.CreateDirectory(folder);
            string name = Path.GetFileNameWithoutExtension(path.Text) + suffix;
            string output = Path.Combine(folder, name + extension);
            for (int n = 2; File.Exists(output); n++) output = Path.Combine(folder, name + " (" + n + ")" + extension);
            return output;
        }

        static string Clock(double seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            string clock = time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
            return clock + "." + (int)(seconds * 10 % 10);
        }
    }
}
