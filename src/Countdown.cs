using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        bool first;
        using (var instance = new Mutex(true, @"Local\ViceCityCountdown", out first))
        {
            if (!first) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (var fonts = new PrivateFontCollection())
                {
                    fonts.AddFontFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "fonts", "ArchivoBlack-Regular.ttf"));
                    using (var face = new FontFamily("Archivo Black", fonts))
                    using (var countdown = new Countdown(face))
                        Application.Run(countdown);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "罪城倒计时", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

internal sealed class Countdown : ApplicationContext
{
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly List<Overlay> overlays = new List<Overlay>();
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly FontFamily face;
    private Preferences preferences;
    private string screenLayout;
    private long lastSeconds = -1;
    private Stage lastStage;
    private Stage preview;
    private readonly Stopwatch previewClock = new Stopwatch();

    internal Countdown(FontFamily face)
    {
        this.face = face;
        preferences = Preferences.Load();
        menu.Items.Add("编辑配置", null, (sender, args) => OpenPreferences());
        menu.Items.Add("重载配置", null, (sender, args) => ReloadPreferences());
        var previews = new ToolStripMenuItem("预览动画");
        AddPreview(previews, "整小时扫光", Stage.Hour);
        AddPreview(previews, "六小时日落", Stage.Sunset);
        AddPreview(previews, "最后一小时", Stage.FinalHour);
        AddPreview(previews, "最后一分钟全屏表盘", Stage.Dial);
        previews.DropDownItems.Add("结束预览", null, (sender, args) =>
        {
            previewClock.Reset();
            Update();
        });
        menu.Items.Add(previews);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出倒计时", null, (sender, args) => ExitThread());
        tray = new NotifyIcon
        {
            Text = "罪城倒计时",
            Icon = SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true
        };
        timer.Interval = 200;
        timer.Tick += (sender, args) => Update();
        Update();
        timer.Start();
    }

    private void AddPreview(ToolStripMenuItem parent, string label, Stage stage)
    {
        parent.DropDownItems.Add(label, null, (sender, args) =>
        {
            preview = stage;
            previewClock.Restart();
            Update();
        });
    }

