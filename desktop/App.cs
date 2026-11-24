using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace MountZantas;

static class Program {
    [STAThread]
    public static int Main(string[] args) {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            if (e.ExceptionObject is Exception x)
                try {
                    Directory.CreateDirectory(Config.DataDir);
                    File.AppendAllText(Path.Combine(Config.DataDir, "error.log"), $"[{DateTime.Now:s}] FATAL {x}\n\n");
                } catch { }
        };
        App.ShotArgs = args;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

class App : Application {
    public static string[] ShotArgs = Array.Empty<string>();
    static ColorPaletteResources palette;

    public override void Initialize() {
        palette = new ColorPaletteResources { Accent = Brand.Accent, RegionColor = Brand.Bg };
        var theme = new FluentTheme();
        theme.Palettes[ThemeVariant.Dark] = palette;
        Styles.Add(theme);
        RequestedThemeVariant = ThemeVariant.Dark;
        var r = Resources;
        r["ButtonBackground"] = Brand.B(Brand.Panel2);
        r["ButtonBackgroundPointerOver"] = Brand.B(Brand.Hover);
        r["ButtonBackgroundPressed"] = Brand.B(Brand.Hover);
        r["ButtonForeground"] = Brand.B(Brand.Text);
        r["ButtonForegroundPointerOver"] = Brand.B(Colors.White);
        r["ButtonForegroundPressed"] = Brand.B(Colors.White);
        r["ButtonBackgroundDisabled"] = Brand.B(Brand.Panel);
        r["ButtonBorderBrush"] = Brushes.Transparent;
        r["ButtonBorderBrushPointerOver"] = Brushes.Transparent;
        r["ButtonBorderBrushPressed"] = Brushes.Transparent;
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" }) r[k] = Brand.B(Brand.Panel2);
        r["TextControlForeground"] = Brand.B(Brand.Text);
        r["TextControlForegroundFocused"] = Brand.B(Brand.Text);
        r["TextControlForegroundPointerOver"] = Brand.B(Brand.Text);
        foreach (var k in new[] { "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxDropDownBackground" }) r[k] = Brand.B(Brand.Panel2);
        r["MenuFlyoutPresenterBackground"] = Brand.B(Brand.Panel2);
    }
    public static void SetAccent(Color c) {
        Brand.Accent = c;
        if (palette != null) palette.Accent = c;
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

static class Brand {
    public const string Name = "Mount Zantas";
    public static readonly Color Bg = Color.FromRgb(0x14, 0x15, 0x1e), Panel = Color.FromRgb(0x1f, 0x21, 0x2d), Panel2 = Color.FromRgb(0x2a, 0x2c, 0x3b),
        Hover = Color.FromRgb(0x3a, 0x3d, 0x52), Text = Color.FromRgb(0xee, 0xee, 0xf6), Dim = Color.FromRgb(0x9a, 0x9d, 0xb2),
        Green = Color.FromRgb(0x2f, 0x6b, 0x40), Orange = Color.FromRgb(0x7a, 0x5c, 0x1c), Warn = Color.FromRgb(0xe3, 0xa2, 0x4a), Good = Color.FromRgb(0x7f, 0xd0, 0x92);
    public static Color Accent = Color.Parse("#6a4cff");
    public static IBrush B(Color c) => new SolidColorBrush(c);
    public static Color Darker(Color c, double f = 0.45) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    static WindowIcon icon;
    public static WindowIcon Icon() {
        if (icon != null) return icon;
        try {
            var logo = new Logo(64);
            logo.Measure(new Size(64, 64));
            logo.Arrange(new Avalonia.Rect(0, 0, 64, 64));
            using var rtb = new RenderTargetBitmap(new PixelSize(64, 64));
            rtb.Render(logo);
            var ms = new MemoryStream();
            rtb.Save(ms);
            ms.Position = 0;
            icon = new WindowIcon(ms);
        } catch { }
        return icon;
    }
}

// Original logo: twin volcano peaks (Shasta and Shastina) with a snowcap and a star. Drawn, so it follows the accent colour.
// The app's logo is the Zantas mark (see core/ZantasMark.cs and ZantasMarkView in GameSplash.cs).
class Logo : ZantasMarkView {
    public Logo(double size) : base(size) { }
}

static class Ui {
    public static double Pt(double pt) => pt * 4 / 3;   // the WinForms app used points; Avalonia uses pixels
    public static Button Btn(string text, Action click, double w = double.NaN, double h = 36) {
        var b = new Button {
            Content = text,
            Height = h,
            Padding = new Thickness(12, 0),
            Margin = new Thickness(4),
            FontSize = Pt(10),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            CornerRadius = new CornerRadius(3)
        };
        if (!double.IsNaN(w)) b.Width = w;
        b.Click += (_, _) => click();
        return b;
    }
    public static Button Green(Button b) {
        b.Background = Brand.B(Brand.Green);
        return b;
    }
    public static Button Big(string text, Action click) {
        var b = Btn(text, click, double.NaN, double.NaN);
        b.Content = new TextBlock { Text = text, FontSize = Pt(15), FontWeight = FontWeight.Bold };
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.VerticalAlignment = VerticalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Padding = new Thickness(18, 0);
        b.Margin = new Thickness(5);
        return b;
    }
    public static TextBlock Lbl(string t, double size = 10, bool bold = false, Color? c = null, bool wrap = false) =>
        new() { Text = t, FontSize = Pt(size), FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, Foreground = Brand.B(c ?? Brand.Text), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    public static TextBox Text(string placeholder = "") => new() { Watermark = placeholder, FontSize = Pt(11), Margin = new Thickness(4), MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
    public static CheckBox Check(string t, bool v = false) => new() { Content = t, IsChecked = v, Foreground = Brand.B(Brand.Text), Margin = new Thickness(8, 0), FontSize = Pt(10), VerticalAlignment = VerticalAlignment.Center };
    public static bool On(this CheckBox c) => c.IsChecked == true;
    public static ComboBox Combo(params string[] items) => new() { ItemsSource = items, SelectedIndex = 0, Width = 320, Margin = new Thickness(0, 2, 0, 6), FontSize = Pt(10) };
    public static Border Card(Control inner, double pad = 12) => new() { Background = Brand.B(Brand.Panel), Padding = new Thickness(pad), Margin = new Thickness(6), Child = inner, CornerRadius = new CornerRadius(4) };
    public static StackPanel Row(params Control[] c) {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var x in c) p.Children.Add(x);
        return p;
    }
    public static WrapPanel Wrap(params Control[] c) {
        var p = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var x in c) p.Children.Add(x);
        return p;
    }
    public static StackPanel Col(params Control[] c) {
        var p = new StackPanel();
        foreach (var x in c) p.Children.Add(x);
        return p;
    }
    public static T Dock<T>(this T c, Avalonia.Controls.Dock d) where T : Control {
        DockPanel.SetDock(c, d);
        return c;
    }
    public static T At<T>(this T c, int col, int row = 0) where T : Control {
        Grid.SetColumn(c, col);
        Grid.SetRow(c, row);
        return c;
    }

    // A titled card that groups related settings.
    public static Control Section(string title, params Control[] rows) {
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(new TextBlock {
            Text = title,
            FontSize = Pt(13),
            FontWeight = FontWeight.Bold,
            Foreground = Brand.B(Brand.Accent),
            Margin = new Thickness(0, 0, 0, 6)
        });
        foreach (var r in rows) p.Children.Add(r);
        return new Border {
            Background = Brand.B(Brand.Panel),
            Padding = new Thickness(18, 12, 18, 14),
            Margin = new Thickness(0, 0, 14, 14),
            Child = p,
            CornerRadius = new CornerRadius(4)
        };
    }

    // Compact list rows (Fluent's default item padding is roomy).
    public static void Compact(ListBox l) {
        l.Background = Brand.B(Brand.Panel);
        l.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)), new Setter(Layoutable.MinHeightProperty, 0.0) } });
    }
    public static TextBlock Cell(string t, IBrush fg = null, bool bold = false) => new() { Text = t ?? "", Foreground = fg ?? Brand.B(Brand.Text), FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 6), FontSize = Pt(10) };
}

