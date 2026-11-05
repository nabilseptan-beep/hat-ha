using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hatha
{
    // The one window: a "هاتها" tab to download and a "قصقوصة" tab to edit, with the engines' status below.
    internal sealed class MainForm : Form
    {
        readonly Settings settings = Settings.Load();
        readonly TabControl tabs;
        readonly HathaTab hatha;
        readonly QasqousaTab qasqousa = new QasqousaTab();
        readonly ToolStripStatusLabel status;
        readonly ToolStripProgressBar progress;
        readonly ToolStripStatusLabel retry;

        public MainForm()
        {
            Text = "هاتها";
            Icon = LoadIcon();
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(720, 620);
            MinimumSize = Size;
            KeyPreview = true;

            hatha = new HathaTab(settings);
            hatha.EditRequested += delegate(string file)
            {
                tabs.SelectedTab = qasqousa;
                qasqousa.Load(file);
            };
            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 6), RightToLeftLayout = true };
            tabs.TabPages.Add(hatha);
            tabs.TabPages.Add(qasqousa);

            // The form mirrors its children, so the strip is laid out left-to-right and shows up right-to-left.
            status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            progress = new ToolStripProgressBar { Width = 200, Visible = false };
            retry = new ToolStripStatusLabel("حاول تاني") { IsLink = true, Visible = false };
            retry.Click += delegate { Setup(); };
            var about = new ToolStripStatusLabel("عن البرنامج") { IsLink = true };
            about.Click += delegate { using (var form = new AboutForm()) form.ShowDialog(this); };

            var bar = new StatusStrip { SizingGrip = false };
            bar.Items.AddRange(new ToolStripItem[] { status, progress, retry, about });

            Controls.Add(tabs);
            Controls.Add(bar);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Setup();
        }

        // Coming back to the window with a link on the clipboard is as good as pasting it.
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (tabs.Enabled) hatha.OfferClipboard();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (tabs.Enabled && tabs.SelectedTab == hatha && hatha.Shortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // A yt-dlp or ffmpeg left running would keep writing into Downloads after the window is gone.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            hatha.Cancel();
            qasqousa.Cancel();
            base.OnFormClosing(e);
        }

        // Installs the engines on the first run, otherwise updates yt-dlp once a week. The tabs wait for it.
        async void Setup()
        {
            tabs.Enabled = false;
            retry.Visible = false;
            try
            {
                if (!Tools.Installed)
                {
                    progress.Visible = true;
                    await Tools.Install(delegate(string what, int percent) { status.Text = what; progress.Value = percent; });
                    progress.Visible = false;
                    settings.YtDlpUpdated = DateTime.Today;
                    settings.Save();
                }
                else if ((DateTime.Today - settings.YtDlpUpdated).TotalDays >= 7)
                {
                    status.Text = "بيحدّث yt-dlp...";
                    if (await Tools.UpdateYtDlp() == 0)
                    {
                        settings.YtDlpUpdated = DateTime.Today;
                        settings.Save();
                    }
                }
                status.Text = "جاهز";
                tabs.Enabled = true;
                hatha.OfferClipboard();
                await Tools.ReadVersions();
            }
            catch (Exception ex)
            {
                progress.Visible = false;
                status.Text = "مش عارف أنزّل المحركات (اتأكد من النت): " + ex.Message;
                retry.Visible = true;
            }
        }

        static Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hatha.icon.ico"))
                return new Icon(stream);
        }
    }

    // A tab page mirrored the way a form is, so controls laid out from the left show up from the right.
    // A mirrored TabControl doesn't pass that on to its pages.
    internal class MirroredTabPage : TabPage
    {
        // What the page measures inside the window at its starting size. Controls anchored to the right or
        // bottom keep their distance from these edges, so the page must have them before the controls do.
        protected MirroredTabPage()
        {
            Size = new Size(712, 554);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYOUTRTL | Native.WS_EX_NOINHERITLAYOUT;
                cp.ExStyle &= ~(Native.WS_EX_RTLREADING | Native.WS_EX_RIGHT | Native.WS_EX_LEFTSCROLLBAR);
                return cp;
            }
        }
    }
}