    private static void OpenPreferences()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Preferences.FilePath + "\"")
            {
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法打开配置");
        }
    }

    private void ReloadPreferences()
    {
        try
        {
            preferences = Preferences.Load();
            screenLayout = null;
            lastSeconds = -1;
            Update();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "配置读取失败");
        }
    }

    private void Update()
    {
        var remaining = preferences.Deadline - DateTimeOffset.UtcNow;
        var previewElapsed = previewClock.Elapsed.TotalSeconds;
        if (previewClock.IsRunning && previewElapsed >= StageFrame.PreviewLength(preview))
            previewClock.Reset();
        bool isPreview = previewClock.IsRunning;
        if (!isPreview && remaining.TotalSeconds < -10)
        {
            if (screenLayout != string.Empty)
            {
                ClearOverlays();
                screenLayout = string.Empty;
                tray.Text = "罪城倒计时 · 已结束";
            }
            return;
        }
        var frame = isPreview ? StageFrame.Preview(preview, previewElapsed) : StageFrame.For(remaining.TotalSeconds);
        timer.Interval = frame.Animated ? 50 : 150;
        if (frame.Seconds == lastSeconds && frame.Kind == lastStage && !frame.Animated) return;
        lastSeconds = frame.Seconds;
        lastStage = frame.Kind;

        var selected = preferences.SelectScreens(Screen.AllScreens);
        bool fullscreen = frame.Kind == Stage.Dial;
        var layout = fullscreen + "|" + string.Join("|", selected.Select(screen =>
            screen.DeviceName + (fullscreen ? screen.Bounds : screen.WorkingArea)));
        if (screenLayout != layout)
        {
            ClearOverlays();
            foreach (var screen in selected)
                overlays.Add(new Overlay(fullscreen ? screen.Bounds : screen.WorkingArea, face, preferences, fullscreen));
            screenLayout = layout;
            tray.Text = "罪城倒计时 · " + preferences.DateLabel;
        }

        foreach (var overlay in overlays) overlay.Draw(frame);
    }

    private void ClearOverlays()
    {
        foreach (var overlay in overlays) overlay.Dispose();
        overlays.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            ClearOverlays();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class Preferences
{
    internal static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "countdown.ini");
    internal DateTimeOffset Deadline { get; private set; }
    internal byte Alpha { get; private set; }
    private string Displays { get; set; }

    internal string DateLabel
    {
        get { return Deadline.ToString("MM / dd    HH:mm", CultureInfo.InvariantCulture); }
    }

    internal static Preferences Load()
    {
        var result = new Preferences
        {
            Deadline = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.FromHours(8)),
            Alpha = 79,
            Displays = "AUTO"
        };
        if (!File.Exists(FilePath)) return result;

        foreach (var raw in File.ReadLines(FilePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var setting = line.Split(new[] { '=' }, 2);
            if (setting.Length != 2) throw new FormatException("配置行应使用“名称=值”格式。");
            var value = setting[1].Trim();
            switch (setting[0].Trim().ToUpperInvariant())
            {
                case "DEADLINE":
                    DateTimeOffset end;
                    if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:sszzz",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                        throw new FormatException("Deadline 示例为 2026-11-19T00:00:00+08:00。");
                    result.Deadline = end;
                    break;
                case "OPACITY":
                    int percent;
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out percent)
                        || percent < 1 || percent > 100)
                        throw new FormatException("Opacity 需要是 1 到 100 之间的整数。");
                    result.Alpha = (byte)Math.Round(255 * percent / 100.0);
                    break;
                case "DISPLAYS":
                    result.Displays = value.ToUpperInvariant();
                    break;
                default:
                    throw new FormatException("未知配置项 " + setting[0].Trim());
            }
        }
        return result;
    }

    internal Screen[] SelectScreens(Screen[] screens)
    {
        if (Displays == "ALL") return screens;
        if (Displays == "PRIMARY") return screens.Where(screen => screen.Primary).ToArray();
        if (Displays == "AUTO")
        {
            var preferred = screens.Where(screen =>
                screen.DeviceName == @"\\.\DISPLAY1" || screen.DeviceName == @"\\.\DISPLAY2").ToArray();
            if (preferred.Length == 2) return preferred;
            return screens.OrderByDescending(screen => screen.Primary)
                .ThenByDescending(screen => screen.Bounds.Height > screen.Bounds.Width)
                .ThenBy(screen => screen.Bounds.Left)
                .Take(2).ToArray();
        }
        var names = Displays.Split(',').Select(name => name.Trim());
        return screens.Where(screen => names.Contains(screen.DeviceName.Substring(4).ToUpperInvariant())).ToArray();
    }
}

internal sealed class Overlay : NativeWindow, IDisposable
{
    private readonly Rectangle monitor;
    private readonly bool portrait;
    private readonly bool fullscreen;
    private readonly Size canvas;
    private readonly FontFamily face;
    private readonly Preferences preferences;

    internal Overlay(Rectangle bounds, FontFamily face, Preferences preferences, bool fullscreen = false)
    {
        monitor = bounds;
        this.face = face;
        this.preferences = preferences;
        this.fullscreen = fullscreen;
        portrait = bounds.Height > bounds.Width;
        canvas = fullscreen ? bounds.Size : new Size((int)(bounds.Width * (portrait ? .84 : .96)),
            (int)(bounds.Width * (portrait ? .48 : .21)));
        CreateHandle(new CreateParams
        {
            Caption = "Vice City Countdown Overlay",
            Style = unchecked((int)0x80000000), // WS_POPUP
            ExStyle = 0x080800A8, // NOACTIVATE, LAYERED, TOOLWINDOW, TRANSPARENT, TOPMOST
            Width = canvas.Width,
            Height = canvas.Height
        });
    }

    internal void Draw(StageFrame frame)
    {
        using (var bitmap = Render(frame)) Present(bitmap, frame.WithBaseAlpha(preferences.Alpha));
    }

