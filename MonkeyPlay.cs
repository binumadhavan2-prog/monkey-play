using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Windows.Automation;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Text.RegularExpressions;

// A cool cartoon monkey (shades, puffer jacket) hanging by its tail from a branch at the top-right of the screen.
// Works with touch or mouse:
//   drag  - grab it, stretch the tail, let go to fling it
//   tap   - it gets bumped and grins
//   hold / right-click - Quit menu
// Its eyes follow the cursor.
// When you click a tab in Chrome or Edge, the monkey holds your click, swings over,
// grabs the top of the current page and pulls it down off the screen, revealing the
// tab you picked behind it. Then it swings back to its branch.
// (Can be turned off from the right-click menu.)
public class Monkey : Form
{
    [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; public PT(int x, int y) { this.x = x; this.y = y; } }
    [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; public SZ(int w, int h) { cx = w; cy = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte op, flags, alpha, format; }

    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT dst, ref SZ size,
        IntPtr hdcSrc, ref PT src, int key, ref BLEND blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(PT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(PT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [StructLayout(LayoutKind.Sequential)] struct NativeMessage { public IntPtr hwnd; public uint msg; public IntPtr w, l; public uint time; public PT p; }
    [DllImport("user32.dll")] static extern bool PeekMessage(out NativeMessage m, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr hwnd, ref Guid setting, int flags);
    [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("user32.dll")] static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint ms, uint wake);
    [DllImport("user32.dll")] static extern int GetMessage(out NativeMessage m, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    const int TerminalHotkey = 1;
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [StructLayout(LayoutKind.Sequential)] struct MSLL { public PT pt; public uint mouseData, flags, time; public IntPtr extra; }

    // Window geometry. The tail is wrapped around a branch at (PX, PY).
    const int W = 440, H = 470;
    const float PX = W / 2f, PY = 124;   // the branch is this far down the screen, leaving room to sit on top of it
    const int BodyReach = 100;      // how far the body extends below the hips
    const double L = 110;           // tail rest length (branch to hips)
    const double ArmLen = 42;       // arm length when it hangs from a tab by its hand

    // Physics (pixels, seconds). (x, y) is the hips, relative to the branch.
    const double Gravity = 800, Stiffness = 120, Damping = 0.6;      // lighter = slower, floatier swings
    const double SwayForce = 30, SwayPeriod = 2.4;   // matches the slower swing so the gentle sway keeps going

    static readonly Color Fur = Color.FromArgb(255, 112, 64, 32);
    static readonly Color FurDark = Color.FromArgb(255, 72, 40, 18);
    static readonly Color Skin = Color.FromArgb(255, 244, 206, 158);
    static readonly Color Ink = Color.FromArgb(255, 45, 25, 10);

    double x = 0, y = L + Gravity / Stiffness;
    double rope = L;
    double vx, vy, t;
    volatile bool grabbed;
    bool moved;
    double grabDx, grabDy, downTime;
    Point downAt;

    double grinTime, blinkTime, nextBlink = 3, frameDt = 1 / 60.0;
    float lookX, lookY, lag;

    // Little habits it does on its own while hanging at home.
    enum Act { None, Scratch, Look, Pump, Climb, Wave, Eat, Drink, EyeRest, Throw, Shades }
    Act act = Act.None, lastAct = Act.None;
    double actStart, actLen, nextAct = 5, waveCooldown;
    float actW;          // 0..1, eases each habit in and out
    int waveSide = 1;

    // Sleeping at night (10 PM - 6 AM). Touching it or a tab trip wakes it for a while.
    const int NightStart = 22, NightEnd = 6;
    const double StayAwake = 25;
    bool night;
    double nightCheckAt, wakeUntil, breathPhase, nextZ;
    float sleepW;   // 0 awake .. 1 fast asleep
    class Zz { public PointF p; public double age; public float drift; public bool note; public string ch = "Z"; public Color col; }

    // Dancing when music plays: loudness comes from the speakers' level meter (local, no internet).
    volatile float audioLevel;
    double soundSecs, lastBeat, beatInterval = 0.5, beatPhase;
    float slowLevel, danceW;
    bool musicOn;
    int beatCount;
    static readonly Color[] NoteColors = {
        Color.FromArgb(255, 255, 120, 170), Color.FromArgb(255, 90, 210, 240),
        Color.FromArgb(255, 255, 210, 70), Color.FromArgb(255, 160, 120, 255) };
    readonly List<Zz> zzz = new List<Zz>();

    // Talking: a speech bubble next to its head (no sound).
    string sayText;
    double sayStart, sayLen, lastSay = -10, nextChat = 18;
    PointF headWin;          // where its head is in the window, for placing the bubble
    bool wasAsleep;

    static readonly string[] IdleLines = {
        "Just hanging around!", "Ooh ooh ah ah!", "Is it snack time yet?", "Click a tab, I'll fetch it!",
        "Hang in there!", "You're doing great!", "Sit up straight! (I'm upside down, but you know.)", "Nice tabs you've got.",
        "I could swing all day.", "Anyone seen my bananas?", "Looking sharp today!", "Stretch break? I'm already stretching.",
        "These shades? Very cool.", "Press Ctrl+M if you need me to build something!", "Time flies when you're swinging." };

    string Pick(params string[] options) { return options[rnd.Next(options.Length)]; }

    // Shows a bubble. 'urgent' lines (reactions to you) skip the polite pause between lines.
    void Say(string text, bool urgent = false, double minLen = 0, Sky icon = Sky.None)
    {
        double now = clock.Elapsed.TotalSeconds;
        if (!urgent && now - lastSay < 6) return;
        sayText = text;
        sayIcon = icon;
        sayStart = now;
        sayLen = Math.Max(minLen, 1.8 + text.Length * 0.06);
        lastSay = now;
    }

    // ---- Hourly chime ----
    // On the hour it waves and tells you the time. Not while it's asleep, put away, behind a film, or when
    // nobody's at the PC; if it's busy (off at a tab, say) it tells you when it's back, up to 3 minutes late.
    bool chimeOn = true;
    int chimedHour = -1;   // the hour already told (or skipped)
    double nextChimeCheck;

    void UpdateChime(double now)
    {
        if (now < nextChimeCheck) return;
        nextChimeCheck = now + 1;
        DateTime t = DateTime.Now;
#if CHIMEDEMO
        if (now > 3) t = DateTime.Today.AddHours(16);   // test build: pretend it just turned 4 o'clock
#endif
        if (chimedHour < 0) { chimedHour = t.Hour; return; }   // just started: wait for the next hour
        if (t.Hour == chimedHour) return;
        if (t.Minute >= 3 || !chimeOn || Resting || night || userAway) { chimedHour = t.Hour; return; }
        if (mode != Mode.Home || grabbed || perch == Perch.Up || perch == Perch.Over || perch == Perch.Down) return;   // busy: in a moment
        chimedHour = t.Hour;

        int h12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        string extra;
        switch (t.Hour)
        {
            case 6: extra = " Good morning!"; break;
            case 9: extra = " Let's get going!"; break;
            case 12: extra = " Noon already!"; break;
            case 13: extra = " Lunch time!"; break;
            case 16: extra = " Chai time!"; break;
            case 21: extra = " Winding down soon?"; break;
            default: extra = Pick("", "", " Time flies!", " Stretch break?", " Ooh ooh!"); break;
        }
        bool sittingNow = perch == Perch.Sit;
        if (act == Act.None && sleepW < 0.5f) { waveSide = sittingNow ? 1 : -1; StartAct(Act.Wave, 2.2, now); waveCooldown = now + 12; }
        grinTime = 2.2;
        Say("It's " + h12 + " o'clock!" + extra, true, 4);
    }

    // Eye-rest reminder: shades up, three big slow blinks.
    bool EyeRest { get { return ShownAct == Act.EyeRest && actW > 0.5f && !grabbed; } }
    bool RestBlinkNow
    {
        get
        {
            if (!EyeRest) return false;
            double ap = clock.Elapsed.TotalSeconds - actStart;
            foreach (double b in RestBlinks) if (ap > b && ap < b + 0.28) return true;
            return false;
        }
    }

    bool Talking { get { return sayText != null && clock.Elapsed.TotalSeconds - sayStart < sayLen * 0.6; } }

    string Greeting()
    {
        int h = DateTime.Now.Hour;
        if (h < 12) return Pick("Good morning!", "Morning! Coffee first?");
        if (h < 17) return Pick("Good afternoon!", "Lunch was good? Mine was a banana.");
        return Pick("Good evening!", "Long day? You've got this.");
    }

    void UpdateChat(double now, double dt)
    {
        if (sayText != null && now - sayStart > sayLen) sayText = null;
        bool calm = mode == Mode.Home && !grabbed && sleepW < 0.05f && danceW < 0.05f && act == Act.None;
        if (!calm) return;
        if ((nextChat -= dt) <= 0)
        {
            Say(rnd.NextDouble() < 0.2 ? Greeting() : IdleLines[rnd.Next(IdleLines.Length)]);
            nextChat = 25 + rnd.NextDouble() * 35;
        }
    }

    void DrawBubble(Graphics g)
    {
        if (sayText == null) return;
        double age = clock.Elapsed.TotalSeconds - sayStart;
        float fade = (float)Math.Max(0, Math.Min(1, Math.Min(age / 0.15, (sayLen - age) / 0.3)));
        if (fade <= 0.02f) return;
        float pop = (float)Math.Min(1, 0.7 + age / 0.15 * 0.3);

        var oldHint = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using (var font = new Font("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            SizeF sz = g.MeasureString(sayText, font, 150, fmt);
            float iconW = sayIcon == Sky.None ? 0 : 34;   // a little weather picture at the left
            float w = sz.Width + 20 + iconW, h = Math.Max(sz.Height, iconW) + 12;
            // Beside the head: on the left if there is room (it hangs near the right edge), else on the right.
            bool left = headWin.X - 34 - w > 4;
            float bx = left ? headWin.X - 34 - w : headWin.X + 34;
            float by = headWin.Y - 50 - h / 2;
            bx = Math.Max(4, Math.Min(W - w - 4, bx));
            by = Math.Max(4, Math.Min(H - h - 4, by));
            var tip = new PointF(headWin.X + (left ? -18 : 18), headWin.Y - 20);

            var st = g.Save();
            g.TranslateTransform(tip.X, tip.Y);
            g.ScaleTransform(pop, pop);
            g.TranslateTransform(-tip.X, -tip.Y);
            using (var path = new GraphicsPath())
            {
                const float r = 10;
                path.AddArc(bx, by, 2 * r, 2 * r, 180, 90);
                path.AddArc(bx + w - 2 * r, by, 2 * r, 2 * r, 270, 90);
                path.AddArc(bx + w - 2 * r, by + h - 2 * r, 2 * r, 2 * r, 0, 90);
                path.AddArc(bx, by + h - 2 * r, 2 * r, 2 * r, 90, 90);
                path.CloseFigure();
                // The little tail pointing at its mouth.
                float baseX = Math.Max(bx + 14, Math.Min(bx + w - 14, left ? bx + w - 18 : bx + 18));
                float baseY = by + h - 2;
                path.AddPolygon(new[] { new PointF(baseX - 7, baseY), new PointF(baseX + 7, baseY), tip });
                using (var shadow = new SolidBrush(Color.FromArgb((int)(40 * fade), 0, 0, 0)))
                {
                    g.TranslateTransform(2, 3);
                    g.FillPath(shadow, path);
                    g.TranslateTransform(-2, -3);
                }
                using (var fill = new SolidBrush(Color.FromArgb((int)(250 * fade), 255, 255, 255))) g.FillPath(fill, path);
                using (var o = new Pen(Color.FromArgb((int)(255 * fade), Outline), 2f) { LineJoin = LineJoin.Round }) g.DrawPath(o, path);
                using (var ink = new SolidBrush(Color.FromArgb((int)(255 * fade), Ink)))
                    g.DrawString(sayText, font, ink, new RectangleF(bx + 10 + iconW, by + 6, w - 20 - iconW, h - 12), fmt);
                if (iconW > 0) DrawSky(g, sayIcon, bx + 8 + iconW / 2, by + h / 2, (int)(255 * fade));
            }
            g.Restore(st);
        }
        g.TextRenderingHint = oldHint;
    }

    // ---- Weather ----
    // Every half hour it looks up the forecast where you are: your rough location from your internet address
    // (or "weatherAt=lat,lon" in settings.txt), then the forecast from Open-Meteo (free, no account or key).
    // It warns you before rain, and now and then says what today (or, in the evening, tomorrow) looks like.
    enum Sky { None, Sun, Moon, PartCloud, Cloud, Fog, Rain, Snow, Storm }
    // Its face follows the weather outside: sunny smile, sweaty and panting in the heat, glum in the rain,
    // scared of thunder, shivering with chattering teeth in the cold, unimpressed by grey skies.
    enum Mood { Normal, Happy, Hot, Gloomy, Scared, Cold, Meh }
    Mood mood = Mood.Normal;
    Sky sayIcon = Sky.None;
    bool weatherOn = true;
    string weatherAt = "";           // "lat,lon" to skip the location lookup ("" = find it)
    volatile Forecast weather;      // the latest forecast, filled in by a background thread
    volatile bool weatherBusy, weatherFailed;
    bool weatherAsked, weatherToldOnce;
    double nextWeatherFetch = 2, nextWeatherTalk, weatherAskedAt;
    DateTime rainWarnedFor = DateTime.MinValue, fetchedAt;
    string savedPlace = "";
    DateTime placeSavedFor;

    sealed class Forecast
    {
        public string Place = "";
        public bool Fahrenheit;
        public TimeSpan Offset;      // the place's clock vs UTC
        public double Temp; public int Code; public bool IsDay;
        public DateTime[] Hours = new DateTime[0]; public int[] HourCode = new int[0], HourRain = new int[0];
        public double[] DayMax = new double[0], DayMin = new double[0];
        public int[] DayCode = new int[0], DayRain = new int[0];

        public DateTime Now { get { return DateTime.UtcNow + Offset; } }
        public string Deg(double v) { return Math.Round(v) + "°"; }
        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append((Place.Length > 0 ? Place + ", " : "") + "now " + Deg(Temp) + (Fahrenheit ? "F " : "C ") + Words(Code));
            for (int d = 0; d < DayCode.Length && d < 2; d++)
                sb.Append("; " + (d == 0 ? "today" : "tomorrow") + " " + Words(DayCode[d]) + ", " + Deg(DayMin[d]) + " to " + Deg(DayMax[d])
                    + ", " + DayRain[d] + "% chance of rain");
            return sb.ToString();
        }
    }

    static bool Wet(int code) { return code >= 51; }   // drizzle, rain, snow, showers, storms
    static Sky SkyOf(int code, bool day)
    {
        if (code <= 1) return day ? Sky.Sun : Sky.Moon;
        if (code == 2) return day ? Sky.PartCloud : Sky.Cloud;
        if (code == 45 || code == 48) return Sky.Fog;
        if ((code >= 71 && code <= 77) || code == 85 || code == 86) return Sky.Snow;
        if (code >= 95) return Sky.Storm;
        return Wet(code) ? Sky.Rain : Sky.Cloud;
    }
    static string Words(int code)
    {
        if (code <= 1) return "clear skies";
        if (code == 2) return "a few clouds";
        if (code == 45 || code == 48) return "foggy";
        if (code >= 51 && code <= 57) return "drizzle";
        if (code == 65 || code == 82) return "heavy rain";
        if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "snow";
        if (code >= 95) return "thunderstorms";
        return Wet(code) ? "rain" : "cloudy";
    }
    static string Clock(DateTime t)
    {
        bool h24 = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H");
        return t.ToString(h24 ? "HH:mm" : "h tt", System.Globalization.CultureInfo.InvariantCulture);
    }

    // The first rainy hour from now to 'hours' ahead (index into Hours), or -1.
    static int RainAhead(Forecast f, double hours, int minChance)
    {
        DateTime now = f.Now;
        for (int i = 0; i < f.Hours.Length; i++)
        {
            if (f.Hours[i] <= now.AddHours(-1)) continue;
            if (f.Hours[i] > now.AddHours(hours)) break;
            if (Wet(f.HourCode[i]) && f.HourRain[i] >= minChance) return i;
        }
        return -1;
    }

    string RainLine(Forecast f, int i, out Sky icon)
    {
        int code = f.HourCode[i];
        icon = SkyOf(code, true);
        bool already = f.Hours[i] <= f.Now && Wet(f.Code);
        string at = Clock(f.Hours[i]);
        if (code >= 95) return already ? "Thunderstorm out there! Stay in." : "Thunderstorm around " + at + ". Stay inside!";
        if (icon == Sky.Snow) return already ? "It's snowing! Bundle up." : "Snow around " + at + ". Bundle up!";
        return already ? Pick("It's raining out there. Stay dry!", "Rainy right now. Umbrella if you go out!")
                       : Pick("Rain around " + at + ". Take an umbrella!", "My tail says rain around " + at + ". Umbrella!");
    }

    // What the monkey predicts: rain coming soon, else today (or tomorrow, in the evening).
    string Predict(Forecast f, out Sky icon)
    {
        int r = RainAhead(f, 6, 50);
        if (r >= 0) return RainLine(f, r, out icon);
        double hot = f.Fahrenheit ? 93 : 34, cold = f.Fahrenheit ? 46 : 8;
        if (f.Now.Hour >= 18 && f.DayCode.Length > 1)
        {
            icon = SkyOf(f.DayCode[1], true);
            string w = Words(f.DayCode[1]);
            if (Wet(f.DayCode[1]) && f.DayRain[1] >= 50) return "Tomorrow looks like " + w + ", up to " + f.Deg(f.DayMax[1]) + ". Plan for it!";
            return Pick("Tomorrow: " + w + ", " + f.Deg(f.DayMin[1]) + " to " + f.Deg(f.DayMax[1]) + ".",
                        "My banana senses say tomorrow: " + w + ", up to " + f.Deg(f.DayMax[1]) + ".");
        }
        icon = SkyOf(f.Code, f.IsDay);
        if (f.DayCode.Length > 0 && f.DayMax[0] >= hot) return "Scorcher today, up to " + f.Deg(f.DayMax[0]) + "! Drink extra water.";
        if (f.Temp <= cold) return "Brr, only " + f.Deg(f.Temp) + "! Good thing I've got my puffer.";
        string now = f.Deg(f.Temp) + " and " + Words(f.Code);
        if (f.DayCode.Length == 0) return "It's " + now + " right now.";
        if (f.DayRain[0] < 30 && rnd.Next(3) == 0) return "It's " + now + ". No rain coming!";
        return Pick("It's " + now + ". High of " + f.Deg(f.DayMax[0]) + " today.",
                    "Weather check: " + now + ", up to " + f.Deg(f.DayMax[0]) + ".");
    }

    void SayForecast(Forecast f, bool urgent)
    {
        Sky icon;
        Say(Predict(f, out icon), urgent, 4.5, icon);
        if (act == Act.None && mode == Mode.Home && !grabbed && (perch == Perch.None || perch == Perch.Sit))
            StartAct(Act.Look, 3.0, clock.Elapsed.TotalSeconds);   // checks the sky
    }

    // "What's the weather?" from the menu.
    void AskWeather()
    {
        double now = clock.Elapsed.TotalSeconds;
        wakeUntil = Math.Max(wakeUntil, now + StayAwake);
        Forecast f = weather;
        if (f != null && (DateTime.UtcNow - fetchedAt).TotalHours < 3)
        {
            SayForecast(f, true);   // answer right away from what it knows...
            if ((DateTime.UtcNow - fetchedAt).TotalMinutes > 20) FetchWeather();   // ...and freshen up quietly
            return;
        }
        weatherAsked = true; weatherAskedAt = now;
        weatherFailed = false;
        Say(Pick("Let me check the sky...", "Sniffing the air...", "Hmm, one sec..."), true, 10);
        FetchWeather();
    }

    Mood WeatherMood(Forecast f)
    {
        if (f == null || !weatherOn || (DateTime.UtcNow - fetchedAt).TotalHours > 3) return Mood.Normal;   // old news
        double hot = f.Fahrenheit ? 93 : 34, cold = f.Fahrenheit ? 46 : 8;
        Sky sky = SkyOf(f.Code, f.IsDay);
        if (sky == Sky.Storm) return Mood.Scared;
        if (sky == Sky.Snow || f.Temp <= cold) return Mood.Cold;
        if (Wet(f.Code) || RainAhead(f, 1, 60) >= 0) return Mood.Gloomy;   // raining, or about to
        if (f.Temp >= hot) return Mood.Hot;
        if (sky == Sky.Sun || sky == Sky.PartCloud) return Mood.Happy;
        if (sky == Sky.Cloud || sky == Sky.Fog) return f.IsDay ? Mood.Meh : Mood.Normal;
        return Mood.Normal;
    }

    // The mood its face shows right now (being grabbed, grinning or asleep comes first).
    Mood FaceMood { get { return grabbed || grinTime > 0 || sleepW >= 0.5f ? Mood.Normal : mood; } }

    void UpdateWeather(double now)
    {
        Forecast f = weather;
        mood = WeatherMood(f);
        if (f != null && !weatherBusy && fetchedAt != placeSavedFor) { placeSavedFor = fetchedAt; if (PlaceSetting() != savedPlace) SaveSettings(); }
#if WEATHERDEMO
        mood = (Mood)(1 + (int)(now / 5) % 6);   // test build: every face in turn, 5 seconds each
#endif
        if (weatherAsked && !weatherBusy)
        {
            weatherAsked = false;
            if (f != null && !weatherFailed) SayForecast(f, true);
            else Say("I can't see the sky right now. Is the internet on?", true);
        }
        if (!weatherOn) return;
        if (now >= nextWeatherFetch && !Resting && !weatherBusy)
        {
            FetchWeather();
            nextWeatherFetch = now + 30 * 60;
        }
        if (weatherFailed && !weatherBusy && !weatherAsked) { weatherFailed = false; nextWeatherFetch = Math.Min(nextWeatherFetch, now + 5 * 60); }
        if (f == null) return;

        bool calm = mode == Mode.Home && !grabbed && sleepW < 0.05f && danceW < 0.05f && act == Act.None && !Resting
            && (perch == Perch.None || perch == Perch.Sit) && now - lastSay > 6;
        if (!calm) return;

        // Rain on its way in the next two hours: warn once per rainy spell.
        int r = RainAhead(f, 2, 60);
        if (r >= 0 && f.Hours[r] > f.Now && !Wet(f.Code) && Math.Abs((f.Hours[r] - rainWarnedFor).TotalHours) > 3)
        {
            rainWarnedFor = f.Hours[r];
            Sky icon;
            Say(RainLine(f, r, out icon), false, 5, icon);
            StartAct(Act.Look, 3.0, now);
            nextWeatherTalk = now + 40 * 60;
            return;
        }
        // Otherwise once soon after it first knows, then every hour or so.
        if (!weatherToldOnce || now >= nextWeatherTalk)
        {
            weatherToldOnce = true;
            SayForecast(f, false);
            nextWeatherTalk = now + 50 * 60 + rnd.NextDouble() * 40 * 60;
#if WEATHERDEMO
            nextWeatherTalk = now + 12;   // test build: a forecast every few seconds
#endif
        }
    }

    void FetchWeather()
    {
        if (weatherBusy) return;
        weatherBusy = true;
        bool fahrenheit = !System.Globalization.RegionInfo.CurrentRegion.IsMetric;
        string at = weatherAt;
        var th = new Thread(() =>
        {
            try
            {
                Forecast f = LoadForecast(at, fahrenheit);
                fetchedAt = DateTime.UtcNow;
                weather = f;
                weatherFailed = false;
            }
            catch { weatherFailed = true; }
            finally { weatherBusy = false; }
        });
        th.IsBackground = true;
        th.Start();
    }

    static string Download(string url)
    {
        System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;   // TLS 1.2
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.Timeout = req.ReadWriteTimeout = 8000;
        req.UserAgent = "MonkeyPlay";
        using (var resp = req.GetResponse())
        using (var rd = new System.IO.StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
            return rd.ReadToEnd();
    }

    static double Num(object o)
    {
        if (o == null) return double.NaN;
        return Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture);
    }

    static Dictionary<string, object> Json(string text)
    {
        return (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text);
    }

    // Where you are, roughly, from your internet address (looked up again every few hours: laptops move).
    static double placeLat = double.NaN, placeLon;
    static string placeName = "";
    static DateTime placeFoundAt;

    static void Locate(string at)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] parts = (at ?? "").Split(',');
        double la, lo;
        if (parts.Length == 2 && double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, inv, out la)
            && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, inv, out lo))
        { placeLat = la; placeLon = lo; placeName = ""; return; }
        if (!double.IsNaN(placeLat))
        {
            // Known (maybe from last time): use it now, and check it again in the background now and then.
            if ((DateTime.UtcNow - placeFoundAt).TotalHours >= 3) { placeFoundAt = DateTime.UtcNow; new Thread(() => FindPlace()) { IsBackground = true }.Start(); }
            return;
        }
        if (!FindPlace()) throw new Exception("couldn't find where you are");
    }

    // Asks both location services at once and takes whichever answers first.
    static bool FindPlace()
    {
        var done = new ManualResetEvent(false);
        object gate = new object();
        bool got = false;
        string[] urls = { "https://get.geojs.io/v1/ip/geo.json", "https://ipapi.co/json/" };
        int left = urls.Length;
        foreach (string url in urls)
        {
            string u = url;
            new Thread(() =>
            {
                try
                {
                    var d = Json(Download(u));
                    double a = Num(d["latitude"]), b = Num(d["longitude"]);
                    object city;
                    string name = d.TryGetValue("city", out city) && city != null ? city.ToString() : "";
                    if (!double.IsNaN(a) && !double.IsNaN(b))
                        lock (gate)
                            if (!got) { got = true; placeLat = a; placeLon = b; placeName = name; placeFoundAt = DateTime.UtcNow; }
                }
                catch { }
                finally { if (Interlocked.Decrement(ref left) == 0 || got) done.Set(); }
            }) { IsBackground = true }.Start();
        }
        done.WaitOne(9000);
        lock (gate) return got;
    }

    // "lat,lon,city" for settings.txt, so the next start doesn't have to look it up.
    static string PlaceSetting()
    {
        if (double.IsNaN(placeLat)) return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return placeLat.ToString("0.###", inv) + "," + placeLon.ToString("0.###", inv) + "," + placeName.Replace(",", " ");
    }

    static void LoadPlace(string val)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] parts = val.Split(new[] { ',' }, 3);
        double la, lo;
        if (parts.Length == 3 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out la)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out lo))
        { placeLat = la; placeLon = lo; placeName = parts[2]; placeFoundAt = DateTime.MinValue; }   // checked again soon
    }

    static Forecast LoadForecast(string at, bool fahrenheit)
    {
        Locate(at);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string url = "https://api.open-meteo.com/v1/forecast?latitude=" + placeLat.ToString("0.###", inv) + "&longitude=" + placeLon.ToString("0.###", inv)
            + "&current=temperature_2m,weather_code,is_day&hourly=weather_code,precipitation_probability"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
            + "&timezone=auto&forecast_days=2" + (fahrenheit ? "&temperature_unit=fahrenheit" : "");
        var d = Json(Download(url));
        var f = new Forecast { Place = placeName, Fahrenheit = fahrenheit };
        f.Offset = TimeSpan.FromSeconds(Num(d["utc_offset_seconds"]));
        var cur = (Dictionary<string, object>)d["current"];
        f.Temp = Num(cur["temperature_2m"]);
        f.Code = (int)Num(cur["weather_code"]);
        f.IsDay = Num(cur["is_day"]) == 1;

        var hr = (Dictionary<string, object>)d["hourly"];
        object[] ht = (object[])hr["time"], hc = (object[])hr["weather_code"], hp = (object[])hr["precipitation_probability"];
        f.Hours = new DateTime[ht.Length]; f.HourCode = new int[ht.Length]; f.HourRain = new int[ht.Length];
        for (int i = 0; i < ht.Length; i++)
        {
            f.Hours[i] = DateTime.ParseExact((string)ht[i], "yyyy-MM-dd'T'HH:mm", inv);
            double c = Num(hc[i]), p = Num(hp[i]);
            f.HourCode[i] = double.IsNaN(c) ? 0 : (int)c;
            f.HourRain[i] = double.IsNaN(p) ? 0 : (int)p;
        }

        var dy = (Dictionary<string, object>)d["daily"];
        object[] dc = (object[])dy["weather_code"], dmax = (object[])dy["temperature_2m_max"],
                 dmin = (object[])dy["temperature_2m_min"], dp = (object[])dy["precipitation_probability_max"];
        int n = dc.Length;
        f.DayCode = new int[n]; f.DayMax = new double[n]; f.DayMin = new double[n]; f.DayRain = new int[n];
        for (int i = 0; i < n; i++)
        {
            double c = Num(dc[i]), p = Num(dp[i]);
            f.DayCode[i] = double.IsNaN(c) ? 0 : (int)c;
            f.DayMax[i] = Num(dmax[i]); f.DayMin[i] = Num(dmin[i]);
            f.DayRain[i] = double.IsNaN(p) ? 0 : (int)p;
        }
        return f;
    }

    // A small weather picture for the speech bubble (drawn on its white background).
    static void DrawSky(Graphics g, Sky sky, float cx, float cy, int alpha)
    {
        Func<int, int, int, Color> C = (r, gr, b) => Color.FromArgb(alpha, r, gr, b);
        Action<float, float, float, Color> cloud = (x, y, s, col) =>
        {
            using (var path = new GraphicsPath { FillMode = FillMode.Winding })
            {
                path.AddEllipse(x - 11 * s, y - 2 * s, 10 * s, 9 * s);
                path.AddEllipse(x - 6 * s, y - 8 * s, 12 * s, 12 * s);
                path.AddEllipse(x + 1 * s, y - 3 * s, 10 * s, 10 * s);
                path.AddRectangle(new RectangleF(x - 6 * s, y + 1 * s, 12 * s, 6 * s));
                using (var fill = new SolidBrush(col)) g.FillPath(fill, path);
            }
        };
        Action<float, float, float> sun = (x, y, r) =>
        {
            using (var ray = new Pen(C(250, 180, 30), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(ray, x + (float)Math.Cos(a) * (r + 2), y + (float)Math.Sin(a) * (r + 2),
                                    x + (float)Math.Cos(a) * (r + 5), y + (float)Math.Sin(a) * (r + 5));
                }
            using (var b = new SolidBrush(C(255, 200, 40))) g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
        };
        switch (sky)
        {
            case Sky.Sun: sun(cx, cy, 7); break;
            case Sky.Moon:
                using (var b = new SolidBrush(C(245, 205, 80))) g.FillEllipse(b, cx - 8, cy - 8, 16, 16);
                using (var b = new SolidBrush(C(255, 255, 255))) g.FillEllipse(b, cx - 3, cy - 11, 14, 14);
                break;
            case Sky.PartCloud:
                sun(cx - 4, cy - 4, 6);
                cloud(cx + 3, cy + 3, 0.9f, C(200, 208, 220));
                break;
            case Sky.Cloud: cloud(cx, cy, 1.1f, C(185, 192, 205)); break;
            case Sky.Fog:
                using (var p = new Pen(C(170, 178, 190), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = -1; i <= 1; i++) g.DrawLine(p, cx - 10 + (i == 0 ? 3 : 0), cy + i * 6, cx + 10 - (i == 0 ? 0 : 3), cy + i * 6);
                break;
            case Sky.Rain:
            case Sky.Snow:
            case Sky.Storm:
                cloud(cx, cy - 4, 1.0f, sky == Sky.Storm ? C(120, 128, 145) : C(170, 178, 192));
                if (sky == Sky.Rain)
                    using (var p = new Pen(C(60, 140, 230), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        for (int i = -1; i <= 1; i++) g.DrawLine(p, cx + i * 6 + 1, cy + 6, cx + i * 6 - 1, cy + 11);
                else if (sky == Sky.Snow)
                    using (var b = new SolidBrush(C(110, 170, 230)))
                        for (int i = -1; i <= 1; i++) g.FillEllipse(b, cx + i * 6 - 1.8f, cy + 7 + (i == 0 ? 2 : 0), 3.6f, 3.6f);
                else
                    using (var b = new SolidBrush(C(255, 200, 30)))
                        g.FillPolygon(b, new[] { new PointF(cx + 1, cy + 3), new PointF(cx - 4, cy + 10), new PointF(cx, cy + 10),
                                                 new PointF(cx - 2, cy + 15), new PointF(cx + 5, cy + 7), new PointF(cx + 1, cy + 7) });
                break;
        }
    }

    // Health reminders for you: a water break and an eye-blink break, each on its own timer
    // (minutes, picked from the right-click menu; 20 minutes for eyes follows the 20-20-20 rule).
    const double DrinkLen = 6.0, EyeRestLen = 3.4;
    // Drinking, start to finish (seconds into the habit): reach for the bottle in its holder, pull it out, bring
    // it round, unscrew the cap, three gulps, "Ahh!", cap back on, bottle back in the holder.
    const double DkReach = 0.35, DkOut = 0.6, DkFront = 1.05, DkCapOff = 1.65, DkLift = 2.05,
                 DkLower = 3.95, DkDown = 4.35, DkCapOn = 4.9, DkBack = 5.35, DkIn = 5.65;
    static readonly double[] GulpTimes = { 2.35, 2.95, 3.55 };
    static readonly PointF HolsterNeck = new PointF(15.5f, 22), HolsterOut = new PointF(18, -2), DrinkHold = new PointF(26, 130);
    static readonly PointF AroundHead = new PointF(56, 58);   // carried round the side of its head, not across its face
    static PointF Curve(PointF a, PointF via, PointF b, float k)
    {
        float u = 1 - k;
        return new PointF(u * u * a.X + 2 * u * k * via.X + k * k * b.X, u * u * a.Y + 2 * u * k * via.Y + k * k * b.Y);
    }
    static readonly RectangleF HolsterRect = new RectangleF(8, 27, 15, 20);
    const float HolsterScale = 0.75f, NeckGrip = 22;
    bool ahhSaid;
    static float Phase(double ap, double a, double b) { return Smooth((ap - a) / (b - a)); }
    static int GulpsDone(double ap) { int n = 0; foreach (double gt in GulpTimes) if (ap > gt + 0.15) n++; return n; }
    static float GulpBob(double ap)
    {
        foreach (double gt in GulpTimes) { double d = ap - gt; if (d > 0 && d < 0.35) return 2.5f * (float)Math.Sin(Math.PI * d / 0.35); }
        return 0;
    }
    // How far the bottle is raised to the lips: 0 held in front of its chin .. 1 drinking.
    static float DrinkLift(double ap) { return ap < DkLower ? Phase(ap, DkCapOff, DkLift) : 1 - Phase(ap, DkLower, DkDown); }
    static readonly int[] WaterChoices = { 15, 30, 45, 60 }, EyesChoices = { 10, 20, 30, 60 };
    double waterEvery = 30 * 60, eyesEvery = 20 * 60;
    static readonly double[] RestBlinks = { 0.8, 1.5, 2.2 };
    double nextWater, nextEyes;
    volatile bool healthReminders = true;

    // Menu choices are kept in %APPDATA%\MonkeyPlay\settings.txt as key=value lines.
    static readonly string SettingsFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"MonkeyPlay\settings.txt");

    void LoadSettings()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsFile)) return;
            foreach (string line in System.IO.File.ReadAllLines(SettingsFile))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                bool b; int n;
                if (key == "pullTabs" && bool.TryParse(val, out b)) pullTabs = b;
                else if (key == "healthReminders" && bool.TryParse(val, out b)) healthReminders = b;
                else if (key == "hideInFullscreen" && bool.TryParse(val, out b)) hideInFullscreen = b;
                else if (key == "hiddenByUser" && bool.TryParse(val, out b)) hiddenByUser = b;
                else if (key == "blastTabs" && bool.TryParse(val, out b)) blastTabs = b;
                else if (key == "screen") homeScreen = val;
                else if (key == "chatModel" && (val == "fast" || val == "balanced" || val == "smart")) chatModel = val;
                else if (key == "shades" && bool.TryParse(val, out b)) shadesOn = b;
                else if (key == "waterMinutes" && int.TryParse(val, out n) && n > 0) waterEvery = n * 60;
                else if (key == "eyesMinutes" && int.TryParse(val, out n) && n > 0) eyesEvery = n * 60;
                else if (key == "weather" && bool.TryParse(val, out b)) weatherOn = b;
                else if (key == "chime" && bool.TryParse(val, out b)) chimeOn = b;
                else if (key == "weatherAt") weatherAt = val;
                else if (key == "weatherPlace") { LoadPlace(val); savedPlace = val; }
            }
        }
        catch { }   // a broken settings file just means defaults
    }

    void SaveSettings()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
            System.IO.File.WriteAllLines(SettingsFile, new[] {
                "pullTabs=" + pullTabs,
                "healthReminders=" + healthReminders,
                "hideInFullscreen=" + hideInFullscreen,
                "hiddenByUser=" + hiddenByUser,
                "blastTabs=" + blastTabs,
                "screen=" + homeScreen,
                "chatModel=" + chatModel,
                "shades=" + shadesOn,
                "waterMinutes=" + (int)(waterEvery / 60),
                "eyesMinutes=" + (int)(eyesEvery / 60),
                "weather=" + weatherOn,
                "chime=" + chimeOn,
                "weatherAt=" + weatherAt,
                "weatherPlace=" + (savedPlace = PlaceSetting()),
            });
        }
        catch { }
    }

    // A "Water every / Eye break every" submenu; picking one restarts that timer.
    ToolStripMenuItem IntervalMenu(string title, int[] choices, Func<double> get, Action<double> set)
    {
        var parent = new ToolStripMenuItem(title);
        foreach (int min in choices)
        {
            var item = new ToolStripMenuItem(min + " minutes") { Checked = get() == min * 60 };
            int m = min;
            item.Click += (s, e) =>
            {
                set(m * 60);
                foreach (ToolStripMenuItem other in parent.DropDownItems) other.Checked = other == item;
                SaveSettings();
            };
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    // Banana snack: bites happen at these times (seconds into the habit), the peel is tossed at the end.
    // Before that, the free hand peels it: one strip at a time, each in its own slot (reach, pinch, pull down).
    // It starts by fetching the banana from its jacket pocket: reach back, pull it out, swing it round to the chin.
    static readonly double[] BiteTimes = { 2.95, 3.85, 4.75 };
    const double TossTime = 5.85, EatLen = 6.55, PeelStart = 0.95, PeelSlot = 0.55;
    const double GrabTime = 0.4, PullOutTime = 0.15, CarryTime = 0.35;
    static readonly PointF PocketGrip = new PointF(13, -6);    // the banana's base, just peeking out of the pocket
    static readonly PointF PocketOut = new PointF(17, -26);    // pulled clear of the pocket
    static readonly RectangleF PocketRect = new RectangleF(3.5f, -2, 20, 20);
    const float PocketScale = 0.75f;                            // it looks smaller tucked away

    static float Smooth(double v) { v = Math.Max(0, Math.Min(1, v)); return (float)(v * v * (3 - 2 * v)); }
    static float CarryK(double ap) { return Smooth((ap - GrabTime - PullOutTime) / CarryTime); }
    static bool BananaInPocket(double ap) { return ap < GrabTime + PullOutTime; }

    // Which way the held banana points: tip down into the pocket, then swung round to point at the mouth.
    static float BananaAngle(double ap, PointF hand, PointF mouth)
    {
        float aim = AngleTo(hand, mouth);
        return BananaInPocket(ap) ? 180 : 180 + (aim - 180) * CarryK(ap);
    }
    // Which way to turn something held at 'from' so it points at 'to' (degrees from upright).
    static float AngleTo(PointF from, PointF to) { return (float)(Math.Atan2(to.X - from.X, -(to.Y - from.Y)) * 180 / Math.PI); }

    // A point placed relative to the face (designed for hanging, head below the hips). Sitting, the body is
    // turned over but the head stays upright, so it's mirrored top-to-bottom about the head's centre.
    const float HeadY = 70;
    PointF Face(PointF p) { return perch == Perch.Sit ? new PointF(p.X, 2 * HeadY - p.Y) : p; }

    static float BananaScale(double ap) { return BananaInPocket(ap) ? PocketScale : PocketScale + (1 - PocketScale) * CarryK(ap); }
    float peelW;          // 0 .. 1 how much the free hand is busy peeling
    float capW;           // 0 .. 1 how much the free hand is busy with the bottle cap
    PointF capHand;       // where that hand is (smoothed)
    bool gulping;         // eyes shut while it gulps
    PointF peelHand;      // where the peeling hand is (smoothed)

    // How far the peeling is, 0 (closed) .. 3 (all three strips down), and which strip is being worked on.
    static float PeelProgress(double ap, out int strip, out bool reaching)
    {
        double slot = (ap - PeelStart) / PeelSlot;
        strip = (int)Math.Max(0, Math.Min(2, Math.Floor(slot)));
        double u = slot - strip;
        reaching = slot < 3 && u < 0.35;
        if (slot <= 0) return 0;
        if (slot >= 3) return 3;
        return strip + (float)Math.Max(0, Math.Min(1, (u - 0.35) / 0.65));
    }
    bool chewing, biteOpen, peelTossed, peelFlying;
    PointF peelPos, peelVel;     // screen coordinates, pixels per second
    float peelAngle, peelSpin;
    double peelAge;
#if HABITDEMO
    int demoHabit = 4;   // start the test cycle with the banana
#endif

    // Body turns a moment after the tail does; flips when switching between tail and hand.
    double bodyAngle, bodyAngVel, flip, flipVel;
    bool prevHand;
    float faceShift;     // head turning left/right
    float mirror = 1;    // which hand it is swinging from
    int travelSteps = 1, lastStep = -1;

    // The free hand: reaches ahead for the next grip while swinging, holds the tab too when pulling.
    PointF nextGrip;          // screen point the free hand is reaching for
    float swingReach;         // 0..1 progress of the current swing
    float freeW;              // 0 dangling .. 1 reaching/holding
    PointF freeLocal;         // target in the body's own coordinates (set each frame)

    // Each trip is a set of uneven swings: different lengths, speeds, heights and pauses.
    double[] stepFrac = new double[0], stepTime = new double[0], stepHold = new double[0];
    float[] stepLift = new float[0], stepDip = new float[0];
    bool[] stepSnap = new bool[0];
    Random rnd = new Random();
    Stopwatch clock = Stopwatch.StartNew();
    double last;
    Canvas canvas;
    // Staying on top: other apps that make themselves "always on top" (video players, fullscreen apps)
    // can cover the monkey, so it puts itself back on top when you switch windows and every couple of seconds.
    WinEventProc foregroundProc;
    IntPtr foregroundHook;
    double nextTopCheck, topAgainAt = -1;

    // Hiding while a video, game or presentation fills the screen the monkey is on.
    bool hideInFullscreen = true;
    volatile bool hiddenForFullscreen;
    double nextFullCheck;

    void CheckFullscreen(double now)
    {
        if (now < nextFullCheck) return;
        nextFullCheck = now + 0.5;
        bool full = hideInFullscreen && SomethingFullscreen();
        if (full == hiddenForFullscreen) return;
        hiddenForFullscreen = full;
        ApplyVisibility();
    }

    // Put away or brought back by holding M (or from the menu); remembered between runs.
    volatile bool hiddenByUser;
    double hideAt = -1;   // waves goodbye first, then goes
    bool Hidden { get { return hiddenForFullscreen || hiddenByUser; } }
    ToolStripMenuItem hideItem;

    // "Start with Windows": a Task Scheduler task that runs this exe the moment you sign in. A Run-key entry or a
    // Startup-folder shortcut works too, but Windows holds those back until well after the desktop is up, behind
    // every other startup app, so the monkey showed up late. Those older entries are moved over to the task.
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "MonkeyPlay", TaskName = "MonkeyPlay";
    static readonly string StartupLink = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Monkey Play.lnk");

    // Runs schtasks.exe quietly; the exit code (0 = fine), or -1 if it couldn't run.
    static int Schtasks(string args, out string output)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(15000)) return -1;
                return p.ExitCode;
            }
        }
        catch { return -1; }
    }

    static bool HasStartTask()
    {
        string xml;
        return Schtasks("/query /tn " + TaskName + " /xml", out xml) == 0
            && xml.IndexOf(System.Security.SecurityElement.Escape(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool HasOldStartEntry()
    {
        if (System.IO.File.Exists(StartupLink)) return true;
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var v = k == null ? null : k.GetValue(RunName) as string;
                return v != null && v.Trim('"').Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { return false; }
    }

    static void SetStartWithWindows(bool on)
    {
        try { System.IO.File.Delete(StartupLink); } catch { }
        try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue(RunName, false); } catch { }
        string output;
        if (!on) { Schtasks("/delete /tn " + TaskName + " /f", out output); return; }

        // Normal priority (a task's default is below normal) and no time limit (the default stops it after 3 days).
        string user = System.Security.SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName);
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
            + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
            + "<RegistrationInfo><Description>Starts Monkey Play when you sign in.</Description></RegistrationInfo>"
            + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>"
            + "<Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType>"
            + "<RunLevel>LeastPrivilege</RunLevel></Principal></Principals>"
            + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"
            + "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"
            + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority></Settings>"
            + "<Actions Context=\"Author\"><Exec><Command>" + System.Security.SecurityElement.Escape(Application.ExecutablePath) + "</Command></Exec></Actions>"
            + "</Task>";
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MonkeyPlayTask.xml");
        try
        {
            System.IO.File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            if (Schtasks("/create /tn " + TaskName + " /xml \"" + file + "\" /f", out output) == 0) return;
        }
        catch { }
        finally { try { System.IO.File.Delete(file); } catch { } }
        // No task (blocked by policy, say): the Run key still starts it, just later.
        try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\""); }
        catch { }
    }

    // On start, off the UI thread: move an older Run-key or Startup-folder entry over to the task, and tick the menu.
    void CheckStartWithWindows(ToolStripMenuItem item)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool on = HasStartTask();
            if (!on && HasOldStartEntry()) { SetStartWithWindows(true); on = true; }
            try { BeginInvoke((Action)(() => item.Checked = on)); } catch { }
        });
    }

    void ApplyVisibility()
    {
        ShowWindow(Handle, Hidden ? 0 /* SW_HIDE */ : 4 /* SW_SHOWNOACTIVATE */);
        if (Hidden && branchStay.Visible) branchStay.Hide();   // shown again with the next frame
        if (!Hidden) KeepOnTop();
        if (hideItem != null) hideItem.Text = hiddenByUser ? "Show monkey (hold M)" : "Hide monkey (hold M)";
    }

    void ToggleHidden()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (hiddenByUser || hideAt >= 0)
        {
            hiddenByUser = false; hideAt = -1;
            ApplyVisibility();
            SaveSettings();
            grinTime = 1.0;
            Say(Pick("I'm back!", "Did you miss me?", "Hello again!"), true);
        }
        else
        {
            Say(Pick("See you later!", "Bye for now!", "Off I go!"), true);
            grinTime = 0.8;
            hideAt = now + 0.9;
        }
    }

    // Holding M (no Ctrl/Alt/Win) for a moment, anywhere, shows or hides the monkey. A quick tap types
    // as usual; while M is held down its key-repeats are swallowed so it doesn't type "mmmm".
    // The hold is timed by looking at the key itself (CheckHoldM), not by the keyboard hook: Windows may
    // skip or quietly drop a hook when the PC is busy or short of memory, and then holding M did nothing.
    HookProc keyProc;
    IntPtr keyHook;
    bool mHeld;                     // seen by the hook: swallow the repeats
    bool mDown, mToggled, mSpoilt;  // seen by CheckHoldM
    int mDownAt, mHookAt, hookedAt;
    const int HoldMs = 700;

    static bool ModsDown()
    {
        return GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0 || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;
    }

    // Runs on the hook thread 25 times a second.
    void CheckHoldM()
    {
        bool down = GetAsyncKeyState(0x4D /* M */) < 0;
        if (!down)
        {
            mDown = false;
            if (mHeld && Environment.TickCount - mHookAt > 150) mHeld = false;   // let go while the hook wasn't listening
            return;
        }
        int nowMs = Environment.TickCount;
        if (!mDown) { mDown = true; mToggled = false; mSpoilt = ModsDown(); mDownAt = nowMs; return; }
        if (ModsDown()) mSpoilt = true;   // Ctrl+M and friends are something else
        if (!mToggled && !mSpoilt && nowMs - mDownAt >= HoldMs)
        {
            mToggled = true;
            try { BeginInvoke((Action)ToggleHidden); } catch { }   // the app is closing
        }
    }

    // (Re)installs the mouse and keyboard hooks, on the hook thread.
    void Hook()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
        hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, hookProc, GetModuleHandle(null), 0);
        keyHook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, keyProc, GetModuleHandle(null), 0);
        hookedAt = Environment.TickCount;
    }

    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vk, scan, flags, time; public IntPtr extra; }

    IntPtr OnKeyHook(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(KBDLLHOOKSTRUCT));
            int msg = (int)w;
            bool injected = (k.flags & 0x10) != 0;
