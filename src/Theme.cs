using System.Drawing;
using System.Windows.Forms;

namespace Hatha
{
    // The few looks that are not Windows defaults: the main button of each screen wears the accent colour
    // the user chose for Windows, and the status line speaks in colour.
    internal static class Theme
    {
        public static readonly Color Accent = ReadAccent();
        public static readonly Color Success = Color.FromArgb(16, 124, 16);
        public static readonly Color Failure = Color.Firebrick;
        static readonly Color Disabled = Color.FromArgb(204, 204, 204);

        public static void Primary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Shade(Accent, 0.88);
            button.FlatAppearance.MouseDownBackColor = Shade(Accent, 0.76);
            button.UseVisualStyleBackColor = false;
            button.ForeColor = Luma(Accent) > 170 ? Color.Black : Color.White;
            button.Font = new Font(button.Font, FontStyle.Bold);
            button.EnabledChanged += delegate { button.BackColor = button.Enabled ? Accent : Disabled; };
            button.BackColor = button.Enabled ? Accent : Disabled;
        }

        static Color ReadAccent()
        {
            uint argb = Native.AccentColor();
            Color accent = Color.FromArgb((int)(argb >> 16) & 0xFF, (int)(argb >> 8) & 0xFF, (int)argb & 0xFF);
            // Windows reports a near-white colour when it has no accent to give; then our own stands in.
            return argb == 0 || Luma(accent) > 235 ? Color.FromArgb(228, 87, 46) : accent;
        }

        static Color Shade(Color color, double factor)
        {
            return Color.FromArgb((int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));
        }

        static double Luma(Color color)
        {
            return 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        }
    }
}
