using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace MountZantas;

// The mod description window: preview image(s), title, text, Install / Open in browser / Close. Returns the chosen file.
class ModDetailsDialog : Window {
    readonly GbProfile p;
    readonly Image pic = new() { Stretch = Stretch.Uniform };
    readonly TextBlock counter = Ui.Lbl("", 9, false, Brand.Dim);
    int idx;
    public ModDetailsDialog(GbProfile p) {
        this.p = p;
        Title = p.Name;
        Width = 900;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brand.B(Brand.Bg);
        Icon = Brand.Icon();
        ShowInTaskbar = false;
        var meta = $"by {p.Submitter}    ·    {p.Downloads:N0} downloads    ·    {p.Likes:N0} likes" + (p.Added != default ? $"    ·    added {p.Added:yyyy-MM-dd}" : "") + $"    ·    {p.Files.Count} file(s)";
        var head = Ui.Col(Ui.Lbl(p.Name, 22, true), Ui.Lbl(meta, 10, false, Brand.Dim));
        head.Margin = new Thickness(26, 12, 26, 8);
        var dark = Brand.B(Color.FromRgb(0x0b, 0x0c, 0x12));
        counter.HorizontalAlignment = HorizontalAlignment.Center;
        var img = new DockPanel { Height = 270, Background = dark, IsVisible = p.Images.Count > 0 };
        img.Children.Add(counter.Dock(Dock.Bottom));
        if (p.Images.Count > 1) {
            Button Arrow(string t, int d) {
                var b = Ui.Btn(t, () => ShowImage(idx + d), 44, double.NaN);
                b.VerticalAlignment = VerticalAlignment.Stretch;
                b.FontSize = Ui.Pt(20);
                return b;
            }
            img.Children.Add(Arrow("‹", -1).Dock(Dock.Left));
            img.Children.Add(Arrow("›", 1).Dock(Dock.Right));
        }
        img.Children.Add(pic);
        var body = new ScrollViewer { Content = new SelectableTextBlock {
            Text = p.Text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brand.B(Brand.Text),
            FontSize = Ui.Pt(10.5)
        }, Margin = new Thickness(26, 8, 20, 8) };
        var inst = Ui.Green(Ui.Btn("Install...", () => _ = PickFile(), 120));
        inst.IsEnabled = p.Files.Count > 0;
        var bar = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8),
            Children = { inst, Ui.Btn("Open in browser", () => Platform.OpenUrl(p.Url)), Ui.Btn("Close", () => Close(null), 100) }
        };
        Content = new DockPanel { Children = { head.Dock(Dock.Top), img.Dock(Dock.Top), bar.Dock(Dock.Bottom), body } };
        if (p.Images.Count > 0) ShowImage(0);
    }
    async Task PickFile() {
        var f = await new FilesDialog(p).ShowDialog<GbFile>(this);
        if (f != null) Close(f);
    }
    async void ShowImage(int i) {
        if (p.Images.Count == 0) return;
        idx = (i + p.Images.Count) % p.Images.Count;
        counter.Text = p.Images.Count > 1 ? $"{idx + 1} / {p.Images.Count}" : "";
        int my = idx;
        var b = await Images.Get(p.Images[idx]);
        if (my == idx) pic.Source = b;
    }
}