#if KEYTEST
            injected = false;   // test build: accept simulated key presses
#endif
            if (k.vk == 0x4D /* M */ && !injected)
            {
                if (msg == 0x100 || msg == 0x104)   // key down (and its repeats)
                {
                    mHookAt = Environment.TickCount;
                    if (!mHeld) { if (!ModsDown()) mHeld = true; }
                    else return (IntPtr)1;   // swallow the repeats (the hold itself is timed in CheckHoldM)
                }
                else if (msg == 0x101 || msg == 0x105) mHeld = false;   // key up
            }
        }
        return CallNextHookEx(keyHook, code, w, l);
    }

    // Is the window in front covering the whole screen the monkey lives on? (The desktop doesn't count.)
    bool SomethingFullscreen()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        string c = cls.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
        const int WS_CAPTION = 0xC00000;
        if ((GetWindowLong(fg, -16 /* GWL_STYLE */) & WS_CAPTION) == WS_CAPTION) return false;   // a normal (maybe maximised) window
        RECT r;
        if (!GetWindowRect(fg, out r)) return false;
        Rectangle screen = Screen.FromPoint(Point.Round(home)).Bounds;
        return r.L <= screen.Left && r.T <= screen.Top && r.R >= screen.Right && r.B >= screen.Bottom;
    }

    void KeepOnTop()
    {
        if (menu.Visible) return;   // don't cover its own right-click menu
        SetWindowPos(Handle, new IntPtr(-1) /* TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 /* NOSIZE|NOMOVE|NOACTIVATE */);
    }

#if PROFILE
    double profSince, profDraw, profPush, profBranch, profBody, profCoil, profCompose, profBlit; int profFrames;
    static double profHead, profJacket, profSprite, profPocket; static int profSprites;
    static Stopwatch profClock = Stopwatch.StartNew();
#endif
    ContextMenuStrip menu = new ContextMenuStrip();

    // Where the tail is hooked, in screen coordinates. Home is the branch.
    PointF home, anchor, travelFrom, travelTo;
    double travelStart, travelTime = 0.5, stayUntil, nextTug;
    int tugsLeft;
    enum Mode { Home, ToTab, AtTab, PullDown, ToHome }
    volatile Mode mode = Mode.Home;

    // Unselected tabs of every open Chrome/Edge window (filled by the watcher thread).
    readonly object tabLock = new object();
    Dictionary<IntPtr, List<RectangleF>> browserTabs = new Dictionary<IntPtr, List<RectangleF>>();
    Dictionary<IntPtr, RectangleF> selectedTabs = new Dictionary<IntPtr, RectangleF>();   // the open tab of each window
    volatile bool browserInFront = true;

    // Chrome/Edge tabs overlap their neighbours by about 18px at each end, and draw the close (x) about
    // 28px in from the right edge. Narrow tabs only show the x on the open tab.
    const float TabOverlap = 9, WideTab = 140;

    static bool OnCloseButton(PT pt, RectangleF r)
    {
        return pt.x >= r.Right - 37 && pt.x < r.Right - 19 && Math.Abs(pt.y - (r.Top + r.Height / 2)) <= 10;
    }

    // Holding back your click on a tab until the monkey pulls it.
    HookProc hookProc;
    IntPtr hook;
    volatile bool pullTabs = true;
    bool swallowUp, hasPending;
    Point pendingPt;
    double pendingSince;

    // The old page snapshot the monkey pulls down.
    Cover cover = new Cover();
    RectangleF pendingTab;
    IntPtr pendingWindow;
    double pullStart, pullFrom, pullDist;
    const double PullTime = 0.4;
    const int ShadowH = 18;

    // Starting the app again while it's running (say, after hiding it and forgetting about hold-M)
    // signals the running monkey to show itself.
    static EventWaitHandle showSignal;

    void ComeOut()
    {
        if (hiddenByUser || hideAt >= 0) ToggleHidden();
        else { grinTime = 1.0; Say(Pick("I'm already here!", "Right here!", "Hi! Still hanging around."), true); }
    }

    [STAThread]
    static void Main()
    {
        bool created;
        using (var m = new Mutex(true, "HangingMonkey_SingleInstance", out created))
        using (showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "HangingMonkey_Show"))
        {
            if (!created) { showSignal.Set(); return; }   // already running: tell it to come out instead
            // Work in real screen pixels on every screen (mouse, tabs, windows all agree), and draw at each screen's scale.
            try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4) /* PER_MONITOR_AWARE_V2 */)) SetProcessDPIAware(); }
            catch { try { SetProcessDPIAware(); } catch { } }
            // Your own apps come first: the monkey only uses the CPU they leave over (it needs very little).
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            Application.EnableVisualStyles();
            Application.Run(new Monkey());
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x08000000 | 0x80 | 0x8; // LAYERED | NOACTIVATE | TOOLWINDOW | TOPMOST
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public Monkey()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        LoadSettings();
        PlaceOnScreen();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => BeginInvoke((Action)(() => PlaceOnScreen()));
        // Started at sign-in, the taskbar may not be there yet: settle in again when it takes its space.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            { if (e.Category == Microsoft.Win32.UserPreferenceCategory.Desktop && IsHandleCreated) BeginInvoke((Action)(() => PlaceOnScreen())); };
        nextWater = waterEvery; nextEyes = eyesEvery;
#if WEATHERDEMO
        nextWeatherFetch = 1;   // test build: check the weather right away
#endif
#if DRINKDEMO
        nextWater = 1;   // test build: water break right away
#endif
#if SITDEMO
        nextSit = 1.5;   // test build: go and sit right away
#endif
#if CHIMEDEMO
        hiddenByUser = false;   // test build: out even if it was put away (not saved)
#endif
#if SITACTTEST
        // Test build: sit right away, then do things up there one after another.
        nextSit = 1.5;
        var plan = new[] { new { at = 0.6, what = "wave" }, new { at = 4.5, what = "banana" }, new { at = 12.0, what = "drink" }, new { at = 16.5, what = "shades_off" },
                           new { at = 18.5, what = "wave" }, new { at = 21.5, what = "shades_on" } };
        foreach (var step in plan)
        {
            var tm = new System.Windows.Forms.Timer { Interval = (int)(step.at * 1000) };
            string what = step.what;
            tm.Tick += (s, e) => { tm.Stop(); DoAction(what); };
            tm.Start();
        }
#endif
#if CHATTEST
        // Test build: open the chat and ask one question by itself.
        var chatDemo = new System.Windows.Forms.Timer { Interval = 2000 };
        chatDemo.Tick += (s, e) => { chatDemo.Stop(); OpenChat(); chatAskedAt = clock.Elapsed.TotalSeconds; Ask("Hi Monkey! In one sentence, what can you help me with?", false); };
        chatDemo.Start();
#endif
#if FILETEST
        // Test build: find files by name, then ask about the file in MONKEY_TESTFILE.
        hiddenByUser = false;
        var fileDemo = new System.Windows.Forms.Timer { Interval = 2000 };
        int fileStep = 0;
        fileDemo.Tick += (s, e) =>
        {
            fileStep++;
            if (fileStep == 1) { OpenChat(); Ask("find gst", false, null); }
            else if (fileStep == 4) Ask("What is the total of the Amount column? Just the number.", false, Environment.GetEnvironmentVariable("MONKEY_TESTFILE"));
            else if (fileStep > 4) fileDemo.Stop();
        };
        fileDemo.Start();
#endif
#if BLASTDEMO
        // Test build: blast a pretend tab on the tab bar a moment after starting (never clicks anything).
        var blastDemo = new System.Windows.Forms.Timer { Interval = 2000 };
        blastDemo.Tick += (s, e) => { blastDemo.Stop(); StartBlast(new RectangleF(home.X - 700, 8, 220, 34), new Point(-1, -1)); };
        blastDemo.Start();
#endif

        menu.Items.Add("Ask Monkey... (double-click me)", null, (s, e) => OpenChat());
        menu.Items.Add(BrainMenu());
        menu.Items.Add(new ToolStripSeparator());
        var pullItem = new ToolStripMenuItem("Monkey opens my tabs") { Checked = pullTabs, CheckOnClick = true };
        pullItem.CheckedChanged += (s, e) => { pullTabs = pullItem.Checked; SaveSettings(); };
        menu.Items.Add(pullItem);
        var healthItem = new ToolStripMenuItem("Water && blink reminders") { Checked = healthReminders, CheckOnClick = true };
        healthItem.CheckedChanged += (s, e) => { healthReminders = healthItem.Checked; SaveSettings(); };
        menu.Items.Add(healthItem);
        menu.Items.Add(IntervalMenu("Water every", WaterChoices, () => waterEvery,
            v => { waterEvery = v; nextWater = clock.Elapsed.TotalSeconds + v; }));
        menu.Items.Add(IntervalMenu("Eye break every", EyesChoices, () => eyesEvery,
            v => { eyesEvery = v; nextEyes = clock.Elapsed.TotalSeconds + v; }));
        menu.Items.Add("What's the weather?", null, (s, e) => AskWeather());
        var weatherItem = new ToolStripMenuItem("Weather forecasts") { Checked = weatherOn, CheckOnClick = true };
        weatherItem.CheckedChanged += (s, e) => { weatherOn = weatherItem.Checked; SaveSettings(); if (weatherOn) nextWeatherFetch = 0; };
        menu.Items.Add(weatherItem);
        var chimeItem = new ToolStripMenuItem("Hourly chime") { Checked = chimeOn, CheckOnClick = true };
        chimeItem.CheckedChanged += (s, e) => { chimeOn = chimeItem.Checked; SaveSettings(); };
        menu.Items.Add(chimeItem);
        var fullItem = new ToolStripMenuItem("Hide during fullscreen") { Checked = hideInFullscreen, CheckOnClick = true };
        fullItem.CheckedChanged += (s, e) => { hideInFullscreen = fullItem.Checked; SaveSettings(); nextFullCheck = 0; };
        menu.Items.Add(fullItem);
        var screenMenu = new ToolStripMenuItem("Live on screen");
        screenMenu.DropDownOpening += (s, e) => FillScreenMenu(screenMenu);
        screenMenu.DropDownItems.Add("...");   // filled when opened (screens can come and go)
        menu.Items.Add(screenMenu);
        var blastItem = new ToolStripMenuItem("Monkey blasts closed tabs") { Checked = blastTabs, CheckOnClick = true };
        blastItem.CheckedChanged += (s, e) => { blastTabs = blastItem.Checked; SaveSettings(); };
        menu.Items.Add(blastItem);
        var startItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        startItem.Click += (s, e) => { bool on = startItem.Checked; ThreadPool.QueueUserWorkItem(_ => SetStartWithWindows(on)); };
        menu.Items.Add(startItem);
        hideItem = new ToolStripMenuItem("Hide monkey (hold M)");
        hideItem.Click += (s, e) => ToggleHidden();
        menu.Items.Add(hideItem);
        menu.Items.Add("Quit monkey", null, (s, e) => Close());
        Shown += (s, e) => { ApplyVisibility(); CheckStartWithWindows(startItem); };   // starts hidden if it was put away last time
        MouseDown += OnDown;
        MouseUp += OnUp;

        Application.Idle += RunLoop;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
#if HABITDEMO
        nextAct = 0.5;
#endif


#if DEMO
        // Test build only: visit a spot on the tab bar a moment after starting.
        var demo = new System.Windows.Forms.Timer { Interval = 1000 };
        demo.Tick += (s, e) =>
        {
            demo.Stop();
            foreach (var pr in Process.GetProcessesByName("chrome"))
                if (pr.MainWindowHandle != IntPtr.Zero) { pendingWindow = pr.MainWindowHandle; break; }
            pendingTab = new RectangleF(home.X - 340, 0, 40, 41);
#if !HOLDDEMO
            hasPending = true;
#endif
            pendingSince = clock.Elapsed.TotalSeconds;
            GoToTab(new PointF(pendingTab.X + 20, pendingTab.Bottom - 6));
        };
        demo.Start();
#endif
        var listener = new Thread(ListenToAudio);
        listener.IsBackground = true;
        listener.Start();

        var watcher = new Thread(WatchTabs);
        watcher.IsBackground = true;
        watcher.Start();
    }

    // ---- Browser tab watching (background thread) ----

    // Visible, non-minimised top-level windows that belong to Chrome or Edge.
    static List<IntPtr> BrowserWindows(Dictionary<uint, string> names)
    {
        var found = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string name;
            if (!names.TryGetValue(pid, out name))
            {
                try { name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { name = ""; }
                names[pid] = name;
            }
            if (name == "chrome" || name == "msedge") found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    void WatchTabs()
    {
        var strips = new Dictionary<IntPtr, AutomationElement>();
        var noStripUntil = new Dictionary<IntPtr, DateTime>();   // windows without tabs (pop-ups): don't search them every time
        var lastSelected = new Dictionary<IntPtr, string>();
        var names = new Dictionary<uint, string>();
        DateTime namesReset = DateTime.Now;
        var tabItem = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
        var tabStrip = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab);
        var cache = new CacheRequest();
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.RuntimeIdProperty);
        cache.Add(SelectionItemPattern.IsSelectedProperty);

        while (true)
        {
            // Often while a browser is in front (its tabs can move), now and then otherwise.
            Thread.Sleep(Hidden ? 1000 : browserInFront ? 120 : 500);
            try
            {
                if ((DateTime.Now - namesReset).TotalSeconds > 30) { names.Clear(); namesReset = DateTime.Now; }
                IntPtr fg = GetForegroundWindow();
                bool anyFront = false;
                var fresh = new Dictionary<IntPtr, List<RectangleF>>();
                var selected = new Dictionary<IntPtr, RectangleF>();

                foreach (IntPtr win in BrowserWindows(names))
                {
                    if (win == fg) anyFront = true;
                    AutomationElement strip;
                    if (!strips.TryGetValue(win, out strip))
                    {
                        DateTime until;
                        if (noStripUntil.TryGetValue(win, out until) && DateTime.Now < until) continue;
                        try { strip = AutomationElement.FromHandle(win).FindFirst(TreeScope.Descendants, tabStrip); } catch { strip = null; }
                        if (strip == null) { noStripUntil[win] = DateTime.Now.AddSeconds(5); continue; }
                        strips[win] = strip;
                    }

                    // Fetch every tab's selected state, position and id in a single request.
                    string selectedId = null;
                    System.Windows.Rect selectedRect = System.Windows.Rect.Empty;
                    var others = new List<RectangleF>();
                    try
                    {
                        AutomationElementCollection tabs;
                        using (cache.Activate()) tabs = strip.FindAll(TreeScope.Descendants, tabItem);
                        foreach (AutomationElement tab in tabs)
                        {
                            var tr = (System.Windows.Rect)tab.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty);
                            object sel = tab.GetCachedPropertyValue(SelectionItemPattern.IsSelectedProperty, true);
                            if (sel is bool && (bool)sel)
                            {
                                selectedId = string.Join(".", (int[])tab.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty));
                                selectedRect = tr;
                            }
                            else if (!tr.IsEmpty) others.Add(new RectangleF((float)tr.X, (float)tr.Y, (float)tr.Width, (float)tr.Height));
                        }
                    }
                    catch { strips.Remove(win); continue; }
                    fresh[win] = others;
                    if (!selectedRect.IsEmpty)
                        selected[win] = new RectangleF((float)selectedRect.X, (float)selectedRect.Y, (float)selectedRect.Width, (float)selectedRect.Height);

                    // Swing over when you switch tabs some other way (keyboard, or a click it didn't hold).
                    if (selectedId == null) continue;
                    string before;
                    bool known = lastSelected.TryGetValue(win, out before);
                    lastSelected[win] = selectedId;
                    if (win != fg || !known || before == selectedId) continue;
                    if (quietUntil > clock.Elapsed.TotalSeconds) continue;   // the browser picked a new tab after one was blasted
                    System.Windows.Rect r = selectedRect;
                    if (r.IsEmpty || r.Width < 1) continue;
                    var target = new PointF((float)(r.Left + r.Width / 2), (float)r.Bottom - 6);
                    BeginInvoke(new Action(() => GoToTab(target)));
                }

                lock (tabLock) { browserTabs = fresh; selectedTabs = selected; }
                browserInFront = anyFront;
                // Forget closed windows.
                foreach (var gone in new List<IntPtr>(strips.Keys)) if (!fresh.ContainsKey(gone)) strips.Remove(gone);
            }
            catch { /* a browser window closed or changed; try again next tick */ }
        }
    }

    void GoToTab(PointF tab)
    {
        if (!SystemInformation.VirtualScreen.Contains(Point.Round(tab))) return;
        if (mode == Mode.PullDown) return;
        // Already there (this is the tab it just pulled open).
        if ((mode == Mode.ToTab || mode == Mode.AtTab) && Math.Abs(tab.X - travelTo.X) < 30) return;
        StartTravel(tab, Mode.ToTab);
    }

    // ---- Holding your tab click until the monkey pulls it ----

    IntPtr OnMouseHook(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && (pullTabs || blastTabs) && !Hidden)
        {
            int msg = (int)w;
            var info = (MSLL)Marshal.PtrToStructure(l, typeof(MSLL));
            bool injected = (info.flags & 1) != 0;   // our own replayed click passes straight through
#if CLICKTEST
            // Test build: simulated clicks never reach the browser; say what they would have done.
            if (injected && (msg == 0x201 || msg == 0x202))
            {
                if (msg == 0x201)
                {
                    RectangleF ct;
                    bool onClose = FindCloseButton(info.pt, out ct);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "monkey_clicktest.txt"),
                        info.pt.x + "," + info.pt.y + " close=" + onClose + " mode=" + mode + " blastPhase=" + blastPhase + Environment.NewLine);
                    if (onClose && blastTabs && blastPhase == 0 && mode == Mode.Home)
                    {
                        var cp0 = new Point(info.pt.x, info.pt.y);
                        BeginInvoke(new Action(() => StartBlast(ct, cp0)));
                    }
                }
                return (IntPtr)1;
            }