// A list with column headers: one row per item, cells built by the caller.
class Table<T> : DockPanel where T : class {
    public readonly ListBox List = new() { SelectionMode = SelectionMode.Single };
    readonly string widths;
    readonly Func<T, Control[]> cells;
    readonly Func<T, IBrush> tint;
    public event Action<T> Activated;
    public Table(string widths, string[] headers, Func<T, Control[]> cells, Func<T, IBrush> tint = null) {
        this.widths = widths;
        this.cells = cells;
        this.tint = tint;
        Ui.Compact(List);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions(widths), Background = Brand.B(Brand.Panel2) };
        for (int i = 0; i < headers.Length; i++) head.Children.Add(Ui.Cell(headers[i], null, true).At(i));
        Children.Add(head.Dock(Avalonia.Controls.Dock.Top));
        Children.Add(List);
        List.ItemTemplate = new FuncDataTemplate<T>((item, _) => item == null ? null : RowFor(item));
        List.DoubleTapped += (_, _) => { if (Selected != null) Activated?.Invoke(Selected); };
    }
    Control RowFor(T item) {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(widths), Background = tint?.Invoke(item) ?? Brushes.Transparent };
        var cs = cells(item);
        for (int i = 0; i < cs.Length; i++) g.Children.Add(cs[i].At(i));
        return g;
    }
    public IEnumerable<T> Items { set => List.ItemsSource = value.ToList(); }
    public T Selected => List.SelectedItem as T;
    public List<T> SelectedAll => List.SelectedItems?.Cast<T>().ToList() ?? new();
}