// The file-version picker: newest file first (green), with date and download count.
class FilesDialog : Window {
    public FilesDialog(GbProfile p) {
        Title = p.Name + " - choose a file";
        Width = 760;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brand.B(Brand.Bg);
        Icon = Brand.Icon();
        ShowInTaskbar = false;
        var first = p.Files.FirstOrDefault();
        var t = new Table<GbFile>("250,140,105,*", new[] { "File", "Added", "Downloads", "Version / notes" },
            f => new Control[] { Ui.Cell(f.Name), Ui.Cell(f.Date.ToString("yyyy-MM-dd HH:mm")), Ui.Cell(f.Downloads.ToString("N0")), Ui.Cell(f.Description) }, f => f == first ? Brand.B(Brand.Green) : null);
        t.Items = p.Files;
        if (p.Files.Count > 0) t.List.SelectedIndex = 0;
        void Go() { if (t.Selected != null) Close(t.Selected); }
        t.Activated += _ => Go();
        t.Margin = new Thickness(24, 0, 24, 0);
        var head = Ui.Col(
            Ui.Lbl(p.Name, 22, true),
            Ui.Lbl("Every file is checked against the MD5 GameBanana publishes before it is installed.", 9.5, false, Brand.Dim));
        head.Margin = new Thickness(24, 14, 24, 10);
        var bar = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8),
            Children = { Ui.Green(Ui.Btn("Install selected", Go)), Ui.Btn("Close", () => Close(null), 100) }
        };
        Content = new DockPanel { Children = { head.Dock(Dock.Top), bar.Dock(Dock.Bottom), t } };
    }
}

// One mod as a card: thumbnail, name, author, date and view/like counts.
class ModCard : Border {
    public readonly GbMod Mod;
    public ModCard(GbMod m, Action<ModCard> select, Action<ModCard> open) {
        Mod = m;
        Width = 336;
        Height = 238;
        Margin = new Thickness(0, 0, 12, 12);
        Background = Brand.B(Brand.Panel);
        Cursor = new Cursor(StandardCursorType.Hand);
        Padding = new Thickness(10);
        CornerRadius = new CornerRadius(4);
        var pic = new Image { Height = 134, Stretch = Stretch.Uniform };
        Child = new StackPanel { Children = {
            new Border { Background = Brand.B(Brand.Bg), Height = 134, Child = pic },
            Ui.Cell(m.Name, null, true), Ui.Cell($"{m.Submitter}" + (m.Category.Length > 0 ? $"   ·   {m.Category}" : ""), Brand.B(Brand.Dim)),
            Ui.Cell((m.Added != default ? m.Added.ToString("yyyy-MM-dd") + "   ·   " : "") + $"{m.Views:N0} views   ·   {m.Likes:N0} likes", Brand.B(Brand.Dim)) } };
        foreach (var c in ((StackPanel)Child).Children.OfType<TextBlock>()) c.Margin = new Thickness(0, 4, 0, 0);
        ((TextBlock)((StackPanel)Child).Children[1]).FontSize = Ui.Pt(11.5);
        PointerPressed += (_, _) => select(this);
        DoubleTapped += (_, _) => open(this);
        _ = Load(pic);
    }
    async Task Load(Image pic) {
        var b = await Images.Get(Mod.Thumb);
        if (b != null) pic.Source = b;
    }
    public void SetSelected(bool on) => Background = Brand.B(on ? Brand.Hover : Brand.Panel);
}

// ---------- GameBanana browser ----------
class GameBananaPage : Page {
    readonly WrapPanel cards = new();
    readonly ScrollViewer scroll;
    readonly TextBox search = Ui.Text("Search GameBanana for Celeste mods (empty = most downloaded)");
    readonly LogBox log = new(100);
    readonly TextBlock info = Ui.Lbl("", 10, false, Brand.Dim);
    int page = 1, total, loadToken;
    ModCard selected;

