// SparkleCursor - sparkles flutter from the mouse cursor as it moves.
// Build: csc /target:winexe /optimize /win32icon:SparkleCursor.ico SparkleCursor.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Native.MakeDpiAware();
        if (args.Length == 2 && args[0] == "--export-icon")
        {
            Gfx.WriteIco(args[1], new[] { 16, 20, 24, 32, 40, 48, 64, 256 });
            return;
        }
        bool created;
        using (var mutex = new Mutex(true, "SparkleCursor_SingleInstance", out created))
        {
            if (!created)
            {
                // Already running: launching again (e.g. from the Start Menu) opens its settings window.
                try
                {
                    Native.AllowSetForegroundWindow(-1); // ASFW_ANY: let the running instance take focus
                    EventWaitHandle.OpenExisting(SparkleContext.ShowEventName).Set();
                }
                catch { }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SparkleContext());
        }
    }
}

// ======================================================================
// Settings
// ======================================================================

enum Palette { Rainbow, Gold, Ice, Pink, Aurora, Fire, Pastel, Custom }

[Flags]
enum Sprite { Sparkle = 1, Star = 2, Heart = 4, Diamond = 8, Orb = 16, Snowflake = 32, Ring = 64, Glint = 128, Confetti = 256 }

class Settings
{
    const string Key = @"Software\SparkleCursor";

    public Palette Palette = Palette.Rainbow;
    public Color[] Custom = { Color.FromArgb(255, 122, 200), Color.FromArgb(120, 190, 255), Color.FromArgb(255, 225, 120) };
    public int Sprites = (int)(Sprite.Sparkle | Sprite.Star | Sprite.Orb | Sprite.Glint);
    public float Density = 1, SpriteSize = 1, Lifetime = 1, Gravity = 140, Flutter = 1, Spread = 1, Glow = 1, Twinkle = 1;
    public bool Enabled = true, Welcomed;

    public bool Has(Sprite sp) { return (Sprites & (int)sp) != 0; }

    public void ResetLook()
    {
        var d = new Settings();
        Palette = d.Palette; Custom = d.Custom; Sprites = d.Sprites;
        Density = d.Density; SpriteSize = d.SpriteSize; Lifetime = d.Lifetime; Gravity = d.Gravity;
        Flutter = d.Flutter; Spread = d.Spread; Glow = d.Glow; Twinkle = d.Twinkle;
    }

    public void Load()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
            {
                if (k == null) return;
                Palette = (Palette)Enum.Parse(typeof(Palette), Str(k, "Palette", Str(k, "Mode", Palette.ToString())));
                for (int i = 0; i < Custom.Length; i++) Custom[i] = Color.FromArgb(Int(k, "Custom" + i, Custom[i].ToArgb()));
                Sprites = Int(k, "Sprites", Sprites);
                Density = Flt(k, "Density", Density);
                SpriteSize = Flt(k, "Size", SpriteSize);
                Lifetime = Flt(k, "Lifetime", Lifetime);
                Gravity = Flt(k, "Gravity", Gravity);
                Flutter = Flt(k, "Flutter", Flutter);
                Spread = Flt(k, "Spread", Spread);
                Glow = Flt(k, "Glow", Glow);
                Twinkle = Flt(k, "Twinkle", Twinkle);
                Enabled = Int(k, "Enabled", 1) != 0;
                Welcomed = Int(k, "Welcomed", 0) != 0;
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                k.SetValue("Palette", Palette.ToString());
                for (int i = 0; i < Custom.Length; i++) k.SetValue("Custom" + i, Custom[i].ToArgb(), RegistryValueKind.DWord);
                k.SetValue("Sprites", Sprites, RegistryValueKind.DWord);
                SetF(k, "Density", Density); SetF(k, "Size", SpriteSize); SetF(k, "Lifetime", Lifetime);
                SetF(k, "Gravity", Gravity); SetF(k, "Flutter", Flutter); SetF(k, "Spread", Spread);
                SetF(k, "Glow", Glow); SetF(k, "Twinkle", Twinkle);
                k.SetValue("Enabled", Enabled ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("Welcomed", Welcomed ? 1 : 0, RegistryValueKind.DWord);
                if (k.GetValue("Mode") != null) k.DeleteValue("Mode", false);
            }
        }
        catch { }
    }

    static string Str(RegistryKey k, string n, string d) { var v = k.GetValue(n) as string; return v ?? d; }
    static int Int(RegistryKey k, string n, int d) { var v = k.GetValue(n); return v is int ? (int)v : d; }
    static float Flt(RegistryKey k, string n, float d)
    {
        float f;
        var v = k.GetValue(n) as string;
        return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : d;
    }
    static void SetF(RegistryKey k, string n, float v) { k.SetValue(n, v.ToString("R", CultureInfo.InvariantCulture)); }
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "SparkleCursor";

    public static bool IsEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppName) != null;
    }

    public static void Set(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(AppName, false);
        }
    }
}

// ======================================================================
// Particles
// ======================================================================

class Particle
{
    public float X, Y, VX, VY, Age, Life, Size, Rot, Spin, Phase, WobbleAmp, WobbleFreq, FlipFreq;
    public Sprite Sp;
    public Color Col;
}

class ParticleSystem
{
    public static readonly Sprite[] AllSprites = (Sprite[])Enum.GetValues(typeof(Sprite));

    public readonly List<Particle> Particles = new List<Particle>();
    public int Max = 500;
    readonly Settings s;
    readonly float scale;
    readonly Random rng = new Random();
    readonly List<Sprite> enabled = new List<Sprite>();
    float spawnDebt, hueCursor;

    public ParticleSystem(Settings settings, float scale)
    {
        s = settings;
        this.scale = scale;
    }

