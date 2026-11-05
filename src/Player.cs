using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Hatha
{
    // Plays a piece of a video inside one of our controls through ffplay: its borderless window is pulled
    // in as a child and its clock is read off the status line it keeps printing. The start and the end are
    // exact (-ss and -t), sound and picture stay together, and whatever ffmpeg can decode plays.
    // There is no pause: stopping is enough, since the still picture is a frame ffmpeg draws anyway.
    internal sealed class Player
    {
        const int HookEveryMs = 50;
        const int HookForMs = 5000;
        static readonly Regex Clock = new Regex(@"^\s*(\d+\.\d+)");

        readonly Control host;
        readonly SynchronizationContext context = SynchronizationContext.Current;
        readonly System.Windows.Forms.Timer hook = new System.Windows.Forms.Timer { Interval = HookEveryMs };
        Process process;
        IntPtr window;
        string title;
        Size video;
        long positionMillis = -1;
        bool stopping;

        // Raised when ffplay reaches the end of the piece by itself.
        public event Action Ended = delegate { };

        public Player(Control host)
        {
            this.host = host;
            host.Resize += delegate { if (window != IntPtr.Zero) Native.MoveWindow(window, Fit()); };
            hook.Tick += delegate { Grab(); };
        }

        public bool Playing
        {
            get { return process != null; }
        }

        // Seconds into the file, as ffplay last reported; -1 before the first report.
        public double Position
        {
            get { return Interlocked.Read(ref positionMillis) / 1000.0; }
        }

        public void Play(string file, double from, double length, Size videoSize)
        {
            Stop();
            video = videoSize;
            title = "hatha-player-" + Process.GetCurrentProcess().Id;
            Rectangle rect = Fit();
            Point screen = host.PointToScreen(rect.Location);
            process = new Process
            {
                StartInfo = new ProcessStartInfo(Tools.Ffplay,
                    "-hide_banner -autoexit -noborder -window_title " + title
                    + " -left " + screen.X + " -top " + screen.Y + " -x " + rect.Width + " -y " + rect.Height
                    + " -ss " + Ffmpeg.Seconds(from) + " -t " + Ffmpeg.Seconds(length) + " \"" + file + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };
            Process started = process;
            started.Exited += delegate
            {
                context.Post(delegate
                {
                    if (process != started) return;   // already stopped, or replaced
                    bool byItself = !stopping;
                    Forget();
                    if (byItself) Ended();
                }, null);
            };
            stopping = false;
            Interlocked.Exchange(ref positionMillis, -1);
            started.Start();
            new Thread(delegate() { ReadClock(started); }) { IsBackground = true }.Start();
            hook.Start();
        }

        public void Stop()
        {
            if (process == null) return;
            stopping = true;
            try { process.Kill(); }
            catch (InvalidOperationException) { }   // just exited
            Forget();
        }

        void Forget()
        {
            hook.Stop();
            process = null;
            window = IntPtr.Zero;
        }

        // ffplay's window shows up a few hundred milliseconds after it starts; then it becomes ours.
        void Grab()
        {
            if (process == null) { hook.Stop(); return; }
            IntPtr found = Native.FindWindow(null, title);
            if (found == IntPtr.Zero)
            {
                if ((DateTime.Now - process.StartTime).TotalMilliseconds > HookForMs) hook.Stop();
                return;
            }
            hook.Stop();
            window = found;
            Native.MakeChild(window, host.Handle);
            Native.MoveWindow(window, Fit());
            Native.EnableWindow(window, false);   // clicks and keys stay with our window
        }

        // The largest rectangle with the video's shape that fits in the host, centred.
        Rectangle Fit()
        {
            Size box = host.ClientSize;
            if (video.Width <= 0 || video.Height <= 0) return new Rectangle(Point.Empty, box);
            int width = box.Width, height = box.Width * video.Height / video.Width;
            if (height > box.Height)
            {
                height = box.Height;
                width = box.Height * video.Width / video.Height;
            }
            return new Rectangle((box.Width - width) / 2, (box.Height - height) / 2, width, height);
        }

        // ffplay rewrites one status line with \r: "   3.45 A-V:  0.007 fd=  17 aq= ...". The number is the clock.
        void ReadClock(Process ffplay)
        {
            var buffer = new char[256];
            var line = new StringBuilder();
            int count;
            while ((count = ffplay.StandardError.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i] != '\r' && buffer[i] != '\n')
                    {
                        line.Append(buffer[i]);
                        continue;
                    }
                    Match match = Clock.Match(line.ToString());
                    if (match.Success)
                        Interlocked.Exchange(ref positionMillis, (long)(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 1000));
                    line.Length = 0;
                }
            }
        }
    }
}