#endif
            if (!injected)
            {
                if (msg == 0x202 /* LBUTTONUP */ && swallowUp) { swallowUp = false; return (IntPtr)1; }
                RectangleF tab;
                if (msg == 0x201 && blastTabs && blastPhase == 0 && mode == Mode.Home && !grabbed && !PerchedPose && sleepW < 0.5f
                    && FindCloseButton(info.pt, out tab))
                {
                    swallowUp = true;
                    var cp = new Point(info.pt.x, info.pt.y);
                    BeginInvoke(new Action(() => StartBlast(tab, cp)));
                    return (IntPtr)1;
                }
                if (msg == 0x201 /* LBUTTONDOWN */ && pullTabs && !grabbed && mode != Mode.PullDown && FindTab(info.pt, out tab))
                {
                    swallowUp = true;
                    var p = new Point(info.pt.x, info.pt.y);
                    BeginInvoke(new Action(() => PullTab(tab, p)));
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(hook, code, w, l);
    }

    // ---- Ask Monkey: a chat assistant powered by Claude Code on this PC ----
    // Each question goes to the local `claude` command, locked down: no tools at all (it can't read files,
    // run commands or browse), no personal settings, hooks or add-on servers, and it runs in an empty
    // folder of its own. It only ever sees what you type, plus your copied text when you tick the box.

    ChatBox chat;
    volatile bool asking;
    string chatSession;          // keeps the conversation going until "New chat"
    double lastTapAt = -10;
    internal static readonly string ChatDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"MonkeyPlay\chat");

    internal const string Persona =
        "You are Monkey, a friendly cartoon monkey in sunglasses (the user may call them coolers, shades or glasses) " +
        "and a teal puffer jacket who lives on the user's " +
        "Windows desktop, hanging from a branch, and helps them out. You have no tools and no access to their " +
        "computer, files, browser or the internet: you only see what they type, plus text they copied when they " +
        "choose to attach it, or one file they attach with the File button. If they ask you to do something on their PC, " +
        "say you can't and suggest how they can. To find or open a file or folder, tell them to type \"find\" and part of " +
        "its name (like: find GST invoice) or \"open downloads\": the monkey app does that on their PC, and you never see it. " +
        "Be warm, playful and brief: usually two to four short sentences, more only when they ask for detail or " +
        "the task needs it (like rewriting text). Write plain text that reads well in a small chat window: no " +
        "tables, headings or markdown symbols, and don't narrate actions in asterisks; short lists are fine. " +
        "Answer what they actually asked first, and get it right; if a message is misspelled, go with the likely " +
        "meaning. Each message ends with their local date and time in brackets; use it only when it helps. " +
        "An occasional monkey or banana emoji is welcome. " +
        "You can move your own cartoon body (nothing else) by putting one tag at the very end of your reply: " +
        "[do:shades_off] take off your sunglasses (you push them up onto your head), [do:shades_on] put them back over your eyes, [do:wave] wave, " +
        "[do:banana] eat a banana from your pocket, [do:drink] drink some water, [do:sit] sit on your branch, " +
        "[do:climb] climb up your tail, [do:look] look around. Use a tag only when the user asks you to do that " +
        "(or it clearly fits), at most one per reply. The user never sees the tag, so also say what you're doing.";

    void OpenChat()
    {
        if (chat == null || chat.IsDisposed)
        {
            chat = new ChatBox(ScaleAt(Point.Round(home)));
            chat.Send += (text, useClip, file) => Ask(text, useClip, file);
            chat.NewChat += () =>
            {
                chatSession = null;
                if (link != null && !asking) link.Stop();
                EnsureLink();   // a fresh conversation, already warming up
                Say(Pick("Fresh start!", "New chat, who dis?", "Clean slate!"), true);
            };
        }
        // Beside the monkey, toward the middle of the screen.
        Rectangle wa = Screen.FromPoint(Point.Round(home)).WorkingArea;
        int x = (int)(home.X - 90 * S) - chat.Width, y = (int)(home.Y + 20 * S);
        chat.Location = new Point(Math.Max(wa.Left + 8, Math.Min(wa.Right - chat.Width - 8, x)), Math.Max(wa.Top + 8, Math.Min(wa.Bottom - chat.Height - 8, y)));
        chat.Show();
        chat.Activate();
        chat.FocusInput();
        EnsureLink();   // start Claude Code now, so the first answer is quick too
        if (chatSession == null && chat.IsEmpty) Say(Pick("Ask me anything!", "What's up?", "Hi! How can I help?"), true);
    }

    // Claude Code is kept running while you chat (starting it takes a few seconds; answering takes about one),
    // and replies are shown word by word as they arrive. It's stopped after 10 quiet minutes and picks the
    // conversation back up next time.
    ClaudeLink link;
    double lastChatUse;
    string chatModel = "fast";   // fast (Haiku), balanced (Sonnet) or smart (Opus)
    bool replyStarted;

    void EnsureLink()
    {
        if (link == null)
        {
            link = new ClaudeLink();
            link.Text += piece => { try { BeginInvoke((Action)(() => GotPiece(piece))); } catch { } };
            link.Done += (ok, text, session) => { try { BeginInvoke((Action)(() => GotReply(ok, text, session))); } catch { } };
        }
        if (!link.Running)
        {
            string error;
            if (!link.Start(chatModel, chatSession, out error) && chat != null && !chat.IsDisposed) chat.AddNote(error);
        }
        lastChatUse = clock.Elapsed.TotalSeconds;
    }

    void Ask(string question, bool useClip, string file)
    {
        if (asking || string.IsNullOrWhiteSpace(question)) return;
        // "find ..." and "open ..." are done here on the PC; they never go to Claude.
        if (!useClip && file == null && FileCommand(question.Trim())) return;
        string prompt = question.Trim();
        if (file != null)
        {
            string body, why;
            if (ReadForChat(file, out body, out why))
            {
                if (body.Length > 30000) body = body.Substring(0, 30000) + "\n[...the rest was cut off...]";
                prompt += "\n\n--- File I attached: " + System.IO.Path.GetFileName(file) + " ---\n" + body;
            }
            else chat.AddNote("(" + why + " So I sent just your message.)");
        }
        if (useClip)
        {
            string clip = "";
            try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
            if (clip.Length > 20000) clip = clip.Substring(0, 20000) + "\n[...the rest was cut off...]";
            if (clip.Trim().Length > 0) prompt += "\n\n--- Text I copied ---\n" + clip;
            else chat.AddNote("(Nothing was copied, so I sent just your message.)");
        }
        Forecast wf = weather;
        if (wf != null && Regex.IsMatch(question, @"weather|rain|umbrella|sunny|snow|storm|forecast|temperature|\bhot\b|\bcold\b|tomorrow", RegexOptions.IgnoreCase))
            prompt += "\n\n(Live forecast where I am, from Open-Meteo: " + wf.Summary() + ")";
        prompt += "\n\n(It's " + DateTime.Now.ToString("dddd d MMMM yyyy, h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + " here.)";
        chat.AddMessage("You", question.Trim());
        EnsureLink();
        string error;
        if (!link.Ask(prompt, out error)) { chat.AddNote(error); return; }
        chat.SetBusy(true);
        asking = true;
        replyStarted = false;
        streamHeld = "";
        double now = clock.Elapsed.TotalSeconds;
        Say(Pick("Hmm, let me think...", "Ooh, good one...", "Thinking...", "One sec..."), true, 30);
        if (act == Act.None && mode == Mode.Home && perch == Perch.None) StartAct(Act.Scratch, 30, now);   // scratches its head while it thinks
    }

    // ---- Finding and opening your files (on this PC only; nothing here is sent to Claude) ----
    // Uses the Windows Search index (what the Start menu searches), so it's instant and costs nothing while idle.

    List<string> lastFound = new List<string>();

    static readonly Regex FindCmd = new Regex(@"^(?:please\s+)?(?:can you\s+|could you\s+)?(find|search(?:\s+for)?|look\s+for|locate|where\s+is|where's|open)\s+(.+?)[\s?.!]*$",
        RegexOptions.IgnoreCase);
    static readonly Regex OpenNumber = new Regex(@"^open\s+(?:no\.?\s*|number\s+|#)?(\d{1,2})[\s.!]*$", RegexOptions.IgnoreCase);
    static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "my", "the", "a", "an", "file", "files", "document", "documents", "doc", "named", "called", "with", "for", "of", "on",
        "in", "me", "please", "that", "this", "some", "any", "all", "from", "pc", "computer", "laptop", "folder" };

    bool FileCommand(string q)
    {
        Match m = OpenNumber.Match(q);
        if (m.Success && lastFound.Count > 0)
        {
            int n = int.Parse(m.Groups[1].Value);
            chat.AddMessage("You", q);
            if (n >= 1 && n <= lastFound.Count) { OpenFile(lastFound[n - 1], false); chat.AddNote("Opening " + System.IO.Path.GetFileName(lastFound[n - 1].TrimEnd('\\')) + "..."); }
            else chat.AddNote("There's no number " + n + " in the list.");
            return true;
        }
        m = FindCmd.Match(q);
        if (!m.Success) return false;
        string verb = m.Groups[1].Value.ToLowerInvariant(), what = m.Groups[2].Value.Trim();

        // "open downloads", "open my documents folder", "open file explorer"...
        string known = KnownFolder(what);
        if (known != null && verb == "open")
        {
            chat.AddMessage("You", q);
            OpenFile(known, false);
            chat.AddNote("Opening " + (known.Length == 0 ? "File Explorer" : System.IO.Path.GetFileName(known)) + "...");
            Say(Pick("Here you go!", "Opening it!", "Ta-da!"), true);
            return true;
        }

        // The words to look for, and a file type if one was named ("my GST pdf", "excel sheet").
        var exts = new List<string>();
        var words = new List<string>();
        foreach (string raw in Regex.Split(what, @"[\s,]+"))
        {
            string w = Regex.Replace(raw, @"[^\w.\-]", "").Trim('.');
            if (w.Length == 0 || Filler.Contains(w)) continue;
            string[] kind = KindExtensions(w);
            if (kind != null) { exts.AddRange(kind); continue; }
            if (Regex.IsMatch(w, @"^\.?[a-z0-9]{2,5}$", RegexOptions.IgnoreCase) && raw.StartsWith(".")) { exts.Add("." + w.TrimStart('.').ToLowerInvariant()); continue; }
            words.Add(w);
        }
        if (words.Count == 0 && exts.Count == 0) return false;   // nothing to look for: let Monkey answer

        chat.AddMessage("You", q);
        chat.SetBusy(true);
        Say(Pick("Let me look...", "Searching my branches...", "Hmm, where did you put it..."), true, 10);
        var found = new List<string>();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { found = SearchFiles(words, exts, 15); } catch { }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    chat.SetBusy(false);
                    string label = string.Join(" ", words) + (exts.Count > 0 ? (words.Count > 0 ? " " : "") + "(" + string.Join(", ", exts.Distinct()) + ")" : "");
                    if (found.Count == 0)
                    {
                        chat.AddNote("I couldn't find any file matching \"" + label + "\". Try fewer or different words, or part of the file name.");
                        Say(Pick("Couldn't find it!", "No luck, sorry!"), true);
                        return;
                    }
                    lastFound = found;
                    chat.AddFiles((found.Count == 1 ? "Found 1 match" : "Found " + found.Count + " matches") + " for \"" + label
                        + "\" (newest first). Click a name to open it, or type \"open 1\":", found);
                    if (verb == "open" && found.Count == 1) { OpenFile(found[0], false); chat.AddNote("Opening it..."); }
                    Say(Pick("Found it!", "Here's what I found!", "Got some!"), true);
                }));
            }
            catch { }
        });
        return true;
    }

    static string[] KindExtensions(string w)
    {
        switch (w.ToLowerInvariant())
        {
            case "pdf": case "pdfs": return new[] { ".pdf" };
            case "excel": case "sheet": case "sheets": case "spreadsheet": case "xlsx": case "csv": return new[] { ".xlsx", ".xls", ".csv" };
            case "word": case "docx": return new[] { ".docx", ".doc" };
            case "powerpoint": case "ppt": case "pptx": case "slides": case "presentation": return new[] { ".pptx", ".ppt" };
            case "photo": case "photos": case "image": case "images": case "picture": case "pictures": case "pic": case "pics": case "screenshot": case "screenshots":
                return new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".bmp" };
            case "video": case "videos": case "movie": case "movies": return new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm" };
            case "song": case "songs": case "audio": case "mp3": return new[] { ".mp3", ".wav", ".m4a", ".flac" };
            case "zip": case "zips": return new[] { ".zip", ".rar", ".7z" };
            case "text": case "txt": case "notes": return new[] { ".txt", ".md" };
        }
        return null;
    }

    static string KnownFolder(string what)
    {
        string w = Regex.Replace(what.ToLowerInvariant(), @"\b(my|the|folder)\b", "").Trim();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        switch (w)
        {
            case "download": case "downloads": return System.IO.Path.Combine(home, "Downloads");
            case "document": case "documents": case "docs": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            case "desktop": return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            case "picture": case "pictures": case "photos": return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            case "music": return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            case "video": case "videos": return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            case "home": case "user": return home;
            case "file explorer": case "explorer": case "this pc": case "files": return "";
        }
        return null;
    }

    // Folders full of program and code-library files nobody means when they say "find my ...".
    static readonly string[] SkipDirs = { "\\AppData\\", "\\$Recycle.Bin\\", "\\node_modules\\", "\\site-packages\\", "\\.git\\",
        "\\venv\\", "\\.venv\\", "\\__pycache__\\", "\\Program Files", "\\Windows\\", "\\.vscode\\", "\\.cache\\" };

    // Does each word start a word in the name? "gst" fits "GST invoice.pdf", "my_gst.xlsx" and "MonthlyGst.csv",
    // not "pgstattuple.html".
    static bool StartsWords(string name, List<string> words)
    {
        return words.All(w => Regex.IsMatch(name, @"(?:^|(?<=[^A-Za-z0-9])|(?<=[0-9])(?=[A-Za-z])|(?-i:(?<=[a-z])(?=[A-Z])))" + Regex.Escape(w), RegexOptions.IgnoreCase));
    }

    // Newest first: names where the words start a word, then files whose contents have them, then names
    // that only have them in the middle of a word (only if nothing better turned up).
    static List<string> SearchFiles(List<string> words, List<string> exts, int max)
    {
        string extSql = exts.Count == 0 ? "" : " AND (" + string.Join(" OR ", exts.Distinct().Select(e => "System.FileExtension = '" + e + "'")) + ")";
        string where = string.Join(" AND ", SkipDirs.Select(d => "System.ItemPathDisplay NOT LIKE '%" + d + "%'")) + extSql;
        try
        {
            var byName = new List<string>();
            var byContent = new List<string>();
            using (var c = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
            {
                c.Open();
                Action<string, int, List<string>> run = (cond, top, into) =>
                {
                    string sql = "SELECT TOP " + top + " System.ItemPathDisplay FROM SystemIndex WHERE " + where + cond + " ORDER BY System.DateModified DESC";
                    using (var cmd = new System.Data.OleDb.OleDbCommand(sql, c))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) { string p = r[0] as string; if (p != null) into.Add(p); }
                };
                run(string.Join("", words.Select(w => " AND System.FileName LIKE '%" + w.Replace("'", "").Replace("%", "") + "%'")), 300, byName);
                if (words.Count > 0)
                    try { run(string.Join("", words.Select(w => " AND CONTAINS(*, '\"" + Regex.Replace(w, "[\"'*]", "") + "*\"')")), max, byContent); } catch { }
            }
            var good = byName.Where(p => StartsWords(System.IO.Path.GetFileName(p.TrimEnd('\\')), words));
            var found = good.Concat(byContent).Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
            if (found.Count == 0) found = byName.Take(max).ToList();
            return found;
        }
        catch { }

        // No Windows Search: look through your own folders by name instead (a few seconds at most).
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sw = Stopwatch.StartNew();
        var stack = new Stack<string>(new[] { "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music", "OneDrive" }
            .Select(f => System.IO.Path.Combine(home, f)).Where(System.IO.Directory.Exists));
        var hits = new List<System.IO.FileInfo>();
        while (stack.Count > 0 && sw.ElapsedMilliseconds < 4000)
        {
            string dir = stack.Pop();
            try
            {
                foreach (string d in System.IO.Directory.GetDirectories(dir)) if (!System.IO.Path.GetFileName(d).StartsWith(".")) stack.Push(d);
                foreach (string f in System.IO.Directory.GetFiles(dir))
                {
                    string name = System.IO.Path.GetFileName(f);
                    if (StartsWords(name, words)
                        && (exts.Count == 0 || exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())))
                        hits.Add(new System.IO.FileInfo(f));
                }
            }
            catch { }
        }
        return hits.OrderByDescending(f => f.LastWriteTime).Take(max).Select(f => f.FullName).ToList();
    }

    // Opens a file with its usual app, or shows it selected in File Explorer. "" opens File Explorer.
    internal static void OpenFile(string path, bool inFolder)
    {
        try
        {
            if (path.Length == 0) Process.Start("explorer.exe");
            else if (inFolder) Process.Start("explorer.exe", "/select,\"" + path.TrimEnd('\\') + "\"");
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    // ---- Reading a file you attached ----
    // Plain text of all kinds, and Word, Excel and PowerPoint files (read straight from inside them).

    static bool ReadForChat(string path, out string text, out string why)
    {
        text = null; why = null;
        try
        {
            var fi = new System.IO.FileInfo(path);
            if (!fi.Exists) { why = "I couldn't find that file."; return false; }
            if (fi.Length > 25 * 1024 * 1024) { why = "That file is too big for me (over 25 MB)."; return false; }
            string ext = fi.Extension.ToLowerInvariant();
            if (ext == ".docx" || ext == ".xlsx" || ext == ".pptx") text = ReadOffice(path, ext);
            else if (ext == ".pdf" || ext == ".doc" || ext == ".xls" || ext == ".ppt")
            {
                why = "I can't read " + ext + " files. Open it, copy the text, and use \"Use copied text\" instead.";
                return false;
            }
            else
            {
                byte[] head = new byte[Math.Min(4096, (int)fi.Length)];
                using (var s = fi.OpenRead()) s.Read(head, 0, head.Length);
                if (Array.IndexOf(head, (byte)0) >= 0 && !(head.Length > 1 && (head[0] == 0xFF || head[0] == 0xFE)))
                {
                    why = "That looks like a picture or program file, not text, so I can't read it.";
                    return false;
                }
                text = System.IO.File.ReadAllText(path);
            }
            if (string.IsNullOrWhiteSpace(text)) { why = "I couldn't find any text in that file."; return false; }
            return true;
        }
        catch (Exception ex) { why = "I couldn't read that file (" + ex.Message + ")."; return false; }
    }

    static string ReadOffice(string path, string ext)
    {
        var sb = new System.Text.StringBuilder();
        using (var zip = new System.IO.Compression.ZipArchive(System.IO.File.OpenRead(path), System.IO.Compression.ZipArchiveMode.Read))
        {
            Func<System.IO.Compression.ZipArchiveEntry, System.Xml.Linq.XDocument> load = e => { using (var s = e.Open()) return System.Xml.Linq.XDocument.Load(s); };
            // Entries by name, with forward slashes (some zip tools write backslashes).
            var entries = zip.Entries.GroupBy(x => x.FullName.Replace('\\', '/')).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Func<string, System.IO.Compression.ZipArchiveEntry> entry = n => { System.IO.Compression.ZipArchiveEntry e; return entries.TryGetValue(n, out e) ? e : null; };
            Func<string, IEnumerable<System.IO.Compression.ZipArchiveEntry>> numbered = pattern => entries.Where(x => Regex.IsMatch(x.Key, pattern))
                .OrderBy(x => int.Parse(Regex.Match(x.Key, @"(\d+)\.xml$").Groups[1].Value)).Select(x => x.Value);
            if (ext == ".docx")
            {
                var e = entry("word/document.xml");
                if (e == null) return "";
                foreach (var p in load(e).Descendants().Where(x => x.Name.LocalName == "p"))
                    sb.AppendLine(string.Concat(p.Descendants().Where(x => x.Name.LocalName == "t" || x.Name.LocalName == "tab")
                        .Select(x => x.Name.LocalName == "tab" ? "\t" : x.Value)));
            }
            else if (ext == ".pptx")
            {
                int slide = 0;
                foreach (var e in numbered(@"^ppt/slides/slide\d+\.xml$"))
                {
                    sb.AppendLine("[Slide " + (++slide) + "]");
                    foreach (var p in load(e).Descendants().Where(x => x.Name.LocalName == "p"))
                    {
                        string line = string.Concat(p.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value));
                        if (line.Trim().Length > 0) sb.AppendLine(line);
                    }
                }
            }
            else   // .xlsx: each sheet as tab-separated rows
            {
                var shared = new List<string>();
                var ss = entry("xl/sharedStrings.xml");
                if (ss != null)
                    foreach (var si in load(ss).Root.Elements().Where(x => x.Name.LocalName == "si"))
                        shared.Add(string.Concat(si.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value)));
                var names = new List<string>();
                var wb = entry("xl/workbook.xml");
                if (wb != null) names.AddRange(load(wb).Descendants().Where(x => x.Name.LocalName == "sheet").Select(x => (string)x.Attribute("name")));
                int n = 0;
                foreach (var e in numbered(@"^xl/worksheets/sheet\d+\.xml$"))
                {
                    sb.AppendLine("[Sheet: " + (n < names.Count ? names[n] : "Sheet" + (n + 1)) + "]");
                    n++;
                    foreach (var row in load(e).Descendants().Where(x => x.Name.LocalName == "row"))
                    {
                        var cells = new List<string>();
                        foreach (var c in row.Elements().Where(x => x.Name.LocalName == "c"))
                        {
                            string t = (string)c.Attribute("t");
                            var v = c.Elements().FirstOrDefault(x => x.Name.LocalName == "v");
                            string val = t == "inlineStr" ? string.Concat(c.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value))
                                : v == null ? "" : t == "s" ? (int.Parse(v.Value) < shared.Count ? shared[int.Parse(v.Value)] : "") : v.Value;
                            // Put the value in its own column (cells can be skipped when empty).
                            string refA = (string)c.Attribute("r") ?? "";
                            int col = 0;
                            foreach (char ch in refA) { if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1); else break; }
                            while (col > 0 && cells.Count < col - 1) cells.Add("");
                            cells.Add(val);
                        }
                        if (cells.Any(x => x.Length > 0)) sb.AppendLine(string.Join("\t", cells));
                        if (sb.Length > 60000) break;
                    }
                }
            }
        }
        return sb.ToString();
    }

#if CHATTEST
    double chatAskedAt; int chatTurns; bool firstLogged;
    void ChatLog(string what) { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "monkey_chattest.txt"),
        what + " after " + (clock.Elapsed.TotalSeconds - chatAskedAt).ToString("F2") + " s" + Environment.NewLine); }