    float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    public void Emit(float x0, float y0, float x1, float y1, float dt)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
        if (dist < 0.5f || dist > 3000) return;
        spawnDebt += Math.Min(dist / (7f * scale), 14f) * s.Density;
        int n = (int)spawnDebt;
        spawnDebt -= n;
        float idt = 1f / Math.Max(dt, 0.001f);
        for (int i = 0; i < n && Particles.Count < Max; i++)
        {
            float t = (i + 1f) / n; // spread along the path moved this frame
            Spawn(x0 + dx * t, y0 + dy * t, dx * idt, dy * idt);
        }
    }

    void Spawn(float x, float y, float cvx, float cvy)
    {
        enabled.Clear();
        foreach (var sp in AllSprites) if (s.Has(sp)) enabled.Add(sp);
        if (enabled.Count == 0) enabled.Add(Sprite.Sparkle);

        var p = new Particle
        {
            Sp = enabled[rng.Next(enabled.Count)],
            Col = NextColor(),
            X = x + R(-4, 4) * scale,
            Y = y + R(-4, 4) * scale,
            VX = R(-70, 70) * s.Spread * scale - cvx * 0.04f,
            VY = R(-90, 10) * s.Spread * scale - cvy * 0.04f,
            Life = R(0.7f, 1.6f) * s.Lifetime,
            Size = (rng.NextDouble() < 0.15 ? R(7, 11) : R(3, 7)) * s.SpriteSize * scale,
            Rot = R(0, 360),
            Spin = R(-200, 200),
            Phase = R(0, 6.28f),
            WobbleAmp = R(20, 60) * scale,
            WobbleFreq = R(4, 9),
            FlipFreq = R(6, 14),
        };
        if (p.Sp == Sprite.Confetti) p.Spin = R(-420, 420);
        if (p.Sp == Sprite.Glint) p.Spin *= 0.2f;
        Particles.Add(p);
    }

    Color NextColor()
    {
        hueCursor = (hueCursor + 3.5f) % 360f;
        switch (s.Palette)
        {
            case Palette.Gold: return Gfx.Hsv(R(35, 55), 0.7f, 1);
            case Palette.Ice: return Gfx.Hsv(R(180, 220), 0.55f, 1);
            case Palette.Pink: return Gfx.Hsv(R(300, 345), 0.55f, 1);
            case Palette.Aurora: return Gfx.Hsv(120 + 160 * (0.5f + 0.5f * (float)Math.Sin(hueCursor * Math.PI / 180)) + R(-10, 10), 0.65f, 1);
            case Palette.Fire: return Gfx.Hsv(R(0, 50), R(0.75f, 0.95f), 1);
            case Palette.Pastel: return Gfx.Hsv(hueCursor + R(-20, 20), 0.32f, 1);
            case Palette.Custom: return Gfx.Blend(s.Custom[rng.Next(s.Custom.Length)], Color.White, R(0, 0.25f));
            default: return Gfx.Hsv(hueCursor + R(-20, 20), 0.6f, 1);
        }
    }

    public void Update(float dt)
    {
        float dragX = (float)Math.Pow(0.25, dt), dragY = (float)Math.Pow(0.55, dt);
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Age += dt;
            if (p.Age >= p.Life) { Particles.RemoveAt(i); continue; }
            p.VY += s.Gravity * scale * dt;
            p.VX *= dragX;
            p.VY *= dragY;
            p.X += (p.VX + (float)Math.Sin(p.Age * p.WobbleFreq + p.Phase) * p.WobbleAmp * s.Flutter) * dt;
            p.Y += p.VY * dt;
            p.Rot += p.Spin * dt;
        }
    }

    public RectangleF Bounds()
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in Particles)
        {
            float e = p.Size * 2.6f;
            minX = Math.Min(minX, p.X - e); minY = Math.Min(minY, p.Y - e);
            maxX = Math.Max(maxX, p.X + e); maxY = Math.Max(maxY, p.Y + e);
        }
        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    public void Draw(Graphics g, float ox, float oy, double now)
    {
        float amp = 0.7f * s.Twinkle;
        foreach (var p in Particles)
        {
            float lt = p.Age / p.Life;
            float fadeIn = Math.Min(1f, p.Age / 0.08f);
            float fadeOut = 1f - lt * lt;
            float tw = 1f - amp * (0.5f - 0.5f * (float)Math.Sin(now * 18 + p.Phase * 3));
            float a = Math.Max(0f, Math.Min(1f, fadeIn * fadeOut * tw));
            float sz = p.Sp == Sprite.Ring ? p.Size * (0.6f + 0.8f * lt) : p.Size * (1f - 0.4f * lt);
            Gfx.DrawSprite(g, p.Sp, p.X - ox, p.Y - oy, sz, p.Rot, p.Col, a, s.Glow, p.Age * p.FlipFreq);
        }
    }
}

// ======================================================================
// Drawing helpers
// ======================================================================

