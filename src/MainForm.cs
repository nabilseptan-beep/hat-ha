using System;
using System.Drawing;
using System.Diagnostics;
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
        readonly ToolStripStatusLabel update;
        Version latest;   // a release newer than this build, once one is found
        string fresh;     // its exe, once downloaded
        bool fetchFailed;

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
            update = new ToolStripStatusLabel { IsLink = true, Visible = false };
            update.Click += delegate { Upgrade(); };
            var about = new ToolStripStatusLabel("عن البرنامج") { IsLink = true };
            about.Click += delegate { using (var form = new AboutForm()) form.ShowDialog(this); };

            var bar = new StatusStrip { SizingGrip = false };
            bar.Items.AddRange(new ToolStripItem[] { status, progress, retry, update, about });

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
                await CheckForUpdate();
            }
            catch (Exception ex)
            {
                progress.Visible = false;
                status.Text = "مش عارف أنزّل المحركات (اتأكد من النت): " + ex.Message;
                retry.Visible = true;
            }
        }

        // Once a day, asks GitHub whether a newer release exists and offers it on the status line.
        async Task CheckForUpdate()
        {
            if (settings.UpdateChecked >= DateTime.Today) return;
            Version found = await Updater.Latest();
            if (found == null) return;   // offline, or nothing released yet: ask again next time
            settings.UpdateChecked = DateTime.Today;
            settings.Save();
            if (!Updater.IsNewer(found, Program.Version)) return;
            latest = found;
            update.Text = "فيه إصدار " + latest.ToString(3) + " جديد — حدّث دلوقتي";
            update.Visible = true;
        }

        // Fetches the new exe, then swaps it in and restarts, with one question in between. A second click
        // after a refusal restarts without fetching again; after a failure it opens the releases page.
        async void Upgrade()
        {
            if (fetchFailed)
            {
                Process.Start(Updater.Releases);
                return;
            }
            if (fresh == null)
            {
                update.Enabled = false;
                progress.Visible = true;
                try
                {
                    fresh = await Updater.Fetch(latest, Application.ExecutablePath, delegate(int percent)
                    {
                        progress.Value = percent;
                        update.Text = "بينزّل الإصدار " + latest.ToString(3) + " (" + percent + "%)";
                    });
                }
                catch (Exception ex)   // no network, or the exe sits somewhere it can't write
                {
                    fetchFailed = true;
                    update.Text = "مش عارف أنزّل التحديث (" + ex.Message + ") — افتح صفحة الإصدارات";
                }
                progress.Visible = false;
                update.Enabled = true;
                if (fetchFailed) return;
            }

            DialogResult answer = MessageBox.Show(this,
                "الإصدار " + latest.ToString(3) + " اتنزّل. هاتها هيقفل ويفتح تاني بيه. نعمل كده دلوقتي؟",
                "تحديث", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer != DialogResult.Yes)
            {
                update.Text = "الإصدار " + latest.ToString(3) + " جاهز — أعد التشغيل";
                return;
            }
            Updater.Apply(Application.ExecutablePath, fresh, Process.GetCurrentProcess().Id);
            Close();
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
