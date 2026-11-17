using System;
using System.Drawing;
using System.Drawing.Drawing2D;

internal enum Stage { Regular, Hour, Sunset, FinalHour, Dial }

internal struct StageFrame
{
    internal Stage Kind;
    internal long Seconds;
    internal int Milestone;
    internal float Motion;
    internal byte Alpha;

    internal bool Animated { get { return Kind == Stage.Hour || Kind == Stage.Sunset; } }

    internal byte WithBaseAlpha(byte baseAlpha)
    {
        return (byte)(baseAlpha + Math.Round((255 - baseAlpha) * (Alpha - 79) / 176.0));
    }

    internal static StageFrame For(double remaining)
    {
        var seconds = Math.Max(0L, (long)Math.Ceiling(remaining));
        if (seconds <= 60) return Frame(Stage.Dial, seconds, 1, 255);
        if (seconds <= 3600) return Frame(Stage.FinalHour, seconds, 1, 210);

        var hours = (int)Math.Ceiling(remaining / 3600);
        var elapsed = hours * 3600.0 - remaining;
        var kind = hours % 6 == 0 ? Stage.Sunset : Stage.Hour;
        var length = PreviewLength(kind);
        if (elapsed < length)
        {
            var motion = (float)(elapsed / length);
            var strength = StageArtwork.Smooth(motion / .16f) *
                StageArtwork.Smooth((1 - motion) / .27f);
            var animation = Frame(kind, seconds, motion, (byte)(79 + 141 * strength));
            animation.Milestone = hours;
            return animation;
        }
        return Frame(Stage.Regular, seconds, 0, 79);
    }

    internal static double PreviewLength(Stage kind)
    {
        if (kind == Stage.Hour) return 2.6;
        if (kind == Stage.Sunset) return 5.2;
        return kind == Stage.Dial ? 9.25 : 8;
    }

    internal static StageFrame Preview(Stage kind, double elapsed)
    {
        if (kind == Stage.Hour) return For(27 * 3600 - elapsed);
        if (kind == Stage.Sunset) return For(24 * 3600 - elapsed);
        if (kind == Stage.FinalHour) return For(3600 - elapsed);
        return For(8 - elapsed);
    }

    private static StageFrame Frame(Stage kind, long seconds, float motion, byte alpha)
    {
        return new StageFrame { Kind = kind, Seconds = seconds, Motion = motion, Alpha = alpha };
    }
}

internal static class StageArtwork
{
    internal delegate void DisplayText(Graphics graphics, string text, float width, float y, float height);

    private static readonly Color Peach = Color.FromArgb(255, 174, 119);
    private static readonly Color Lilac = Color.FromArgb(210, 147, 254);
    private static readonly Color HotPink = Color.FromArgb(255, 105, 174);
    private static readonly Color Cream = Color.FromArgb(255, 228, 218);

    internal static float Smooth(float value)
    {
        value = Math.Max(0, Math.Min(1, value));
        return value * value * (3 - 2 * value);
    }

    internal static void Hour(Graphics g, Size size, StageFrame frame)
    {
        float w = size.Width, h = size.Height;
        var edge = w * .038f;
        var scan = -w * .12f + Smooth(frame.Motion) * w * 1.24f;
        var bright = Smooth(frame.Motion / .17f) * Smooth((1 - frame.Motion) / .28f);
        using (var line = new Pen(Color.FromArgb((int)(177 * bright), HotPink), Math.Max(2, h * .005f)))
        using (var light = new Pen(Color.FromArgb((int)(255 * bright), Peach), Math.Max(2, h * .009f)))
        {
            foreach (float y in new[] { h * .035f, h * .96f })
            {
                g.DrawLine(line, edge, y, w - edge, y);
                if (scan + w * .14f > edge && scan - w * .14f < w - edge)
                    g.DrawLine(light, Math.Max(edge, scan - w * .14f), y,
                        Math.Min(w - edge, scan + w * .14f), y);
            }
        }
        Label(g, frame.Milestone + " HOURS LEFT", w * .5f, h * .94f, h * .031f,
            Color.FromArgb((int)(255 * bright), Cream));
    }

    internal static void Sunset(Graphics g, Size size, StageFrame frame, DisplayText numbers)
    {
        float w = size.Width, h = size.Height;
        float appear = Smooth(frame.Motion / .24f);
        var panel = new RectangleF(w * .06f, h * .045f, w * .88f, h * .88f);
        using (var sky = new LinearGradientBrush(panel, Color.FromArgb(40, 12, 64),
            Color.FromArgb(64, 19, 50), 90f))
        using (var border = new Pen(Color.FromArgb(130, HotPink), Math.Max(2, h * .003f)))
        {
            g.FillRectangle(sky, panel);
            g.DrawRectangle(border, panel.X, panel.Y, panel.Width, panel.Height);
        }

        var horizon = h * .70f;
        var sunSize = h * .52f;
        var sun = new RectangleF(w * .5f - sunSize / 2, h * (.48f - appear * .32f), sunSize, sunSize);
        var original = g.Save();
        g.SetClip(RectangleF.FromLTRB(panel.Left, panel.Top, panel.Right, horizon));
        using (var sunPaint = new LinearGradientBrush(sun, Color.FromArgb(98, Peach),
            Color.FromArgb(190, HotPink), 90f))
        {
            g.FillEllipse(sunPaint, sun);
        }
        using (var stripe = new Pen(Color.FromArgb(217, 55, 17, 57), h * .010f))
            for (var i = 0; i < 6; i++)
            {
                var y = sun.Y + sun.Height * (.57f + i * .068f);
                var halfWidth = sun.Width * (float)Math.Sqrt(Math.Max(0,
                    1 - Math.Pow((y - sun.Y - sun.Height / 2) / (sun.Height / 2), 2))) / 2;
                g.DrawLine(stripe, w * .5f - halfWidth, y, w * .5f + halfWidth, y);
            }
        g.Restore(original);

        using (var grid = new Pen(Color.FromArgb(99, HotPink), Math.Max(1, h * .0025f)))
        {
            original = g.Save();
            g.SetClip(panel);
            for (var i = -5; i <= 5; i++)
                g.DrawLine(grid, w * .5f + i * w * .027f, horizon, w * .5f + i * w * .18f, panel.Bottom);
            for (var i = 0; i < 6; i++)
            {
                var y = horizon + h * .205f * i * i / 25;
                g.DrawLine(grid, panel.Left, y, panel.Right, y);
            }
            g.Restore(original);
        }

        Label(g, "VICE CITY COUNTDOWN", w * .5f, h * .165f, h * .047f, Cream);
        numbers(g, frame.Milestone.ToString("00"), w * .49f, h * .47f, h * .39f);
        Label(g, "HOURS REMAINING", w * .5f, h * .79f, h * .051f, Cream);
    }