static class Gfx
{
    public static Color Hsv(float h, float s, float v)
    {
        h = ((h % 360) + 360) % 360;
        float c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    public static Color Blend(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static PointF[] StarPoints(float cx, float cy, float r, float innerRatio, float rotDeg, int n)
    {
        var pts = new PointF[n * 2];
        double rot = rotDeg * Math.PI / 180;
        for (int i = 0; i < n * 2; i++)
        {
            double ang = rot + i * Math.PI / n;
            float rr = (i % 2 == 0) ? r : r * innerRatio;
            pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rr), cy + (float)(Math.Sin(ang) * rr));
        }
        return pts;
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static LinearGradientBrush Grad(RectangleF r, Color[] stops, float angle)
    {
        var rr = new RectangleF(r.X - 1, r.Y - 1, Math.Max(r.Width, 1) + 2, Math.Max(r.Height, 1) + 2);
        var b = new LinearGradientBrush(rr, stops[0], stops[stops.Length - 1], angle);
        if (stops.Length > 2)
        {
            var cb = new ColorBlend(stops.Length);
            var pos = new float[stops.Length];
            for (int i = 0; i < pos.Length; i++) pos[i] = i / (pos.Length - 1f);
            cb.Colors = stops;
            cb.Positions = pos;
            b.InterpolationColors = cb;
        }
        return b;
    }

    public static Color[] PaletteStops(Palette p, Settings s)
    {
        switch (p)
        {
            case Palette.Gold: return new[] { Color.FromArgb(255, 180, 60), Color.FromArgb(255, 222, 120), Color.FromArgb(255, 245, 205) };
            case Palette.Ice: return new[] { Color.FromArgb(120, 175, 255), Color.FromArgb(140, 225, 255), Color.FromArgb(225, 248, 255) };
            case Palette.Pink: return new[] { Color.FromArgb(255, 110, 190), Color.FromArgb(255, 170, 225), Color.FromArgb(220, 120, 255) };
            case Palette.Aurora: return new[] { Hsv(120, .65f, 1), Hsv(170, .65f, 1), Hsv(220, .65f, 1), Hsv(280, .65f, 1) };
            case Palette.Fire: return new[] { Color.FromArgb(255, 60, 50), Color.FromArgb(255, 140, 40), Color.FromArgb(255, 225, 90) };
            case Palette.Pastel: return new[] { Hsv(0, .32f, 1), Hsv(60, .32f, 1), Hsv(120, .32f, 1), Hsv(200, .32f, 1), Hsv(280, .32f, 1) };
            case Palette.Custom: return (Color[])s.Custom.Clone();
            default: return new[] { Hsv(0, .6f, 1), Hsv(60, .6f, 1), Hsv(120, .6f, 1), Hsv(180, .6f, 1), Hsv(240, .6f, 1), Hsv(300, .6f, 1) };
        }
    }

    public static void DrawSprite(Graphics g, Sprite sp, float cx, float cy, float sz, float rot, Color c, float a, float glow, float flip)
    {
        if (sz < 0.3f || a <= 0.004f) return;
        int A = (int)(Math.Min(1f, a) * 255);

        if (glow > 0.01f)
        {
            float gr = sz * 2.1f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                using (var pb = new PathGradientBrush(path))
                {
                    pb.CenterColor = Color.FromArgb(Math.Min(255, (int)(a * 110 * glow)), c);
                    pb.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(pb, path);
                }
            }
        }

        using (var fill = new SolidBrush(Color.FromArgb(A, c)))
        using (var white = new SolidBrush(Color.FromArgb(A, 255, 255, 255)))
        {
            GraphicsState st;
            switch (sp)
            {
                case Sprite.Sparkle:
                    g.FillPolygon(fill, StarPoints(cx, cy, sz * 1.5f, 0.22f, rot, 4));
                    g.FillPolygon(white, StarPoints(cx, cy, sz * 0.7f, 0.3f, rot, 4));
                    break;

                case Sprite.Star:
                    g.FillPolygon(fill, StarPoints(cx, cy, sz * 1.35f, 0.45f, rot - 90, 5));
                    g.FillPolygon(white, StarPoints(cx, cy, sz * 0.55f, 0.45f, rot - 90, 5));
                    break;

                case Sprite.Glint:
                    g.FillPolygon(fill, StarPoints(cx, cy, sz * 2.2f, 0.07f, rot, 4));
                    g.FillPolygon(fill, StarPoints(cx, cy, sz * 1.1f, 0.1f, rot + 45, 4));
                    g.FillEllipse(white, cx - sz * 0.35f, cy - sz * 0.35f, sz * 0.7f, sz * 0.7f);
                    break;

                case Sprite.Orb:
                {
                    float d = sz * 0.6f;
                    using (var b = new SolidBrush(Color.FromArgb(A, Blend(c, Color.White, 0.4f))))
                        g.FillEllipse(b, cx - d, cy - d, d * 2, d * 2);
                    float w = d * 0.45f;
                    g.FillEllipse(white, cx - w - d * 0.2f, cy - w - d * 0.2f, w * 2, w * 2);
                    break;
                }

                case Sprite.Ring:
                    using (var pen = new Pen(Color.FromArgb(A, Blend(c, Color.White, 0.3f)), Math.Max(1f, sz * 0.25f)))
                        g.DrawEllipse(pen, cx - sz * 0.8f, cy - sz * 0.8f, sz * 1.6f, sz * 1.6f);
                    break;

                case Sprite.Diamond:
                {
                    st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(rot * 0.3f);
                    g.FillPolygon(fill, new[] { new PointF(0, -1.5f * sz), new PointF(0.85f * sz, 0), new PointF(0, 1.5f * sz), new PointF(-0.85f * sz, 0) });
                    float k = 0.42f;
                    g.FillPolygon(white, new[] { new PointF(0, -1.5f * sz * k), new PointF(0.85f * sz * k, 0), new PointF(0, 1.5f * sz * k), new PointF(-0.85f * sz * k, 0) });
                    g.Restore(st);
                    break;
                }

                case Sprite.Heart:
                {
                    st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform((float)(Math.Sin(rot * Math.PI / 180) * 20));
                    float k = sz * 1.15f;
                    g.ScaleTransform(k, k);
                    using (var hp = new GraphicsPath())
                    {
                        hp.AddBezier(0f, .95f, -1.4f, -.1f, -.75f, -1.2f, 0f, -.4f);
                        hp.AddBezier(0f, -.4f, .75f, -1.2f, 1.4f, -.1f, 0f, .95f);
                        hp.CloseFigure();
                        g.FillPath(fill, hp);
                    }
                    using (var hb = new SolidBrush(Color.FromArgb((int)(A * 0.7f), 255, 255, 255)))
                        g.FillEllipse(hb, -.68f, -.62f, .38f, .26f);
                    g.Restore(st);
                    break;
                }

                case Sprite.Snowflake:
                {
                    st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(rot);
                    using (var pen = new Pen(Color.FromArgb(A, Blend(c, Color.White, 0.45f)), Math.Max(1f, sz * 0.17f)))
                    {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        float r = sz * 1.4f;
                        for (int i = 0; i < 6; i++)
                        {
                            double an = i * Math.PI / 3;
                            float ex = (float)Math.Cos(an) * r, ey = (float)Math.Sin(an) * r;
                            g.DrawLine(pen, 0, 0, ex, ey);
                            float bx = ex * 0.55f, by = ey * 0.55f;
                            for (int sgn = -1; sgn <= 1; sgn += 2)
                            {
                                double ba = an + sgn * 0.7;
                                g.DrawLine(pen, bx, by, bx + (float)Math.Cos(ba) * r * 0.35f, by + (float)Math.Sin(ba) * r * 0.35f);
                            }
                        }
                    }
                    g.FillEllipse(white, -sz * 0.25f, -sz * 0.25f, sz * 0.5f, sz * 0.5f);
                    g.Restore(st);
                    break;
                }

                case Sprite.Confetti:
                {
                    st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(rot);
                    float cos = (float)Math.Cos(flip);
                    float w = sz * 1.5f * Math.Max(0.15f, Math.Abs(cos)), h = sz * 0.8f;
                    var col = cos >= 0 ? c : Blend(c, Color.Black, 0.25f);
                    using (var b = new SolidBrush(Color.FromArgb(A, col)))
                        g.FillRectangle(b, -w / 2, -h / 2, w, h);
                    g.Restore(st);
                    break;
                }
            }
        }
    }

    public static Bitmap MakeLogo(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float s = size, cx = .44f * s, cy = .56f * s;
            if (size >= 40)
            {
                using (var path = new GraphicsPath())
                {
                    float gr = .46f * s;
                    path.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                    using (var pb = new PathGradientBrush(path))
                    {
                        pb.CenterColor = Color.FromArgb(110, UI.A2);
                        pb.SurroundColors = new[] { Color.FromArgb(0, UI.A2) };
                        g.FillPath(pb, path);
                    }
                }
            }
            using (var br = Grad(new RectangleF(0, 0, s, s), new[] { UI.A1, UI.A2, UI.A3 }, 45f))
                g.FillPolygon(br, StarPoints(cx, cy, .43f * s, .26f, 0, 4));
            using (var b = new SolidBrush(Color.White))
                g.FillPolygon(b, StarPoints(cx, cy, .16f * s, .32f, 0, 4));
            using (var b = new SolidBrush(Color.FromArgb(255, 215, 240)))
                g.FillPolygon(b, StarPoints(.80f * s, .2f * s, .17f * s, .28f, 0, 4));
            if (size >= 20)
                using (var b = new SolidBrush(UI.A3))
                    g.FillEllipse(b, .11f * s, .11f * s, .12f * s, .12f * s);
        }
        return bmp;
    }

    public static Icon MakeIcon(int size)
    {
        using (var bmp = MakeLogo(size)) return Icon.FromHandle(bmp.GetHicon());
    }

    public static Bitmap PaletteSwatch(Palette p, Settings s, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var r = new RectangleF(1, 1, size - 2, size - 2);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                using (var br = Grad(r, PaletteStops(p, s), 45f)) g.FillPath(br, path);
            }
        }
        return bmp;
    }

    public static void WriteIco(string path, int[] sizes)
    {
        var pngs = new List<byte[]>();
        foreach (var size in sizes)
            using (var bmp = MakeLogo(size))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var png in pngs) w.Write(png);
        }
    }
}

