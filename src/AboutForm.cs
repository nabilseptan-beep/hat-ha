using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Hatha
{
    // Small dialog with the app name, version, developer, engine versions and the links that matter.
    internal sealed class AboutForm : Form
    {
        const string Developer = "Nabil Septan";
        const string Repo = "https://github.com/nabilseptan-beep/hat-ha";

        public AboutForm()
        {
            Text = "عن البرنامج";
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(400, 250);

            var logo = new PictureBox
            {
                Location = new Point(16, 16),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.StretchImage,
            };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hatha.icon.ico"))
                logo.Image = new Icon(stream, 48, 48).ToBitmap();

            var name = new Label
            {
                Text = "هاتها",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Location = new Point(80, 14),
                Size = new Size(304, 30),
            };

            var version = new Label
            {
                Text = "الإصدار " + Program.Version + " · تطوير: " + Developer,
                Location = new Point(80, 46),
                Size = new Size(304, 24),
                ForeColor = SystemColors.GrayText,
            };

            var engines = new Label
            {
                Text = "بينزّل بـ yt-dlp وبيعدّل بـ ffmpeg، والاتنين في " + Tools.Folder + "\n" + Tools.Versions,
                Location = new Point(16, 84),
                Size = new Size(368, 66),
            };

            var repo = new LinkLabel { Text = "الكود على GitHub", Location = new Point(16, 156), Size = new Size(180, 24) };
            repo.LinkClicked += delegate { Process.Start(Repo); };
            var report = new LinkLabel { Text = "بلّغ عن مشكلة أو اقترح حاجة", Location = new Point(16, 182), Size = new Size(260, 24) };
            report.LinkClicked += delegate { Process.Start(Repo + "/issues/new"); };

            var close = new Button
            {
                Text = "إغلاق",
                DialogResult = DialogResult.OK,
                Location = new Point(288, 206),
                Size = new Size(96, 34),
            };

            Controls.AddRange(new Control[] { logo, name, version, engines, repo, report, close });
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