    internal static void FinalHour(Graphics g, Size size, StageFrame frame, string date, DisplayText numbers)
    {
        float w = size.Width, h = size.Height;
        var panel = new RectangleF(w * .055f, h * .04f, w * .89f, h * .91f);
        using (var field = new LinearGradientBrush(panel, Color.FromArgb(218, 29, 10, 51),
            Color.FromArgb(224, 58, 17, 46), 19f))
        using (var edge = new Pen(Color.FromArgb(155, Lilac), Math.Max(2, h * .004f)))
        using (var light = new Pen(Peach, Math.Max(3, h * .008f)))
        {
            g.FillRectangle(field, panel);
            g.DrawRectangle(edge, panel.X, panel.Y, panel.Width, panel.Height);
            g.DrawLine(light, panel.Left, panel.Top, panel.Left + w * .105f, panel.Top);
            g.DrawLine(light, panel.Right - w * .105f, panel.Bottom, panel.Right, panel.Bottom);
            var left = panel.Left + panel.Width * .15f;
            var right = panel.Right - panel.Width * .15f;
            var y = h * .81f;
            g.DrawLine(edge, left, y, right, y);
            g.DrawLine(light, left, y, left + (right - left) * frame.Seconds / 3600f, y);
        }
        Label(g, "FINAL HOUR", w * .5f, h * .18f, h * .078f, Cream);
        numbers(g, Clock(frame.Seconds), w * .75f, h * .48f, h * .44f);
        Label(g, date, w * .5f, h * .89f, h * .044f, Lilac);
    }

    internal static void Dial(Graphics g, Size size, long seconds, string date, DisplayText numbers)
    {
        float w = size.Width, h = size.Height;
        float r = Math.Min(w, h) * .385f;
        var center = new PointF(w / 2, h / 2);
        var rect = new RectangleF(0, 0, w, h);
        using (var field = new LinearGradientBrush(rect, Color.FromArgb(247, 15, 7, 34),
            Color.FromArgb(250, 54, 15, 49), 58f))
            g.FillRectangle(field, rect);

        using (var faint = new Pen(Color.FromArgb(96, Lilac), r * .006f))
        using (var tick = new Pen(Color.FromArgb(135, Cream), r * .004f))
        using (var major = new Pen(Lilac, r * .008f))
        using (var arc = new Pen(HotPink, r * .020f))
        using (var hand = new Pen(Peach, r * .012f))
        {
            var track = Circle(center, r * 1.05f);
            g.DrawEllipse(faint, track);
            g.DrawEllipse(faint, Circle(center, r * .79f));
            for (int i = 0; i < 60; i++)
            {
                var longTick = i % 5 == 0;
                var angle = i * Math.PI / 30;
                g.DrawLine(longTick ? major : tick,
                    Polar(center, r * (longTick ? .85f : .91f), angle), Polar(center, r, angle));
            }
            var sweep = (float)(seconds * 6);
            if (sweep > 0) g.DrawArc(arc, track, -90, sweep);
            var progress = seconds * Math.PI / 30;
            g.DrawLine(hand, Polar(center, r * .83f, progress), Polar(center, r * 1.10f, progress));
        }
        Label(g, seconds == 0 ? "TIME UP" : "FINAL MINUTE", center.X,
            center.Y - r * .53f, r * .083f, Cream);
        numbers(g, Clock(seconds), r * 1.43f, center.Y, r * .52f);
        Label(g, date, center.X, center.Y + r * .55f, r * .065f, Lilac);
    }

    private static PointF Polar(PointF center, float radius, double angle)
    {
        return new PointF(center.X + radius * (float)Math.Sin(angle),
            center.Y - radius * (float)Math.Cos(angle));
    }

    private static RectangleF Circle(PointF center, float radius)
    {
        return new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    private static string Clock(long seconds)
    {
        return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    private static void Label(Graphics g, string text, float x, float y, float size, Color color)
    {
        using (var font = new Font("Segoe UI", Math.Max(8, size), FontStyle.Bold, GraphicsUnit.Pixel))
        using (var ink = new SolidBrush(color))
        using (var format = new StringFormat { Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center })
            g.DrawString(text, font, ink, new PointF(x, y), format);
    }
}