// ======================================================================
// Theme
// ======================================================================

static class UI
{
    public static float S = 1f;
    public static readonly Color Bg = Color.FromArgb(18, 16, 26);
    public static readonly Color Card = Color.FromArgb(28, 25, 39);
    public static readonly Color CardBorder = Color.FromArgb(48, 43, 66);
    public static readonly Color ChipBg = Color.FromArgb(38, 34, 53);
    public static readonly Color ChipHover = Color.FromArgb(48, 43, 66);
    public static readonly Color ChipSel = Color.FromArgb(55, 42, 84);
    public static readonly Color Track = Color.FromArgb(52, 47, 72);
    public static readonly Color Text = Color.FromArgb(240, 236, 250);
    public static readonly Color Sub = Color.FromArgb(150, 143, 172);
    public static readonly Color A1 = Color.FromArgb(255, 122, 200);
    public static readonly Color A2 = Color.FromArgb(150, 120, 255);
    public static readonly Color A3 = Color.FromArgb(100, 205, 255);
    public static readonly Color[] Accent = { A1, A2, A3 };

    public static Font Title, H, Body, Small, SmallBold, Value;
    public static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    public static readonly StringFormat LeftMid = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
    public static readonly StringFormat RightMid = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

    public static void Init(float scale)
    {
        S = scale;
        Title = new Font("Segoe UI Semibold", 17f);
        H = new Font("Segoe UI Semibold", 10.5f);
        Body = new Font("Segoe UI", 9.5f);
        Small = new Font("Segoe UI", 8.5f);
        SmallBold = new Font("Segoe UI Semibold", 8.5f);
        Value = new Font("Segoe UI Semibold", 9f);
    }

    public static int D(float v) { return (int)Math.Round(v * S); }

    public static void Prep(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}

// ======================================================================
// Custom controls
// ======================================================================

class FancyControl : Control
{
    protected bool Hover;

    public FancyControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick | ControlStyles.Selectable, false);
        BackColor = UI.Card;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected Graphics Prep(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        UI.Prep(g);
        return g;
    }
}

class Chip : FancyControl
{
    public bool Selected;
    public Action<Graphics, RectangleF, bool> Art;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var path = Gfx.Round(r, UI.D(10)))
        {
            using (var b = new SolidBrush(Selected ? UI.ChipSel : Hover ? UI.ChipHover : UI.ChipBg)) g.FillPath(b, path);
            if (Selected)
                using (var br = Gfx.Grad(r, UI.Accent, 0f))
                using (var pen = new Pen(br, 1.6f * UI.S))
                    g.DrawPath(pen, path);
        }
        float textH = UI.D(20);
        if (Art != null)
            Art(g, new RectangleF(r.X + UI.D(6), r.Y + UI.D(6), r.Width - UI.D(12), r.Height - textH - UI.D(8)), Selected);
        using (var b = new SolidBrush(Selected ? UI.Text : UI.Sub))
            g.DrawString(Text, Selected ? UI.SmallBold : UI.Small, b, new RectangleF(r.X, r.Bottom - textH - UI.D(4), r.Width, textH), UI.Center);
    }
}

class Slider : FancyControl
{
    public string Label;
    public float Min, Max, Value, Default;
    public Func<float, string> Format;
    public event Action<float> Changed;
    bool drag;

    public Slider()
    {
        SetStyle(ControlStyles.StandardDoubleClick, true);
    }

    RectangleF Track
    {
        get { float pad = UI.D(9); return new RectangleF(pad, UI.D(28), Width - pad * 2, UI.D(6)); }
    }

    void Set(float v)
    {
        v = Math.Max(Min, Math.Min(Max, v));
        if (v == Value) return;
        Value = v;
        Invalidate();
        if (Changed != null) Changed(v);
    }

    void SetFromX(int x)
    {
        var t = Track;
        Set(Min + (x - t.X) / t.Width * (Max - Min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { drag = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Set(Value + Math.Sign(e.Delta) * (Max - Min) / 50f); base.OnMouseWheel(e); }
    protected override void OnDoubleClick(EventArgs e) { Set(Default); base.OnDoubleClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        using (var b = new SolidBrush(UI.Sub))
            g.DrawString(Label, UI.Small, b, new RectangleF(UI.D(4), 0, Width, UI.D(20)), UI.LeftMid);
        using (var b = new SolidBrush(UI.Text))
            g.DrawString(Format != null ? Format(Value) : Value.ToString("0.00"), UI.Value, b, new RectangleF(0, 0, Width - UI.D(4), UI.D(20)), UI.RightMid);

        var t = Track;
        using (var path = Gfx.Round(t, t.Height / 2))
        using (var b = new SolidBrush(UI.Track))
            g.FillPath(b, path);

        float frac = (Value - Min) / (Max - Min);
        float fx = t.X + t.Width * frac;
        if (fx - t.X > 1)
            using (var path = Gfx.Round(new RectangleF(t.X, t.Y, fx - t.X, t.Height), t.Height / 2))
            using (var br = Gfx.Grad(t, UI.Accent, 0f))
                g.FillPath(br, path);

        float cy = t.Y + t.Height / 2;
        float gr = UI.D(11), tr = UI.D(7), dr = UI.D(3);
        using (var b = new SolidBrush(Color.FromArgb(Hover || drag ? 80 : 40, UI.A2)))
            g.FillEllipse(b, fx - gr, cy - gr, gr * 2, gr * 2);
        using (var b = new SolidBrush(Color.White))
            g.FillEllipse(b, fx - tr, cy - tr, tr * 2, tr * 2);
        var dot = frac < 0.5f ? Gfx.Blend(UI.A1, UI.A2, frac * 2) : Gfx.Blend(UI.A2, UI.A3, frac * 2 - 1);
        using (var b = new SolidBrush(dot))
            g.FillEllipse(b, fx - dr, cy - dr, dr * 2, dr * 2);
    }
}

class Toggle : FancyControl
{
    public bool Checked;
    public event Action<bool> Changed;

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        Invalidate();
        if (Changed != null) Changed(Checked);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float sw = UI.D(40), sh = UI.D(22);
        var r = new RectangleF(1, (Height - sh) / 2, sw, sh);
        using (var path = Gfx.Round(r, sh / 2))
        {
            if (Checked) using (var br = Gfx.Grad(r, UI.Accent, 0f)) g.FillPath(br, path);
            else using (var b = new SolidBrush(Hover ? Color.FromArgb(70, 63, 92) : Color.FromArgb(58, 52, 78))) g.FillPath(b, path);
        }
        float d = sh - UI.D(6);
        float kx = Checked ? r.Right - UI.D(3) - d : r.X + UI.D(3);
        using (var b = new SolidBrush(Checked ? Color.White : Color.FromArgb(205, 200, 222)))
            g.FillEllipse(b, kx, r.Y + UI.D(3), d, d);
        using (var b = new SolidBrush(UI.Text))
            g.DrawString(Text, UI.Body, b, new RectangleF(sw + UI.D(10), 0, Width - sw - UI.D(10), Height), UI.LeftMid);
    }
}

class Swatch : FancyControl
{
    public Color Color;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float w = UI.D(2);
        var r = new RectangleF(w, w, Width - w * 2 - 1, Height - w * 2 - 1);
        using (var pen = new Pen(Hover ? UI.Text : UI.CardBorder, w)) g.DrawEllipse(pen, r);
        r.Inflate(-w * 1.5f, -w * 1.5f);
        using (var b = new SolidBrush(Color)) g.FillEllipse(b, r);
    }
}

class PillButton : FancyControl
{
    public bool Primary;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var path = Gfx.Round(r, r.Height / 2))
        {
            if (Primary)
            {
                using (var br = Gfx.Grad(r, UI.Accent, 0f)) g.FillPath(br, path);
                if (Hover) using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(b, path);
            }
            else
            {
                using (var b = new SolidBrush(Hover ? UI.ChipHover : UI.ChipBg)) g.FillPath(b, path);
                using (var pen = new Pen(UI.CardBorder, 1f)) g.DrawPath(pen, path);
            }
        }
        using (var b = new SolidBrush(Primary ? Color.FromArgb(24, 18, 36) : UI.Text))
            g.DrawString(Text, UI.Value, b, r, UI.Center);
    }
}