    public GameBananaPage(MainWindow app) : base(app) {
        search.Width = 480;
        search.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) {
                page = 1;
                _ = Load();
                e.Handled = true;
            }
        };
        info.Margin = new Thickness(10, 0);
        var top = Ui.Wrap(search, Ui.Green(Ui.Btn("Search", () => {
            page = 1;
            _ = Load();
        })), Ui.Btn("Details & install", () => _ = Details()),
            Ui.Btn("‹ Prev", () => {
                if (page > 1) {
                    page--;
                    _ = Load();
                }
            }, 80), Ui.Btn("Next ›", () => {
                if (page * 30 < total) {
                    page++;
                    _ = Load();
                }
            }, 80), info);
        scroll = new ScrollViewer {
            Content = cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Children.Add(top.Dock(Dock.Top));
        Children.Add(log.Dock(Dock.Bottom));
        Children.Add(scroll);
        _ = Load();
    }
    void Select(ModCard c) {
        selected?.SetSelected(false);
        selected = c;
        c.SetSelected(true);
    }
    async Task Load() {
        int my = ++loadToken;
        try {
            info.Text = "Loading...";
            var (items, t) = await GameBanana.Search(search.Text ?? "", page);
            if (my != loadToken) return;
            total = t;
            cards.Children.Clear();
            selected = null;
            foreach (var m in items) cards.Children.Add(new ModCard(m, Select, c => {
                Select(c);
                _ = Details();
            }));
            scroll.Offset = new Vector(0, 0);
            info.Text = $"Page {page} of {Math.Max(1, (total + 29) / 30)}  ·  {total:N0} results";
        } catch (Exception ex) {
            info.Text = "";
            log.Log("Couldn't load GameBanana: " + ex.Message);
        }
    }
    async Task Details() {
        if (selected == null) {
            log.Log("Click a mod first.");
            return;
        }
        var m = selected.Mod;
        try {
            log.Log($"Loading {m.Name}...");
            var prof = await GameBanana.Profile(m.Id);
            var file = await new ModDetailsDialog(prof).ShowDialog<GbFile>(App);
            if (file == null) return;
            if (Inst == null) {
                log.Log("Select a Celeste install first.");
                return;
            }
            var inst = Inst;
            Run(log, async () => {
                await GameBanana.InstallFile(inst, file, App.Cfg, log.Log, log.Prog);
                log.Log("Done.");
            });
        } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }
}

