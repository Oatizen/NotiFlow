using System;
using System.Windows.Media;

namespace NotiFlow.Helpers
{
    public static class ColorHelper
    {
        public static void RgbToHsv(Color color, out double h, out double s, out double v)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;

            if (max == 0)
            {
                s = 0;
                h = 0;
            }
            else
            {
                s = delta / max;

                if (delta == 0)
                {
                    h = 0;
                }
                else
                {
                    if (r == max)
                        h = (g - b) / delta;
                    else if (g == max)
                        h = 2 + (b - r) / delta;
                    else
                        h = 4 + (r - g) / delta;

                    h *= 60;
                    if (h < 0) h += 360;
                }
            }
        }

        public static Color HsvToRgb(double h, double s, double v, byte a = 255)
        {
            double r = 0, g = 0, b = 0;

            if (s == 0)
            {
                r = v;
                g = v;
                b = v;
            }
            else
            {
                if (h >= 360) h = 0;
                else h /= 60;

                int i = (int)Math.Truncate(h);
                double f = h - i;

                double p = v * (1.0 - s);
                double q = v * (1.0 - (s * f));
                double t = v * (1.0 - (s * (1.0 - f)));

                switch (i)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    default: r = v; g = p; b = q; break;
                }
            }

            return Color.FromArgb(a, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }
    }
}