class PreviewPanel : FancyControl
{
    public readonly ParticleSystem Sys;
    double now, ghostT;
    PointF last;
    bool hasLast, wasInside;

    public PreviewPanel(Settings s)
    {
        Sys = new ParticleSystem(s, UI.S);
        Sys.Max = 350;
        BackColor = UI.Bg;
        Cursor = Cursors.Default;
    }

    public void Step(float dt, double t)
    {
        now = t;
        Point mp = PointToClient(Control.MousePosition);
        bool inside = ClientRectangle.Contains(mp);
        PointF p;
        if (inside) p = new PointF(mp.X, mp.Y);
        else
        {
            ghostT += dt;
            p = new PointF(Width / 2f + Width * 0.36f * (float)Math.Sin(ghostT * 1.3),
                           Height / 2f + Height * 0.28f * (float)Math.Sin(ghostT * 2.1 + 0.6));
        }
        if (hasLast && inside == wasInside) Sys.Emit(last.X, last.Y, p.X, p.Y, dt);
        last = p; hasLast = true; wasInside = inside;
        Sys.Update(dt);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var path = Gfx.Round(r, UI.D(14)))
        {
            using (var bg = Gfx.Grad(r, new[] { Color.FromArgb(14, 12, 22), Color.FromArgb(34, 24, 54) }, 60f)) g.FillPath(bg, path);
            var st = g.Save();
            g.SetClip(path);
            float step = UI.D(22), dot = Math.Max(1.5f, UI.S * 1.5f);
            using (var b = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                for (float x = step / 2; x < Width; x += step)
                    for (float y = step / 2; y < Height; y += step)
                        g.FillEllipse(b, x - dot / 2, y - dot / 2, dot, dot);

            Sys.Draw(g, 0, 0, now);

            using (var b = new SolidBrush(UI.Sub))
                g.DrawString("LIVE PREVIEW", UI.SmallBold, b, UI.D(14), UI.D(10));
            if (!wasInside)
                using (var b = new SolidBrush(Color.FromArgb(150, UI.Sub)))
                    g.DrawString("Move your mouse in here to play", UI.Small, b, new RectangleF(0, Height - UI.D(34), Width, UI.D(24)), UI.Center);
            g.Restore(st);
            using (var pen = new Pen(UI.CardBorder, 1f)) g.DrawPath(pen, path);
        }
    }
}

// ======================================================================
// Settings window
// ======================================================================

class SettingsForm : Form
{
    readonly Settings s;
    readonly Dictionary<Palette, Chip> paletteChips = new Dictionary<Palette, Chip>();
    readonly Dictionary<Sprite, Chip> spriteChips = new Dictionary<Sprite, Chip>();
    readonly List<Tuple<Slider, Func<float>>> sliders = new List<Tuple<Slider, Func<float>>>();
    readonly Swatch[] swatches = new Swatch[3];
    readonly Toggle enabledToggle, startupToggle;
    readonly PreviewPanel preview;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Bitmap logo;
    double lastT;

    static readonly Rectangle PaletteCard = new Rectangle(24, 92, 420, 214);
    static readonly Rectangle SpriteCard = new Rectangle(24, 318, 420, 238);
    static readonly Rectangle BehaviourCard = new Rectangle(456, 318, 420, 238);

    static int D(float v) { return UI.D(v); }
    static Rectangle DR(int x, int y, int w, int h) { return new Rectangle(D(x), D(y), D(w), D(h)); }