    internal Bitmap Render(StageFrame frame)
    {
        var bitmap = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (frame.Kind == Stage.Dial)
                    StageArtwork.Dial(graphics, canvas, frame.Seconds, preferences.DateLabel, DrawText);
                else if (frame.Kind == Stage.FinalHour)
                    StageArtwork.FinalHour(graphics, canvas, frame, preferences.DateLabel, DrawText);
                else if (frame.Kind == Stage.Sunset)
                    StageArtwork.Sunset(graphics, canvas, frame, DrawText);
                else
                {
                    DrawRegular(graphics, TimeSpan.FromSeconds(frame.Seconds));
                    if (frame.Kind == Stage.Hour) StageArtwork.Hour(graphics, canvas, frame);
                }
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private void DrawRegular(Graphics graphics, TimeSpan remaining)
    {
        var clock = remaining.ToString(@"hh\:mm\:ss");
        var days = remaining.Days.ToString("00") + " DAY";
        if (portrait)
        {
            DrawText(graphics, days, canvas.Width * .29f, canvas.Height * .17f, canvas.Height * .18f);
            DrawText(graphics, clock, canvas.Width * .89f, canvas.Height * .51f, canvas.Height * .43f);
        }
        else
            DrawText(graphics, days + "   " + clock, canvas.Width * .96f, canvas.Height * .36f,
                canvas.Height * .69f);
        DrawDate(graphics);
    }

    private void DrawText(Graphics graphics, string label, float maxWidth, float centerY, float maxHeight)
    {
        using (var path = new GraphicsPath())
        {
            path.AddString(label, face, (int)FontStyle.Regular, 100, PointF.Empty,
                StringFormat.GenericTypographic);
            var bounds = path.GetBounds();
            const float widthRatio = .76f;
            var scale = Math.Min(maxWidth / (bounds.Width * widthRatio), maxHeight / bounds.Height);
            using (var matrix = new Matrix())
            {
                matrix.Translate(canvas.Width / 2f, centerY);
                matrix.Scale(scale * widthRatio, scale);
                matrix.Translate(-bounds.Left - bounds.Width / 2f, -bounds.Top - bounds.Height / 2f);
                path.Transform(matrix);
            }
            var frame = path.GetBounds();
            frame.Inflate(1, 1);
            using (var border = new Pen(Color.FromArgb(229, 47, 15, 70), Math.Max(2, frame.Height * .030f)))
            using (var sunset = new LinearGradientBrush(frame, Color.Violet, Color.Orange, 90f))
            {
                border.LineJoin = LineJoin.Round;
                sunset.InterpolationColors = new ColorBlend
                {
                    Positions = new[] { 0f, .50f, 1f },
                    Colors = new[] { Color.FromArgb(151, 74, 244), Color.FromArgb(248, 67, 152),
                        Color.FromArgb(255, 157, 77) }
                };
                graphics.DrawPath(border, path);
                graphics.FillPath(sunset, path);
            }
        }
    }

    private void DrawDate(Graphics graphics)
    {
        float dateY = canvas.Height * (portrait ? .85f : .77f);
        float labelSize = canvas.Width * (portrait ? .031f : .022f);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var font = new Font("Segoe UI", labelSize, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var color = new SolidBrush(Color.FromArgb(228, 145, 219)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var line = new Pen(Color.FromArgb(255, 138, 171), Math.Max(1, labelSize / 13f)))
        {
            string date = preferences.DateLabel;
            float mid = canvas.Width / 2f;
            float gap = graphics.MeasureString(date, font).Width / 2f + labelSize * .55f;
            float length = labelSize * 1.85f;
            graphics.DrawLine(line, mid - gap - length, dateY, mid - gap, dateY);
            graphics.DrawLine(line, mid + gap, dateY, mid + gap + length, dateY);
            graphics.DrawString(date, font, color, new PointF(mid, dateY), format);
        }
    }

    private void Present(Bitmap bitmap, byte alpha)
    {
        var dc = Native.CreateCompatibleDC(IntPtr.Zero);
        var pixels = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = Native.SelectObject(dc, pixels);
        try
        {
            var destination = fullscreen ? monitor.Location : new Point(monitor.Left + (monitor.Width - canvas.Width) / 2,
                monitor.Top + (int)(monitor.Height * .48) - canvas.Height / 2);
            var source = Point.Empty;
            var size = canvas;
            var blend = new Native.Blend { Alpha = alpha, UsePixelAlpha = 1 };
            if (!Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size,
                dc, ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Native.ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
        }
        finally
        {
            Native.SelectObject(dc, previous);
            Native.DeleteObject(pixels);
            Native.DeleteDC(dc);
        }
    }

    public void Dispose() { DestroyHandle(); }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Blend
    {
        internal byte Operation, Flags, Alpha, UsePixelAlpha;
    }

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc,
        ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, int key,
        ref Blend blend, int flags);
    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(IntPtr dc);
}