#endif
    void GotPiece(string piece)
    {
#if CHATTEST
        if (!firstLogged) { firstLogged = true; ChatLog("turn " + (chatTurns + 1) + ": first words"); }
#endif
        if (chat == null || chat.IsDisposed || !asking) return;
        // Action tags ("[do:wave]") are for the monkey, never shown: drop complete ones, and hold back
        // anything that might be the start of one (and trailing spaces) until the next piece arrives.
        streamHeld += piece;
        streamHeld = Regex.Replace(streamHeld, @"\s*\[do:[a-z_]*\]", "");
        int cut = streamHeld.Length;
        int open = streamHeld.LastIndexOf('[');
        if (open >= 0 && streamHeld.IndexOf(']', open) < 0 && streamHeld.Length - open <= 24) cut = open;
        while (cut > 0 && char.IsWhiteSpace(streamHeld[cut - 1])) cut--;
        if (cut == 0) return;
        if (!replyStarted) { chat.BeginReply(); replyStarted = true; }
        chat.AppendReply(streamHeld.Substring(0, cut));
        streamHeld = streamHeld.Substring(cut);
    }

    string streamHeld = "";

    void GotReply(bool ok, string reply, string session)
    {
        if (!asking) return;
        asking = false;
        lastChatUse = clock.Elapsed.TotalSeconds;
#if CHATTEST
        ChatLog("turn " + (++chatTurns) + ": done (ok=" + ok + ")");
        if (chatTurns == 1)
        {
            var again = new System.Windows.Forms.Timer { Interval = 1500 };
            again.Tick += (s2, e2) => { again.Stop(); firstLogged = false; chatAskedAt = clock.Elapsed.TotalSeconds; Ask("Put your sunglasses back on please!", false); };
            again.Start();
        }
#endif
        if (act == Act.Scratch) act = Act.None;
        if (session != null) chatSession = session;
        string action = null;
        if (ok)
        {
            // An action tag ("[do:wave]") is for the monkey, not for you: take it out and do it.
            var m = Regex.Match(reply, @"\[do:([a-z_]+)\]");
            if (m.Success)
            {
                action = m.Groups[1].Value;
                reply = Regex.Replace(reply, @"\s*\[do:[a-z_]+\]", "").Trim();
            }
            string rest = Regex.Replace(streamHeld, @"\s*\[do:[a-z_]*\]?", "").TrimEnd();
            if (rest.Length > 0 && replyStarted && chat != null && !chat.IsDisposed) chat.AppendReply(rest);
            streamHeld = "";
        }
        if (chat != null && !chat.IsDisposed)
        {
            chat.SetBusy(false);
            if (ok && !replyStarted) chat.AddMessage("Monkey", reply);   // nothing streamed: show it all at once
            else if (ok) chat.FinishReply(reply);
            else if (OutOfUsage(reply))
                chat.AddNote("My brain needs a rest: your Claude usage limit is used up. " + Regex.Replace(reply, @"^Claude Code had a problem:\s*", ""));
            else chat.AddNote(reply);
        }
        if (action != null) DoAction(action);
        if (ok)
        {
            grinTime = 1.2;
            string brief = Regex.Replace(reply, @"[*_`#>]+", "").Trim();
            brief = Regex.Replace(brief, @"[\uD800-\uDFFF\uFE0F\u200D]", "");   // the bubble's font has no emoji
            brief = Regex.Replace(brief, @"\s+", " ").Trim();
            if (brief.Length > 90) brief = brief.Substring(0, 87).TrimEnd() + "...";
            Say(brief, true, Math.Min(9, 3 + brief.Length * 0.06));
        }
        else if (OutOfUsage(reply)) Say(Pick("I'm all out of brain juice for now!", "Too much thinking! I need a rest."), true);
        else Say(Pick("Oops, my brain slipped on a peel.", "Hmm, that didn't work."), true);
    }

    static bool OutOfUsage(string error) { return Regex.IsMatch(error, @"\blimit\b|quota|credit balance", RegexOptions.IgnoreCase); }

    // Finds the claude command (installed with npm, or anywhere on PATH).
    internal static string FindClaude()
    {
        var places = new List<string> { System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"npm\claude.cmd") };
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (dir.Trim().Length == 0) continue;
            places.Add(System.IO.Path.Combine(dir.Trim(), "claude.exe"));
            places.Add(System.IO.Path.Combine(dir.Trim(), "claude.cmd"));
        }
        foreach (string f in places) try { if (System.IO.File.Exists(f)) return f; } catch { }
        return null;
    }

    // Shut Claude Code down after a quiet spell (and when the monkey quits).
    void CheckChatIdle(double now)
    {
        if (link != null && link.Running && !asking && now - lastChatUse > 600) link.Stop();
    }

    // Things you can ask the monkey (in the chat) to do with its own body.
    void DoAction(string action)
    {
        double now = clock.Elapsed.TotalSeconds;
        bool sittingNow = perch == Perch.Sit;
        bool free = mode == Mode.Home && !grabbed && sleepW < 0.5f && (perch == Perch.None || sittingNow);
        switch (action)
        {
            case "shades_off":
            case "shades_on":
                bool want = action == "shades_on";
                if (want == shadesOn) break;
                shadesTarget = want; shadesFlipped = false;
                if (free) StartAct(Act.Shades, ShadesLen, now);
                else { shadesOn = want; SaveSettings(); }   // busy (sitting, away): just do it
                break;
            case "wave":
                if (free) { waveSide = sittingNow ? 1 : -1; StartAct(Act.Wave, 2.2, now); grinTime = 2.2; waveCooldown = now + 12; }   // toward the chat
                break;
            case "banana":
                if (free) { StartAct(Act.Eat, EatLen, now); peelTossed = false; }
                break;
            case "drink":
                if (free) StartAct(Act.Drink, DrinkLen, now);
                break;
            case "sit":
                if (free && !sittingNow && !night) StartSit(now);
                break;
            case "climb":
                if (free && !sittingNow) StartAct(Act.Climb, 2.6, now);
                break;
            case "look":
                if (free) StartAct(Act.Look, 3.0, now);
                break;
        }
    }

    // Sunglasses on its eyes or pushed up on its head (remembered). Either way: hand up, move them, hand down.
    bool shadesOn = true, shadesTarget = true, shadesFlipped = true;
    const double ShadesLen = 1.1, ShadesLift = 0.35, ShadesSwap = 0.6;

    void UpdateShades(double now)
    {
        if (!shadesFlipped && (act != Act.Shades || now - actStart >= ShadesSwap))
        {
            shadesOn = shadesTarget; shadesFlipped = true;
            SaveSettings();
        }
    }

    ToolStripMenuItem BrainMenu()
    {
        var parent = new ToolStripMenuItem("Monkey's brain");
        string[] keys = { "fast", "balanced", "smart" };
        string[] names = { "Fast (Haiku)", "Balanced (Sonnet)", "Smartest (Opus, slower)" };
        for (int i = 0; i < keys.Length; i++)
        {
            string k = keys[i];
            var it = new ToolStripMenuItem(names[i]) { Checked = chatModel == k };
            it.Click += (s, e) =>
            {
                chatModel = k;
                foreach (ToolStripMenuItem other in parent.DropDownItems) other.Checked = other == (ToolStripMenuItem)s;
                SaveSettings();
                if (link != null && link.Running && !asking) link.Stop();   // the next question starts it with the new brain, same conversation
            };
            parent.DropDownItems.Add(it);
        }
        return parent;
    }

    // ---- Blasting closed tabs ----
    // Click a tab's close (x) and the monkey flings a banana at it: BOOM, the tab flies apart, and your
    // click goes through at the moment of impact so the tab really closes under the explosion.

    bool blastTabs = true;
    volatile int blastPhase;      // 0 idle, 1 winding up, 2 banana flying, 3 exploding
    double blastStart, flightStart, boomStart, quietUntil;
    RectangleF blastTab;
    Point blastClick;
    PointF blastFrom, blastAt, throwDir = new PointF(-1, 0), handScreen;
    Bitmap tabShot;
    FxLayer fx = new FxLayer();
    Bitmap fxBmp;
    Rectangle fxArea;
    const double ThrowWind = 0.32, FlightTime = 0.42, BoomTime = 1.1;

    struct Bit { public PointF p, v; public float a, spin; public RectangleF src; }
    Bit[] debris = new Bit[0];

    // The close button is the little x near a tab's right end (narrow tabs only show one on the open tab).
    bool FindCloseButton(PT pt, out RectangleF hit)
    {
        hit = RectangleF.Empty;
        IntPtr under = GetAncestor(WindowFromPoint(pt), 2 /* GA_ROOT */);
        lock (tabLock)
        {
            RectangleF open;
            if (selectedTabs.TryGetValue(under, out open) && OnCloseButton(pt, open)) { hit = open; return true; }
            List<RectangleF> tabs;
            if (browserTabs.TryGetValue(under, out tabs))
                foreach (var r in tabs)
                    if (r.Width >= WideTab && OnCloseButton(pt, r)) { hit = r; return true; }
        }
        return false;
    }

    void StartBlast(RectangleF tab, Point click)
    {
        double now = clock.Elapsed.TotalSeconds;
        blastTab = tab; blastClick = click;
        blastAt = new PointF(tab.X + tab.Width / 2, tab.Y + tab.Height / 2);
        try
        {
            if (tabShot != null) tabShot.Dispose();
            tabShot = new Bitmap(Math.Max(1, (int)tab.Width), Math.Max(1, (int)tab.Height), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(tabShot)) g.CopyFromScreen((int)tab.X, (int)tab.Y, 0, 0, tabShot.Size);
        }
        catch { tabShot = null; }
        float hx = anchor.X + (float)x * S, hy = anchor.Y + (float)y * S;
        float ddx = blastAt.X - hx, ddy = blastAt.Y - hy, len = Math.Max(1, (float)Math.Sqrt(ddx * ddx + ddy * ddy));
        throwDir = new PointF(ddx / len, ddy / len);
        handScreen = new PointF(hx, hy);
        StartAct(Act.Throw, 0.9, now);
        grinTime = 0.9;
        Say(Pick("Bye bye, tab!", "Target locked!", "Fire in the hole!", "This one's mine!"), true);
        blastStart = now;
        blastPhase = 1;
    }

    void UpdateBlast(double now)
    {
        if (blastPhase == 0) return;
        if (blastPhase == 1 && now - blastStart >= ThrowWind)
        {
            // Let go of the banana: it flies from the hand to the tab.
            blastFrom = handScreen;
            Rectangle scr = Screen.FromPoint(Point.Round(blastAt)).Bounds;
            int left = (int)Math.Min(blastFrom.X, blastAt.X) - 170, right = (int)Math.Max(blastFrom.X, blastAt.X) + 170;
            int top = Math.Max(scr.Top, (int)Math.Min(blastFrom.Y, blastAt.Y) - 200), bottom = (int)Math.Max(blastFrom.Y, blastAt.Y) + 230;
            fxArea = Rectangle.FromLTRB(left, top, right, bottom);
            if (fxBmp != null) fxBmp.Dispose();
            fxBmp = new Bitmap(fxArea.Width, fxArea.Height, PixelFormat.Format32bppArgb);
            flightStart = now;
            blastPhase = 2;
        }
        if (blastPhase == 2 && now - flightStart >= FlightTime)
        {
            // Impact: close the tab for real, and blow its picture to bits.
            ReplayClick(blastClick);
            quietUntil = now + 2;
            boomStart = now;
            blastPhase = 3;
            MakeDebris();
            grinTime = 1.2;
            if (rnd.NextDouble() < 0.6) Say(Pick("BOOM!", "Kaboom!", "Got it!", "Bullseye!"), true);
        }
        if (blastPhase >= 2) DrawFx(now);
        if (blastPhase == 3 && now - boomStart >= BoomTime)
        {
            fx.Hide();
            if (tabShot != null) { tabShot.Dispose(); tabShot = null; }
            blastPhase = 0;
        }
    }

    // The tab's picture in pieces, flying out from the blast.
    void MakeDebris()
    {
        const int cols = 4, rows = 2;
        debris = new Bit[cols * rows];
        for (int i = 0; i < debris.Length; i++)
        {
            float cw = blastTab.Width / cols, ch = blastTab.Height / rows;
            var src = new RectangleF((i % cols) * cw, (i / cols) * ch, cw, ch);
            var center = new PointF(blastTab.X + src.X + cw / 2, blastTab.Y + src.Y + ch / 2);
            float ang = (float)Math.Atan2(center.Y - blastAt.Y + (float)rnd.NextDouble() * 6 - 3, center.X - blastAt.X + 0.01f);
            float speed = 260 + (float)rnd.NextDouble() * 320;
            debris[i] = new Bit { p = center, v = new PointF((float)Math.Cos(ang) * speed, (float)Math.Sin(ang) * speed - 280), a = 0,
                spin = ((float)rnd.NextDouble() - 0.5f) * 1400, src = src };
        }
    }

    void DrawFx(double now)
    {
        using (var g = Graphics.FromImage(fxBmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.TranslateTransform(-fxArea.Left, -fxArea.Top);   // draw in screen coordinates

            if (blastPhase == 2)
            {
                // The banana, spinning along an arc, with a little trail.
                float u = (float)Math.Min(1, (now - flightStart) / FlightTime);
                float dist = (float)Math.Sqrt(Math.Pow(blastAt.X - blastFrom.X, 2) + Math.Pow(blastAt.Y - blastFrom.Y, 2));
                for (int i = 4; i >= 0; i--)
                {
                    float uu = Math.Max(0, u - i * 0.035f);
                    var pt = Lerp(blastFrom, blastAt, uu);
                    pt.Y -= (float)Math.Sin(Math.PI * uu) * Math.Min(140, dist * 0.18f);
                    if (i > 0)
                        using (var b = new SolidBrush(Color.FromArgb(90 - i * 16, 255, 225, 90))) g.FillEllipse(b, pt.X - 7 + i, pt.Y - 7 + i, 14 - 2 * i, 14 - 2 * i);
                    else DrawBanana(g, new PointF(pt.X, pt.Y + 10), (float)((now - flightStart) * 1100), 1, 0, 1, false);
                }
            }
            else
            {
                float a = (float)(now - boomStart);
                var c = blastAt;

                // Smoke puffs drifting up and fading.
                for (int i = 0; i < 7; i++)
                {
                    double ang = i * 2 * Math.PI / 7 + 0.4;
                    float k = (float)Math.Min(1, a / BoomTime), rr = 14 + 40 * Smooth(a / 0.6);
                    float px = c.X + (float)Math.Cos(ang) * (18 + 50 * k), py = c.Y + (float)Math.Sin(ang) * (12 + 30 * k) - 40 * k;
                    int al = (int)(150 * Math.Max(0, 1 - k) * Math.Min(1, a / 0.1));
                    using (var b = new SolidBrush(Color.FromArgb(al, 120, 120, 125))) g.FillEllipse(b, px - rr, py - rr, rr * 2, rr * 2);
                }

                // Pieces of the tab flying apart and falling.
                if (tabShot != null)
                    for (int i = 0; i < debris.Length; i++)
                    {
                        var d = debris[i];
                        var pos = new PointF(d.p.X + d.v.X * a, d.p.Y + d.v.Y * a + 900 * a * a / 2);
                        var st = g.Save();
                        g.TranslateTransform(pos.X, pos.Y);
                        g.RotateTransform(d.spin * a);
                        float fade = (float)Math.Max(0, Math.Min(1, (BoomTime - a) / 0.4));
                        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = fade };
                        using (var ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            g.DrawImage(tabShot, new Rectangle((int)(-d.src.Width / 2), (int)(-d.src.Height / 2), (int)d.src.Width, (int)d.src.Height),
                                d.src.X, d.src.Y, d.src.Width, d.src.Height, GraphicsUnit.Pixel, ia);
                        }
                        g.Restore(st);
                    }

                // A quick white flash.
                if (a < 0.14f)
                {
                    float rr = 12 + 380 * a;
                    using (var b = new SolidBrush(Color.FromArgb((int)(230 * (1 - a / 0.14f)), 255, 255, 235))) g.FillEllipse(b, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                }

                // The cartoon burst: an orange star with a yellow heart.
                if (a < 0.5f)
                {
                    float grow = Smooth(a / 0.16f), fade = Math.Max(0, Math.Min(1, (0.5f - a) / 0.25f));
                    DrawStar(g, c, 22 + 92 * grow, 0.55f, 12, a * 40, Color.FromArgb((int)(255 * fade), 255, 128, 20));
                    DrawStar(g, c, 14 + 58 * grow, 0.6f, 10, -a * 60, Color.FromArgb((int)(255 * fade), 255, 225, 60));
                }

                // Sparks shooting out.
                if (a < 0.45f)
                    for (int i = 0; i < 16; i++)
                    {
                        double ang = i * 2 * Math.PI / 16 + (i % 2) * 0.2;
                        float r0 = 30 + 260 * a, r1 = r0 + 18;
                        int al = (int)(255 * (1 - a / 0.45f));
                        using (var pn = RoundPen(Color.FromArgb(al, 255, 210, 80), 3f))
                            g.DrawLine(pn, c.X + (float)Math.Cos(ang) * r0, c.Y + (float)Math.Sin(ang) * r0, c.X + (float)Math.Cos(ang) * r1, c.Y + (float)Math.Sin(ang) * r1);
                    }

                // "BOOM!" pops up.
                if (a > 0.04f && a < 0.75f)
                {
                    float pop = a < 0.14f ? 0.6f + 2.8f * (a - 0.04f) : 1f, fade = Math.Max(0, Math.Min(1, (0.75f - a) / 0.2f));
                    var st = g.Save();
                    g.TranslateTransform(c.X, c.Y - 6);
                    g.RotateTransform(-8);
                    g.ScaleTransform(pop, pop);
                    using (var path = new GraphicsPath())
                    using (var ff = new FontFamily("Arial Black"))
                    {
                        path.AddString("BOOM!", ff, (int)FontStyle.Bold, 30, new PointF(0, 0), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        using (var o = new Pen(Color.FromArgb((int)(255 * fade), 90, 25, 10), 5f) { LineJoin = LineJoin.Round }) g.DrawPath(o, path);
                        using (var b = new SolidBrush(Color.FromArgb((int)(255 * fade), 255, 255, 255))) g.FillPath(b, path);
                    }
                    g.Restore(st);
                }
            }
        }
        if (!fx.Visible) fx.Show();
        Monkey.Layer(fx.Handle, fxBmp, fxArea.Location);
        if (!Hidden) KeepOnTop();   // the monkey stays in front of its own blast
    }

    static void DrawStar(Graphics g, PointF c, float r, float inner, int points, float turn, Color col)
    {
        var pts = new PointF[points * 2];
        for (int i = 0; i < pts.Length; i++)
        {
            double ang = (turn + i * 180.0 / points) * Math.PI / 180;
            float rr = i % 2 == 0 ? r : r * inner;
            pts[i] = new PointF(c.X + (float)Math.Cos(ang) * rr, c.Y + (float)Math.Sin(ang) * rr);
        }
        using (var b = new SolidBrush(col)) g.FillPolygon(b, pts);
        using (var o = new Pen(Color.FromArgb(col.A, 120, 45, 10), 2f) { LineJoin = LineJoin.Round }) g.DrawPolygon(o, pts);
    }

    bool FindTab(PT pt, out RectangleF hit)
    {
        hit = RectangleF.Empty;
        IntPtr under = GetAncestor(WindowFromPoint(pt), 2 /* GA_ROOT */);
        lock (tabLock)
        {
            List<RectangleF> tabs;
            if (!browserTabs.TryGetValue(under, out tabs)) return false;
            foreach (var r in tabs)
            {
                // Leave the tab's close (x) button alone (narrow tabs don't show one), and the ends it shares with its neighbours.
                if (r.Width >= WideTab && OnCloseButton(pt, r)) continue;
                if (pt.x >= r.Left + TabOverlap && pt.x < r.Right - TabOverlap && pt.y >= r.Top && pt.y < r.Bottom) { hit = r; pendingWindow = under; return true; }
            }
        }
        return false;
    }

    void PullTab(RectangleF tab, Point clickAt)
    {
        hasPending = true;
        pendingPt = clickAt;
        pendingTab = tab;
        pendingSince = clock.Elapsed.TotalSeconds;
        travelTo = PointF.Empty;   // so GoToTab does not think it is already there
        GoToTab(new PointF(tab.X + tab.Width / 2, tab.Bottom - 6));
    }

    void ReplayClick() { ReplayClick(pendingPt); }

    void ReplayClick(Point at)
    {
        hasPending = false;
#if DEMO || BLASTDEMO || CLICKTEST
        return;   // test build never clicks anything
#endif
        Point pendingPt = at;
        Point back = Cursor.Position;
        Rectangle b = SystemInformation.VirtualScreen;
        const uint MOVE = 0x1, DOWN = 0x2, UP = 0x4, ABS = 0x8000, VDESK = 0x4000;
        int cx = (int)((pendingPt.X - b.Left) * 65535.0 / Math.Max(1, b.Width - 1));
        int cy = (int)((pendingPt.Y - b.Top) * 65535.0 / Math.Max(1, b.Height - 1));
        mouse_event(MOVE | ABS | VDESK | DOWN, cx, cy, 0, UIntPtr.Zero);
        mouse_event(MOVE | ABS | VDESK | UP, cx, cy, 0, UIntPtr.Zero);
        if (back != pendingPt)
        {
            int bx = (int)((back.X - b.Left) * 65535.0 / Math.Max(1, b.Width - 1));
            int by = (int)((back.Y - b.Top) * 65535.0 / Math.Max(1, b.Height - 1));
            mouse_event(MOVE | ABS | VDESK, bx, by, 0, UIntPtr.Zero);
        }
    }

    void StartTravel(PointF to, Mode m)
    {
        travelFrom = anchor; travelTo = to;
        travelStart = clock.Elapsed.TotalSeconds;
        double dist = Math.Sqrt(Math.Pow(to.X - anchor.X, 2) + Math.Pow(to.Y - anchor.Y, 2));
        if (m == Mode.ToHome) PlanClimbHome(dist);
        else PlanSwings(dist, m == Mode.ToTab);
        lastStep = -1;
        mode = m;
    }

    // Going home after opening a tab there's no hurry, so it climbs back hand over hand: a ladder of
    // holds up to the branch, zig-zagging a little. Each step: both hands hold while the body swings
    // up, then the lower hand lets go and reaches past the other for the next hold, and they swap.
    PointF[] homeHolds = new PointF[0];
    int homeStep;            // the hold the holding hand is on
    double homeStepStart;
    PointF freeFrom;         // where the free hand let go
    PointF freeHandAt;       // where the free hand is now (screen)
    const double HomeStepTime = 0.36, HomeHang = 0.4;   // seconds per step; part of it spent hanging on with both hands

    void PlanClimbHome(double dist)
    {
        int n = (int)Math.Max(2, Math.Min(14, Math.Round(dist / 85)));
        homeHolds = new PointF[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float k = i / (float)n;
            var h = new PointF(travelFrom.X + (travelTo.X - travelFrom.X) * k, travelFrom.Y + (travelTo.Y - travelFrom.Y) * k);
            if (i > 0 && i < n) h.X += (i % 2 == 0 ? -1 : 1) * 14;
            homeHolds[i] = h;
        }
        homeStep = 0;
        homeStepStart = clock.Elapsed.TotalSeconds;
        freeFrom = freeHandAt = anchor;
    }

    // 'hurry' = on the way to a tab you clicked: fewer, quicker swings and no idle pauses.
    void PlanSwings(double dist, bool hurry)
    {
        int n = Math.Max(1, Math.Min(hurry ? 5 : 7, (int)Math.Round(dist / (hurry ? 160 : 115) + rnd.NextDouble() * 1.2 - 0.6)));
        var w = new double[n];
        double sum = 0;
        for (int i = 0; i < n; i++) { w[i] = 0.55 + rnd.NextDouble() * 1.1; sum += w[i]; }
        // Now and then one swing is a big leap.
        if (n >= 3 && rnd.NextDouble() < 0.45) { int big = 1 + rnd.Next(n - 1); sum += w[big]; w[big] *= 2; }

        stepFrac = new double[n]; stepTime = new double[n]; stepHold = new double[n];
        stepLift = new float[n]; stepDip = new float[n]; stepSnap = new bool[n];
        double acc = 0, time = 0;
        for (int i = 0; i < n; i++)
        {
            double share = w[i] / sum;
            acc += share;
            double reachTime = 0.08 + share * 0.6 + rnd.NextDouble() * 0.05;    // longer swings take longer
            double hold = !hurry && rnd.NextDouble() < 0.2 ? 0.14 + rnd.NextDouble() * 0.2   // sometimes hangs and looks around
                                                           : 0.02 + rnd.NextDouble() * 0.05;
            time += reachTime + hold;
            stepFrac[i] = i == n - 1 ? 1 : acc;
            stepTime[i] = time;
            stepHold[i] = hold / (reachTime + hold);
            stepLift[i] = (float)(6 + share * 70 + rnd.NextDouble() * 8);      // big leaps fly higher
            stepDip[i] = i == n - 1 ? 0 : (float)(rnd.NextDouble() * 10 - 4);  // catches at slightly different heights
            stepSnap[i] = rnd.NextDouble() < 0.5;                              // a quick snap or a lazy reach
        }
        // Never keep you waiting long for a tab.
        double limit = hurry ? 0.45 : 0.8;
        if (time > limit)
        {
            for (int i = 0; i < n; i++) stepTime[i] *= limit / time;
            time = limit;
        }
        travelSteps = n;
        travelTime = time;
    }

    void UpdateAnchor(double now)
    {
        if (mode == Mode.ToHome)
        {
            double u = (now - homeStepStart) / HomeStepTime;
            float reach = Smooth((u - HomeHang) / (1 - HomeHang));
            swingReach = reach;
            nextGrip = homeHolds[homeStep + 1];
            // The free hand travels from where it let go to the next hold, swinging out a little on the way.
            freeHandAt = Lerp(freeFrom, nextGrip, reach);
            freeHandAt.X += -mirror * 16 * (float)Math.Sin(Math.PI * reach);
            if (u >= 1)
            {
                // Caught it: that hand now holds, the other one becomes the free hand.
                freeFrom = homeHolds[homeStep];
                homeStep++;
                mirror = -mirror;
                MoveAnchor(homeHolds[homeStep]);   // the body stays put and swings up to the new hold
                vy -= 160;
                vx += Math.Sign(travelTo.X - travelFrom.X) * 40;
                homeStepStart = now;
                if (homeStep == homeHolds.Length - 1) mode = Mode.Home;
            }
        }
        else if (mode == Mode.ToTab)
        {
            // Hand over hand, unevenly: each planned swing reaches, then hangs for its own beat.
            double el = now - travelStart;
            double k = Math.Min(1, el / travelTime);
            int seg = 0;
            while (seg < travelSteps - 1 && el > stepTime[seg]) seg++;
            double t0 = seg == 0 ? 0 : stepTime[seg - 1];
            double f = Math.Min(1, (el - t0) / Math.Max(0.01, stepTime[seg] - t0));
            double reach = Math.Min(1, f / (1 - stepHold[seg]));
            double eased = stepSnap[seg] ? 1 - Math.Pow(1 - reach, 3)          // snappy grab
                                         : reach * reach * (3 - 2 * reach);    // lazy reach
            double f0 = seg == 0 ? 0 : stepFrac[seg - 1];
            double e = k >= 1 ? 1 : f0 + (stepFrac[seg] - f0) * eased;
            float dip0 = seg == 0 ? 0 : stepDip[seg - 1];
            float dip = k >= 1 ? 0 : dip0 + (stepDip[seg] - dip0) * (float)eased;
            float lift = k >= 1 ? 0 : (float)Math.Sin(Math.PI * reach) * stepLift[seg];
            swingReach = (float)reach;
            nextGrip = new PointF(
                (float)(travelFrom.X + (travelTo.X - travelFrom.X) * stepFrac[seg]),
                (float)(travelFrom.Y + (travelTo.Y - travelFrom.Y) * stepFrac[seg]) + stepDip[seg]);
            // Keep the reaching hand on screen; near the top edge a leap goes into the body swing instead.
            float baseY = (float)(travelFrom.Y + (travelTo.Y - travelFrom.Y) * e) + dip;
            lift = Math.Min(lift, Math.Max(0, baseY - (Screen.FromPoint(Point.Round(new PointF(anchor.X, baseY))).Bounds.Top + 8)));
            if (seg != lastStep)
            {
                lastStep = seg;
                mirror = seg % 2 == 0 ? 1 : -1;                                  // swap hands each swing
                double push = 40 + (stepFrac[seg] - f0) * 900 + rnd.NextDouble() * 40;
                vx += Math.Sign(travelTo.X - travelFrom.X) * push;                // throw the body forward
                vy -= stepLift[seg] * 3;
            }
            MoveAnchor(new PointF(
                (float)(travelFrom.X + (travelTo.X - travelFrom.X) * e),
                (float)(travelFrom.Y + (travelTo.Y - travelFrom.Y) * e) - lift + dip));
            if (k >= 1)
            {
                if (mode == Mode.ToTab)
                {
                    mode = Mode.AtTab;
                    tugsLeft = 2; nextTug = now;
                    stayUntil = now + 0.8;
#if HOLDDEMO
                    stayUntil = now + 30;   // test build: keep holding the tab
#endif
                }
                else mode = Mode.Home;
            }
        }
        else if (mode == Mode.AtTab)
        {
            if (tugsLeft > 0 && now >= nextTug && !grabbed)
            {
                vy += 900; vx *= 0.3;           // tug the tab down
                grinTime = 0.8;
                tugsLeft--; nextTug = now + 0.3;
                if (hasPending) StartPullDown();  // ...grab the page and pull it down
            }
            if (now >= stayUntil && !grabbed) StartTravel(home, Mode.ToHome);
        }

        else if (mode == Mode.PullDown)
        {
            double k = Math.Min(1, (now - pullStart) / PullTime);
            double e = k * k * (3 - 2 * k);                       // smooth start and stop
            float edge = (float)(pullFrom + pullDist * e);
            MoveAnchor(new PointF(anchor.X, edge), false);
            if (!grabbed) { x *= Math.Exp(-13 * frameDt); vx *= Math.Exp(-40 * frameDt); }   // hang straight down while pulling
            cover.MoveTo(cover.Left, (int)edge - ShadowH);
            if (k >= 1)
            {
                cover.Clear();
                StartTravel(home, Mode.ToHome);
                if (rnd.NextDouble() < 0.5) Say(Pick("Here you go!", "Ta-da!", "Tab delivered!", "Fresh tab, coming up!"), true);
            }
        }

        if (hasPending && now - pendingSince > 2) ReplayClick();   // never hold a click for long
    }

    // At a tab the monkey hangs by its hand; at the branch it hangs by its tail.
    bool HandPose { get { return mode != Mode.Home; } }

    void StartPullDown()
    {
        Point clickAt = Point.Round(new PointF(pendingTab.X + Math.Min(20, pendingTab.Width / 2), pendingTab.Y + pendingTab.Height / 2 - 2));
        Bitmap shot = null;
        Rectangle area = Rectangle.Empty;
        try
        {
            RECT r;
            GetWindowRect(pendingWindow, out r);
            Rectangle scr = Screen.FromHandle(pendingWindow).Bounds;
            int top = (int)Math.Ceiling(pendingTab.Bottom);
            area = Rectangle.FromLTRB(Math.Max(r.L, scr.Left), top, Math.Min(r.R, scr.Right), Math.Min(r.B, scr.Bottom));
            if (area.Width < 50 || area.Height < 50) throw new Exception("window too small");

            // Snapshot the browser itself (not the screen), so the monkey isn't in the picture.
            using (var full = new Bitmap(r.R - r.L, r.B - r.T))
            {
                using (var g = Graphics.FromImage(full))
                {
                    IntPtr dc = g.GetHdc();
                    bool ok = PrintWindow(pendingWindow, dc, 2 /* PW_RENDERFULLCONTENT */);
                    g.ReleaseHdc(dc);
                    if (!ok) throw new Exception("PrintWindow failed");
                }
                shot = new Bitmap(area.Width, area.Height + ShadowH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(shot))
                {
                    g.Clear(Color.Transparent);
                    using (var shade = new LinearGradientBrush(new Rectangle(0, 0, area.Width, ShadowH + 1),
                        Color.FromArgb(0, 0, 0, 0), Color.FromArgb(110, 0, 0, 0), LinearGradientMode.Vertical))
                        g.FillRectangle(shade, 0, 0, area.Width, ShadowH);
                    g.DrawImage(full, new Rectangle(0, ShadowH, area.Width, area.Height),
                        new Rectangle(area.Left - r.L, area.Top - r.T, area.Width, area.Height), GraphicsUnit.Pixel);
                    using (var edge = new Pen(Color.FromArgb(160, 255, 255, 255), 1f))
                        g.DrawLine(edge, 0, ShadowH, area.Width, ShadowH);
                }
            }
        }
        catch
        {
            if (shot != null) shot.Dispose();
            ReplayClick(clickAt);   // couldn't snapshot: just open the tab
            return;
        }

        // Cover the browser with the old page, switch tabs underneath, then pull it away.
        cover.Display(shot, area.Left, area.Top - ShadowH);
        SetWindowPos(Handle, new IntPtr(-1) /* TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 /* NOSIZE|NOMOVE|NOACTIVATE */);
        ReplayClick(clickAt);

        mode = Mode.PullDown;
        tugsLeft = 0;
        pullStart = clock.Elapsed.TotalSeconds;
        pullFrom = area.Top;
        pullDist = area.Height + 40;
        MoveAnchor(new PointF(anchor.X, area.Top), false);
    }

    void MoveAnchor(PointF a) { MoveAnchor(a, true); }

    void MoveAnchor(PointF a, bool lagBehind)
    {
        // The body keeps its place on screen and swings to catch up.
        if (!grabbed && lagBehind) { x -= (a.X - anchor.X) / S; y -= (a.Y - anchor.Y) / S; }
        anchor = a;
    }

    // ---- Touch / mouse ----

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Ctrl+M anywhere opens a Claude terminal in the Monkey Play folder.
        RegisterHotKey(Handle, TerminalHotkey, 0x2 /* MOD_CONTROL */ | 0x4000 /* MOD_NOREPEAT */, 0x4D /* M */);
        // Told when the screen turns off or on, so it doesn't draw for nobody.
        Guid displayState = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");   // GUID_CONSOLE_DISPLAY_STATE
        displayNotify = RegisterPowerSettingNotification(Handle, ref displayState, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);
        // Whenever another window comes to the front, hop back on top of it (and again a moment later,
        // for apps that make themselves topmost just after they open).
        foregroundProc = (h, ev, hwnd, obj, child, thread, time) => { KeepOnTop(); topAgainAt = clock.Elapsed.TotalSeconds + 0.4; };
        foregroundHook = SetWinEventHook(0x3 /* EVENT_SYSTEM_FOREGROUND */, 0x3, IntPtr.Zero, foregroundProc, 0, 0, 0 /* OUTOFCONTEXT */);
        // Windows waits for this hook on every mouse move in the whole system, so it gets a
        // thread of its own that does nothing else; the animation never slows your cursor down.
        var t = new Thread(() =>
        {
            hookProc = OnMouseHook;   // keep references so they are not garbage collected
            keyProc = OnKeyHook;
            Hook();
            // Hooks are called while this thread waits for messages. In between, time a held M, and put the
            // hooks back every half minute in case Windows dropped them (it never says when it does).
            NativeMessage m;
            while (true)
            {
                MsgWaitForMultipleObjects(0, null, false, 40, 0x04FF /* QS_ALLINPUT */);
                bool quit = false;
                while (PeekMessage(out m, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */)) if (m.msg == 0x12 /* WM_QUIT */) quit = true;
                if (quit) break;
                CheckHoldM();
                if (Environment.TickCount - hookedAt > 30000 && !mDown && !mHeld && !swallowUp
                    && GetAsyncKeyState(0x01) >= 0 && GetAsyncKeyState(0x02) >= 0) Hook();   // not mid-click
            }
        });
        t.IsBackground = true;
        t.Start();

        var waiter = new Thread(() =>
        {
            try
            {
                while (showSignal.WaitOne())
                    if (!IsDisposed) BeginInvoke((Action)ComeOut);
            }
            catch { }   // the app is closing
        });
        waiter.IsBackground = true;
        waiter.Start();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 /* WM_HOTKEY */ && (int)m.WParam == TerminalHotkey) { OpenTerminal(); return; }
        if (m.Msg == 0x0218 /* WM_POWERBROADCAST */ && (int)m.WParam == 0x8013 /* PBT_POWERSETTINGCHANGE */)
        {
            // POWERBROADCAST_SETTING: the setting's GUID, the data's length, then the data (0 off, 1 on, 2 dimmed).
            screenOff = Marshal.ReadInt32(m.LParam, 20) == 0;
            m.Result = new IntPtr(1);
            return;
        }
        base.WndProc(ref m);
    }

    void OpenTerminal()
    {
        string launcher = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\open-monkeyplay.cmd");
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + launcher + "\"") { CreateNoWindow = true, UseShellExecute = false });
            grinTime = 1.0;   // the monkey is happy to see you
            Say(Pick("Let's build something!", "Ooh, coding time!", "What are we making?"), true);
        }
        catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHotKey(Handle, TerminalHotkey);
        if (displayNotify != IntPtr.Zero) UnregisterPowerSettingNotification(displayNotify);
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
        if (link != null) link.Stop();
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        base.OnFormClosed(e);
    }

    void OnDown(object s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { menu.Show(Cursor.Position); return; }
        if (e.Button != MouseButtons.Left) return;
        if (PerchedPose || perch == Perch.Up) LeavePerchNow(clock.Elapsed.TotalSeconds);   // pulled off its seat
        grabbed = true; moved = false;
        downAt = new Point((int)(e.X / S), (int)(e.Y / S)); downTime = clock.Elapsed.TotalSeconds;
        if (sleepW > 0.5f) Say(Pick("Huh? Five more minutes...", "Wha-? I'm awake! I'm awake!"), true);
        else Say(Pick("Whoa!", "Hey!", "Eep!", "Where are we going?"), true);
        // Keep the spot you touched under your finger (no jump).
        grabDx = (e.X / S - PX) - x;
        grabDy = (e.Y / S - PY) - y;
        vx = vy = 0;
    }

    void OnUp(object s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !grabbed) return;
        grabbed = false;
        if (!moved && clock.Elapsed.TotalSeconds - downTime < 0.35)
        {
            // Tap: bump it away from where it was touched.
            if (clock.Elapsed.TotalSeconds - lastTapAt < 0.45) { lastTapAt = -10; OpenChat(); return; }   // double-click: chat
            lastTapAt = clock.Elapsed.TotalSeconds;
            vx += (e.X / S - PX < x ? 1 : -1) * 450;
            vy -= 120;
            grinTime = 1.0;
            Say(Pick("Hehe!", "That tickles!", "Again! Again!", "Boop!"), true);
        }
        else if (Math.Sqrt(vx * vx + vy * vy) > 900) Say(Pick("Wheee!", "Woohoo!", "Yeehaw!"), true);
        vx = Math.Max(-3000, Math.Min(3000, vx));
        vy = Math.Max(-3000, Math.Min(3000, vy));
    }

    // ---- Animation ----

    static float Ease(double rate, double dt) { return (float)(1 - Math.Exp(-rate * dt)); }

    // Runs the animation while the app is otherwise idle, in step with the screen. It only draws as often as
    // it needs to (see FrameInterval), and between frames it sleeps but wakes straight away for clicks,
    // the menu or hold-M.
    double nextFrameAt;
    void RunLoop(object s, EventArgs e)
    {
        NativeMessage m;
        while (!IsDisposed && !PeekMessage(out m, IntPtr.Zero, 0, 0, 0))
        {
            double wait = nextFrameAt - clock.Elapsed.TotalSeconds;
            if (wait > 0.02 || (Resting && wait > 0.001))
            {
                // A while to go: sleep until just before the frame is due (the last bit is lined up with the screen).
                MsgWaitForMultipleObjects(0, null, false, (uint)Math.Max(1, (wait - (Resting ? 0 : 0.015)) * 1000), 0x04FF /* QS_ALLINPUT */);
                continue;
            }
            if (!Resting)
                while (wait > 0.003 && !IsDisposed)   // wait for the screen refresh the frame is due at
                {
                    if (DwmFlush() != 0) Thread.Sleep(8);
                    wait = nextFrameAt - clock.Elapsed.TotalSeconds;
                }
            double start = clock.Elapsed.TotalSeconds;
            Frame();
            nextFrameAt = start + FrameInterval();
        }
    }

    // Seconds until the next frame. Fast moves get 60 frames a second (30 on battery). Calm moments get
    // only as many as the motion needs to look smooth, about 4 pixels per frame: 12 to 20 frames a second
    // (15 on battery), 24 for a habit like eating. Nobody at the PC for a few minutes: 10. Put away, screen
    // off or locked: nothing is drawn at all.
    double FrameInterval()
    {
        if (Resting) return 0.2;
        if (!Calm) return onBattery ? 1 / 30.0 : 1 / 60.0;
        double fps = 24;
        if (act == Act.None && zzz.Count == 0)
        {
            double r = Math.Max(1, Math.Sqrt(x * x + y * y));
            double speed = Math.Max(Math.Sqrt(vx * vx + vy * vy), Math.Abs(bodyAngVel) * (r + 70)) * S;   // canvas pixels a second
            fps = Math.Max(12, Math.Min(20, speed / 4));
        }
        Mood fm = FaceMood;
        if (fm == Mood.Hot || fm == Mood.Cold || fm == Mood.Scared) fps = Math.Max(fps, 20);   // panting, chattering, shivering
        if (onBattery) fps = Math.Min(fps, 15);
        if (userAway) fps = Math.Min(fps, 10);
        return 1 / fps;
    }

    // Nobody can see it: put away, the screen is off, or the PC is locked.
    bool screenOff, locked, userAway;
    IntPtr displayNotify;
    bool Resting { get { return Hidden || screenOff || locked; } }

    void OnSessionSwitch(object s, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
            || e.Reason == Microsoft.Win32.SessionSwitchReason.RemoteDisconnect) locked = true;
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleConnect
            || e.Reason == Microsoft.Win32.SessionSwitchReason.RemoteConnect) locked = false;
    }

    bool onBattery;
    double nextPowerCheck;

    // Nothing fast going on (just a gentle sway, sitting, or a calm habit): 30 frames a second looks the same.
    bool Calm
    {
        get
        {
            return mode == Mode.Home && !grabbed && blastPhase == 0 && danceW < 0.05f
                && act != Act.Climb && act != Act.Pump && act != Act.Throw
                && (perch == Perch.None || perch == Perch.Sit)
                && Math.Abs(vx) + Math.Abs(vy) < 160 && Math.Abs(bodyAngVel) < 1.5 && Math.Abs(flipVel) < 1 && Math.Abs(bendVel) < 20;
        }
    }

    void CheckPower(double now)
    {
        if (now < nextPowerCheck) return;
        nextPowerCheck = now + 1;
        onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        var li = new LASTINPUTINFO { cbSize = 8 };
        userAway = GetLastInputInfo(ref li) && unchecked((uint)Environment.TickCount - li.dwTime) > 3 * 60 * 1000;
    }

    void Frame()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - last, 0.12);   // allows for the slowest frame rate (10 a second), not for long stalls
        last = now;
        if (dt <= 0) return;
        t += dt;
        frameDt = dt;
        UpdateAnchor(now);
        UpdateSleep(now, dt);
        UpdateDance(now, dt);
        UpdateHabits(now, dt);
        UpdatePerch(now);
        UpdateBlast(now);
        CheckChatIdle(now);
        UpdateShades(now);
        UpdateWeather(now);
        UpdateChime(now);
        UpdateChat(now, dt);

        double ropeTarget = HandPose ? ArmLen : L, ropeRate = 7;
        if (act == Act.Climb)
        {
            // Climb up its own tail, hang there a moment, then let go and drop.
            double cp = now - actStart;
            double top = perch == Perch.Up ? 30 : 58;   // climbs right up to the branch when it's going to sit
            if (cp < ClimbTop) ropeTarget = L - (L - top) * cp / ClimbTop;
            else if (cp < ClimbLetGo) ropeTarget = top;
            else ropeRate = 16;
        }
        rope += (ropeTarget - rope) * Ease(ropeRate, dt);

        if (PerchedPose)
        {
            PointF hip = PerchHip();
            x = hip.X; y = hip.Y; vx = vy = 0;
        }
        else if (grabbed)
        {
            Point c = ToLocal(Cursor.Position);
            if (Math.Abs(c.X - downAt.X) + Math.Abs(c.Y - downAt.Y) > 6) moved = true;
            double nx = c.X - PX - grabDx, ny = c.Y - PY - grabDy;
            KeepInside(ref nx, ref ny);
            double k = Ease(25, dt);
            vx += ((nx - x) / dt - vx) * k;   // smoothed, used for the fling
            vy += ((ny - y) / dt - vy) * k;
            x = nx; y = ny;
        }
        else
        {
            int steps = (int)Math.Ceiling(dt / 0.004);
            double h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                double r = Math.Max(1, Math.Sqrt(x * x + y * y));
                double pull = -Stiffness * (r - rope) / r;          // stretchy tail / arm
                double damp = HandPose ? 3.0 : Damping;            // steadier when hanging by a hand
                double sway = SwayForce * (act == Act.Pump ? 1 + 5 * actW : 1) * (1 - 0.75 * sleepW);   // pumping swings higher; asleep, barely
                double fx = pull * x - damp * vx + sway * Math.Sin(2 * Math.PI * t / SwayPeriod);
                double fy = pull * y - damp * vy + Gravity;
                vx += fx * h; vy += fy * h;
                x += vx * h; y += vy * h;
                double nx = x, ny = y;
                KeepInside(ref nx, ref ny);
                if (nx != x) { x = nx; vx = -vx * 0.45; }
                if (ny != y) { y = ny; vy = -vy * 0.45; }
            }
        }

        UpdateBranchBend(dt);
        CheckFullscreen(now);
        CheckPower(now);
        if (hideAt >= 0 && now >= hideAt) { hideAt = -1; hiddenByUser = true; ApplyVisibility(); SaveSettings(); }
        if (now >= nextTopCheck || (topAgainAt >= 0 && now >= topAgainAt))
        {
            KeepOnTop();
            nextTopCheck = now + 2;
            topAgainAt = -1;
        }

        // The body follows the tail's angle with a little weight and wobble, and somersaults when switching
        // between hanging by the tail and by a hand. (Small steps, so the springs stay steady at low frame rates.)
        bool hp = HandPose;
        if (hp != prevHand) { flip += hp ? -Math.PI : Math.PI; prevHand = hp; }
        if (!hp) mirror = 1;
        int springSteps = (int)Math.Ceiling(dt / (1 / 60.0));
        double sh = dt / springSteps;
        double angTarget = Math.Atan2(x, y);
        for (int i = 0; i < springSteps; i++)
        {
            if (!PerchedPose)
            {
                bodyAngVel += (140 * (angTarget - bodyAngle) - 11 * bodyAngVel) * sh;
                bodyAngle += bodyAngVel * sh;
            }
            flipVel += (-110 * flip - 17 * flipVel) * sh;
            flip += flipVel * sh;
        }

        // Head turns a little toward the cursor, and during the look-around habit.
        float turn = Math.Max(-3.5f, Math.Min(3.5f, lookX * 1.3f));
        if (ShownAct == Act.Look)
        {
            double lp = now - actStart;
            turn += (lp < 1.0 ? -5 : lp < 2.1 ? 5 : 0) * actW;
        }
        faceShift += (turn * (1 - sleepW) - faceShift) * Ease(8, dt);

        if (peelFlying)
        {
            peelVel.Y += (float)(Gravity * dt);
            peelPos = new PointF(peelPos.X + peelVel.X * (float)dt, peelPos.Y + peelVel.Y * (float)dt);
            peelAngle += peelSpin * (float)dt;
            peelAge += dt;
            if (peelAge > 1.8) peelFlying = false;
        }

        grinTime = Math.Max(0, grinTime - dt);
        if (blinkTime > 0) blinkTime -= dt;
        else if ((nextBlink -= dt) <= 0) { blinkTime = 0.14; nextBlink = 2.5 + rnd.NextDouble() * 4; }

        if (!Resting) Render();   // nothing to draw while it's put away or nobody can see it
    }

    // Reads how loud the speakers are, about 50 times a second (background thread).
    void ListenToAudio()
    {
#if DANCEDEMO
        // Test build: pretend a song at 120 beats per minute is playing.
        var demo = Stopwatch.StartNew();
        while (true)
        {
            Thread.Sleep(20);
            double bt = demo.Elapsed.TotalSeconds % 0.5;
            audioLevel = (float)(0.15 + 0.6 * Math.Exp(-bt * 14));
        }
#else
        IAudioMeterInformation meter = null;
        var sw = Stopwatch.StartNew();
        double retryAt = 0, refreshAt = 0;
        while (true)
        {
            Thread.Sleep(20);
            try
            {
                // Re-check the default output every few seconds (e.g. when headphones are plugged in).
                if (meter != null && sw.Elapsed.TotalSeconds > refreshAt) meter = null;
                if (meter == null)
                {
                    if (sw.Elapsed.TotalSeconds < retryAt) continue;
                    var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                    IMMDevice dev;
                    Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out dev));
                    Guid iid = typeof(IAudioMeterInformation).GUID;
                    object o;
                    Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o));
                    meter = (IAudioMeterInformation)o;
                    refreshAt = sw.Elapsed.TotalSeconds + 5;
                }
                float pk;
                if (meter.GetPeakValue(out pk) != 0) { meter = null; audioLevel = 0; continue; }
                audioLevel = pk;
            }
            catch { meter = null; audioLevel = 0; retryAt = sw.Elapsed.TotalSeconds + 3; }
        }