    public SettingsForm(Settings settings, Icon icon)
    {
        s = settings;
        Text = "Sparkle Cursor";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UI.Bg;
        ForeColor = UI.Text;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(D(900), D(630));
        logo = Gfx.MakeLogo(D(48));

        // header
        enabledToggle = new Toggle { Text = "Sparkles on", BackColor = UI.Bg, Bounds = DR(736, 32, 140, 32) };
        enabledToggle.Changed += v => { s.Enabled = v; };
        Controls.Add(enabledToggle);

        // palettes
        var palettes = (Palette[])Enum.GetValues(typeof(Palette));
        for (int i = 0; i < palettes.Length; i++)
        {
            var p = palettes[i];
            var chip = new Chip { Text = p.ToString(), Bounds = DR(40 + (i % 4) * 99, 134 + (i / 4) * 58, 91, 50) };
            chip.Art = (g, r, sel) =>
            {
                var bar = new RectangleF(r.X, r.Y + (r.Height - D(10)) / 2, r.Width, D(10));
                using (var path = Gfx.Round(bar, bar.Height / 2))
                using (var br = Gfx.Grad(bar, Gfx.PaletteStops(p, s), 0f))
                    g.FillPath(br, path);
                if (sel)
                    using (var b = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                        g.FillPolygon(b, Gfx.StarPoints(bar.Right - D(6), bar.Y + bar.Height / 2, D(6), 0.25f, 0, 4));
            };
            chip.Click += (o, e) => { s.Palette = p; SyncUI(); };
            paletteChips[p] = chip;
            Controls.Add(chip);
        }
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            swatches[i] = new Swatch { Bounds = DR(318 + i * 40, 252, 30, 30) };
            swatches[i].Click += (o, e) => PickColor(idx);
            Controls.Add(swatches[i]);
        }

        // sprites
        for (int i = 0; i < ParticleSystem.AllSprites.Length; i++)
        {
            var sp = ParticleSystem.AllSprites[i];
            var chip = new Chip { Text = sp.ToString(), Bounds = DR(40 + (i % 5) * 79, 360 + (i / 5) * 92, 71, 84) };
            chip.Art = (g, r, sel) =>
            {
                float sz = Math.Min(r.Width, r.Height) * 0.22f;
                Gfx.DrawSprite(g, sp, r.X + r.Width / 2, r.Y + r.Height / 2, sz, sp == Sprite.Confetti ? 25 : 0,
                               sel ? SpriteTint(sp) : Color.FromArgb(110, 104, 130), 1f, sel ? 0.8f : 0f, 0);
            };
            chip.Click += (o, e) =>
            {
                int next = s.Sprites ^ (int)sp;
                if (next != 0) s.Sprites = next; // keep at least one sprite on
                SyncUI();
            };
            spriteChips[sp] = chip;
            Controls.Add(chip);
        }

        // behaviour sliders
        Func<float, string> pct = v => (v * 100).ToString("0") + "%";
        Func<float, string> mult = v => v.ToString("0.0") + "×";
        AddSlider(0, "Density", 0.2f, 3f, 1f, mult, () => s.Density, v => s.Density = v);
        AddSlider(1, "Size", 0.4f, 3f, 1f, mult, () => s.SpriteSize, v => s.SpriteSize = v);
        AddSlider(2, "Lifetime", 0.3f, 3f, 1f, mult, () => s.Lifetime, v => s.Lifetime = v);
        AddSlider(3, "Gravity", -150f, 400f, 140f, v => v < -5 ? "↑ " + (-v).ToString("0") : v > 5 ? "↓ " + v.ToString("0") : "float", () => s.Gravity, v => s.Gravity = v);
        AddSlider(4, "Flutter", 0f, 3f, 1f, pct, () => s.Flutter, v => s.Flutter = v);
        AddSlider(5, "Spread", 0f, 3f, 1f, pct, () => s.Spread, v => s.Spread = v);
        AddSlider(6, "Glow", 0f, 2f, 1f, pct, () => s.Glow, v => s.Glow = v);
        AddSlider(7, "Twinkle", 0f, 1f, 1f, pct, () => s.Twinkle, v => s.Twinkle = v);

        // preview
        preview = new PreviewPanel(s) { Bounds = DR(456, 92, 420, 214) };
        Controls.Add(preview);

        // footer
        startupToggle = new Toggle { Text = "Start with Windows", BackColor = UI.Bg, Bounds = DR(24, 576, 230, 32) };
        startupToggle.Changed += v => { try { Startup.Set(v); } catch { } };
        Controls.Add(startupToggle);

        var reset = new PillButton { Text = "Reset to defaults", BackColor = UI.Bg, Bounds = DR(594, 574, 150, 36) };
        reset.Click += (o, e) => { s.ResetLook(); SyncUI(); };
        Controls.Add(reset);

        var done = new PillButton { Text = "Done", Primary = true, BackColor = UI.Bg, Bounds = DR(756, 574, 120, 36) };
        done.Click += (o, e) => Close();
        Controls.Add(done);

        SyncUI();

        timer.Interval = 15;
        timer.Tick += (o, e) =>
        {
            double t = clock.Elapsed.TotalSeconds;
            preview.Step((float)Math.Min(t - lastT, 0.05), t);
            lastT = t;
        };
        timer.Start();
    }

    public Rectangle PreviewScreenRect
    {
        get { return preview.RectangleToScreen(preview.ClientRectangle); }
    }

    static Color SpriteTint(Sprite sp)
    {
        switch (sp)
        {
            case Sprite.Sparkle: return Color.FromArgb(255, 215, 110);
            case Sprite.Star: return Color.FromArgb(255, 180, 90);
            case Sprite.Heart: return Color.FromArgb(255, 110, 170);
            case Sprite.Diamond: return Color.FromArgb(120, 210, 255);
            case Sprite.Orb: return Color.FromArgb(200, 150, 255);
            case Sprite.Snowflake: return Color.FromArgb(170, 230, 255);
            case Sprite.Ring: return Color.FromArgb(255, 150, 220);
            case Sprite.Glint: return Color.FromArgb(225, 215, 255);
            default: return Color.FromArgb(120, 240, 180);
        }
    }

    void AddSlider(int i, string label, float min, float max, float def, Func<float, string> fmt, Func<float> get, Action<float> set)
    {
        var sl = new Slider { Label = label, Min = min, Max = max, Default = def, Format = fmt, Bounds = DR(472 + (i % 2) * 202, 356 + (i / 2) * 46, 186, 44) };
        sl.Changed += v => set(v);
        sliders.Add(Tuple.Create(sl, get));
        Controls.Add(sl);
    }

    void PickColor(int idx)
    {
        using (var dlg = new ColorDialog { FullOpen = true, Color = s.Custom[idx] })
        {
            var custom = new int[16];
            for (int i = 0; i < s.Custom.Length; i++) custom[i] = ColorTranslator.ToWin32(s.Custom[i]);
            dlg.CustomColors = custom;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            s.Custom[idx] = dlg.Color;
            s.Palette = Palette.Custom;
            SyncUI();
        }
    }

    public void SyncUI()
    {
        foreach (var kv in paletteChips) { kv.Value.Selected = kv.Key == s.Palette; kv.Value.Invalidate(); }
        foreach (var kv in spriteChips) { kv.Value.Selected = s.Has(kv.Key); kv.Value.Invalidate(); }
        for (int i = 0; i < swatches.Length; i++) { swatches[i].Color = s.Custom[i]; swatches[i].Invalidate(); }
        foreach (var t in sliders) { t.Item1.Value = t.Item2(); t.Item1.Invalidate(); }
        enabledToggle.Checked = s.Enabled; enabledToggle.Invalidate();
        bool startup = false;
        try { startup = Startup.IsEnabled(); } catch { }
        startupToggle.Checked = startup; startupToggle.Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, UI.Bg, UI.Text);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        timer.Dispose();
        logo.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UI.Prep(g);

