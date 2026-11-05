using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Hatha
{
    // Small dialog with the app name, version and developer.
    internal sealed class AboutForm : Form
    {
        const string Developer = "Nabil Septan";

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
            ClientSize = new Size(360, 200);

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
                Size = new Size(264, 30),
            };

            var version = new Label
            {
                Text = "الإصدار " + Assembly.GetExecutingAssembly().GetName().Version.ToString(2),
                Location = new Point(80, 46),
                Size = new Size(264, 24),
                ForeColor = SystemColors.GrayText,
            };

            var engines = new Label
            {
                Text = "بينزّل بـ yt-dlp وبيعدّل بـ ffmpeg، والاتنين في " + Tools.Folder,
                Location = new Point(16, 84),
                Size = new Size(328, 44),
            };

            var developer = new Label
            {
                Text = "تطوير: " + Developer,
                Location = new Point(16, 130),
                Size = new Size(328, 24),
            };

            var close = new Button
            {
                Text = "إغلاق",
                DialogResult = DialogResult.OK,
                Location = new Point(16, 158),
                Size = new Size(96, 34),
            };

            Controls.AddRange(new Control[] { logo, name, version, engines, developer, close });
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