#endif
    }

    void UpdateDance(double now, double dt)
    {
        float lv = audioLevel;

        // Music = sound that keeps going for over a second (so a notification beep doesn't count).
        if (lv > 0.015f) soundSecs = Math.Min(3, soundSecs + dt); else soundSecs = Math.Max(0, soundSecs - dt * 1.5);
        if (!musicOn && soundSecs > 1.2)
        {
            musicOn = true;
            if (mode == Mode.Home) Say(Pick("Ooh, I love this song!", "Let's dance!", "Nice beat!", "Turn it up!"));
        }
        else if (musicOn && soundSecs < 0.2) musicOn = false;

        bool dance = musicOn && mode == Mode.Home && !grabbed && sleepW < 0.5f && !PerchedPose;
        danceW += ((dance ? 1 : 0) - danceW) * Ease(dance ? 4 : 3, dt);

        // A beat is a sudden jump in loudness; between clear beats it keeps its own groove.
        slowLevel += (lv - slowLevel) * Ease(1.5, dt);
        bool beat = (lv > 0.04f && lv > slowLevel * 1.3f && now - lastBeat > 0.28) || now - lastBeat > 0.75;
        if (beat)
        {
            beatInterval = Math.Max(0.3, Math.Min(0.75, beatInterval * 0.7 + (now - lastBeat) * 0.3));
            lastBeat = now;
            beatCount++;
            if (danceW > 0.3f)
            {
                double punch = 0.6 + Math.Min(1, lv * 2);
                vy -= 220 * punch * danceW;                                      // bounce on the tail
                vx += (beatCount % 2 == 0 ? 1 : -1) * 100 * danceW;              // sway side to side
                grinTime = Math.Max(grinTime, 0.5);
                if (beatCount % 2 == 0)
                {
                    float side = rnd.Next(2) == 0 ? -1 : 1;
                    float hx = anchor.X + (float)(x + 70 * Math.Sin(bodyAngle)) * S;
                    float hy = anchor.Y + (float)(y + 70 * Math.Cos(bodyAngle)) * S;
                    zzz.Add(new Zz
                    {
                        p = new PointF(hx + side * 26, hy - 8), drift = side * (10 + (float)rnd.NextDouble() * 12),
                        note = true, ch = beatCount % 4 == 0 ? "\u266B" : "\u266A",
                        col = NoteColors[rnd.Next(NoteColors.Length)]
                    });
                }
            }
        }
        beatPhase = Math.Min(1, (now - lastBeat) / beatInterval);
    }

    static bool IsNightNow()
    {
#if SLEEPDEMO
        return true;
#else
        int h = DateTime.Now.Hour;
        return h >= NightStart || h < NightEnd;
#endif
    }

    void UpdateSleep(double now, double dt)
    {
        if (now >= nightCheckAt) { night = IsNightNow(); nightCheckAt = now + 1; }
        if (mode != Mode.Home || grabbed || musicOn) wakeUntil = Math.Max(wakeUntil, now + StayAwake);   // music wakes it too
        bool asleep = night && now > wakeUntil;
        if (asleep && !wasAsleep && mode == Mode.Home) Say(Pick("Good night...", "*yawn* Sleepy time...", "Nap time. Zzz..."), true);
        wasAsleep = asleep;
        // Dozes off slowly, wakes up quickly.
        sleepW += ((asleep ? 1 : 0) - sleepW) * Ease(asleep ? 0.9 : 7, dt);
        breathPhase += dt * (2.4 - 1.2 * sleepW);

        // Little Zs float up beside its head while it sleeps.
        if (sleepW > 0.8f && now >= nextZ)
        {
            float hx = anchor.X + (float)(x + 70 * Math.Sin(bodyAngle)) * S;
            float hy = anchor.Y + (float)(y + 70 * Math.Cos(bodyAngle)) * S;
            zzz.Add(new Zz { p = new PointF(hx + 20, hy - 12), drift = 14 + (float)rnd.NextDouble() * 10 });
            nextZ = now + 1.1 + rnd.NextDouble() * 0.5;
        }
        for (int i = zzz.Count - 1; i >= 0; i--)
        {
            Zz z = zzz[i];
            z.age += dt;
            z.p = new PointF(z.p.X + z.drift * (float)dt + (float)Math.Sin(z.age * 3) * 0.3f, z.p.Y - 30 * (float)dt);
            if (z.age > 2.6) zzz.RemoveAt(i);
        }
    }

    void DrawZzz(Graphics g)
    {
        if (zzz.Count == 0) return;
        Point wp = WindowPos;
        using (var family = new FontFamily("Segoe UI"))
        using (var symbols = new FontFamily("Segoe UI Symbol"))
        using (var fmt = new StringFormat(StringFormat.GenericTypographic))
            foreach (Zz z in zzz)
            {
                float fade = (float)Math.Min(Math.Min(1, z.age / 0.4), (2.6 - z.age) / 0.9);
                if (fade <= 0) continue;
                float size = z.note ? 15 + (float)z.age * 3 : 10 + (float)z.age * 5;
                Color fill = z.note ? z.col : Color.FromArgb(255, 235, 240, 255);
                using (var path = new GraphicsPath())
                {
                    PointF zl = ToLocalF(z.p);
                    path.AddString(z.ch, z.note ? symbols : family, (int)FontStyle.Bold, size, new PointF(zl.X, zl.Y - size), fmt);
                    using (var o = new Pen(Color.FromArgb((int)(200 * fade), 30, 40, 90), 2.4f) { LineJoin = LineJoin.Round }) g.DrawPath(o, path);
                    using (var f = new SolidBrush(Color.FromArgb((int)(255 * fade), fill))) g.FillPath(f, path);
                }
            }
    }

    Act ShownAct { get { return act != Act.None ? act : lastAct; } }

    void StartAct(Act a, double len, double now)
    {
        act = lastAct = a;
        actStart = now;
        actLen = len;
    }

    void UpdateHabits(double now, double dt)
    {
        bool calm = mode == Mode.Home && !grabbed && sleepW < 0.05f && danceW < 0.05f && perch != Perch.Over && perch != Perch.Down;
        if (act != Act.None && (now - actStart > actLen || !calm)) act = Act.None;
        actW += ((act == Act.None ? 0 : 1) - actW) * Ease(7, dt);

        // Health reminders: when it's home, awake and not busy with something else.
        bool free = mode == Mode.Home && !grabbed && sleepW < 0.05f && act == Act.None && !Hidden && (perch == Perch.None || perch == Perch.Sit);   // no nagging during a film
        if (!healthReminders) { nextWater = Math.Max(nextWater, now + waterEvery); nextEyes = Math.Max(nextEyes, now + eyesEvery); }
        else if (free && now >= nextWater)
        {
            nextWater = now + waterEvery;
            if (danceW < 0.05f) StartAct(Act.Drink, DrinkLen, now);
            Say(Pick("Water break! Take a sip.", "Time to drink some water!", "Hydration time! Have a sip."), true, 4.5);
            return;
        }
        else if (free && now >= nextEyes)
        {
            nextEyes = now + eyesEvery;
            if (danceW < 0.05f) StartAct(Act.EyeRest, EyeRestLen, now);
            Say(Pick("Blink your eyes! Rest them a moment.", "Blink blink! Look away from the screen.", "Eye break! Blink a few times."), true, 4);
            return;
        }
        if (!calm || act != Act.None) return;

        // Wave when your cursor comes close to its face.
        Point wpH = WindowPos;
        float hx = wpH.X + headWin.X * S, hy = wpH.Y + headWin.Y * S;   // its head on the screen, hanging or sitting
        Point c = Cursor.Position;
        if (now > waveCooldown && perch != Perch.Up && Math.Abs(c.X - hx) + Math.Abs(c.Y - hy) < 190)
        {
            waveSide = (c.X > hx ? 1 : -1) * (perch == Perch.Sit ? -1 : 1);   // sitting, its body is turned over
            StartAct(Act.Wave, 2.2, now);
            Say(Pick("Hi there!", "Hello!", "Hey you!", "Oh, hi!"));
            grinTime = 2.2;
            waveCooldown = now + 12;
            return;
        }

        // Now and then it goes and sits on the branch for a while (not at night).
        if (perch == Perch.None && now >= nextSit && !night) { StartSit(now); return; }
        if (perch == Perch.Up) return;   // busy climbing up to sit

        // Otherwise, every so often, do something of its own.
        if ((nextAct -= dt) <= 0 && perch == Perch.Sit)
        {
            // Sitting: just the calm habits (no swinging or tail climbing up there).
            switch (rnd.Next(4))
            {
                case 0: StartAct(Act.Eat, EatLen, now); peelTossed = false; Say(Pick("Snack with a view!", "Banana break!")); break;
                case 1: StartAct(Act.Scratch, 2.0, now); break;
                default: StartAct(Act.Look, 3.0, now); break;
            }
            nextAct = 4 + rnd.NextDouble() * 5;
        }
        else if (nextAct <= 0)
        {
#if HABITDEMO
            switch (demoHabit++ % 5)
#else
            switch (rnd.Next(5))
#endif
            {
                case 4: StartAct(Act.Eat, EatLen, now); peelTossed = false; Say(Pick("Snack time!", "Banana break!")); break;
                case 0: StartAct(Act.Scratch, 2.0, now); break;
                case 1: StartAct(Act.Look, 3.0, now); break;
                case 2: StartAct(Act.Pump, 4.5, now); break;
                default: StartAct(Act.Climb, 2.6, now); break;
            }
            nextAct = 4 + rnd.NextDouble() * 5;
#if HABITDEMO
            nextAct = 0.3;
#endif
        }
    }

    // The monkey is drawn in its own units (window coordinates below are in these), scaled by S to the
    // screen it lives on: 1 at 100% scaling, 1.25 at 125%, and so on. Screen positions are real pixels.
    float S = 1;
    Matrix baseM = new Matrix();
    string homeScreen = "";   // device name of the screen it lives on ("" = the main screen)

    Point WindowPos { get { return new Point((int)Math.Round(anchor.X - PX * S), (int)Math.Round(anchor.Y - PY * S)); } }

    Point ToLocal(Point screen) { Point w = WindowPos; return new Point((int)Math.Round((screen.X - w.X) / S), (int)Math.Round((screen.Y - w.Y) / S)); }
    PointF ToLocalF(PointF screen) { Point w = WindowPos; return new PointF((screen.X - w.X) / S, (screen.Y - w.Y) / S); }

    Screen HomeScreen()
    {
        foreach (var sc in Screen.AllScreens) if (sc.DeviceName == homeScreen) return sc;
        return Screen.PrimaryScreen;   // not chosen, or that screen is unplugged
    }

    static float ScaleAt(Point pt)
    {
        try
        {
            uint dx, dy;
            IntPtr mon = MonitorFromPoint(new PT(pt.X, pt.Y), 2 /* NEAREST */);
            if (GetDpiForMonitor(mon, 0 /* EFFECTIVE */, out dx, out dy) == 0 && dx > 0) return dx / 96f;
        }
        catch { }
        return 1;
    }

    // Put the branch near the top-right of its screen (clear of the window buttons), at that screen's scale.
    void PlaceOnScreen()
    {
        Rectangle wa = HomeScreen().WorkingArea;
        S = ScaleAt(new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2));
        baseM.Dispose();
        baseM = new Matrix();
        baseM.Scale(S, S);
        canvas = null;   // redrawn at the new size
        PointF was = home;
        home = new PointF(wa.Right - 230 * S, wa.Top + PY * S);
        if (mode == Mode.Home || was == PointF.Empty) anchor = home;   // away on a tab trip, it climbs back to the new spot
        Bounds = new Rectangle(WindowPos, new Size((int)Math.Ceiling(W * S), (int)Math.Ceiling(H * S)));
    }

    void FillScreenMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        Screen current = HomeScreen();
        int n = 1;
        foreach (var sc in Screen.AllScreens)
        {
            var it = new ToolStripMenuItem("Screen " + n++ + (sc.Primary ? " (main)" : "") + "  " + sc.Bounds.Width + "\u00d7" + sc.Bounds.Height)
                { Checked = sc.DeviceName == current.DeviceName };
            Screen pick = sc;
            it.Click += (s, e) => { homeScreen = pick.Primary ? "" : pick.DeviceName; SaveSettings(); PlaceOnScreen(); };
            parent.DropDownItems.Add(it);
        }
    }

    void KeepInside(ref double nx, ref double ny)
    {
        nx = Math.Max(-PX + 60, Math.Min(PX - 60, nx));
        ny = Math.Max(30, Math.Min(H - PY - BodyReach - 4, ny));
        // Hanging by the tail, the head is about 70px further out along the tail than the hips,
        // so on a wide swing it would poke out of the window and get cut off. Keep it (and its ears) in.
        double r = Math.Sqrt(nx * nx + ny * ny);
        if (!HandPose && r > 1)
        {
            double reach = 1 + 70 / r, room = PX - 46;
            if (Math.Abs(nx * reach) > room) nx = Math.Sign(nx) * room / reach;
        }
    }

    // ---- Drawing ----

    static readonly Color Outline = Color.FromArgb(255, 58, 31, 12);
    static readonly Color Jacket = Color.FromArgb(255, 38, 104, 106);      // teal puffer
    static readonly Color Sherpa = Color.FromArgb(255, 241, 233, 214);     // fluffy cream collar and cuffs
    static readonly Color Silver = Color.FromArgb(255, 205, 208, 215);      // silver glasses frame
    static readonly Color Mouth = Color.FromArgb(255, 105, 30, 28);
    static readonly Color Tongue = Color.FromArgb(255, 236, 110, 120);
    const float OL = 1.7f;   // outline thickness

    void Render()
    {
#if PROFILE
        double profStart = clock.Elapsed.TotalMilliseconds;
#endif
        if (canvas == null) canvas = new Canvas((int)Math.Ceiling(W * S), (int)Math.Ceiling(H * S));
        Graphics g = canvas.G;
        g.Transform = baseM;   // back to window coordinates, at the screen's scale
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.Bilinear;

        float hx = PX + (float)x, hy = PY + (float)y + (PerchedPose ? wrapDy : 0);   // sitting, it rides the branch
        double r = Math.Max(1, Math.Sqrt(x * x + y * y));
        float dx = (float)(x / r), dy = (float)(y / r);

#if PROFILE
        double q0 = clock.Elapsed.TotalMilliseconds;
#endif
        if (PerchedPose) DrawSitTail(g, hx, hy);              // hanging down behind the branch
        else if (!HandPose) TailCoil(g, false, TailWidth(r));   // the turns behind the branch
#if PROFILE
        profCoil += clock.Elapsed.TotalMilliseconds - q0;
#endif
        DrawBranch(g);
#if PROFILE
        profBranch += clock.Elapsed.TotalMilliseconds - q0;
#endif
        chewing = biteOpen = false;
        if (peelFlying) DrawFlyingPeel(g);
        bool hand = HandPose;
        if (hand) DrawArm(g, hx, hy); else if (!PerchedPose) DrawTail(g, hx, hy, dx, dy, r);
        float headX = (hand ? -20 : 0) * mirror, headY = hand ? 2 : 70;

        double tilt = PerchedPose ? -Math.PI * perchK : bodyAngle + flip;   // swinging up over the branch turns it head-up
        g.TranslateTransform(hx, hy);
        g.RotateTransform((float)(-tilt * 180 / Math.PI));

        // Eyes glide toward the cursor (in the monkey's own tilted coordinates).
        Point cur = ToLocal(Cursor.Position);
        double lx = cur.X - hx, ly = cur.Y - hy;
        double ca = Math.Cos(tilt), sa = Math.Sin(tilt);
        double ex = lx * ca - ly * sa - headX, ey = lx * sa + ly * ca - headY;
        if (PerchedPose) { ex = cur.X - headWin.X; ey = cur.Y - headWin.Y; }   // its head stays upright up there
        double el = Math.Max(1, Math.Sqrt(ex * ex + ey * ey));
        float k = Ease(14, frameDt);
        lookX += ((float)(ex / el * Math.Min(2.6, el / 30)) * mirror - lookX) * k;
        lookY += ((float)(ey / el * Math.Min(2.6, el / 30)) - lookY) * k;

        // Arms trail behind when it swings.
        float lagTarget = (float)Math.Max(-14, Math.Min(14, -(vx * ca - vy * sa) * 0.02)) * mirror;
        lag += (lagTarget - lag) * Ease(10, frameDt);

        // A little squash and stretch along the tail when it moves fast.
        float speed = (float)Math.Sqrt(vx * vx + vy * vy);
        float stretch = Math.Min(0.07f, speed / 14000f);
        var headPt = new[] { new PointF(headX, headY) };
        g.Transform.TransformPoints(headPt);
        headWin = new PointF(headPt[0].X / S, headPt[0].Y / S);   // in its own units
        g.ScaleTransform((hand ? mirror : 1) * (1 - stretch * 0.6f), 1 + stretch);

#if PROFILE
        double q1 = clock.Elapsed.TotalMilliseconds;
#endif
        if (hand)
        {
            // Where should the free hand be? (in window coordinates first)
            float want = 0;
            PointF target = PointF.Empty;
            float side = -mirror;   // the free shoulder's side, on screen
            if (mode == Mode.AtTab || mode == Mode.PullDown)
            {
                target = new PointF((PX + side * 11) * S, (PY + 1) * S);   // both hands on the tab (canvas pixels, like the others)
                want = 1;
            }
            else if (mode == Mode.ToHome)
            {
                Point wp = WindowPos;
                target = new PointF(freeHandAt.X - wp.X, freeHandAt.Y - wp.Y);   // letting go, reaching up, holding on
                want = 1;
            }
            else if (mode == Mode.ToTab)
            {
                Point wp = WindowPos;
                target = new PointF(nextGrip.X - wp.X, nextGrip.Y - wp.Y);   // reach for the next grip
                // Reach out during the second half of each swing, then that hand takes over.
                want = Math.Max(0, Math.Min(1, (swingReach - 0.25f) / 0.5f));
                if (Math.Abs(nextGrip.X - anchor.X) < 4 && Math.Abs(nextGrip.Y - anchor.Y) < 4) want = 0;
            }
            freeW += (want - freeW) * Ease(want > freeW ? 14 : 9, frameDt);
            if (freeW > 0.01f)
            {
                var inv = g.Transform.Clone();
                inv.Invert();
                var pts = new[] { target };
                inv.TransformPoints(pts);
                // Arms only stretch so far.
                PointF sh = new PointF(-27, 24);
                float ddx = pts[0].X - sh.X, ddy = pts[0].Y - sh.Y, len = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                float maxReach = mode == Mode.AtTab || mode == Mode.PullDown ? 140 : mode == Mode.ToHome ? 125 : 80;   // stretches to hold on with both hands
                if (len > maxReach) { ddx *= maxReach / len; ddy *= maxReach / len; }
                freeLocal = new PointF(sh.X + ddx, sh.Y + ddy);
                inv.Dispose();
            }
            DrawBodyByHand(g);
        }
        else DrawBody(g);
        g.Transform = baseM;   // back to window coordinates, at the screen's scale
        DrawZzz(g);
        DrawBubble(g);
#if PROFILE
        profBody += clock.Elapsed.TotalMilliseconds - q1;
#endif

#if PROFILE
        profDraw += clock.Elapsed.TotalMilliseconds - profStart;
        double pushStart = clock.Elapsed.TotalMilliseconds;
#endif
        canvas.Push(Handle, WindowPos);
#if PROFILE
        profPush += clock.Elapsed.TotalMilliseconds - pushStart;
        if (++profFrames == 300)   // every 300 frames, the averages for those frames
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "monkey_prof.txt"),
                string.Format("fps {12:F1} | pocket {11:F2} | sprites {9:F2} ms x{10} | draw {0:F2} ms (branch {2:F2} [coil {6:F2}, leaves {7:F2}, blit {8:F2}], body {3:F2}, head {4:F2}, jacket {5:F2}), push {1:F2} ms per frame",
                    profDraw / 300, profPush / 300, profBranch / 300, profBody / 300, profHead / 300, profJacket / 300,
                    profCoil / 300, profCompose / 300, profBlit / 300, profSprite / 300, profSprites / 300, profPocket / 300, 300 / (clock.Elapsed.TotalSeconds - profSince)));
            profSince = clock.Elapsed.TotalSeconds;
            profSprite = 0; profPocket = 0; profSprites = 0;
            profFrames = 0;
            profDraw = profPush = profBranch = profBody = profHead = profJacket = profCoil = profCompose = profBlit = 0;
        }
#endif
    }

    // ---- Drawing helpers ----

    static Color Mix(Color a, Color b, float k)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * k), (int)(a.R + (b.R - a.R) * k),
            (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    }

    static Color Light(Color c, float k) { return Mix(c, Color.FromArgb(c.A, 255, 255, 255), k); }
    static Color Dark(Color c, float k) { return Mix(c, Color.FromArgb(c.A, 0, 0, 0), k); }

    // A rounded, softly shaded shape with a cartoon outline.
    static void Blob(Graphics g, float x, float y, float w, float h, Color c)
    {
        using (var o = new SolidBrush(Outline)) g.FillEllipse(o, x - OL, y - OL, w + 2 * OL, h + 2 * OL);
        Shade(g, x, y, w, h, c);
    }

    static void Shade(Graphics g, float x, float y, float w, float h, Color c)
    {
        // Lit from the top-left.
        using (var b = new LinearGradientBrush(new RectangleF(x - 1, y - 1, w + 2, h + 2), Light(c, 0.2f), Dark(c, 0.14f), 60f))
            g.FillEllipse(b, x, y, w, h);
    }

    static Pen RoundPen(Color c, float w)
    {
        return new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    }

    static void Limb(Graphics g, float w, params PointF[] pts)
    {
        using (var o = RoundPen(Outline, w + 2 * OL)) g.DrawLines(o, pts);
        using (var f = RoundPen(Fur, w)) g.DrawLines(f, pts);
        using (var hl = RoundPen(Color.FromArgb(50, 255, 220, 180), w * 0.3f))
            g.DrawLines(hl, new[] { pts[0], pts[pts.Length / 2] });
    }

    static void LimbCurve(Graphics g, float w, PointF a, PointF b, PointF c, PointF d)
    {
        using (var o = RoundPen(Outline, w + 2 * OL)) g.DrawBezier(o, a, b, c, d);
        using (var f = RoundPen(Fur, w)) g.DrawBezier(f, a, b, c, d);
    }

    // First part (0..t) of a cubic Bezier.
    static PointF[] SplitBezier(PointF a, PointF b, PointF c, PointF d, float t)
    {
        Func<PointF, PointF, PointF> L = (p, q) => new PointF(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
        PointF ab = L(a, b), bc = L(b, c), cd = L(c, d), abc = L(ab, bc), bcd = L(bc, cd), m = L(abc, bcd);
        return new[] { a, ab, abc, m };
    }

    // An arm in a puffer sleeve: fur forearm, padded sleeve, fluffy cuff.
    static void SleevedArm(Graphics g, PointF a, PointF b, PointF c, PointF d)
    {
        LimbCurve(g, 8, a, b, c, d);
        PointF[] sl = SplitBezier(a, b, c, d, 0.72f);
        using (var o = RoundPen(Outline, 12 + 2 * OL)) g.DrawBezier(o, sl[0], sl[1], sl[2], sl[3]);
        using (var f = RoundPen(Jacket, 12)) g.DrawBezier(f, sl[0], sl[1], sl[2], sl[3]);
        using (var hl = RoundPen(Color.FromArgb(70, 255, 255, 255), 3)) g.DrawBezier(hl, sl[0], sl[1], sl[2], sl[3]);
        Puff(g, sl[3].X, sl[3].Y, 7.5f);
    }

    static void SleevedLine(Graphics g, PointF hand, PointF shoulder)
    {
        PointF cuff = new PointF(shoulder.X + (hand.X - shoulder.X) * 0.65f, shoulder.Y + (hand.Y - shoulder.Y) * 0.65f);
        using (var o = RoundPen(Outline, 8 + 2 * OL)) g.DrawLine(o, shoulder, hand);
        using (var f = RoundPen(Fur, 8)) g.DrawLine(f, shoulder, hand);
        using (var o = RoundPen(Outline, 12 + 2 * OL)) g.DrawLine(o, shoulder, cuff);
        using (var f = RoundPen(Jacket, 12)) g.DrawLine(f, shoulder, cuff);
        using (var hl = RoundPen(Color.FromArgb(70, 255, 255, 255), 3)) g.DrawLine(hl, shoulder, cuff);
        Puff(g, cuff.X, cuff.Y, 7.5f);
    }

    // Small pictures that never change are drawn once and reused every frame.
    static readonly Dictionary<string, Bitmap> sprites = new Dictionary<string, Bitmap>();

    static Bitmap Sprite(string key, int w, int h, float ox, float oy, Action<Graphics> draw)
    {
        Bitmap b;
        if (sprites.TryGetValue(key, out b)) return b;
        b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(ox, oy);
            draw(g);
        }
        sprites[key] = b;
        return b;
    }

    // A fluffy sherpa tuft. Its shape is built once per size and filled in two goes (turned cached
    // pictures are slow to draw in GDI+, and so are lots of little ellipses).
    static readonly Dictionary<float, GraphicsPath[]> puffPaths = new Dictionary<float, GraphicsPath[]>();
    static readonly SolidBrush PuffEdge = new SolidBrush(Color.FromArgb(255, 170, 155, 125));
    static SolidBrush puffFill;

    static void Puff(Graphics g, float cx, float cy, float r)
    {
#if PROFILE
        double qs = profClock.Elapsed.TotalMilliseconds;
#endif
        GraphicsPath[] pp;
        if (!puffPaths.TryGetValue(r, out pp))
        {
            var edge = new GraphicsPath(FillMode.Winding);
            var fill = new GraphicsPath(FillMode.Winding);
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                float ox = (float)Math.Cos(a) * r * 0.55f - r * 0.55f, oy = (float)Math.Sin(a) * r * 0.55f - r * 0.55f;
                edge.AddEllipse(ox - 0.8f, oy - 0.8f, r * 1.1f + 1.6f, r * 1.1f + 1.6f);
                fill.AddEllipse(ox, oy, r * 1.1f, r * 1.1f);
            }
            fill.AddEllipse(-r * 0.6f, -r * 0.6f, r * 1.2f, r * 1.2f);
            puffPaths[r] = pp = new[] { edge, fill };
        }
        if (puffFill == null) puffFill = new SolidBrush(Sherpa);
        var st = g.Save();
        g.TranslateTransform(cx, cy);
        g.FillPath(PuffEdge, pp[0]);
        g.FillPath(puffFill, pp[1]);
        g.Restore(st);
#if PROFILE
        profSprite += profClock.Elapsed.TotalMilliseconds - qs; profSprites++;