// ---------- Map preview: image + file name + picker (shared by the Topo and Lönn pages) ----------
class MapPanel : DockPanel {
    readonly MainWindow app;
    readonly Image view = new() { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    readonly TextBlock placeholder = Ui.Lbl("MAP IMAGE\nfile.bin", 15, true, Brand.Dim);
    readonly TextBlock name = Ui.Lbl("file.bin", 12, true), info = Ui.Lbl("No map chosen yet.", 9.5, false, Brand.Dim, true);
    readonly Button openEditor, pickBtn;
    readonly Action<string> openIn;
    readonly string editorName;
    string currentPath;
    MapSource current;
    int token;

    public MapPanel(MainWindow app, string editorName, Action<string> openIn) {
        this.app = app;
        this.openIn = openIn;
        this.editorName = editorName;
        Margin = new Thickness(14, 0, 0, 0);
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.TextAlignment = TextAlignment.Center;
        openEditor = Ui.Green(Ui.Btn("Open in " + editorName, () => _ = OpenInEditor()));
        openEditor.IsVisible = openIn != null;
        openEditor.IsEnabled = false;
        pickBtn = Ui.Btn("Maps in my mods ▾", () => _ = PickFromMods());
        var row = Ui.Wrap(
            Ui.Btn("Choose map...", () => _ = Choose()),
            pickBtn,
            openEditor,
            Ui.Btn("Copy path", () => { if (currentPath != null) _ = app.Clipboard?.SetTextAsync(currentPath); }));
        var box = new Border {
            Background = Brand.B(Brand.Panel),
            BorderBrush = Brand.B(Brand.Panel2),
            BorderThickness = new Thickness(2),
            Child = new Grid { Children = { placeholder, view } }
        };
        Children.Add(Ui.Lbl("Map preview", 14, true).Dock(Dock.Top));
        Children.Add(Ui.Col(name, info, row).Dock(Dock.Bottom));
        Children.Add(box);
        Load(
            app.Cfg.LastMap is string p && File.Exists(p) ? new MapSource("", Path.GetFileName(p), p, null, null) : Default(),
            false);
    }
    MapSource Default() {
        var all = MapFinder.Scan(app.Cfg.Current, 400).Where(m => m.Loose).ToList();
        return all.FirstOrDefault(m => m.Display.Contains("City", StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault();
    }

    async Task OpenInEditor() {
        if (current == null || openIn == null) return;
        try {
            if (current.Loose) {
                openIn(current.Path);
                return;
            }
            var dest = MapFinder.CopyPath(current);
            if (File.Exists(dest) && !MapFinder.SameAsZip(current, dest)) {
                var r = await Dlg.Ask(
                    app,
                    $"A copy of this map already exists and differs from the one in the zip (it may contain your edits):\n\n{dest}",
                    "Open existing copy",
                    "Extract a fresh copy",
                    "Cancel");
                if (r == null || r == "Cancel") return;
                if (r == "Extract a fresh copy") {
                    dest = MapFinder.UniquePath(dest);
                    MapFinder.ExtractTo(current, dest);
                }
            } else if (!File.Exists(dest)) MapFinder.ExtractTo(current, dest);
            info.Text = "Extracted a copy to: " + dest + "  (the zip is unchanged)";
            currentPath = dest;
            openIn(dest);
        } catch (Exception ex) { info.Text = "Couldn't open the map: " + ex.Message; }
    }
    async Task Choose() {
        IStorageFolder start = null;
        var c = app.Cfg.Current;
        if (c != null && Directory.Exists(Mods.Dir(c))) start = await app.StorageProvider.TryGetFolderFromPathAsync(Mods.Dir(c));
        var r = await app.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Choose a map (.bin)",
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Celeste map") { Patterns = new[] { "*.bin" } } }
        });
        var path = r.Count > 0 ? r[0].TryGetLocalPath() : null;
        if (path != null) Load(new MapSource("", Path.GetFileName(path), path, null, null), true);
    }
    async Task PickFromMods() {
        info.Text = "Looking for maps in your mods...";
        var inst = app.Cfg.Current;
        var maps = await Task.Run(() => MapFinder.Scan(inst));
        info.Text = maps.Count + " map(s) found in your mods.";
        if (maps.Count == 0) return;
        var menu = new MenuFlyout();
        foreach (var g in maps.GroupBy(m => m.Mod).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
            var sub = new MenuItem { Header = $"{g.Key}  ({g.Count()})" };
            foreach (var m in g.OrderBy(m => m.Display, StringComparer.OrdinalIgnoreCase).Take(80)) {
                var ms = m;
                var it = new MenuItem { Header = m.Display };
                it.Click += (_, _) => Load(ms, true);
                sub.Items.Add(it);
            }
            if (g.Count() > 80) sub.Items.Add(new MenuItem { Header = $"... {g.Count() - 80} more (use Choose map...)", IsEnabled = false });
            menu.Items.Add(sub);
        }
        menu.ShowAt(pickBtn);
    }
    async void Load(MapSource s, bool remember) {
        if (s == null) {
            current = null;
            currentPath = null;
            openEditor.IsEnabled = false;
            view.Source = null;
            placeholder.IsVisible = true;
            placeholder.Text = "MAP IMAGE\nfile.bin";
            name.Text = "file.bin";
            return;
        }
        int my = ++token;
        name.Text = (s.Mod.Length > 0 ? s.Mod + " · " : "") + Path.GetFileName(s.Display);
        info.Text = "Rendering...";
        current = s;
        currentPath = s.Loose ? s.Path : null;
        openEditor.IsEnabled = openIn != null;
        openEditor.Content = s.Loose || openIn == null ? "Open in " + editorName : "Open copy in " + editorName;
        try {
            var a = Brand.Accent;
            var raw = await Task.Run(() => MapBin.Render(MapFinder.Load(s), a.R, a.G, a.B));
            if (my != token) return;
            view.Source = MapRender.ToBitmap(raw);
            placeholder.IsVisible = false;
            RenderOptions.SetBitmapInterpolationMode(
                view,
                raw.Width < 600 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            info.Text = raw.Info + (s.Loose ? "" : openIn != null ? "  ·  inside a zip: opens as a copy, the zip stays unchanged" : "  ·  inside a zip (preview only)");
            if (remember && s.Loose) {
                app.Cfg.LastMap = s.Path;
                app.Cfg.Save();
            }
        } catch (Exception ex) {
            if (my == token) {
                view.Source = null;
                placeholder.IsVisible = true;
                placeholder.Text = "Couldn't read this map";
                info.Text = ex.Message;
            }
        }
    }
}
