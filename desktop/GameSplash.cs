using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MountZantas;

// Mount Zantas's own loading screen, shown instead of Everest's splash while Celeste starts.
// GameStart (core/GameStart.cs) reads Everest's log and says what is loading; this window only draws it.
class GameSplash : Window {
    const double W = 800;
    const double H = 360;

    readonly GameStart start;
    readonly DispatcherTimer frame = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly TextBlock title;
    readonly TextBlock detail;
    readonly Rectangle bar;
    GameStartState state;
    // The bar moves smoothly towards the real progress instead of jumping.
    double shownProgress;
    int ticks;
    // Counts down after Celeste closed early, so the message can be read.
    int closingTicks = -1;

    public GameSplash(GameStart start) {
        this.start = start;
        state = start.State;
        Width = W;
        Height = H;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Starting Celeste - " + Brand.Name;
        Icon = Brand.Icon();

        // Background: a dark night sky fading into the accent colour.
        Background = new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.35, 1, RelativeUnit.Relative),
            GradientStops = {
                new GradientStop(Color.FromRgb(0x16, 0x14, 0x26), 0),
                new GradientStop(Mix(Brand.Accent, Colors.Black, 0.62), 1)
            }
        };

        var canvas = new Canvas { Width = W, Height = H };

        // A soft glow behind the logo.
        var glow = new Ellipse {
            Width = 620,
            Height = 520,
            Fill = new RadialGradientBrush {
                GradientStops = {
                    new GradientStop(Color.FromArgb(80, Brand.Accent.R, Brand.Accent.G, Brand.Accent.B), 0),
                    new GradientStop(Color.FromArgb(0, Brand.Accent.R, Brand.Accent.G, Brand.Accent.B), 1)
                }
            }
        };
        Place(canvas, glow, -160, -200);

        // A few fixed stars.
        foreach (var (x, y, r) in Stars) {
            var star = new Ellipse { Width = r, Height = r, Fill = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)) };
            Place(canvas, star, x, y);
        }

        // The Zantas mark and name.
        Place(canvas, new ZantasMarkView(170), 40, 14);
        var name = new TextBlock {
            Text = "Zantas",
            FontSize = 88,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White
        };
        Place(canvas, name, 206, 30);
        var loader = new TextBlock {
            Text = "Mount Zantas  ·  Zantas Loader on Everest",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 220, 214, 255))
        };
        Place(canvas, loader, 220, 146);

        // What is happening right now.
        title = new TextBlock { FontSize = 29, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
        Place(canvas, title, 58, 216);
        detail = new TextBlock { FontSize = 17, Foreground = new SolidColorBrush(Color.FromArgb(230, 235, 232, 255)) };
        Place(canvas, detail, 62, 262);

        // Progress bar along the bottom edge.
        var track = new Rectangle { Width = W, Height = 6, Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) };
        Place(canvas, track, 0, H - 6);
        bar = new Rectangle { Width = 0, Height = 6, Fill = new SolidColorBrush(Mix(Brand.Accent, Colors.White, 0.25)) };
        Place(canvas, bar, 0, H - 6);

        Content = canvas;
        ShowState();
        frame.Tick += (_, _) => Step();
        frame.Start();
        Closed += (_, _) => frame.Stop();
    }

    static void Place(Canvas canvas, Control child, double x, double y) {
        Canvas.SetLeft(child, x);
        Canvas.SetTop(child, y);
        canvas.Children.Add(child);
    }

    void ShowState() {
        title.Text = state.Title;
        detail.Text = state.Detail;
        bar.Width = W * Math.Clamp(shownProgress, 0, 1);
    }

    void Step() {
        ticks++;
        // Read the log about 6 times a second; animate every frame.
        if (ticks % 5 == 0) {
            state = start.Poll();
        }
        shownProgress += (state.Progress - shownProgress) * 0.12;
        ShowState();

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

    static readonly (double x, double y, double r)[] Stars = {
        (610, 40, 3), (700, 92, 2), (540, 120, 2), (760, 30, 2), (660, 170, 3), (735, 210, 2), (480, 60, 2), (590, 230, 2)
    };

    // Blends colour a towards colour b (amount 0 = a, 1 = b).
    static Color Mix(Color a, Color b, double amount) {
        byte R = (byte)(a.R + (b.R - a.R) * amount);
        byte G = (byte)(a.G + (b.G - a.G) * amount);
        byte B = (byte)(a.B + (b.B - a.B) * amount);
        return Color.FromRgb(R, G, B);
    }
}

// Draws the Zantas mark (core/ZantasMark.cs): mountain, snow, a white gear and a gold star in its middle.
class ZantasMarkView : Control {
    public ZantasMarkView(double size) {
        Width = size;
        Height = size;
    }

    public override void Render(DrawingContext g) {
        double u = Bounds.Width / 100;

        StreamGeometry Shape(float[] xy) {
            var shape = new StreamGeometry();
            using (var c = shape.Open()) {
                c.BeginFigure(new Point(xy[0] * u, xy[1] * u), true);
                for (int i = 2; i < xy.Length; i += 2) {
                    c.LineTo(new Point(xy[i] * u, xy[i + 1] * u));
                }
                c.EndFigure(true);
            }
            return shape;
        }

        // Mountain: lighter at the top, the accent colour at the foot.
        var rock = new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = {
                new GradientStop(Mix(Brand.Accent, Colors.White, 0.35), 0),
                new GradientStop(Brand.Accent, 1)
            }
        };
        g.DrawGeometry(rock, null, Shape(ZantasMark.Mountain));
        g.DrawGeometry(Brushes.White, null, Shape(ZantasMark.Snow));
        g.DrawGeometry(Brushes.White, null, Shape(ZantasMark.SmallSnow));

        // Gear with a dark hub, and the star in the hub.
        g.DrawGeometry(Brushes.White, null, Shape(ZantasMark.Gear));
        var hubCentre = new Point(ZantasMark.GearX * u, ZantasMark.GearY * u);
        double hub = ZantasMark.HubRadius * u;
        g.DrawEllipse(new SolidColorBrush(Mix(Brand.Accent, Colors.Black, 0.6)), null, hubCentre, hub, hub);
        g.DrawGeometry(new SolidColorBrush(Color.FromRgb(255, 235, 150)), null, Shape(ZantasMark.Star));
    }

    static Color Mix(Color a, Color b, double amount) {
        byte R = (byte)(a.R + (b.R - a.R) * amount);
        byte G = (byte)(a.G + (b.G - a.G) * amount);
        byte B = (byte)(a.B + (b.B - a.B) * amount);
        return Color.FromRgb(R, G, B);
    }
}
