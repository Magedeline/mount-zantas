using System.Drawing.Drawing2D;

namespace MountZantas;

// Mount Zantas's own loading screen, shown instead of Everest's splash while Celeste starts.
// GameStart (core/GameStart.cs) reads Everest's log and says what is loading; this window only draws it.
class GameSplash : Form {
    readonly GameStart start;
    readonly System.Windows.Forms.Timer frame = new() { Interval = 30 };
    GameStartState state;
    // The bar moves smoothly towards the real progress instead of jumping.
    double shownProgress;
    int ticks;
    // Counts down after Celeste closed early, so the message can be read.
    int closingTicks = -1;

    public GameSplash(GameStart start) {
        this.start = start;
        state = start?.State ?? new GameStartState();
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(800, 360);
        BackColor = Brand.Bg;
        DoubleBuffered = true;
        Text = "Starting Celeste - " + Brand.Name;
        Icon = Brand.AppIcon();
        frame.Tick += (_, _) => Step();
        if (start != null) {
            frame.Start();
        }
    }

    void Step() {
        ticks++;
        // Read the log about 6 times a second; animate every frame.
        if (ticks % 5 == 0) {
            state = start.Poll();
        }
        shownProgress += (state.Progress - shownProgress) * 0.12;
        Invalidate();

        if (!state.Done) {
            return;
        }
        if (!state.Exited) {
            Close();
            return;
        }
        if (closingTicks < 0) {
            state.Title = "Celeste closed";
            state.Detail = "It stopped before it finished loading. Its log.txt says why.";
            closingTicks = 160;
        }
        closingTicks--;
        if (closingTicks == 0) {
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var all = new Rectangle(0, 0, Width, Height);

        // Background: a dark night sky fading into the accent colour.
        var top = Color.FromArgb(0x16, 0x14, 0x26);
        var bottom = Mix(Brand.Accent, Color.Black, 0.62);
        using (var sky = new LinearGradientBrush(all, top, bottom, 70f)) {
            g.FillRectangle(sky, all);
        }

        // A soft glow behind the logo.
        using (var path = new GraphicsPath()) {
            path.AddEllipse(-160, -200, 620, 520);
            using var glow = new PathGradientBrush(path) {
                CenterColor = Color.FromArgb(80, Brand.Accent),
                SurroundColors = new[] { Color.FromArgb(0, Brand.Accent) }
            };
            g.FillPath(glow, path);
        }

        // A few fixed stars.
        using (var star = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) {
            foreach (var (x, y, r) in Stars) {
                g.FillEllipse(star, x, y, r, r);
            }
        }

        // The Zantas mark and name.
        using (var mark = Mark(170)) {
            g.DrawImage(mark, 40, 14, 170, 170);
        }
        using (var f = Ui.F(66, true)) {
            g.DrawString("Zantas", f, Brushes.White, 206, 36);
        }
        using (var f = Ui.F(12)) {
            using var dim = new SolidBrush(Color.FromArgb(200, 220, 214, 255));
            g.DrawString("Mount Zantas  ·  Zantas Loader on Everest", f, dim, 220, 144);
        }

        // What is happening right now.
        using (var f = Ui.F(22, true)) {
            g.DrawString(state.Title, f, Brushes.White, 58, 218);
        }
        using (var f = Ui.F(13)) {
            using var text = new SolidBrush(Color.FromArgb(230, 235, 232, 255));
            g.DrawString(state.Detail, f, text, 62, 262);
        }

        // Progress bar along the bottom edge.
        int barHeight = 6;
        int barY = Height - barHeight;
        using (var track = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) {
            g.FillRectangle(track, 0, barY, Width, barHeight);
        }
        int filled = (int)(Width * Math.Clamp(shownProgress, 0, 1));
        using (var fill = new SolidBrush(Mix(Brand.Accent, Color.White, 0.25))) {
            g.FillRectangle(fill, 0, barY, filled, barHeight);
        }
    }

    // Draws the Zantas mark (core/ZantasMark.cs): mountain, snow, a white gear and a gold star in its middle.
    public static Bitmap Mark(int size) {
        var b = new Bitmap(size, size);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float u = size / 100f;

        PointF[] Points(float[] xy) {
            var points = new PointF[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) {
                points[i] = new PointF(xy[i * 2] * u, xy[i * 2 + 1] * u);
            }
            return points;
        }

        // Mountain: lighter at the top, the accent colour at the foot.
        var light = Mix(Brand.Accent, Color.White, 0.35);
        using (var rock = new LinearGradientBrush(new PointF(0, 14 * u), new PointF(0, 92 * u), light, Brand.Accent)) {
            g.FillPolygon(rock, Points(ZantasMark.Mountain));
        }
        g.FillPolygon(Brushes.White, Points(ZantasMark.Snow));
        g.FillPolygon(Brushes.White, Points(ZantasMark.SmallSnow));

        // Gear with a dark hub, and the star in the hub.
        g.FillPolygon(Brushes.White, Points(ZantasMark.Gear));
        float hub = ZantasMark.HubRadius * u;
        using (var dark = new SolidBrush(Mix(Brand.Accent, Color.Black, 0.6))) {
            g.FillEllipse(dark, ZantasMark.GearX * u - hub, ZantasMark.GearY * u - hub, hub * 2, hub * 2);
        }
        using (var gold = new SolidBrush(Color.FromArgb(255, 235, 150))) {
            g.FillPolygon(gold, Points(ZantasMark.Star));
        }
        return b;
    }

    static readonly (int x, int y, int r)[] Stars = {
        (610, 40, 3), (700, 92, 2), (540, 120, 2), (760, 30, 2), (660, 170, 3), (735, 210, 2), (480, 60, 2), (590, 230, 2)
    };

    // Blends colour a towards colour b (amount 0 = a, 1 = b).
    static Color Mix(Color a, Color b, double amount) {
        int R = (int)(a.R + (b.R - a.R) * amount);
        int G = (int)(a.G + (b.G - a.G) * amount);
        int B = (int)(a.B + (b.B - a.B) * amount);
        return Color.FromArgb(R, G, B);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            frame.Dispose();
        }
        base.Dispose(disposing);
    }

    // For visual checks: MountZantas.exe --game-splash-shot out.png draws the screen halfway through loading mods.
    public static void SaveExample(string file) {
        using var s = new GameSplash(null);
        s.state.Detail = "Loading KirbyHelperMechanics [69/70]";
        s.shownProgress = 0.53;
        s.Show();
        Application.DoEvents();
        using var bmp = new Bitmap(s.Width, s.Height);
        s.DrawToBitmap(bmp, new Rectangle(0, 0, s.Width, s.Height));
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }
}