// Log + progress strip shared by pages that run downloads.
class LogBox : DockPanel {
    readonly TextBox tb = new() {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brand.B(Brand.Dim),
        FontSize = Ui.Pt(9.5),
        Background = Brand.B(Brand.Panel),
        BorderThickness = new Thickness(0)
    };
    readonly ProgressBar pb = new() { Minimum = 0, Maximum = 100, Height = 8, MinHeight = 8 };
    public readonly IProgress<int> Prog;
    public LogBox(double height = 130) {
        Height = height;
        Margin = new Thickness(0, 6, 0, 0);
        Children.Add(pb.Dock(Avalonia.Controls.Dock.Top));
        Children.Add(tb);
        Prog = new Progress<int>(v => pb.Value = Math.Clamp(v, 0, 100));
    }
    public void Log(string s) {
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => Log(s));
            return;
        }
        tb.Text = (tb.Text ?? "") + s + "\n";
        tb.CaretIndex = tb.Text.Length;
    }
    public void Reset() => pb.Value = 0;
}

// Small modal dialogs (Avalonia has no MessageBox).
static class Dlg {
    static Window Base(string title, double w) => new() { Title = title, Width = w, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brand.B(Brand.Bg), Icon = Brand.Icon(), ShowInTaskbar = false };
    public static async Task<string> Ask(Window owner, string text, params string[] buttons) {
        var w = Base(Brand.Name, 540);
        string res = null;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) {
            var k = b;
            var btn = Ui.Btn(b, () => {
                res = k;
                w.Close();
            }, 110);
            if (k == buttons[0]) btn.Background = Brand.B(Brand.Accent);
            bar.Children.Add(btn);
        }
        w.Content = new StackPanel {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brand.B(Brand.Text), FontSize = Ui.Pt(10.5) }, bar }
        };
        await w.ShowDialog(owner);
        return res;
    }
    public static async Task<bool> YesNo(Window owner, string text) => await Ask(owner, text, "Yes", "No") == "Yes";
    public static Task Info(Window owner, string text) => Ask(owner, text, "OK");
    public static async Task<string> Prompt(Window owner, string title, string label) {
        var w = Base(title, 440);
        var tb = Ui.Text();
        string res = null;
        void Ok() {
            res = tb.Text?.Trim();
            w.Close();
        }
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ok(); };
        w.Content = new StackPanel {
            Margin = new Thickness(16),
            Spacing = 8,
            Children = { Ui.Lbl(label), tb, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { Ui.Btn("OK", Ok, 90), Ui.Btn("Cancel", () => w.Close(), 90) } } }
        };
        w.Opened += (_, _) => tb.Focus();
        await w.ShowDialog(owner);
        return string.IsNullOrEmpty(res) ? null : res;
    }
}

// Downloaded images (GameBanana thumbnails, news). Avalonia's Skia decoder also reads WebP.
static class Images {
    public static async Task<Bitmap> Get(string url) {
        var bytes = await ImageStore.GetBytes(url);
        if (bytes == null) return null;
        try {
            return await Task.Run(() => {
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            });
        } catch { return null; }
    }
}

static class MapRender {
    // The core renders BGRA pixels; this wraps them in a bitmap (call on the UI thread).
    public static Bitmap ToBitmap(RawImage raw) {
        var wb = new WriteableBitmap(
            new PixelSize(raw.Width, raw.Height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Unpremul);
        using (var fb = wb.Lock())
            for (int y = 0; y < raw.Height; y++) System.Runtime.InteropServices.Marshal.Copy(
                raw.Bgra,
                y * raw.Width * 4,
                fb.Address + y * fb.RowBytes,
                raw.Width * 4);
        return wb;
    }
}