#endif
    }

    static void DrawPuff(Graphics g, float cx, float cy, float r)
    {
        using (var o = new SolidBrush(Color.FromArgb(255, 170, 155, 125)))
        using (var f = new SolidBrush(Sherpa))
        {
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                g.FillEllipse(o, cx + (float)Math.Cos(a) * r * 0.55f - r * 0.55f - 0.8f, cy + (float)Math.Sin(a) * r * 0.55f - r * 0.55f - 0.8f, r * 1.1f + 1.6f, r * 1.1f + 1.6f);
            }
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                g.FillEllipse(f, cx + (float)Math.Cos(a) * r * 0.55f - r * 0.55f, cy + (float)Math.Sin(a) * r * 0.55f - r * 0.55f, r * 1.1f, r * 1.1f);
            }
            g.FillEllipse(f, cx - r * 0.6f, cy - r * 0.6f, r * 1.2f, r * 1.2f);
        }
    }

    // Puffer jacket over the torso, open at the front. 'neckY' is where the collar sits.
    static void JacketTorso(Graphics g, float x, float y, float w, float h, float neckY, bool headDown)
    {
        Blob(g, x, y, w, h, Jacket);
        var clipBefore = g.Clip;
        using (var body = new GraphicsPath())
        {
            body.AddEllipse(x, y, w, h);
            g.SetClip(body, CombineMode.Intersect);
            // Quilted puffer seams.
            using (var seam = new Pen(Dark(Jacket, 0.35f), 1.4f))
            using (var shine = new Pen(Color.FromArgb(55, 255, 255, 255), 2f))
                for (float sy = y + 8; sy < y + h; sy += 10)
                {
                    g.DrawArc(seam, x - 4, sy - 4, w + 8, 10, 0, 180);
                    g.DrawArc(shine, x - 4, sy - 7, w + 8, 10, 20, 140);
                }
            // Open front showing the chest fur, and the zipper edges.
            float cx = x + w / 2;
            using (var chest = new SolidBrush(Light(Fur, 0.45f))) g.FillEllipse(chest, cx - 6, y - 4, 12, h + 8);
            using (var zip = new Pen(Color.FromArgb(255, 190, 190, 195), 1.3f))
            {
                g.DrawLine(zip, cx - 6, y, cx - 6, y + h);
                g.DrawLine(zip, cx + 6, y, cx + 6, y + h);
            }
        }
        g.Clip = clipBefore;

        // Sherpa collar around the neck.
        for (int i = -3; i <= 3; i++)
        {
            float px = x + w / 2 + i * (w / 7.5f);
            float py = neckY + (headDown ? -1 : 1) * Math.Abs(i) * 1.6f;
            Puff(g, px, py, 6.5f);
        }
    }

    // Cool aviator shades with an iridescent green-to-purple tint.
    // While taking them off or putting them on, they're lifted onto its forehead for a moment.
    bool SwappingShades
    {
        get
        {
            double ap = clock.Elapsed.TotalSeconds - actStart;
            return ShownAct == Act.Shades && actW > 0.3f && ap >= ShadesLift && ap < ShadesSwap + 0.05;
        }
    }
    // Taken off, they stay pushed up on its head.
    bool ShadesShown { get { return true; } }
    bool ShadesLifted { get { return !shadesOn || grabbed || sleepW >= 0.5f || EyeRest || SwappingShades; } }
    bool ShadesOnFace { get { return ShadesShown && !ShadesLifted; } }

    void Shades(Graphics g, float cx, float ey, bool pushedUp)
    {
        float y = pushedUp ? ey - 13 : ey;
        float glint = lookX * 0.8f;
        using (var temple = RoundPen(Silver, 1.6f))
        {
            g.DrawLine(temple, cx - 17, y - 3, cx - 24, y - 6);
            g.DrawLine(temple, cx + 17, y - 3, cx + 24, y - 6);
        }
        foreach (int side in new[] { -1, 1 })
        {
            float lx = cx + side * 9;
            var st = g.Save();
            g.TranslateTransform(lx, y);
            g.ScaleTransform(1.18f, 1.18f);     // nice big lenses
            g.TranslateTransform(-lx, -y);
            using (var lens = new GraphicsPath())
            {
                // Teardrop aviator lens: flat-ish top, rounder and lower toward the outside.
                lens.AddBezier(lx - side * 7.5f, y - 5, lx - side * 2, y - 6.5f, lx + side * 4, y - 6.5f, lx + side * 8, y - 4.5f);
                lens.AddBezier(lx + side * 8, y - 4.5f, lx + side * 9.5f, y + 2, lx + side * 6, y + 7.5f, lx + side * 1, y + 7);
                lens.AddBezier(lx + side * 1, y + 7, lx - side * 5, y + 6.5f, lx - side * 8, y + 1, lx - side * 7.5f, y - 5);
                lens.CloseFigure();
                using (var tint = new LinearGradientBrush(new RectangleF(lx - 9, y - 7, 18, 15),
                    Color.FromArgb(252, 20, 175, 105), Color.FromArgb(252, 125, 40, 195), 35f))
                    g.FillPath(tint, lens);
                var clipBefore = g.Clip;
                g.SetClip(lens, CombineMode.Intersect);
                using (var shine = new SolidBrush(Color.FromArgb(125, 255, 255, 255)))
                {
                    g.FillPolygon(shine, new[] { new PointF(lx - 6 + glint, y - 7), new PointF(lx - 2 + glint, y - 7),
                        new PointF(lx - 7 + glint, y + 8), new PointF(lx - 10 + glint, y + 8) });
                    g.FillEllipse(shine, lx + 2 + glint * 0.5f, y - 4.5f, 3, 2.2f);
                }
                g.Clip = clipBefore;
                using (var o = new Pen(Outline, 2.6f) { LineJoin = LineJoin.Round }) g.DrawPath(o, lens);
                using (var f = new Pen(Silver, 1.4f) { LineJoin = LineJoin.Round }) g.DrawPath(f, lens);
            }
            g.Restore(st);
        }
        using (var o = RoundPen(Outline, 2.8f)) g.DrawArc(o, cx - 3, y - 7, 6, 4, 180, 180);
        using (var f = RoundPen(Silver, 1.5f)) g.DrawArc(f, cx - 3, y - 7, 6, 4, 180, 180);
    }

    static void HandShape(Graphics g, float cx, float cy, float size)
    {
        Blob(g, cx - size / 2, cy - size / 2, size, size, Skin);
        using (var p = RoundPen(Color.FromArgb(150, 170, 115, 70), 1.1f))
        {
            g.DrawLine(p, cx - size * 0.18f, cy + size * 0.05f, cx - size * 0.18f, cy + size * 0.38f);
            g.DrawLine(p, cx + size * 0.12f, cy + size * 0.05f, cx + size * 0.12f, cy + size * 0.38f);
        }
    }

    static void Foot(Graphics g, float x, float y, float w, float h)
    {
        Blob(g, x, y, w, h, Skin);
        using (var p = RoundPen(Color.FromArgb(150, 170, 115, 70), 1.1f))
        {
            g.DrawLine(p, x + w * 0.35f, y + h * 0.2f, x + w * 0.35f, y + h * 0.55f);
            g.DrawLine(p, x + w * 0.62f, y + h * 0.2f, x + w * 0.62f, y + h * 0.55f);
        }
    }

    // ---- Scenery ----

    Bitmap branchCache, woodCache;
    FxLayer branchStay = new FxLayer();   // the branch on its own while the monkey is off at a tab
    Point branchStayAt;
    float composedBend = float.NaN;   // the bend the branch picture was last drawn with
    int branchFrame;
    const int BranchW = 250, BranchH = 80 + (int)PY, BranchOX = 125, BranchOY = 24 + (int)PY;

    // Sitting on the branch now and then: it climbs its tail, swings up over the top of the branch,
    // sits a while (legs dangling, hands on the wood), then swings back down and hangs again.
    enum Perch { None, Up, Over, Sit, Down }
    Perch perch;
    double perchStart, sitUntil, nextSit = 40;
    float perchK;            // 0 hanging .. 1 sitting: drives the swing over the top
    PointF perchFrom;        // hips (relative to the branch) when the swing started
    const double OverTime = 0.45;
    bool PerchedPose { get { return perch == Perch.Over || perch == Perch.Sit || perch == Perch.Down; } }

    void StartSit(double now)
    {
        perch = Perch.Up;
        perchStart = now;
        StartAct(Act.Climb, ClimbTop + 0.3, now);   // first, hand over hand up the tail
    }

    void UpdatePerch(double now)
    {
        if (perch == Perch.None) return;
        if (mode != Mode.Home && PerchedPose) { LeavePerchNow(now); return; }   // off to a tab
        bool leave = mode != Mode.Home || grabbed || musicOn || sleepW > 0.3f;
        double el = now - perchStart;
        switch (perch)
        {
            case Perch.Up:
                if (leave || act != Act.Climb) { perch = Perch.None; break; }
                if (el >= ClimbTop)
                {
                    perch = Perch.Over; perchStart = now;
                    perchFrom = new PointF((float)x, (float)y);
                    act = Act.None;
                }
                break;
            case Perch.Over:
                perchK = Smooth(el / OverTime);
                if (el >= OverTime)
                {
                    perch = Perch.Sit; perchStart = now; perchK = 1;
                    sitUntil = now + 15 + rnd.NextDouble() * 20;
#if SITDEMO
                    sitUntil = now + 4;
#endif
#if SITACTTEST
                    sitUntil = now + 40;
#endif
                    Say(Pick("Nice view from up here.", "Ahh, a seat!", "Time for a little sit.", "Comfy!"));
                }
                break;
            case Perch.Sit:
                if (leave || (now >= sitUntil && act == Act.None)) { perch = Perch.Down; perchStart = now; perchFrom = new PointF(0, 36); }
                break;
            case Perch.Down:
                perchK = 1 - Smooth(el / OverTime);
                if (el >= OverTime) { LeavePerchNow(now); vy = 120; }   // drops into a hang and bounces
                break;
        }
    }

    // Back to hanging by the tail right under the branch.
    void LeavePerchNow(double now)
    {
        perch = Perch.None; perchK = 0;
        x = 0; y = 36; vx = vy = 0;
        bodyAngle = bodyAngVel = 0;
        nextSit = now + 60 + rnd.NextDouble() * 90;
    }

    // Where the hips are (relative to the branch) while swinging up over it or sitting on it.
    PointF PerchHip()
    {
        double ang = Math.PI * perchK;
        float r0 = (float)Math.Sqrt(perchFrom.X * perchFrom.X + perchFrom.Y * perchFrom.Y);
        float r = r0 + (6 - r0) * perchK;
        return new PointF(perchFrom.X * (1 - perchK) + 14 * (float)Math.Sin(ang), r * (float)Math.Cos(ang));
    }

    // Sitting, the tail hangs down behind the branch, swaying, with a curl at the end.
    void DrawSitTail(Graphics g, float hx, float hy)
    {
        float sw = (float)Math.Sin(t * 1.5) * 6;
        var p0 = new PointF(hx - 3, hy + 4);
        var p1 = new PointF(hx - 10, hy + 40);
        var p2 = new PointF(hx + 18 + sw, hy + 58);
        var p3 = new PointF(hx + 8 + sw, hy + 80);
        using (var o = RoundPen(Outline, 6f + 2 * OL)) { g.DrawBezier(o, p0, p1, p2, p3); g.DrawArc(o, p3.X - 12, p3.Y - 6, 12, 12, 0, 230); }
        using (var f = RoundPen(Fur, 6f)) { g.DrawBezier(f, p0, p1, p2, p3); g.DrawArc(f, p3.X - 12, p3.Y - 6, 12, 12, 0, 230); }
    }

    // The branch gives a little under the monkey: a hard tug on the tail (a fling, a drop, a big swing)
    // bends it down and it springs back with a wobble. bendPx is how far the wrap point has moved down.
    double bendPx, bendVel;
    static float wrapDy;   // the tail's coil follows the wood
    const float PivotX = 99, PivotY = -24;   // where the branch comes in from the tree (relative to the wrap)

    void UpdateBranchBend(double dt)
    {
        double target = 0;
        if (PerchedPose) target = 2.5 * perchK;   // its weight on the wood
        else if (mode == Mode.Home)
        {
            // How much harder than usual the tail is pulling (at rest it stretches Gravity / Stiffness).
            double stretch = Math.Sqrt(x * x + y * y) - rope - Gravity / Stiffness;
            target = Math.Max(-1, Math.Min(4, stretch * 0.05));
        }
        int steps = (int)Math.Ceiling(dt / 0.004);
        double h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            bendVel += (-320 * (bendPx - target) - 7 * bendVel) * h;   // stiff, springy wood
            bendPx += bendVel * h;
        }
        wrapDy = (float)bendPx;
    }

    void DrawBranch(Graphics g)
    {
        // The leaves sway slowly, so the branch only needs redrawing every few frames.
        // The wood never changes, so it's painted once; only the leaves (behind and in front of it) are redone.
        // Both pictures are made at the screen's own scale, so each frame is a plain 1:1 copy (stretching a
        // picture while copying it is very slow in GDI+, and blurry too).
        int pw = (int)Math.Ceiling(BranchW * S), ph = (int)Math.Ceiling(BranchH * S);
        if (woodCache == null || woodCache.Width != pw || woodCache.Height != ph)
        {
            if (woodCache != null) woodCache.Dispose();
            if (branchCache != null) branchCache.Dispose();
            branchCache = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
            composedBend = float.NaN;
            woodCache = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
            using (var wg = Graphics.FromImage(woodCache))
            {
                wg.SmoothingMode = SmoothingMode.AntiAlias;
                wg.ScaleTransform(S, S);
                PaintBranch(wg, BranchOX, BranchOY, 1);
            }
        }
#if PROFILE
        double qc = clock.Elapsed.TotalMilliseconds;
#endif
        // Redone every few frames for the swaying leaves, or as soon as the bend visibly changes. Turning the
        // picture is slow, so it's turned here (already bent) and simply copied on the other frames.
        bool recomposed = branchFrame++ % 6 == 0 || !(Math.Abs(bendPx - composedBend) <= 0.15);
        if (recomposed)
            using (var bg = Graphics.FromImage(branchCache))
            {
                bg.SmoothingMode = SmoothingMode.AntiAlias;
                bg.Clear(Color.Transparent);
                bg.ScaleTransform(S, S);
                if (Math.Abs(bendPx) > 0.05)   // bend by turning it a touch around where it joins the tree
                {
                    float px = BranchOX + PivotX, py = BranchOY + PivotY;
                    bg.TranslateTransform(px, py);
                    bg.RotateTransform((float)(Math.Atan2(-bendPx, PivotX) * 180 / Math.PI));
                    bg.TranslateTransform(-px, -py);
                }
                PaintBranch(bg, BranchOX, BranchOY, 0);
                var wst = bg.Save();
                bg.ResetTransform();
                bg.DrawImage(woodCache, new Rectangle(0, 0, pw, ph));
                bg.Restore(wst);
                PaintBranch(bg, BranchOX, BranchOY, 2);
                composedBend = (float)bendPx;
            }

#if PROFILE
        profCompose += clock.Elapsed.TotalMilliseconds - qc;
        double qb = clock.Elapsed.TotalMilliseconds;
#endif
        // Away at a tab, the monkey's window travels with it, so the branch stays at home in a window of its own.
        if (mode != Mode.Home)
        {
            var at = new Point((int)Math.Round(home.X - BranchOX * S), (int)Math.Round(home.Y - BranchOY * S));
            if (recomposed || at != branchStayAt || !branchStay.Visible)
            {
                branchStayAt = at;
                branchStay.Bounds = new Rectangle(at, new Size(pw, ph));
                Layer(branchStay.Handle, branchCache, at);
                if (!branchStay.Visible) { branchStay.Show(); KeepOnTop(); }   // behind the monkey
            }
            return;
        }
        if (branchStay.Visible) branchStay.Hide();
        int bx = (int)Math.Round((PX - BranchOX) * S + (home.X - anchor.X)), by = (int)Math.Round((PY - BranchOY) * S + (home.Y - anchor.Y));
        var st = g.Save();
        g.ResetTransform();
        g.DrawImage(branchCache, new Rectangle(bx, by, pw, ph));
        g.Restore(st);
#if PROFILE
        profBlit += clock.Elapsed.TotalMilliseconds - qb;
#endif
    }

    // layer 0: leaves behind the wood, 1: the wood (branch, twigs, knot), 2: leaves in front.
    void PaintBranch(Graphics g, float bx, float by, int layer)
    {
        Color bark = Color.FromArgb(255, 112, 74, 40);
        float shake = (float)Math.Max(-2, Math.Min(2, bendVel * 0.025));   // leaves rustle when the branch bounces
        float sway = (float)Math.Sin(t * 1.6) + shake, sway2 = (float)Math.Sin(t * 1.3 + 1.7) - shake;

        // Twigs behind the main branch, with their leaf clusters.
        if (layer == 0)
        {
            LeafCluster(g, bx - 84, by + 17, 160, sway2, 3);
            LeafCluster(g, bx - 32, by + 15, 110, sway, 2);
            return;
        }
        if (layer == 2)
        {
            LeafCluster(g, bx + 40, by + 14, 70, sway, 3);
            LeafCluster(g, bx + 102, by + 15, 85, sway2, 2);
            LeafCluster(g, bx + 102, by - 11, 15, sway, 2);   // sprouting from the outside of the bend
            LeafCluster(g, bx - 97, by + BranchY(-97), 190, sway, 2);   // leaves at the thin tip
            Leaf(g, bx + 12, by - 1, 300 + sway * 4, 13, 255, 1);
            Leaf(g, bx - 70, by + 4, 120 - sway * 4, 14, 255, 2);
            return;
        }
        Twig(g, bark, bx, by, new[] { new PointF(-58, 1), new PointF(-70, 10), new PointF(-84, 17) }, 3.6f, 1.2f);
        Twig(g, bark, bx, by, new[] { new PointF(-22, 1), new PointF(-28, 8), new PointF(-32, 15) }, 3f, 1f);

        // Main branch: comes down from the top of the screen on the right (the tree is up there), thickest
        // where it enters, bends over and runs left, tapering to a thin tip with a slight droop.
        // It passes through (0, 0) where the tail wraps.
        const int NH = 34, NB = 22;
        var c = new PointF[NH + NB + 1];
        var hw = new float[c.Length];
        for (int i = 0; i <= NH; i++)
        {
            float x = -98 + (56 + 98) * i / (float)NH, u = (x + 98) / 154;
            c[i] = new PointF(x, BranchY(x));
            hw[i] = (2.2f + 8f * (float)Math.Pow(u, 1.25f)) / 2;
        }
        PointF b0 = c[NH], b1 = new PointF(84, b0.Y - 0.5f), b2 = new PointF(99, -14), b3 = new PointF(104, -PY - 12);   // up past the top edge
        for (int i = 1; i <= NB; i++)
        {
            float s = i / (float)NB, a = 1 - s;
            c[NH + i] = new PointF(a * a * a * b0.X + 3 * a * a * s * b1.X + 3 * a * s * s * b2.X + s * s * s * b3.X,
                                   a * a * a * b0.Y + 3 * a * a * s * b1.Y + 3 * a * s * s * b2.Y + s * s * s * b3.Y);
            hw[NH + i] = hw[NH] + (9f - hw[NH]) * (float)Math.Sqrt(s);   // flares out toward the trunk
        }
        int N = c.Length - 1;
        var top = new PointF[N + 1];
        var bottom = new PointF[N + 1];
        var ridgeLine = new PointF[N + 1];
        var shadeLine = new PointF[N + 1];
        var shadeW = new float[N + 1];
        for (int i = 0; i <= N; i++)
        {
            PointF p0 = c[Math.Max(0, i - 1)], p1 = c[Math.Min(N, i + 1)];
            float tx = p1.X - p0.X, ty = p1.Y - p0.Y, tl = (float)Math.Sqrt(tx * tx + ty * ty);
            float nx = -ty / tl, ny = tx / tl;   // points to the underside
            float w = hw[i] + 0.35f * (float)Math.Sin(i * 1.7f) + 0.25f * (float)Math.Sin(i * 0.6f + 2);
            top[i] = new PointF(bx + c[i].X - nx * w, by + c[i].Y - ny * w);
            bottom[N - i] = new PointF(bx + c[i].X + nx * w, by + c[i].Y + ny * w);
            ridgeLine[i] = new PointF(bx + c[i].X - nx * w * 0.5f, by + c[i].Y - ny * w * 0.5f);
            shadeLine[i] = new PointF(bx + c[i].X + nx * w * 0.6f, by + c[i].Y + ny * w * 0.6f);
            shadeW[i] = w;
        }
        using (var path = new GraphicsPath())
        {
            path.AddCurve(top, 0.4f);
            path.AddCurve(bottom, 0.4f);
            path.CloseFigure();
            using (var o = new Pen(Outline, 3.2f) { LineJoin = LineJoin.Round }) g.DrawPath(o, path);
            // Plain bark, lit along the upper side and shaded underneath (works along the bend and up the stem too).
            using (var b = new SolidBrush(bark)) g.FillPath(b, path);
            var shadeClip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            for (int i = 0; i < N; i++)
                using (var sh = RoundPen(Color.FromArgb(90, 50, 28, 12), shadeW[i] * 0.9f)) g.DrawLine(sh, shadeLine[i], shadeLine[i + 1]);
            for (int i = 0; i < N; i++)
                using (var lt = RoundPen(Color.FromArgb(55, 255, 225, 180), shadeW[i] * 0.6f)) g.DrawLine(lt, ridgeLine[i], ridgeLine[i + 1]);
            g.Clip = shadeClip;

            // Bark: thin grooves running along the wood, and a lit ridge along the upper side.
            var clip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            using (var groove = new Pen(Color.FromArgb(120, 55, 32, 14), 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var ridge = new Pen(Color.FromArgb(60, 255, 230, 190), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawCurve(ridge, ridgeLine, 0.5f);
                for (int i = 2; i < N - 2; i += 3)
                {
                    float off = ((i * 5) % 7 - 3) * 0.28f;   // -1..1 across the wood
                    int j = Math.Min(N, i + 2);
                    PointF a0 = Lerp(top[i], bottom[N - i], 0.5f + off * 0.35f), a1 = Lerp(top[j], bottom[N - j], 0.5f + off * 0.35f + 0.06f);
                    g.DrawLine(groove, a0, a1);
                }
            }
            g.Clip = clip;
        }

        // A knot in the wood, and a short broken-off stub.
        float kx = 40, ky = BranchY(kx);
        using (var b = new SolidBrush(Dark(bark, 0.35f))) g.FillEllipse(b, bx + kx - 3.5f, by + ky - 2, 7, 4.2f);
        using (var o = new Pen(Color.FromArgb(160, 50, 28, 12), 1f))
        {
            g.DrawEllipse(o, bx + kx - 3.5f, by + ky - 2, 7, 4.2f);
            g.DrawArc(o, bx + kx - 6, by + ky - 3.5f, 12, 7.5f, 200, 140);
        }
        Twig(g, bark, bx, by, new[] { new PointF(-46, -1), new PointF(-50, -6), new PointF(-52, -9) }, 3f, 2.6f);
        using (var b = new SolidBrush(Color.FromArgb(255, 214, 180, 130))) g.FillEllipse(b, bx - 54, by - 11.5f, 4.5f, 3.5f);   // pale broken end

        // Twigs in front, hanging down and out to the side (the top of the screen is just above); their leaves are layer 2.
        Twig(g, bark, bx, by, new[] { new PointF(22, -1), new PointF(32, 5), new PointF(40, 14) }, 3.4f, 1.1f);
        Twig(g, bark, bx, by, new[] { new PointF(88, -4), new PointF(98, 4), new PointF(102, 15) }, 3.6f, 1.1f);
    }

    // Height of the branch's centre line at x (0 where the tail wraps around it).
    static float BranchY(float x)
    {
        return -0.035f * x + 1.6f * (float)Math.Sin(x / 26f) + 0.0004f * x * x * (x < 0 ? 1 : 0.4f);
    }

    // A tapering twig along a smooth curve through 'pts' (relative to the branch), from width w0 to w1.
    static void Twig(Graphics g, Color bark, float bx, float by, PointF[] pts, float w0, float w1)
    {
        const int segs = 8;
        var line = new PointF[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            float u = i / (float)segs, a = 1 - u;
            line[i] = new PointF(bx + a * a * pts[0].X + 2 * a * u * pts[1].X + u * u * pts[2].X,
                                 by + a * a * pts[0].Y + 2 * a * u * pts[1].Y + u * u * pts[2].Y);
        }
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < segs; i++)
            {
                float w = w0 + (w1 - w0) * (i + 0.5f) / segs;
                using (var pen = RoundPen(pass == 0 ? Outline : Mix(Light(bark, 0.12f), Dark(bark, 0.1f), i / (float)segs), pass == 0 ? w + 2.4f : w))
                    g.DrawLine(pen, line[i], line[i + 1]);
            }
    }

    // A few leaves of different sizes and shades fanned out from one point.
    static void LeafCluster(Graphics g, float x, float y, float angle, float sway, int count)
    {
        float[] spread = { 0, -34, 32, -62, 58 };
        float[] size = { 22, 17, 19, 14, 15 };
        for (int i = count - 1; i >= 0; i--)
            Leaf(g, x, y, angle + spread[i] + sway * (5 - i), size[i], 255, i % 3);
    }

    static void Leaf(Graphics g, float x, float y, float angle, float len, int a, int shade = 0)
    {
        int w = (int)Math.Ceiling(len) + 4, h = (int)Math.Ceiling(len * 0.7f) + 4;
        Bitmap b = Sprite("leaf" + len + "_" + shade, w, h, 2, h / 2f, sg => DrawLeaf(sg, len, shade));
        var st = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform(angle);
        var dest = new RectangleF(-2, -h / 2f, w, h);
        if (a >= 250) g.DrawImage(b, dest);
        else
        {
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = a / 255f };
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(b, Rectangle.Round(dest), 0, 0, w, h, GraphicsUnit.Pixel, ia);
            }
        }
        g.Restore(st);
    }

    // shade 0 = fresh green, 1 = deeper green, 2 = a touch yellow, so a cluster doesn't look copy-pasted.
    static void DrawLeaf(Graphics g, float len, int shade)
    {
        const int a = 255;
        Color lite = shade == 1 ? Color.FromArgb(a, 92, 172, 78) : shade == 2 ? Color.FromArgb(a, 160, 205, 90) : Color.FromArgb(a, 120, 200, 90);
        Color deep = shade == 1 ? Color.FromArgb(a, 38, 108, 50) : shade == 2 ? Color.FromArgb(a, 82, 140, 52) : Color.FromArgb(a, 50, 130, 55);
        using (var path = new GraphicsPath())
        {
            // Slightly lopsided, with a pointed tip, like a real leaf.
            path.AddBezier(0, 0, len * 0.25f, -len * 0.36f, len * 0.7f, -len * 0.3f, len, 0);
            path.AddBezier(len, 0, len * 0.72f, len * 0.24f, len * 0.3f, len * 0.28f, 0, 0);
            using (var b = new LinearGradientBrush(new RectangleF(0, -len * 0.3f, len, len * 0.6f), lite, deep, LinearGradientMode.Vertical))
                g.FillPath(b, path);
            using (var o = new Pen(Color.FromArgb(a, 35, 90, 35), 1.2f)) g.DrawPath(o, path);
        }
        using (var v = new Pen(Color.FromArgb(a * 2 / 3, 35, 100, 40), 1f)) g.DrawLine(v, 2, 0, len - 3, 0);
    }

    // Tail thins as it stretches.
    static float TailWidth(double r) { return (float)Math.Max(3.5, 6.5 - (r - L) / 60); }

    // The tail is coiled tightly around the branch, like a spring around the wood: about two and a half
    // turns, starting with the tip tucked behind. Angle 0 is the underside, pi/2 the front, pi the top.
    const double CoilStart = -0.7 * Math.PI, CoilEnd = 4 * Math.PI;

    static PointF CoilPoint(double th, float w)
    {
        float R = 3.9f + w * 0.4f;   // the branch's half thickness at the wrap, plus half the tail: snug
        float x = -12 + 24 * (float)((th - CoilStart) / (CoilEnd - CoilStart));
        return new PointF(PX + x, PY + BranchY(x) + wrapDy * (1 + x / PivotX) + R * (float)Math.Cos(th));
    }

    // Draws either the turns behind the branch (in shadow, before the branch) or the ones in front.
    // Each unbroken stretch is one smooth line with one pen (much quicker than lots of little pieces);
    // only the tapering tip is drawn piece by piece.
    static void TailCoil(Graphics g, bool front, float w)
    {
        const int N = 72;
        double step = (CoilEnd - CoilStart) / N, taper = 0.9 * Math.PI;
        float full = w * 0.8f;   // a little squeezed where it grips
        Color fill = front ? Fur : Dark(Fur, 0.35f);
        using (var outline = RoundPen(Outline, full + 2 * OL))
        using (var body = RoundPen(fill, full))
        using (var shine = RoundPen(Color.FromArgb(60, 255, 220, 180), full * 0.3f))
            for (int pass = 0; pass < 2; pass++)
            {
                var run = new List<PointF>();
                PointF prev = CoilPoint(CoilStart, w);
                for (int i = 1; i <= N + 1; i++)
                {
                    double th = CoilStart + step * i;
                    bool mine = i <= N && (Math.Sin(th - step / 2) > 0) == front;
                    PointF p = i <= N ? CoilPoint(th, w) : prev;
                    if (mine && th - CoilStart < taper)
                    {
                        // The tip tapers: a piece at a time.
                        float ww = full * (0.45f + 0.55f * (float)((th - CoilStart) / taper));
                        using (var pen = RoundPen(pass == 0 ? Outline : fill, pass == 0 ? ww + 2 * OL : ww)) g.DrawLine(pen, prev, p);
                    }
                    else if (mine)
                    {
                        if (run.Count == 0) run.Add(prev);
                        run.Add(p);
                    }
                    if ((!mine || i > N) && run.Count > 1)
                    {
                        var pts = run.ToArray();
                        g.DrawLines(pass == 0 ? outline : body, pts);
                        if (pass == 1 && front)   // a soft shine along each front turn, so it looks round
                        {
                            for (int j = 0; j < pts.Length; j++) pts[j].X -= full * 0.15f;
                            g.DrawLines(shine, pts);
                        }
                        run.Clear();
                    }
                    else if (!mine) run.Clear();
                    prev = p;
                }
            }
    }

    void DrawTail(Graphics g, float hx, float hy, float dx, float dy, double r)
    {
        float w = TailWidth(r);
        var start = CoilPoint(CoilEnd, w);   // leaves the branch from underneath
        var c1 = new PointF(start.X + 8, start.Y + 24);
        var c2 = new PointF(hx - dx * 40 - dy * 14, hy - dy * 40 + dx * 14);
        var end = new PointF(hx, hy);
        tailBez[0] = start; tailBez[1] = c1; tailBez[2] = c2; tailBez[3] = end;   // the hands climb along it
        using (var o = RoundPen(Outline, w + 2 * OL)) g.DrawBezier(o, start, c1, c2, end);
        using (var f = RoundPen(Fur, w)) g.DrawBezier(f, start, c1, c2, end);
        TailCoil(g, true, w);   // the turns across the front of the branch
        using (var hl = RoundPen(Color.FromArgb(60, 255, 220, 180), w * 0.3f))
            g.DrawBezier(hl, start, c1, c2, end);
    }

    void DrawArm(Graphics g, float sx, float sy)
    {
        // Arm reaching up from the shoulder to the hand gripping the tab.
        SleevedLine(g, new PointF(PX, PY), new PointF(sx, sy));
        Blob(g, PX - 8, PY - 8, 16, 15, Skin);
        using (var p = RoundPen(Color.FromArgb(170, 170, 115, 70), 1.4f))
        {
            g.DrawLine(p, PX - 4, PY - 7, PX - 4, PY - 2);
            g.DrawLine(p, PX, PY - 8, PX, PY - 2);
            g.DrawLine(p, PX + 4, PY - 7, PX + 4, PY - 2);
        }
    }

    // ---- The monkey ----

    void DrawBody(Graphics g)
    {
        // Local frame: hips at (0,0), +y points away from the branch (head is at the bottom).
        float breathe = (float)Math.Sin(breathPhase) * (1.2f + 1.4f * sleepW);
        Act shown = ShownAct;
        float kickAmp = 2f + (shown == Act.Pump ? 6f * actW : 0);
        float kick = (float)Math.Sin(2 * Math.PI * t / SwayPeriod + 1.2) * kickAmp;
        kick += ((float)Math.Sin(t * 2.1) * 6 - kick) * perchK;   // sitting, its legs dangle and swing
        float beatSide = beatCount % 2 == 0 ? 1 : -1;
        kick += ((float)Math.Sin(beatPhase * Math.PI) * 9 * beatSide - kick) * danceW;   // kick to the beat

        // Legs, bent up beside the tail.
        Limb(g, 9, new PointF(-8, 4), new PointF(-22, -6), new PointF(-14 - kick, -16));
        Limb(g, 9, new PointF(8, 4), new PointF(22, -6), new PointF(14 + kick, -16));
        Foot(g, -21 - kick, -23, 13, 9);
        Foot(g, 8 + kick, -23, 13, 9);

        // Torso in a puffer jacket (collar near the head, which hangs below).
#if PROFILE
        double qj = profClock.Elapsed.TotalMilliseconds;
#endif
        JacketTorso(g, -21 - breathe / 2, -4, 42 + breathe, 50, 44, true);
#if PROFILE
        profJacket += profClock.Elapsed.TotalMilliseconds - qj;
#endif

        // A pocket on the hip, with its snack sticking out (until it's taken out to eat).
        bool bananaTaken = shown == Act.Eat && actW > 0.05f && clock.Elapsed.TotalSeconds - actStart >= GrabTime;
        // It snacks all day, so the pocket is stuffed with a bunch; it pulls the middle one out to eat.
        // It never moves, so it's drawn once (at double size, to stay sharp) for each way it can look.
        bool showBananas = perchK < 0.5f;   // sitting, the jacket's upside down, so they're tucked right in
        string pocketKey = "pocket" + (showBananas ? (bananaTaken ? "2" : "3") : "0") + (bananaTaken ? "e" : "f");
        Bitmap pocket = Sprite(pocketKey, 56, 72, 0, 32, pg =>
        {
            pg.ScaleTransform(2, 2);
            if (showBananas)
            {
                DrawBanana(pg, new PointF(PocketGrip.X - 5.5f, PocketGrip.Y + 2), 165, PocketScale * 0.95f, 0, 1, false);
                DrawBanana(pg, new PointF(PocketGrip.X + 5.5f, PocketGrip.Y + 1.5f), 197, PocketScale * 0.95f, 0, 1, false);
                if (!bananaTaken) DrawBanana(pg, PocketGrip, 180, PocketScale, 0, 1, false);
            }
            DrawPocket(pg, !bananaTaken);
        });
#if PROFILE
        double qp = profClock.Elapsed.TotalMilliseconds;
#endif
        g.DrawImage(pocket, new RectangleF(0, -16, 28, 36));
#if PROFILE
        profPocket += profClock.Elapsed.TotalMilliseconds - qp;
#endif

        // The water bottle's holder, below the pocket, with the bottle's cap peeking out (until it's taken).
        bool bottleOut = shown == Act.Drink && actW > 0.3f && clock.Elapsed.TotalSeconds - actStart >= DkReach
                         && clock.Elapsed.TotalSeconds - actStart < DkIn;
        Bitmap holster = Sprite(bottleOut ? "holster0" : "holster1", 40, 70, -12, -28, hg =>
        {
            hg.ScaleTransform(2, 2);
            if (!bottleOut) DrawBottle(hg, HolsterNeck, 0, HolsterScale, 1, NeckGrip, true, false);
            DrawHolster(hg);
        });
        g.DrawImage(holster, new RectangleF(6, 14, 20, 35));

        // Arms in puffer sleeves: dangling below the head, unless it is busy with a habit.
        PointF restL = new PointF(-28 + lag, 88), restR = new PointF(28 + lag, 88);
        bool sitting = perch == Perch.Sit;
        if (sitting) { restL = new PointF(-24, -1); restR = new PointF(24, -1); }   // hands on the branch beside it
        PointF tL = restL, tR = restR;
        double ap = clock.Elapsed.TotalSeconds - actStart;
        if (shown == Act.Scratch)
            tR = Face(new PointF(23, 54 + (float)Math.Sin(t * 22) * 3));                // scratching its head
        if (shown == Act.Throw)
        {
            // Wind the arm back, then fling it toward the tab and follow through.
            PointF back = new PointF(46, 98), fling = new PointF(8 + throwDir.X * 58, 60 + throwDir.Y * 58);
            tR = ap < ThrowWind ? Lerp(restR, back, Smooth(ap / ThrowWind)) : Lerp(back, fling, Smooth((ap - ThrowWind) / 0.12));
        }
        bool climbing = shown == Act.Climb && ap < ClimbLetGo + 0.4;
        bool gripL = false, gripR = false;
        float angL = 0, angR = 0;
        if (climbing)
        {
            // Hand over hand up its own tail (found in body coordinates from where the tail was drawn).
            var inv = g.Transform.Clone();
            inv.Invert();
            var bz = (PointF[])tailBez.Clone();
            baseM.TransformPoints(bz);
            inv.TransformPoints(bz);
            inv.Dispose();
            tL = ClimbHand(bz, ap, 0f, -1, restL, out gripL, out angL);
            tR = ClimbHand(bz, ap, 0.5f, 1, restR, out gripR, out angR);
        }
        else if (shown == Act.Wave)
        {
            // Waving at you with both hands, swinging out and in together.
            float wv = (float)Math.Sin(t * 14) * 8;
            tL = Face(new PointF(-46 - wv, 58));
            tR = Face(new PointF(46 + wv, 58));
        }
        bool eating = shown == Act.Eat && actW > 0.05f && ap < TossTime + 0.4;
        if (shown == Act.Eat && ap < TossTime)
        {
            // Hold the banana just under the chin, then bring it up so the fruit's tip goes into the mouth.
            int done = 0;
            foreach (double bt in BiteTimes) if (ap > bt + 0.35) done++;
            float len = 7 + 24 * (1 - 0.3f * done);   // from the hand to the tip of what's left
            PointF hold = Face(Lerp(new PointF(20, 122), new PointF(BiteMouth.X + 0.29f * len, BiteMouth.Y + 0.96f * len), BiteReach(ap)));
            if (ap < GrabTime) tR = PocketGrip;                                                                    // reach back to the pocket
            else if (BananaInPocket(ap)) tR = Lerp(PocketGrip, PocketOut, Smooth((ap - GrabTime) / PullOutTime));  // pull it out
            else tR = Lerp(PocketOut, hold, CarryK(ap));                                                           // bring it round
            chewing = IsChewing(ap) && actW > 0.5f;
            biteOpen = BiteReach(ap) > 0.75f && actW > 0.5f;
        }
        bool drinking = shown == Act.Drink && actW > 0.05f;
        float sip = shown == Act.Drink ? DrinkLift(ap) : 0;
        if (drinking)
        {
            // The bottle lives in a holder on its jacket (by the hip); the drinking spots are by its face.
            PointF holdF = Face(DrinkHold), sipF = Face(SipHand);
            sipF.Y += GulpBob(ap) * (sitting ? -1 : 1);
            if (ap < DkReach) tR = Lerp(restR, HolsterNeck, Phase(ap, 0, DkReach));
            else if (ap < DkOut) tR = Lerp(HolsterNeck, HolsterOut, Phase(ap, DkReach, DkOut));      // pull it out by the neck
            else if (ap < DkFront) tR = Curve(HolsterOut, Face(AroundHead), holdF, Phase(ap, DkOut, DkFront));   // bring it round
            else if (ap < DkDown) tR = Lerp(holdF, sipF, sip);                                     // cap off, up to the lips, gulp, down
            else if (ap < DkCapOn) tR = holdF;                                                     // cap back on
            else if (ap < DkBack) tR = Curve(holdF, Face(AroundHead), HolsterOut, Phase(ap, DkCapOn, DkBack));
            else if (ap < DkIn) tR = Lerp(HolsterOut, HolsterNeck, Phase(ap, DkBack, DkIn));        // back in the holder
            else tR = Lerp(HolsterNeck, restR, Phase(ap, DkIn, DrinkLen));
            biteOpen = sip > 0.7f && actW > 0.5f;
            if (ap < 0.2) ahhSaid = false;
            if (ap >= DkDown && !ahhSaid && actW > 0.5f)
            {
                ahhSaid = true; grinTime = 1.2;
                Say(Pick("Ahh! Refreshing!", "Ahh, that hits the spot!", "Mmm, nice cold water!"), true);
            }
        }
        if (shown == Act.Shades && actW > 0.05f)
        {
            // Reach up to the glasses, then lower the hand again.
            float up = Smooth(ap / ShadesLift) * (1 - Smooth((ap - ShadesSwap - 0.15) / 0.35));
            tR = Lerp(restR, Face(new PointF(22, 61)), up);
        }
        PointF hL = Lerp(restL, tL, actW), hR = Lerp(restR, tR, actW);
        if (shown == Act.Eat && ap >= GrabTime && ap < TossTime && actW > 0.9f) hR = tR;   // the banana stays in its hand
        if (climbing && actW > 0.9f) { hL = tL; hR = tR; }                                  // hands stay on the tail
        if (drinking && actW > 0.9f && ap >= DkReach && ap < DkIn) hR = tR;                // the bottle stays in its hand

        // The other hand twists the cap off, holds it while it drinks, and screws it back on.
        float bottleAngle = 0, bottleScale = HolsterScale, bottleGrip = NeckGrip;
        bool capOn = true;
        if (drinking)
        {
            float restA = AngleTo(hR, Face(BiteMouth)), sipA = sitting ? AngleTo(Face(SipHand), Face(SipLips)) : SipAngle;
            sipA += (sitting ? 1 : -1) * 6 * GulpsDone(ap);   // tipped further as it empties
            if (ap < DkOut) bottleAngle = 0;
            else if (ap < DkFront) bottleAngle = restA * Phase(ap, DkOut, DkFront);
            else if (ap < DkCapOn) bottleAngle = restA + (sipA - restA) * sip;
            else if (ap < DkBack) bottleAngle = restA * (1 - Phase(ap, DkCapOn, DkBack));
            bottleScale = ap < DkOut ? HolsterScale : ap < DkFront ? HolsterScale + (1 - HolsterScale) * Phase(ap, DkOut, DkFront)
                        : ap < DkCapOn ? 1 : 1 - (1 - HolsterScale) * Phase(ap, DkCapOn, DkBack);
            bottleGrip = ap < DkOut ? NeckGrip : ap < DkFront ? NeckGrip * (1 - Phase(ap, DkOut, DkFront))
                       : ap < DkCapOn ? 0 : NeckGrip * Phase(ap, DkCapOn, DkBack);
            capOn = ap < (DkFront + DkCapOff) / 2 + 0.1 || ap >= DkCapOn - 0.15;
            bool capWork = ap >= DkFront - 0.15 && ap < DkCapOn + 0.15;
            capW += ((capWork && actW > 0.5f ? 1 : 0) - capW) * Ease(10, frameDt);
            if (capW > 0.01f)
            {
                PointF top = BananaToBody(hR, new PointF(0, -29), 1, bottleAngle);   // the cap, on the bottle in front of it
                bool twisting = (ap >= DkFront && ap < DkCapOff) || (ap >= DkDown && ap < DkCapOn);
                if (twisting) top.X += (float)Math.Sin(t * 28) * 1.6f;               // twist, twist
                PointF capHold = Face(new PointF(-18, 106));
                PointF want = twisting || (ap >= DkCapOff - 0.1 && ap < DkCapOff + 0.1) || (ap >= DkDown - 0.1 && ap < DkDown + 0.05) ? top : capHold;
                capHand = Lerp(capHand, want, Ease(16, frameDt));
                hL = Lerp(hL, capHand, capW);
            }
        }
        else capW = 0;
        if (perchK > 0.01f && !sitting)                                                     // swinging up or down: hands go to the branch
        {
            hL = Lerp(hL, new PointF(-24, -1), perchK);
            hR = Lerp(hR, new PointF(24, -1), perchK);
        }
        // The free hand peels the banana: it pinches the end of the strip being peeled and pulls it down.
        bool peelingNow = shown == Act.Eat && actW > 0.5f && ap > PeelStart - 0.35 && ap < PeelStart + 3 * PeelSlot + 0.1;
        peelW += ((peelingNow ? 1 : 0) - peelW) * Ease(9, frameDt);
        if (shown == Act.Eat && ap < TossTime)
        {
            int strip; bool reaching;
            float pp = PeelProgress(ap, out strip, out reaching);
            float sp = Math.Max(0, Math.Min(1, pp - strip));
            PointF want = BananaToBody(hR, Lerp(PeelClosed[strip], PeelOpen[strip], sp), BananaScale(ap), BananaAngle(ap, hR, Face(BiteMouth)));
            if (peelW < 0.05f) peelHand = hL;
            peelHand = Lerp(peelHand, want, Ease(reaching ? 14 : 30, frameDt));   // quick to follow the strip once pinched
        }
        if (peelW > 0.01f) hL = Lerp(hL, peelHand, peelW);
        if (danceW > 0.01f)
        {
            // Throw one arm out to the side, tuck the other in; swap on every beat.
            float e = (float)(1 - Math.Pow(1 - Math.Min(1, beatPhase * 2.5), 3));
            float wig = (float)Math.Sin(t * 12) * 4;
            PointF rUp = new PointF(46, 50 + wig), rTuck = new PointF(22, 98), lUp = new PointF(-46, 50 - wig), lTuck = new PointF(-22, 98);
            PointF r = beatSide > 0 ? Lerp(rTuck, rUp, e) : Lerp(rUp, rTuck, e);
            PointF l = beatSide > 0 ? Lerp(lUp, lTuck, e) : Lerp(lTuck, lUp, e);
            hL = Lerp(hL, l, danceW);
            hR = Lerp(hR, r, danceW);
        }
        bool scratchInFront = (shown == Act.Scratch || shown == Act.Shades) && actW > 0.3f;
        {
            // Where its throwing hand is on the screen (the banana leaves from here).
            var hp = new[] { hR };
            g.Transform.TransformPoints(hp);
            Point wpos = WindowPos;
            handScreen = new PointF(hp[0].X + wpos.X, hp[0].Y + wpos.Y);
        }

        ArmTo(g, new PointF(-16, 30), hL, -1);
        if (eating || drinking)
            SleevedArm(g, new PointF(16, 30), new PointF(36, 44), new PointF(hR.X + 16, hR.Y - 14), hR);   // arm behind the head
        else if (!scratchInFront)
            ArmTo(g, new PointF(16, 30), hR, 1);
        if (climbing && actW > 0.5f)
        {
            // Fingers wrapped round the tail where a hand is holding on.
            if (gripL) Grip(g, hL, angL);
            if (gripR) Grip(g, hR, angR);
        }

#if PROFILE
        double qh = profClock.Elapsed.TotalMilliseconds;
#endif
        float headCY = 70 + danceW * 3f * (float)Math.Sin(beatPhase * Math.PI);   // nod to the beat
        if (drinking) headCY += GulpBob(ap);                                        // and bob with each gulp
        gulping = drinking && ap >= DkLift - 0.05 && ap < DkLower;
        var headState = g.Save();
        if (perchK > 0)
        {
            // The body turns over to sit, but the head stays upright.
            g.TranslateTransform(0, headCY);
            g.RotateTransform(-180 * perchK);
            g.TranslateTransform(0, -headCY);
        }
        DrawHead(g, 0, headCY);
        g.Restore(headState);
#if PROFILE
        profHead += profClock.Elapsed.TotalMilliseconds - qh;
#endif
        if (scratchInFront) ArmTo(g, new PointF(16, 30), hR, 1);

        if (shown == Act.Throw && ap < ThrowWind && actW > 0.3f)   // the banana it's about to throw
            DrawBanana(g, hR, (float)(Math.Atan2(throwDir.X, -throwDir.Y) * 180 / Math.PI), 1, 0, 1);
        if (eating)
        {
            if (ap < TossTime)
            {
                float appear = BananaScale(ap) * Math.Min(1, actW / 0.9f);
                int strip; bool reaching;
                float peel = PeelProgress(ap, out strip, out reaching);
                int bitten = 0;
                foreach (double bt in BiteTimes) if (ap > bt) bitten++;
                if (ap < GrabTime) HandShape(g, hR.X, hR.Y + 3, 13);   // still reaching for the pocket
                else
                {
                    // Hide the part of the fruit above the middle of the mouth, so the tip looks like it's inside,
                    // and, while it's being pulled out, the part still in the pocket.
                    var clip = g.Clip;
                    float lipsY = BiteMouth.Y + 3;
                    g.SetClip(sitting ? new RectangleF(-40, 2 * HeadY - lipsY, 80, lipsY - 20) : new RectangleF(-40, 20, 80, lipsY - 20), CombineMode.Exclude);
                    if (BananaInPocket(ap)) g.SetClip(PocketRect, CombineMode.Exclude);
                    DrawBanana(g, hR, BananaAngle(ap, hR, Face(BiteMouth)), appear, peel, 1 - 0.3f * bitten);
                    g.Clip = clip;
                }
                if (peelW > 0.3f) HandShape(g, hL.X, hL.Y + 2, 11);   // pinching fingers, in front of the peel
            }
            else if (!peelTossed)
            {
                // Toss the empty peel away.
                peelTossed = true;
                var pts = new[] { hR };
                g.Transform.TransformPoints(pts);
                Point wp = WindowPos;
                peelPos = new PointF(pts[0].X + wp.X, pts[0].Y + wp.Y);
                peelVel = new PointF((rnd.Next(2) == 0 ? -1 : 1) * (150 + (float)rnd.NextDouble() * 120), -320);
                peelAngle = 0;
                peelSpin = (rnd.Next(2) == 0 ? -1 : 1) * (5 + (float)rnd.NextDouble() * 4);
                peelAge = 0;
                peelFlying = true;
                grinTime = 1.0;
                Say(Pick("Yum!", "Delicious!", "Mmm, banana!"), true);
            }
            if (ap >= TossTime) HandShape(g, hR.X, hR.Y + 3, 13);   // empty-handed again
        }
        if (drinking)
        {
            if (ap >= DkReach && ap < DkIn && actW > 0.3f)
            {
                float left = 1 - 0.22f * GulpsDone(ap);
                var clip = g.Clip;
                if (ap < DkOut || ap >= DkBack) g.SetClip(HolsterRect, CombineMode.Exclude);   // the part still in the holder
                DrawBottle(g, hR, bottleAngle, bottleScale, left, bottleGrip, capOn, true);
                g.Clip = clip;
                if (!capOn) DrawCap(g, hL, bottleAngle);   // the cap, in its other hand
            }
            else HandShape(g, hR.X, hR.Y + 3, 13);
        }
    }

    // Drinking pose: the bottle lies almost level with its bottom tipped up a little, the cap's top
    // at the lips (just right of the mouth's middle, head drawn at 0, 70), the hand beside the jaw.
    static readonly PointF SipLips = new PointF(2, 84), SipDir = new PointF(0.994f, -0.11f);   // lips -> bottom
    static readonly PointF SipHand = new PointF(SipLips.X + SipDir.X * 31, SipLips.Y + SipDir.Y * 31);
    static readonly float SipAngle = (float)(Math.Atan2(-SipDir.X, SipDir.Y) * 180 / Math.PI);

    // A small water bottle held in a fist at 'hand', turned 'angle' degrees from upright.
    // 'grip' slides the bottle so the fist holds it higher up (22 = by the neck); without 'held', no hand at all.
    void DrawBottle(Graphics g, PointF hand, float angle, float appear, float waterLeft, float grip = 0, bool capOn = true, bool held = true)
    {
        if (appear < 0.02f) { if (held) HandShape(g, hand.X, hand.Y + 3, 13); return; }   // bottle put away: open hand
        var st = g.Save();
        g.TranslateTransform(hand.X, hand.Y);
        g.RotateTransform(angle);
        if (held) FistBack(g);
        g.ScaleTransform(appear, appear);
        g.TranslateTransform(0, grip);
        using (var body = new GraphicsPath())
        {
            body.AddArc(-5.5f, -26, 11, 8, 180, 180);
            body.AddLine(5.5f, -22, 5.5f, 2);
            body.AddArc(-5.5f, -2, 11, 6, 0, 180);
            body.CloseFigure();
            using (var glass = new SolidBrush(Color.FromArgb(150, 205, 235, 255))) g.FillPath(glass, body);
            var clip = g.Clip;
            g.SetClip(body, CombineMode.Intersect);
            // Upright the water sits at the bottom; tipped up to drink, it runs down to the cap end.
            bool tipped = Math.Abs(angle) > 75;
            float top = tipped ? -28 : 4 - 30 * waterLeft, depth = tipped ? 30 * waterLeft : 34;
            using (var water = new LinearGradientBrush(new RectangleF(-6, top - 1, 12, 36), Color.FromArgb(220, 90, 175, 245), Color.FromArgb(230, 40, 120, 220), LinearGradientMode.Vertical))
                g.FillRectangle(water, -6, top, 12, depth);
            using (var shine = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillRectangle(shine, -3.5f, -21, 1.6f, 18);
            using (var label = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) g.FillRectangle(label, -6, -13, 12, 4);
            g.Clip = clip;
            using (var o = new Pen(Color.FromArgb(255, 40, 90, 150), 1.4f) { LineJoin = LineJoin.Round }) g.DrawPath(o, body);
        }
        if (capOn) DrawCapShape(g, 0, -28);
        else using (var rim = new Pen(Color.FromArgb(255, 40, 90, 150), 1.6f)) g.DrawLine(rim, -2.8f, -26.2f, 2.8f, -26.2f);   // open neck
        g.TranslateTransform(0, -grip);
        g.ScaleTransform(1 / appear, 1 / appear);
        if (held) FistFront(g);   // fingers around the bottle
        g.Restore(st);
    }

    // The bottle's screw cap, centred at (x, y), with ridges.
    static void DrawCapShape(Graphics g, float x, float y)
    {
        using (var cap = new SolidBrush(Color.FromArgb(255, 30, 85, 175))) g.FillRectangle(cap, x - 3.5f, y - 3, 7, 6);
        using (var ridge = new Pen(Color.FromArgb(120, 255, 255, 255), 0.8f))
            for (float rx = -2; rx <= 2; rx += 2) g.DrawLine(ridge, x + rx, y - 2.2f, x + rx, y + 2.2f);
        using (var o = new Pen(Outline, 1f)) g.DrawRectangle(o, x - 3.5f, y - 3, 7, 6);
    }

    // The cap held between its fingers, while it drinks.
    static void DrawCap(Graphics g, PointF hand, float angle)
    {
        var st = g.Save();
        g.TranslateTransform(hand.X, hand.Y);
        g.RotateTransform(angle);
        DrawCapShape(g, 0, -6);
        g.Restore(st);
    }

    // Where the banana's tip goes when it takes a bite: just inside the open mouth (head drawn at 0, 70).
    static readonly PointF BiteMouth = new PointF(0, 81);

    // 0 = banana held under the chin, 1 = at the mouth taking a bite.
    static float BiteReach(double p)
    {
        double best = 0;
        foreach (double bt in BiteTimes)
        {
            double d = p - bt, v = 0;
            if (d > -0.3 && d <= 0) v = 1 + d / 0.3;
            else if (d > 0 && d < 0.35) v = 1 - d / 0.35;
            best = Math.Max(best, v * v * (3 - 2 * v));
        }
        return (float)best;
    }

    static bool IsChewing(double p)
    {
        foreach (double bt in BiteTimes) if (p > bt + 0.15 && p < bt + 0.9) return true;
        return false;
    }

    static readonly Color BananaYellow = Color.FromArgb(255, 250, 212, 58);
    static readonly Color BananaFruit = Color.FromArgb(255, 252, 244, 212);
    static readonly Color BananaBrown = Color.FromArgb(255, 105, 72, 30);

    // A banana held at 'hand', pointing toward 'aim'. 'peel' opens the skin, 'remain' is how much fruit is left.
    void DrawBanana(Graphics g, PointF hand, float angle, float appear, float peel, float remain, bool held = true)
    {
        var st = g.Save();
        g.TranslateTransform(hand.X, hand.Y);
        g.RotateTransform(angle);
        if (held) FistBack(g);   // the palm, behind the banana (the hand doesn't grow with the banana)
        if (appear >= 0.02f)
        {
            g.ScaleTransform(appear, appear);

            // The fruit: a curved stroke that gets shorter with each bite.
            PointF[] fruit = SplitBezier(new PointF(1, -7), new PointF(4.5f, -14), new PointF(4.5f, -23), new PointF(1, -31), Math.Max(0.05f, remain));
            using (var o = RoundPen(Color.FromArgb(255, 200, 175, 110), 8.5f)) g.DrawBezier(o, fruit[0], fruit[1], fruit[2], fruit[3]);
            using (var f = RoundPen(BananaFruit, 6.5f)) g.DrawBezier(f, fruit[0], fruit[1], fruit[2], fruit[3]);

            // Fingers curl around the bottom of the skin; the peeled strips hang down over them.
            float inv = 1 / appear;
            DrawPeelSkin(g, peel, true, held ? (Action<Graphics>)(sg => { var s2 = sg.Save(); sg.ScaleTransform(inv, inv); FistFront(sg); sg.Restore(s2); }) : null);
        }
        else if (held) FistFront(g);
        g.Restore(st);
    }

    // A little sleeve for the water bottle, sewn on the jacket.
    static void DrawHolster(Graphics g)
    {
        RectangleF r = HolsterRect;
        using (var path = new GraphicsPath())
        {
            path.AddLine(r.Left, r.Top, r.Right, r.Top);
            path.AddArc(r.Right - 7, r.Bottom - 7, 7, 7, 0, 90);
            path.AddArc(r.Left, r.Bottom - 7, 7, 7, 90, 90);
            path.CloseFigure();
            using (var b = new SolidBrush(Dark(Jacket, 0.22f))) g.FillPath(b, path);
            using (var o = new Pen(Outline, 1.3f) { LineJoin = LineJoin.Round }) g.DrawPath(o, path);
        }
        using (var band = RoundPen(Color.FromArgb(255, 241, 233, 214), 2.6f)) g.DrawLine(band, r.Left + 1.5f, r.Top + 1.6f, r.Right - 1.5f, r.Top + 1.6f);   // elastic top
        using (var stitch = new Pen(Color.FromArgb(140, 200, 225, 220), 0.8f) { DashPattern = new[] { 2f, 1.6f } })
            g.DrawLine(stitch, r.Left + (r.Width / 2), r.Top + 5, r.Left + (r.Width / 2), r.Bottom - 3);
    }

    // The jacket's hip pocket: a stitched patch a shade darker than the jacket, with a hem along the opening.
    static void DrawPocket(Graphics g, bool full)
    {
        RectangleF r = PocketRect;
        using (var path = new GraphicsPath())
        {
            path.AddLine(r.Left, r.Top, r.Right, r.Top);
            path.AddArc(r.Right - 8, r.Bottom - 8, 8, 8, 0, 90);
            path.AddArc(r.Left, r.Bottom - 8, 8, 8, 90, 90);
            path.CloseFigure();
            using (var b = new SolidBrush(Dark(Jacket, 0.14f))) g.FillPath(b, path);
            using (var o = new Pen(Outline, 1.4f) { LineJoin = LineJoin.Round }) g.DrawPath(o, path);
        }
        using (var stitch = new Pen(Color.FromArgb(150, 200, 225, 220), 0.9f) { DashPattern = new[] { 2.2f, 1.8f } })
            g.DrawLines(stitch, new[] { new PointF(r.Left + 2.5f, r.Top + 4), new PointF(r.Left + 2.5f, r.Bottom - 4),
                new PointF(r.Left + 5, r.Bottom - 2.5f), new PointF(r.Right - 5, r.Bottom - 2.5f),
                new PointF(r.Right - 2.5f, r.Bottom - 4), new PointF(r.Right - 2.5f, r.Top + 4) });
        // The bananas inside push the fabric out: soft curved bulges with a shadow along one side
        // (the middle one only while it's still in there).
        float[] bx = { 8, 18, 13 };
        for (int i = full ? 2 : 1; i >= 0; i--)
        {
            float x0 = bx[i], lean = i == 0 ? -1.5f : i == 1 ? 1.5f : 0;
            PointF a = new PointF(x0, r.Top + 3), b = new PointF(x0 - 2 + lean, r.Top + 8), c = new PointF(x0 - 2 + lean * 1.6f, r.Top + 13), d = new PointF(x0 + lean * 2, r.Bottom - 3);
            using (var bulge = RoundPen(Color.FromArgb(60, 255, 255, 255), 5f)) g.DrawBezier(bulge, a, b, c, d);
            using (var shadow = RoundPen(Color.FromArgb(80, 10, 40, 40), 1.3f))
                g.DrawBezier(shadow, new PointF(a.X + 2.8f, a.Y), new PointF(b.X + 3, b.Y), new PointF(c.X + 3, c.Y), new PointF(d.X + 2.6f, d.Y));
        }
        using (var hem = RoundPen(Dark(Jacket, 0.3f), 2.4f)) g.DrawLine(hem, r.Left + 1, r.Top + 1.2f, r.Right - 1, r.Top + 1.2f);
        // The opening gapes around the bananas, stretched by the bunch.
        using (var gap = RoundPen(Color.FromArgb(160, 15, 35, 35), 1.6f))
            g.DrawArc(gap, r.Left + 2, r.Top - 0.5f, r.Width - 4, 4, 0, 180);
    }

    // Climbing its tail (see the Climb habit's timing in Frame): pulls itself up until ClimbTop, hangs, lets go at ClimbLetGo.
    const double ClimbTop = 1.3, ClimbLetGo = 1.9, ClimbRate = 1.8;   // hand-over-hand cycles per second, per hand
    PointF[] tailBez = new PointF[4];   // the tail as last drawn (window coordinates), branch end first

    static PointF Bez(PointF[] b, float t)
    {
        float a = 1 - t;
        return new PointF(a * a * a * b[0].X + 3 * a * a * t * b[1].X + 3 * a * t * t * b[2].X + t * t * t * b[3].X,
                          a * a * a * b[0].Y + 3 * a * a * t * b[1].Y + 3 * a * t * t * b[2].Y + t * t * t * b[3].Y);
    }

    // Where one hand is on the tail. Each hand grips high and pulls down while the body rises, then lets go,
    // swings a little out to the side and reaches up past the other hand to grip high again ('offset' keeps
    // the two hands half a cycle apart). At the top both hold on; then they let go and drop back to 'rest'.
    static PointF ClimbHand(PointF[] bz, double ap, float offset, int side, PointF rest, out bool grip, out float angle)
    {
        float len = (float)Math.Sqrt(Math.Pow(bz[0].X - bz[3].X, 2) + Math.Pow(bz[0].Y - bz[3].Y, 2));
        float tHi = Math.Max(0.3f, 1 - 40 / len), tLo = Math.Max(0.35f, 1 - 10 / len);
        float tt, outward = 0;
        double ph = (Math.Min(ap, ClimbTop) * ClimbRate + offset) % 1.0;
        grip = true;
        if (ap >= ClimbTop) tt = offset == 0 ? tHi : (tHi + tLo) / 2;   // hanging at the top: one hand above the other
        else if (ph < 0.55) tt = tHi + (tLo - tHi) * (float)(ph / 0.55);  // holding on and pulling down
        else
        {
            float u = (float)((ph - 0.55) / 0.45);                          // reaching up for the next grip
            tt = tLo + (tHi - tLo) * Smooth(u);
            outward = side * 8 * (float)Math.Sin(Math.PI * u);
            grip = false;
        }
        PointF p = Bez(bz, tt), q = Bez(bz, Math.Max(0, tt - 0.05f));
        angle = (float)(Math.Atan2(q.X - p.X, -(q.Y - p.Y)) * 180 / Math.PI);   // fist turned along the tail
        p = new PointF(p.X + outward, p.Y);
        if (ap >= ClimbLetGo)
        {
            grip = false;
            return Lerp(p, rest, Smooth((ap - ClimbLetGo) / 0.35));   // let go and drop
        }
        return p;
    }

    // Fingers curled round something running along 'angle' (the tail) at 'hand'.
    static void Grip(Graphics g, PointF hand, float angle)
    {
        var st = g.Save();
        g.TranslateTransform(hand.X, hand.Y);
        g.RotateTransform(angle);
        FistFront(g);
        g.Restore(st);
    }

    // A fist holding something upright at (0, 0), drawn in two parts: the palm behind it...
    static void FistBack(Graphics g)
    {
        Blob(g, -7.5f, -5.5f, 15, 14, Skin);
    }

    // ...and three curled fingers across the front, with the thumb pressed along the side.
    static void FistFront(Graphics g)
    {
        for (int i = 2; i >= 0; i--)   // the bottom finger first, so each one above overlaps it
        {
            float y = -4.2f + i * 3.7f, len = i == 2 ? 4.5f : 5.5f;
            using (var o = RoundPen(Outline, 4f + 2.2f)) g.DrawLine(o, -len, y, len, y + 0.5f);
            using (var f = RoundPen(Light(Skin, 0.04f), 4f)) g.DrawLine(f, -len, y, len, y + 0.5f);
            using (var sh = RoundPen(Color.FromArgb(70, 170, 115, 70), 1f)) g.DrawLine(sh, -len + 1, y + 1.2f, len - 1, y + 1.6f);   // knuckle shadow
        }
        using (var o = RoundPen(Outline, 4.2f + 2.2f)) g.DrawLine(o, -7, 3.5f, -4, -6.5f);
        using (var f = RoundPen(Skin, 4.2f)) g.DrawLine(f, -7, 3.5f, -4, -6.5f);
    }

    // The yellow skin: bottom part plus three strips that lie closed along the fruit and fold open as it is peeled.
    // The three skin strips (banana coordinates): tips when closed, ends when folded down, and where they start.
    static readonly PointF[] PeelClosed = { new PointF(-1.5f, -30), new PointF(5, -29), new PointF(2, -31) };
    static readonly PointF[] PeelOpen = { new PointF(-13, -1), new PointF(15, 0), new PointF(2, 2) };
    static readonly PointF[] PeelRoots = { new PointF(-2, -9), new PointF(4, -9), new PointF(1, -9.5f) };

    // A point on the held banana, in the body's coordinates (DrawBanana's transform, done by hand).
    static PointF BananaToBody(PointF hand, PointF local, float appear, float angle)
    {
        double a = angle * Math.PI / 180;
        float lx = local.X * appear, ly = local.Y * appear, c = (float)Math.Cos(a), s = (float)Math.Sin(a);
        return new PointF(hand.X + lx * c - ly * s, hand.Y + lx * s + ly * c);
    }

    // 'peel' is 0 (closed) .. 3 (all strips down). 'grip' (optional) draws whatever holds the banana,
    // between the skin's bottom and the strips.
    static void DrawPeelSkin(Graphics g, float peel, bool withTip, Action<Graphics> grip = null)
    {
        using (var o = RoundPen(Outline, 8.5f + 2.6f))
        using (var f = RoundPen(BananaYellow, 8.5f))
        {
            g.DrawBezier(o, new PointF(0, 4), new PointF(2, 0), new PointF(2, -4), new PointF(1, -9));
            g.DrawBezier(f, new PointF(0, 4), new PointF(2, 0), new PointF(2, -4), new PointF(1, -9));
        }
        using (var b = new SolidBrush(BananaBrown)) g.FillRectangle(b, -1.8f, 3, 3.6f, 4);   // stem
        if (grip != null) grip(g);
        PointF[] closed = PeelClosed, open = PeelOpen, roots = PeelRoots;
        for (int i = 2; i >= 0; i--)
        {
            float pi = Math.Max(0, Math.Min(1, peel - i));   // strips come down one after another
            if (i == 2 && pi > 0.5f) continue;   // the back strip hides behind once open
            PointF end = Lerp(closed[i], open[i], pi);
            PointF mid = new PointF((roots[i].X + end.X) / 2 + (i == 0 ? -3 : 3) * (1 - pi) + (i == 0 ? -4 : 4) * pi,
                                    (roots[i].Y + end.Y) / 2 - 6 * pi);
            using (var o = RoundPen(Outline, 4.6f + 2.4f)) g.DrawBezier(o, roots[i], mid, mid, end);
            using (var f = RoundPen(i == 2 ? Dark(BananaYellow, 0.08f) : BananaYellow, 4.6f)) g.DrawBezier(f, roots[i], mid, mid, end);
        }
        // The brown tip while it is still closed.
        if (withTip && peel < 0.3f)
            using (var b = new SolidBrush(BananaBrown)) g.FillEllipse(b, 0, -32.5f, 3.6f, 3.6f);
    }

    void DrawFlyingPeel(Graphics g)
    {
        PointF lp = ToLocalF(peelPos);
        float px = lp.X, py = lp.Y;
        float fade = Math.Max(0, Math.Min(1, (H - py) / 70f)) * (float)Math.Min(1, (1.8 - peelAge) / 0.4);
        if (fade <= 0.02f) return;
        var st = g.Save();
        g.TranslateTransform(px, py);
        g.RotateTransform(peelAngle * 57.3f);
        using (var ia = new SolidBrush(Color.FromArgb((int)(40 * fade), 0, 0, 0))) g.FillEllipse(ia, -10, -6, 20, 12);
        DrawPeelSkin(g, 3, false);
        g.Restore(st);
    }

    static PointF Lerp(PointF a, PointF b, float k) { return new PointF(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k); }

    // A sleeved arm from the shoulder to wherever the hand should be.
    static void ArmTo(Graphics g, PointF shoulder, PointF hand, int side)
    {
        float k = Math.Max(-1, Math.Min(1, (hand.Y - shoulder.Y) / 58f));
        SleevedArm(g, shoulder,
            new PointF(shoulder.X + side * 15, shoulder.Y + 16 * k),
            new PointF(hand.X + side * 4, hand.Y - 22 * k),
            hand);
        HandShape(g, hand.X, hand.Y + 4 * k, 13);
    }

    void DrawBodyByHand(Graphics g)
    {
        // Local frame: raised shoulder at (0,0), +y points down away from the hand.
        float breathe = (float)Math.Sin(t * 2.4) * 1.2f;
        float wag = (float)Math.Sin(t * 3.0) * 4f;

        // Tail curling out behind.
        using (var o = RoundPen(Outline, 6f + 2 * OL))
        {
            g.DrawBezier(o, new PointF(-10, 64), new PointF(16, 72), new PointF(28 - lag / 2 + wag, 94), new PointF(14 - lag / 2 + wag, 104));
            g.DrawArc(o, 4 - lag / 2 + wag, 96, 14, 14, 90, 240);
        }
        using (var f = RoundPen(Fur, 6f))
        {
            g.DrawBezier(f, new PointF(-10, 64), new PointF(16, 72), new PointF(28 - lag / 2 + wag, 94), new PointF(14 - lag / 2 + wag, 104));
            g.DrawArc(f, 4 - lag / 2 + wag, 96, 14, 14, 90, 240);
        }

        // Legs dangling.
        Limb(g, 9, new PointF(-20, 60), new PointF(-25 + lag / 2, 78), new PointF(-19 + lag, 90));
        Limb(g, 9, new PointF(-4, 60), new PointF(1 + lag / 2, 78), new PointF(-4 + lag, 90));
        Foot(g, -26 + lag, 87, 13, 9);
        Foot(g, -11 + lag, 87, 13, 9);

        // Torso in a puffer jacket.
        JacketTorso(g, -31 - breathe / 2, 12, 38 + breathe, 52, 22, false);

        // The free arm: dangles, or reaches up for the next grip / holds the tab.
        PointF shoulder = new PointF(-27, 24);
        bool holding = mode == Mode.AtTab || mode == Mode.PullDown;
        bool reachingUp = freeW > 0.4f && !holding;   // when holding the tab the arm goes behind the head, keeping the face clear
        if (!reachingUp) DrawFreeArm(g, shoulder);

        DrawHead(g, -20, 2);
        if (reachingUp) DrawFreeArm(g, shoulder);   // in front of the head when it reaches up past it
    }

    void DrawFreeArm(Graphics g, PointF shoulder)
    {
        PointF dangle = new PointF(-40 + lag, 66);
        PointF hand = Lerp(dangle, freeLocal, freeW);
        if (freeW < 0.05f)
        {
            SleevedArm(g, shoulder, new PointF(-42, 36), new PointF(-44 + lag, 52), dangle);
            HandShape(g, dangle.X, dangle.Y + 4, 13);
            return;
        }
        // Bend the elbow outward, away from the body.
        float k = Math.Max(-1, Math.Min(1, (hand.Y - shoulder.Y) / 58f));
        SleevedArm(g, shoulder,
            new PointF(shoulder.X - 16, shoulder.Y + 16 * k - 4),
            new PointF(hand.X - 10, hand.Y - 22 * k),
            hand);
        HandShape(g, hand.X, hand.Y + 3 * k, 13);
        if (freeW > 0.85f && (mode == Mode.AtTab || mode == Mode.PullDown))
        {
            // Fingers curled over the edge it is holding.
            using (var p = RoundPen(Color.FromArgb(170, 170, 115, 70), 1.4f))
            {
                g.DrawLine(p, hand.X - 4, hand.Y - 5, hand.X - 4, hand.Y);
                g.DrawLine(p, hand.X, hand.Y - 6, hand.X, hand.Y);
                g.DrawLine(p, hand.X + 4, hand.Y - 5, hand.X + 4, hand.Y);
            }
        }
    }

    void DrawHead(Graphics g, float cx, float cy)
    {
        float shiver = 0;
        if ((FaceMood == Mood.Cold || FaceMood == Mood.Scared) && t % 3.0 < 1.2) shiver = 0.9f * (float)Math.Sin(t * 60);
        g.TranslateTransform(shiver, 0);
        DrawHeadParts(g, cx, cy);
        g.TranslateTransform(-shiver, 0);
    }

    void DrawHeadParts(Graphics g, float cx, float cy)
    {
        const float hr = 25;
        bool closed = (blinkTime > 0 || gulping) && !grabbed;   // eyes shut while it gulps
        bool grin = grinTime > 0 && !grabbed;

        // Ears.
        Blob(g, cx - hr - 10, cy - 13, 19, 19, Fur);
        Blob(g, cx + hr - 9, cy - 13, 19, 19, Fur);
        Shade(g, cx - hr - 5, cy - 8, 10, 10, Mix(Skin, Color.FromArgb(255, 240, 150, 150), 0.35f));
        Shade(g, cx + hr - 4, cy - 8, 10, 10, Mix(Skin, Color.FromArgb(255, 240, 150, 150), 0.35f));

        // Head.
        Blob(g, cx - hr, cy - hr, 2 * hr, 2 * hr, Fur);

        // Hair tuft.
        using (var tuft = new GraphicsPath())
        {
            tuft.AddBezier(cx - 7, cy - hr + 4, cx - 9, cy - hr - 4, cx - 4, cy - hr - 9, cx - 2, cy - hr - 10);
            tuft.AddBezier(cx - 2, cy - hr - 10, cx - 1, cy - hr - 5, cx + 1, cy - hr - 4, cx + 3, cy - hr - 3);
            tuft.AddBezier(cx + 3, cy - hr - 3, cx + 5, cy - hr - 8, cx + 9, cy - hr - 9, cx + 11, cy - hr - 8);
            tuft.AddBezier(cx + 11, cy - hr - 8, cx + 8, cy - hr - 3, cx + 7, cy - hr + 1, cx + 6, cy - hr + 4);
            tuft.CloseFigure();
            tuft.AddBezier(cx - 13, cy - hr + 6, cx - 16, cy - hr - 2, cx - 12, cy - hr - 6, cx - 8, cy - hr - 7);
            tuft.AddBezier(cx - 8, cy - hr - 7, cx - 9, cy - hr - 2, cx - 8, cy - hr + 1, cx - 6, cy - hr + 5);
            tuft.CloseFigure();
            using (var b = new SolidBrush(Dark(Fur, 0.15f))) g.FillPath(b, tuft);
            using (var o = new Pen(Outline, 1.4f) { LineJoin = LineJoin.Round }) g.DrawPath(o, tuft);
        }

        g.TranslateTransform(faceShift, 0);   // the face turns a little

        // Face mask: two eye patches and a muzzle, merged into one soft shape.
        using (var mask = new GraphicsPath(FillMode.Winding))
        {
            mask.AddEllipse(cx - 19, cy - 16, 20, 20);
            mask.AddEllipse(cx - 1, cy - 16, 20, 20);
            mask.AddEllipse(cx - 17, cy - 3, 34, 23);
            using (var b = new LinearGradientBrush(new RectangleF(cx - 19, cy - 17, 38, 41),
                Light(Skin, 0.35f), Dark(Skin, 0.05f), LinearGradientMode.Vertical))
                g.FillPath(b, mask);
        }
        using (var shine = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
            g.FillEllipse(shine, cx - 8, cy + 0, 16, 6);

        // Eyebrows (raised in surprise when grabbed), above the shades.
        float lift = grabbed ? 3.5f : grin ? 1.5f : 0;
        Mood fm = FaceMood;
        using (var brow = RoundPen(Dark(Fur, 0.3f), 2.4f))
        {
            if (fm == Mood.Gloomy || fm == Mood.Scared || fm == Mood.Cold)
            {
                // Worried: the inner ends pulled up (way up when scared).
                float up = fm == Mood.Scared ? 5 : 2.5f;
                g.DrawLine(brow, cx - 17, cy - 18, cx - 5, cy - 21 - up);
                g.DrawLine(brow, cx + 17, cy - 18, cx + 5, cy - 21 - up);
            }
            else if (fm == Mood.Meh)
            {
                // Flat and unimpressed.
                g.DrawLine(brow, cx - 17, cy - 19.5f, cx - 5, cy - 19.5f);
                g.DrawLine(brow, cx + 5, cy - 19.5f, cx + 17, cy - 19.5f);
            }
            else
            {
                if (fm == Mood.Happy) lift = 1;
                g.DrawArc(brow, cx - 17, cy - 22 - lift, 13, 6, 200, 120);
                g.DrawArc(brow, cx + 4, cy - 22 - lift, 13, 6, 220, 120);
            }
        }

        // Eyes are hidden behind the shades, unless you grab it: then they get pushed up in surprise.
        float eh = grabbed ? 14.5f : 12.5f;
        foreach (float ex in new[] { cx - 8.5f, cx + 8.5f })
        {
            bool sleepy = sleepW >= 0.5f && !grabbed;
            if (!grabbed && !sleepy && !EyeRest && ShadesOnFace) break;
            if (sleepy)
            {
                // Peacefully closed eyes with little lashes.
                using (var lid = RoundPen(Ink, 2.2f))
                {
                    float sy = cy - 6;
                    g.DrawArc(lid, ex - 5, sy - 5, 10, 7, 20, 140);
                    g.DrawLine(lid, ex - 4.5f, sy - 0.5f, ex - 6.5f, sy + 1.5f);
                }
                continue;
            }
            float ey = cy - 6;
            if (closed || (grin && ex > cx) || RestBlinkNow)
            {
                // Closed (blink), or a happy wink with the right eye while grinning.
                using (var lid = RoundPen(Ink, 2.2f)) g.DrawArc(lid, ex - 5, ey - 3, 10, 7, 200, 140);
                continue;
            }
            using (var o = new SolidBrush(Color.FromArgb(150, Outline))) g.FillEllipse(o, ex - 6, ey - eh / 2 - 1, 12, eh + 2);
            using (var wb = new SolidBrush(Color.White)) g.FillEllipse(wb, ex - 5, ey - eh / 2, 10, eh);
            float ix = ex + lookX, iy = ey + lookY + 0.5f;
            using (var iris = new SolidBrush(Color.FromArgb(255, 120, 72, 35))) g.FillEllipse(iris, ix - 3.8f, iy - 4.2f, 7.6f, 8.4f);
            using (var pupil = new SolidBrush(Ink)) g.FillEllipse(pupil, ix - 2.3f, iy - 2.6f, 4.6f, 5.2f);
            using (var hl = new SolidBrush(Color.White))
            {
                g.FillEllipse(hl, ix - 2.6f, iy - 3.6f, 2.8f, 2.8f);
                g.FillEllipse(hl, ix + 1f, iy + 1.2f, 1.3f, 1.3f);
            }
        }

        if (chewing)
        {
            // Cheeks puffed out with banana.
            Shade(g, cx - 19, cy - 1, 12, 11, Skin);
            Shade(g, cx + 7, cy - 1, 12, 11, Skin);
        }

        if (ShadesShown) Shades(g, cx, cy - 6, ShadesLifted);   // pushed up to sleep, rest its eyes, or take them off

        // Nose (rosy when it's cold).
        if (fm == Mood.Cold)
            using (var b = new SolidBrush(Color.FromArgb(90, 235, 70, 80))) g.FillEllipse(b, cx - 5, cy + 2.5f, 10, 5);
        using (var ink = new SolidBrush(Ink))
        {
            g.FillEllipse(ink, cx - 4.2f, cy + 3.5f, 3.2f, 2.4f);
            g.FillEllipse(ink, cx + 1f, cy + 3.5f, 3.2f, 2.4f);
        }

        // Mouth: "ooh" when grabbed or biting, chomping while chewing, big grin when tapped, smirk otherwise.
        if (sleepW >= 0.5f && !grabbed)
        {
            // Mouth slightly open, breathing slowly.
            float m = 3.4f + 1.3f * (float)Math.Sin(breathPhase);
            using (var b = new SolidBrush(Mouth)) g.FillEllipse(b, cx - m / 2, cy + 10.5f, m, m * 0.9f);
            using (var o = new Pen(Outline, 1.1f)) g.DrawEllipse(o, cx - m / 2, cy + 10.5f, m, m * 0.9f);
        }
        else if (Talking && !grabbed && !chewing)
        {
            // Mouth flaps while it talks.
            float open = 2 + 5 * Math.Abs((float)Math.Sin(t * 16));
            using (var b = new SolidBrush(Mouth)) g.FillEllipse(b, cx - 5, cy + 9.5f, 10, open);
            using (var b = new SolidBrush(Tongue)) if (open > 4) g.FillEllipse(b, cx - 3, cy + 9.5f + open - 3, 6, 3);
            using (var o = new Pen(Outline, 1.2f)) g.DrawEllipse(o, cx - 5, cy + 9.5f, 10, open);
        }
        else if (chewing && !grabbed)
        {
            bool open = Math.Sin(t * 17) > 0;
            if (open)
            {
                using (var b = new SolidBrush(Mouth)) g.FillEllipse(b, cx - 3.5f, cy + 10, 7, 4.5f);
                using (var o = new Pen(Outline, 1.2f)) g.DrawEllipse(o, cx - 3.5f, cy + 10, 7, 4.5f);
            }
            else using (var p = RoundPen(Ink, 2.2f)) g.DrawLine(p, cx - 4, cy + 12, cx + 4, cy + 11.5f);
        }
        else if (grabbed || biteOpen)
        {
            using (var b = new SolidBrush(Mouth)) g.FillEllipse(b, cx - 4.5f, cy + 9, 9, 10);
            using (var b = new SolidBrush(Tongue)) g.FillEllipse(b, cx - 3, cy + 14, 6, 4);
            using (var o = new Pen(Outline, 1.3f)) g.DrawEllipse(o, cx - 4.5f, cy + 9, 9, 10);
        }
        else if (grin)
        {
            using (var path = new GraphicsPath())
            {
                path.AddArc(cx - 11, cy + 1, 22, 17, 0, 180);
                path.CloseFigure();
                using (var b = new SolidBrush(Mouth)) g.FillPath(b, path);
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var b = new SolidBrush(Tongue)) g.FillEllipse(b, cx - 6, cy + 11, 12, 9);
                using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, cx - 11, cy + 9, 22, 3);
                g.Clip = clip;
                using (var o = new Pen(Outline, 1.4f) { LineJoin = LineJoin.Round }) g.DrawPath(o, path);
            }
        }
        else if (fm == Mood.Happy)
        {
            // A big easy smile in the sunshine.
            using (var p = RoundPen(Ink, 2.3f))
            {
                g.DrawBezier(p, new PointF(cx - 10, cy + 9), new PointF(cx - 5, cy + 16), new PointF(cx + 5, cy + 16), new PointF(cx + 10, cy + 9));
                g.DrawArc(p, cx - 13, cy + 6.5f, 4, 4, 300, 90);
                g.DrawArc(p, cx + 9, cy + 6.5f, 4, 4, 150, 90);
            }
        }
        else if (fm == Mood.Hot)
        {
            // Panting, tongue out.
            float pant = (float)Math.Abs(Math.Sin(t * 7));
            using (var b = new SolidBrush(Mouth)) g.FillEllipse(b, cx - 5.5f, cy + 9, 11, 6 + 2 * pant);
            using (var o = new Pen(Outline, 1.2f)) g.DrawEllipse(o, cx - 5.5f, cy + 9, 11, 6 + 2 * pant);
            using (var b = new SolidBrush(Tongue)) g.FillEllipse(b, cx - 3.5f, cy + 12 + 2 * pant, 7, 7);
            using (var o = new Pen(Dark(Tongue, 0.25f), 1f)) g.DrawLine(o, cx, cy + 14 + 2 * pant, cx, cy + 17.5f + 2 * pant);
        }
        else if (fm == Mood.Gloomy)
        {
            // A glum little frown.
            using (var p = RoundPen(Ink, 2.3f))
                g.DrawBezier(p, new PointF(cx - 8, cy + 14), new PointF(cx - 4, cy + 10), new PointF(cx + 4, cy + 10), new PointF(cx + 8, cy + 14));
        }
        else if (fm == Mood.Scared)
        {
            // A wobbly, nervous mouth.
            using (var p = RoundPen(Ink, 2f))
            {
                var pts = new PointF[7];
                for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(cx - 9 + i * 3, cy + 12 + (i % 2 == 0 ? 1.5f : -1.5f));
                g.DrawCurve(p, pts, 0.4f);
            }
        }
        else if (fm == Mood.Cold)
        {
            // Teeth chattering.
            float open = 1.2f + 1.6f * (Math.Sin(t * 38) > 0 ? 1 : 0);
            var teeth = new RectangleF(cx - 7, cy + 10.5f - open / 2, 14, 4 + open);
            using (var b = new SolidBrush(Mouth)) g.FillRectangle(b, teeth);
            using (var b = new SolidBrush(Color.White))
            {
                g.FillRectangle(b, teeth.X, teeth.Y, teeth.Width, 2);
                g.FillRectangle(b, teeth.X, teeth.Bottom - 2, teeth.Width, 2);
            }
            using (var o = new Pen(Outline, 1.2f))
            {
                g.DrawRectangle(o, teeth.X, teeth.Y, teeth.Width, teeth.Height);
                for (int i = 1; i < 4; i++) g.DrawLine(o, teeth.X + i * 3.5f, teeth.Y, teeth.X + i * 3.5f, teeth.Bottom);
            }
        }
        else if (fm == Mood.Meh)
        {
            using (var p = RoundPen(Ink, 2.3f)) g.DrawLine(p, cx - 6, cy + 12.5f, cx + 6, cy + 12);
        }
        else
        {
            using (var p = RoundPen(Ink, 2.3f))
            {
                // A confident little smirk, higher on one side.
                g.DrawBezier(p, new PointF(cx - 8, cy + 11), new PointF(cx - 3, cy + 14.5f), new PointF(cx + 4, cy + 13.5f), new PointF(cx + 9, cy + 8.5f));
                g.DrawArc(p, cx + 8, cy + 6.5f, 4, 4, 150, 90);    // dimple
            }
        }

        // Soft blush.
        foreach (float bx in new[] { cx - 13.5f, cx + 13.5f })
        {
            int a = grabbed || grin ? 80 : fm == Mood.Hot || fm == Mood.Cold ? 115 : 55;   // flushed in the heat, rosy in the cold
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 120, 130)))
            {
                g.FillEllipse(b, bx - 5, cy + 5, 10, 6);
                g.FillEllipse(b, bx - 3.5f, cy + 6, 7, 4);
            }
        }
        if (fm == Mood.Hot || fm == Mood.Scared)
        {
            // A bead of sweat sliding down by its brow, again and again.
            float k = (float)((t * (fm == Mood.Scared ? 0.9 : 0.6)) % 1.0);
            float sx = cx + 19, sy = cy - 16 + 9 * k;
            int alpha = (int)(230 * Math.Min(1, (1 - k) * 3));
            using (var drop = new GraphicsPath())
            {
                drop.AddBezier(sx, sy - 5, sx + 1.5f, sy - 2, sx + 3.5f, sy, sx + 3.5f, sy + 2);
                drop.AddArc(sx - 3.5f, sy - 1.5f, 7, 7, 0, 180);
                drop.AddBezier(sx - 3.5f, sy + 2, sx - 3.5f, sy, sx - 1.5f, sy - 2, sx, sy - 5);
                using (var b = new SolidBrush(Color.FromArgb(alpha, 150, 210, 255))) g.FillPath(b, drop);
                using (var o = new Pen(Color.FromArgb(alpha, 60, 130, 200), 1.1f)) g.DrawPath(o, drop);
                using (var hl = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) g.FillEllipse(hl, sx - 1.8f, sy + 0.5f, 1.8f, 2.2f);
            }
        }
        g.TranslateTransform(-faceShift, 0);
    }

    // A drawing surface that is reused every frame and copied straight to the screen.
    sealed class Canvas
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bi, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        public readonly Graphics G;
        readonly Bitmap bmp;
        readonly IntPtr dc, dib;
        readonly int w, h;

        public Canvas(int width, int height)
        {
            w = width; h = height;
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr bits;
            dc = CreateCompatibleDC(IntPtr.Zero);
            dib = CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
            SelectObject(dc, dib);
            bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            G = Graphics.FromImage(bmp);
        }

        public void Push(IntPtr hwnd, Point pos)
        {
            G.Flush(FlushIntention.Sync);
            var size = new SZ(w, h);
            var src = new PT(0, 0);
            var dst = new PT(pos.X, pos.Y);
            var blend = new BLEND { op = 0, flags = 0, alpha = 255, format = 1 };
            UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, 2);
        }
    }

    // Copy the bitmap onto the screen with per-pixel transparency.
    internal static void Layer(IntPtr handle, Bitmap bmp, Point pos)
    {
        int W = bmp.Width, H = bmp.Height;
        int Left = pos.X, Top = pos.Y;
        IntPtr Handle = handle;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(mem, hbmp);
        try
        {
            var size = new SZ(W, H);
            var src = new PT(0, 0);
            var dst = new PT(Left, Top);
            var blend = new BLEND { op = 0, flags = 0, alpha = 255, format = 1 };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

// A Claude Code process kept running for the chat, locked down: no tools, no personal settings, hooks or
// add-on servers, in an empty folder of its own. Questions go in, and replies stream out, as JSON lines.
sealed class ClaudeLink
{
    Process proc;
    readonly System.Text.StringBuilder errors = new System.Text.StringBuilder();
    volatile bool pending;
    public event Action<string> Text;                  // pieces of the reply as they arrive
    public event Action<bool, string, string> Done;    // ok, the whole reply (or what went wrong), session id

    public bool Running { get { try { return proc != null && !proc.HasExited; } catch { return false; } } }

    public bool Start(string brain, string resumeSession, out string error)
    {
        error = null;
        Stop();
        string exe = Monkey.FindClaude();
        if (exe == null) { error = "I can't find Claude Code on this PC. Install it (npm install -g @anthropic-ai/claude-code), then ask me again."; return false; }
        try
        {
            System.IO.Directory.CreateDirectory(Monkey.ChatDir);
            string personaFile = System.IO.Path.Combine(Monkey.ChatDir, "persona.txt");
            System.IO.File.WriteAllText(personaFile, Monkey.Persona);
            string model = brain == "smart" ? "opus" : brain == "balanced" ? "sonnet" : "haiku";
            string args = "-p --input-format stream-json --output-format stream-json --verbose --include-partial-messages"
                + " --tools \"\" --restricted --strict-mcp-config --model " + model + (brain == "smart" ? "" : " --effort low")
                + " --append-system-prompt-file \"" + personaFile + "\"";
            if (resumeSession != null && Regex.IsMatch(resumeSession, "^[0-9a-fA-F-]{8,64}$")) args += " --resume " + resumeSession;
            bool isCmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo(isCmd ? "cmd.exe" : exe, isCmd ? "/d /s /c \"\"" + exe + "\" " + args + "\"" : args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Monkey.ChatDir,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            // The monkey runs at low priority, which Claude Code would inherit; replies should come at full speed.
            var me = Process.GetCurrentProcess();
            try { me.PriorityClass = ProcessPriorityClass.Normal; } catch { }
            Process p;
            try { p = Process.Start(psi); }
            finally { try { me.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { } }
            proc = p;
            errors.Length = 0;
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errors) if (errors.Length < 2000) errors.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            var reader = new Thread(() => Read(p));
            reader.IsBackground = true;
            reader.Start();
            return true;
        }
        catch (Exception ex) { error = "I couldn't start Claude Code: " + ex.Message; proc = null; return false; }
    }

    public bool Ask(string prompt, out string error)
    {
        error = null;
        if (!Running) { error = "Claude Code isn't running. Try again in a moment."; return false; }
        try
        {
            var msg = new Dictionary<string, object> {
                { "type", "user" },
                { "message", new Dictionary<string, object> { { "role", "user" }, { "content", prompt } } } };
            string line = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(msg) + "\n";
            byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(line);   // UTF-8, so any language comes through
            pending = true;
            proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            proc.StandardInput.BaseStream.Flush();
            return true;
        }
        catch (Exception ex) { pending = false; error = "I couldn't reach Claude Code: " + ex.Message; return false; }
    }

    void Read(Process p)
    {
        var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        try
        {
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                Dictionary<string, object> d;
                try { d = json.DeserializeObject(line) as Dictionary<string, object>; } catch { continue; }
                if (d == null) continue;
                string type = Str(d, "type");
                if (type == "stream_event")
                {
                    var ev = d.ContainsKey("event") ? d["event"] as Dictionary<string, object> : null;
                    var delta = ev != null && ev.ContainsKey("delta") ? ev["delta"] as Dictionary<string, object> : null;
                    if (delta != null && Str(delta, "type") == "text_delta" && Text != null) Text(Str(delta, "text"));
                }
                else if (type == "result")
                {
                    pending = false;
                    object isErr;
                    bool failed = d.TryGetValue("is_error", out isErr) && isErr is bool && (bool)isErr;
                    string text = Str(d, "result");
                    if (Done != null) Done(!failed && text != null, failed || text == null ? "Claude Code had a problem: " + (text ?? "no answer") : text.Trim(), Str(d, "session_id"));
                }
            }
        }
        catch { }
        if (pending && Done != null)   // it stopped in the middle of an answer
        {
            pending = false;
            string err;
            lock (errors) err = errors.ToString().Trim();
            Done(false, "Claude Code stopped unexpectedly." + (err.Length > 0 ? " It said: " + (err.Length > 300 ? err.Substring(0, 300) + "..." : err) : ""), null);
        }
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        return d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
    }

    // Closing its input ends it politely; if it lingers, end it and everything it started.
    public void Stop()
    {
        var p = proc;
        proc = null;
        if (p == null) return;
        try { p.StandardInput.Close(); } catch { }
        try
        {
            if (!p.WaitForExit(1500))
                Process.Start(new ProcessStartInfo("taskkill", "/pid " + p.Id + " /t /f") { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }
}

// The "Ask Monkey" chat window.
class ChatBox : Form
{
    public event Action<string, bool, string> Send;    // your message, use copied text, attached file (or null)
    public event Action NewChat;
    readonly RichTextBox history = new RichTextBox();
    readonly TextBox input = new TextBox();
    readonly CheckBox useClip = new CheckBox();
    readonly Button send = new Button(), fresh = new Button(), attach = new Button();
    readonly Label status = new Label();
    string attached;     // the one file to send with the next message (you pick it)
    bool busy;

    // Found files, shown as links in the history: click the name to open it, "folder" to see it in Explorer.
    sealed class FileLink { public int start, end; public string path; public bool folder; }
    readonly List<FileLink> links = new List<FileLink>();
    static readonly Color LinkBlue = Color.FromArgb(20, 90, 200);
    static readonly Color Teal = Color.FromArgb(38, 104, 106), Brown = Color.FromArgb(112, 64, 32);

    public ChatBox(float k)   // k: the screen's scale (1.25 at 125%); the text grows with it, so the boxes must too
    {
        Text = "Ask Monkey \U0001F412";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Size = new Size((int)(420 * k), (int)(520 * k));
        MinimumSize = new Size((int)(380 * k), (int)(300 * k));
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(250, 247, 240);

        history.ReadOnly = true;
        history.BorderStyle = BorderStyle.None;
        history.BackColor = Color.White;
        history.Dock = DockStyle.Fill;
        history.Font = new Font("Segoe UI Emoji", 10f);   // like Segoe UI, but emoji show up instead of empty boxes
        history.DetectUrls = true;
        history.LinkClicked += (s, e) => { try { Process.Start(e.LinkText); } catch { } };
        history.MouseMove += (s, e) => history.Cursor = LinkAt(e.Location) != null ? Cursors.Hand : Cursors.IBeam;
        history.MouseUp += (s, e) =>
        {
            if (e.Button != MouseButtons.Left || history.SelectionLength > 0) return;
            var l = LinkAt(e.Location);
            if (l != null) Monkey.OpenFile(l.path, l.folder);
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = (int)(118 * k), Padding = new Padding((int)(8 * k), (int)(6 * k), (int)(8 * k), (int)(8 * k)) };
        input.Multiline = true;
        input.Dock = DockStyle.Fill;
        input.ScrollBars = ScrollBars.Vertical;
        input.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; DoSend(); }   // Shift+Enter: new line
        };
        var row = new Panel { Dock = DockStyle.Bottom, Height = (int)(34 * k) };
        useClip.Text = "Use copied text";
        useClip.AutoSize = true;
        useClip.Location = new Point(0, (int)(8 * k));
        attach.Text = "File...";
        attach.Size = new Size((int)(60 * k), (int)(28 * k));
        attach.FlatStyle = FlatStyle.Flat;
        attach.Click += (s, e) => PickFile();
        status.Click += (s, e) => { if (!busy && attached != null) { attached = null; ShowStatus(); } };   // click to take it off again
        send.Text = "Send";
        send.Size = new Size((int)(70 * k), (int)(28 * k));
        send.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        send.BackColor = Teal; send.ForeColor = Color.White; send.FlatStyle = FlatStyle.Flat;
        send.Click += (s, e) => DoSend();
        fresh.Text = "New chat";
        fresh.Size = new Size((int)(84 * k), (int)(28 * k));
        fresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        fresh.FlatStyle = FlatStyle.Flat;
        fresh.Click += (s, e) => { history.Clear(); links.Clear(); if (NewChat != null) NewChat(); FocusInput(); };
        row.Controls.Add(useClip); row.Controls.Add(attach); row.Controls.Add(fresh); row.Controls.Add(send);
        row.Resize += (s, e) =>
        {
            send.Location = new Point(row.Width - send.Width, (int)(3 * k));
            fresh.Location = new Point(send.Left - fresh.Width - (int)(6 * k), (int)(3 * k));
            attach.Location = new Point(fresh.Left - attach.Width - (int)(6 * k), (int)(3 * k));
        };
        status.Dock = DockStyle.Top; status.Height = (int)(20 * k); status.ForeColor = Color.Gray;
        bottom.Controls.Add(input); bottom.Controls.Add(status); bottom.Controls.Add(row);

        Controls.Add(history);
        Controls.Add(bottom);
        AddNote("Hi! I'm Monkey. Ask me anything; I only see what you type here (plus your copied text or one file, when you add them).\n"
            + "To find a file, type \"find\" and its name (like: find GST invoice), or \"open downloads\". I do that right here on your PC, "
            + "and none of it is sent anywhere.");
    }

    // ---- Attaching one file (sent with the next message only) ----

    void PickFile()
    {
        using (var d = new OpenFileDialog { Title = "Pick a file to ask Monkey about",
            Filter = "Files Monkey can read|*.txt;*.csv;*.md;*.log;*.json;*.xml;*.html;*.htm;*.docx;*.xlsx;*.pptx;*.cs;*.js;*.ts;*.py;*.java;*.sql;*.ini;*.yaml;*.yml|All files|*.*" })
            if (d.ShowDialog(this) == DialogResult.OK) { attached = d.FileName; ShowStatus(); FocusInput(); }
    }

    void ShowStatus()
    {
        status.Text = busy ? "Monkey is thinking..." : attached != null ? "\U0001F4CE " + System.IO.Path.GetFileName(attached) + "  (click here to remove)" : "";
        status.Cursor = !busy && attached != null ? Cursors.Hand : Cursors.Default;
    }

    // ---- Found files ----

    FileLink LinkAt(Point p)
    {
        if (links.Count == 0) return null;
        int i = history.GetCharIndexFromPosition(p);
        foreach (var l in links)
            if (i >= l.start && i < l.end)
            {
                // GetCharIndexFromPosition gives the nearest character, even past the end of a line: check it's really under the mouse.
                Point a = history.GetPositionFromCharIndex(i);
                return Math.Abs(p.Y - a.Y) < history.Font.Height * 1.6 && p.X >= a.X - 2 && p.X <= a.X + history.Font.Height ? l : null;
            }
        return null;
    }

    void AppendLink(string text, string path, bool folder)
    {
        history.SelectionStart = history.TextLength;
        history.SelectionFont = new Font(history.Font, FontStyle.Underline);
        history.SelectionColor = LinkBlue;
        int start = history.TextLength;
        history.AppendText(text);
        links.Add(new FileLink { start = start, end = history.TextLength, path = path, folder = folder });
        history.SelectionFont = history.Font;
        history.SelectionColor = Color.Black;
    }

    public void AddFiles(string heading, List<string> paths)
    {
        history.SelectionStart = history.TextLength;
        history.SelectionFont = new Font(history.Font, FontStyle.Bold);
        history.SelectionColor = Brown;
        history.AppendText((history.TextLength > 0 ? "\n" : "") + "Monkey:\n");
        history.SelectionFont = history.Font;
        history.SelectionColor = Color.Black;
        history.AppendText(heading + "\n");
        for (int n = 0; n < paths.Count; n++)
        {
            string p = paths[n];
            bool isDir = System.IO.Directory.Exists(p);
            history.SelectionStart = history.TextLength;
            history.SelectionColor = Color.Gray;
            history.AppendText((n + 1) + ". ");
            AppendLink((isDir ? "\U0001F4C1 " : "") + System.IO.Path.GetFileName(p.TrimEnd('\\')), p, false);
            history.SelectionStart = history.TextLength;
            history.SelectionFont = new Font(history.Font.FontFamily, history.Font.Size * 0.85f);
            history.SelectionColor = Color.Gray;
            string where = System.IO.Path.GetDirectoryName(p.TrimEnd('\\')) ?? "";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (where.StartsWith(home, StringComparison.OrdinalIgnoreCase)) where = "~" + where.Substring(home.Length);
            string when = "";
            try { when = ", " + (isDir ? System.IO.Directory.GetLastWriteTime(p) : System.IO.File.GetLastWriteTime(p)).ToString("d MMM yyyy"); } catch { }
            history.AppendText("   " + where + when + "  ");
            AppendLink("folder", p, true);
            history.AppendText("\n");
            history.SelectionFont = history.Font;
            history.SelectionColor = Color.Black;
        }
        history.ScrollToCaret();
    }

    public bool IsEmpty { get { return !history.Text.Contains("You:"); } }

    public void FocusInput() { input.Focus(); }

    void DoSend()
    {
        string text = input.Text;
        if (text.Trim().Length == 0 || !send.Enabled) return;
        input.Clear();
        string file = attached;
        attached = null;
        if (Send != null) Send(text, useClip.Checked, file);
        useClip.Checked = false;   // copied text and files are only sent when you ask, each time
        ShowStatus();
    }

    public void SetBusy(bool busy)
    {
        if (!busy && history.TextLength > 0 && !history.Text.EndsWith("\n")) history.AppendText("\n");
        this.busy = busy;
        send.Enabled = !busy;
        ShowStatus();
    }

    public void AddMessage(string who, string text)
    {
        history.SelectionStart = history.TextLength;
        history.SelectionFont = new Font(history.Font, FontStyle.Bold);
        history.SelectionColor = who == "You" ? Teal : Brown;
        history.AppendText((history.TextLength > 0 ? "\n" : "") + who + ":\n");
        history.SelectionFont = history.Font;
        history.SelectionColor = Color.Black;
        if (who == "You") history.AppendText(text.Replace("\r\n", "\n") + "\n");
        else { AppendFormatted(text); history.AppendText("\n"); }
        history.ScrollToCaret();
    }

    // Markdown the reply may use anyway: **bold**, *italic* or _italic_, `code` and # headings are shown
    // as styled text, without the symbols.
    static readonly Regex Marks = new Regex(@"\*\*(.+?)\*\*|__(.+?)__|(?<![\w*])\*(?![\s*])(.+?)(?<![\s*])\*(?![\w*])"
        + @"|(?<!\w)_(?![\s_])(.+?)(?<![\s_])_(?!\w)|`([^`\n]+)`");

    void AppendFormatted(string text)
    {
        text = Regex.Replace(text.Replace("\r\n", "\n"), @"(?m)^#{1,6}\s+", "");
        int at = 0;
        foreach (Match m in Marks.Matches(text))
        {
            AppendStyled(text.Substring(at, m.Index - at), FontStyle.Regular, null);
            if (m.Groups[1].Success) AppendStyled(m.Groups[1].Value, FontStyle.Bold, null);
            else if (m.Groups[2].Success) AppendStyled(m.Groups[2].Value, FontStyle.Bold, null);
            else if (m.Groups[3].Success) AppendStyled(m.Groups[3].Value, FontStyle.Italic, null);
            else if (m.Groups[4].Success) AppendStyled(m.Groups[4].Value, FontStyle.Italic, null);
            else AppendStyled(m.Groups[5].Value, FontStyle.Regular, "Consolas");
            at = m.Index + m.Length;
        }
        AppendStyled(text.Substring(at), FontStyle.Regular, null);
    }

    void AppendStyled(string text, FontStyle style, string family)
    {
        if (text.Length == 0) return;
        history.SelectionStart = history.TextLength;
        history.SelectionFont = family != null ? new Font(family, history.Font.Size)
            : style != FontStyle.Regular ? new Font(history.Font, style) : history.Font;
        history.SelectionColor = Color.Black;
        history.AppendText(text);
        history.SelectionFont = history.Font;
    }

    // A reply that arrives a piece at a time. The pieces are shown as plain text, then the whole reply again,
    // tidied up, once it's in.
    int replyStart = -1;

    public void BeginReply()
    {
        history.SelectionStart = history.TextLength;
        history.SelectionFont = new Font(history.Font, FontStyle.Bold);
        history.SelectionColor = Brown;
        history.AppendText((history.TextLength > 0 ? "\n" : "") + "Monkey:\n");
        history.SelectionFont = history.Font;
        history.SelectionColor = Color.Black;
        replyStart = history.TextLength;
    }

    public void FinishReply(string text)
    {
        if (replyStart < 0 || replyStart > history.TextLength) return;
        history.ReadOnly = false;   // a read-only box won't let even the program take text out
        history.Select(replyStart, history.TextLength - replyStart);
        history.SelectedText = "";
        history.ReadOnly = true;
        AppendFormatted(text);
        history.AppendText("\n");
        replyStart = -1;
        history.ScrollToCaret();
    }

    public void AppendReply(string piece)
    {
        history.SelectionStart = history.TextLength;
        history.SelectionFont = history.Font;
        history.SelectionColor = Color.Black;
        history.AppendText(piece.Replace("\r\n", "\n"));
        history.ScrollToCaret();
    }

    public void AddNote(string text)
    {
        history.SelectionStart = history.TextLength;
        history.SelectionFont = new Font(history.Font, FontStyle.Italic);
        history.SelectionColor = Color.Gray;
        history.AppendText((history.TextLength > 0 ? "\n" : "") + text + "\n");
        history.SelectionFont = history.Font;
        history.ScrollToCaret();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }   // keeps the conversation
        base.OnFormClosing(e);
    }
}

// A click-through window the tab blast is drawn in.
class FxLayer : Form
{
    public FxLayer()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80 | 0x8; // LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW | TOPMOST
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }
}

// A click-through window showing the old page while the monkey pulls it down.
class Cover : Form
{
    Bitmap image;

    public Cover()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80 | 0x8; // LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW | TOPMOST
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public void Display(Bitmap bmp, int x, int y)
    {
        Clear();
        image = bmp;
        Bounds = new Rectangle(x, y, bmp.Width, bmp.Height);
        if (!Visible) Show();
        Monkey.Layer(Handle, image, new Point(x, y));
    }

    public void MoveTo(int x, int y)
    {
        if (image != null) Location = new Point(x, y);
    }

    public void Clear()
    {
        if (Visible) Hide();
        if (image != null) { image.Dispose(); image = null; }
    }
}

// Windows Core Audio: just enough to read the speakers' current loudness.
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    int GetPeakValue(out float peak);
}