        // ambient glow behind the header
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(D(-140), D(-170), D(560), D(330));
            using (var pb = new PathGradientBrush(path))
            {
                pb.CenterColor = Color.FromArgb(46, UI.A2);
                pb.SurroundColors = new[] { Color.FromArgb(0, UI.A2) };
                g.FillPath(pb, path);
            }
        }

        g.DrawImage(logo, D(22), D(20), D(48), D(48));
        var titleRect = new RectangleF(D(76), D(14), D(230), D(40));
        using (var br = Gfx.Grad(titleRect, UI.Accent, 0f))
            g.DrawString("Sparkle Cursor", UI.Title, br, D(74), D(14));
        using (var b = new SolidBrush(UI.Sub))
            g.DrawString("Customize the trail that follows your cursor", UI.Body, b, D(78), D(52));

        DrawCard(g, PaletteCard, "Palette", "tap to apply");
        DrawCard(g, SpriteCard, "Sprites", "mix & match");
        DrawCard(g, BehaviourCard, "Behaviour", "drag, scroll, double-click to reset");

        using (var b = new SolidBrush(UI.Text))
            g.DrawString("Custom colours", UI.Body, b, new RectangleF(D(40), D(252), D(200), D(30)), UI.LeftMid);
        using (var b = new SolidBrush(UI.Sub))
            g.DrawString("click a swatch to pick", UI.Small, b, new RectangleF(D(152), D(252), D(160), D(30)), UI.LeftMid);
    }

    static void DrawCard(Graphics g, Rectangle logical, string title, string hint)
    {
        var r = new RectangleF(D(logical.X), D(logical.Y), D(logical.Width), D(logical.Height));
        using (var path = Gfx.Round(r, D(14)))
        {
            using (var b = new SolidBrush(UI.Card)) g.FillPath(b, path);
            using (var pen = new Pen(UI.CardBorder, 1f)) g.DrawPath(pen, path);
        }
        var star = new RectangleF(r.X + D(14), r.Y + D(14), D(14), D(14));
        using (var br = Gfx.Grad(star, UI.Accent, 45f))
            g.FillPolygon(br, Gfx.StarPoints(star.X + star.Width / 2, star.Y + star.Height / 2, D(7), 0.3f, 0, 4));
        using (var b = new SolidBrush(UI.Text))
            g.DrawString(title, UI.H, b, new RectangleF(r.X + D(32), r.Y + D(9), r.Width, D(24)), UI.LeftMid);
        using (var b = new SolidBrush(UI.Sub))
            g.DrawString(hint, UI.Small, b, new RectangleF(r.X, r.Y + D(9), r.Width - D(16), D(24)), UI.RightMid);
    }
}

// ======================================================================
// Tray menu theme
// ======================================================================

class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return UI.Card; } }
    public override Color ImageMarginGradientBegin { get { return UI.Card; } }
    public override Color ImageMarginGradientMiddle { get { return UI.Card; } }
    public override Color ImageMarginGradientEnd { get { return UI.Card; } }
    public override Color MenuBorder { get { return UI.CardBorder; } }
    public override Color MenuItemBorder { get { return UI.ChipSel; } }
    public override Color MenuItemSelected { get { return UI.ChipSel; } }
    public override Color MenuItemSelectedGradientBegin { get { return UI.ChipSel; } }
    public override Color MenuItemSelectedGradientEnd { get { return UI.ChipSel; } }
    public override Color MenuItemPressedGradientBegin { get { return UI.ChipSel; } }
    public override Color MenuItemPressedGradientEnd { get { return UI.ChipSel; } }
    public override Color SeparatorDark { get { return UI.CardBorder; } }
    public override Color SeparatorLight { get { return UI.Card; } }
    public override Color CheckBackground { get { return UI.Card; } }
    public override Color CheckSelectedBackground { get { return UI.ChipSel; } }
    public override Color CheckPressedBackground { get { return UI.ChipSel; } }
}

class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        UI.Prep(e.Graphics);
        var r = new RectangleF(UI.D(4), 1, e.Item.Width - UI.D(8), e.Item.Height - 2);
        using (var path = Gfx.Round(r, UI.D(6)))
        using (var b = new SolidBrush(UI.ChipSel))
            e.Graphics.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? UI.Text : UI.Sub;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = UI.Sub;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        UI.Prep(g);
        var ir = e.ImageRectangle;
        var r = new RectangleF(ir.X - 2, ir.Y - 2, ir.Width + 3, ir.Height + 3);
        using (var path = Gfx.Round(r, UI.D(4)))
        using (var br = Gfx.Grad(r, UI.Accent, 45f))
        {
            if (e.Item.Image != null)
                using (var pen = new Pen(br, 1.6f * UI.S)) g.DrawPath(pen, path);
            else
            {
                g.FillPath(br, path);
                using (var pen = new Pen(Color.White, 1.8f * UI.S))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new[] {
                        new PointF(r.X + r.Width * .25f, r.Y + r.Height * .52f),
                        new PointF(r.X + r.Width * .43f, r.Y + r.Height * .7f),
                        new PointF(r.X + r.Width * .76f, r.Y + r.Height * .32f) });
                }
            }
        }
    }
}

// ======================================================================
// App
// ======================================================================

class SparkleContext : ApplicationContext
{
    readonly Settings s = new Settings();
    readonly Overlay overlay = new Overlay();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly ParticleSystem sys;
    readonly Icon trayIcon, formIcon;
    readonly DarkRenderer renderer = new DarkRenderer();
    readonly Dictionary<Palette, ToolStripMenuItem> paletteItems = new Dictionary<Palette, ToolStripMenuItem>();

    public const string ShowEventName = "SparkleCursor_ShowSettings";

    SettingsForm form;
    EventWaitHandle showEvent;
    ToolStripMenuItem pauseItem, startupItem;
    double lastTime, lastTopmost;
    Point lastPos;

    public SparkleContext()
    {
        s.Load();
        UI.Init(Native.GetSystemDpi() / 96f);
        sys = new ParticleSystem(s, UI.S);
        trayIcon = Gfx.MakeIcon(SystemInformation.SmallIconSize.Width);
        formIcon = Gfx.MakeIcon(UI.D(32));
        BuildTray();

        var h = overlay.Handle; // force window creation
        Native.GetCursorPos(out lastPos);
        lastTime = clock.Elapsed.TotalSeconds;

        timer.Interval = 15;
        timer.Tick += (o, e) => Tick();
        timer.Start();

        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(showEvent, (st, timedOut) =>
        {
            try { overlay.BeginInvoke((Action)OpenSettings); } catch { }
        }, null, -1, false);

        if (!s.Welcomed)
        {
            tray.ShowBalloonTip(5000, "Sparkle Cursor", "Running in your system tray — click the sparkle icon to customize it.", ToolTipIcon.None);
            s.Welcomed = true;
            s.Save();
        }
    }

    // ---------- tray ----------

    void BuildTray()
    {
        var menu = new ContextMenuStrip { Renderer = renderer, Font = UI.Body, ShowImageMargin = true, ImageScalingSize = new Size(UI.D(16), UI.D(16)) };
        menu.Padding = new Padding(0, UI.D(4), 0, UI.D(4));

        var header = Item("Sparkle Cursor", (o, e) => OpenSettings());
        header.Image = Gfx.MakeLogo(UI.D(16));
        header.Font = new Font(UI.Body, FontStyle.Bold);
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Customize…", (o, e) => OpenSettings()));
        pauseItem = Item("Pause sparkles", (o, e) =>
        {
            s.Enabled = !s.Enabled;
            s.Save();
            if (FormOpen) form.SyncUI();
        });
        menu.Items.Add(pauseItem);

        var colours = Item("Palette", null);
        colours.DropDown.Renderer = renderer;
        colours.DropDown.Font = UI.Body;
        ((ToolStripDropDownMenu)colours.DropDown).ImageScalingSize = new Size(UI.D(16), UI.D(16));
        colours.DropDown.Padding = new Padding(0, UI.D(4), 0, UI.D(4));
        colours.DropDown.Opened += (o, e) => Native.RoundCorners(colours.DropDown.Handle);
        foreach (Palette p in Enum.GetValues(typeof(Palette)))
        {
            var pp = p;
            var item = Item(p.ToString(), (o, e) =>
            {
                s.Palette = pp;
                s.Save();
                if (FormOpen) form.SyncUI();
            });
            paletteItems[p] = item;
            colours.DropDownItems.Add(item);
        }
        menu.Items.Add(colours);

        menu.Items.Add(new ToolStripSeparator());
        startupItem = Item("Start with Windows", (o, e) =>
        {
            try { Startup.Set(!Startup.IsEnabled()); } catch { }
            if (FormOpen) form.SyncUI();
        });
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Exit", (o, e) => Quit()));

        menu.Opening += (o, e) =>
        {
            pauseItem.Text = s.Enabled ? "Pause sparkles" : "Resume sparkles";
            try { startupItem.Checked = Startup.IsEnabled(); } catch { }
            foreach (var kv in paletteItems)
            {
                kv.Value.Checked = kv.Key == s.Palette;
                var old = kv.Value.Image;
                kv.Value.Image = Gfx.PaletteSwatch(kv.Key, s, UI.D(16));
                if (old != null) old.Dispose();
            }
        };
        menu.Opened += (o, e) => Native.RoundCorners(menu.Handle);

        tray.ContextMenuStrip = menu;
        tray.Icon = trayIcon;
        tray.Text = "Sparkle Cursor — click to customize";
        tray.Visible = true;
        tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
        tray.BalloonTipClicked += (o, e) => OpenSettings();
    }

    static ToolStripMenuItem Item(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text, null, onClick);
        item.Padding = new Padding(UI.D(2), UI.D(5), UI.D(8), UI.D(5));
        return item;
    }

    bool FormOpen { get { return form != null && !form.IsDisposed; } }

    void OpenSettings()
    {
        if (!FormOpen)
        {
            form = new SettingsForm(s, formIcon);
            form.FormClosed += (o, e) => s.Save();
            form.Show();
        }
        else if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;
        form.Activate();
    }

    void Quit()
    {
        timer.Stop();
        s.Save();
        if (FormOpen) form.Close();
        tray.Visible = false;
        tray.Dispose();
        overlay.Close();
        ExitThread();
    }

    // ---------- frame loop ----------

    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(now - lastTime, 0.05);
        lastTime = now;

        Point pos;
        Native.GetCursorPos(out pos);

        if (s.Enabled)
        {
            // the settings window has its own preview; don't double up over it
            bool overPreview = FormOpen && form.Visible && form.PreviewScreenRect.Contains(pos);
            if (!overPreview) sys.Emit(lastPos.X, lastPos.Y, pos.X, pos.Y, dt);
        }
        else if (sys.Particles.Count > 0) sys.Particles.Clear();
        lastPos = pos;

        sys.Update(dt);
        if (sys.Particles.Count > 0) Render(now);
        else overlay.HideOverlay();

        if (now - lastTopmost > 2) { lastTopmost = now; overlay.KeepOnTop(); }
    }

    void Render(double now)
    {
        var b = sys.Bounds();
        int left = (int)Math.Floor(b.Left) - 2, top = (int)Math.Floor(b.Top) - 2;
        int w = Math.Min((int)Math.Ceiling(b.Right) + 2 - left, 8000);
        int h = Math.Min((int)Math.Ceiling(b.Bottom) + 2 - top, 8000);
        if (w <= 0 || h <= 0) { overlay.HideOverlay(); return; }

        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                sys.Draw(g, left, top, now);
            }
            overlay.Present(bmp, left, top);
        }
    }
}

// Click-through, per-pixel-alpha, always-on-top window that is resized to fit the live particles each frame.
class Overlay : Form
{
    bool shown;

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // LAYERED | TRANSPARENT (click-through) | TOOLWINDOW (no alt-tab) | NOACTIVATE | TOPMOST
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000 | 0x8;
            return cp;
        }
    }

    public void KeepOnTop()
    {
        Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public void HideOverlay()
    {
        if (!shown) return;
        Native.ShowWindow(Handle, 0);
        shown = false;
    }

    public void Present(Bitmap bmp, int x, int y)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(memDc, hBmp);
        try
        {
            var size = new Native.SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new Point(0, 0);
            var dst = new Point(x, y);
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
        if (!shown)
        {
            Native.ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
            KeepOnTop();
            shown = true;
        }
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref SIZE psize, IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void MakeDpiAware()
    {
        // Per-monitor v2 so cursor and window coordinates are both in physical pixels on every monitor.
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
        try { SetProcessDPIAware(); } catch { }
    }

    public static int GetSystemDpi()
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        try { return GetDeviceCaps(dc, 88); } // LOGPIXELSX
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

    // Dark title bar, rounded corners and matching caption colour (Windows 11; ignored elsewhere).
    public static void StyleWindow(IntPtr h, Color caption, Color text)
    {
        try
        {
            int one = 1, round = 2, cap = ColorRef(caption), txt = ColorRef(text);
            DwmSetWindowAttribute(h, 20, ref one, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(h, 33, ref round, 4); // DWMWA_WINDOW_CORNER_PREFERENCE
            DwmSetWindowAttribute(h, 35, ref cap, 4);   // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(h, 36, ref txt, 4);   // DWMWA_TEXT_COLOR
        }
        catch { }
    }

    public static void RoundCorners(IntPtr h)
    {
        try { int v = 3; DwmSetWindowAttribute(h, 33, ref v, 4); } catch { } // DWMWCP_ROUNDSMALL
    }
}
