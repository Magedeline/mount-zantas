using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace MountZantas;

class MainWindow : Window {
    public Config Cfg = Config.Load();
    readonly ContentControl host = new();
    readonly TextBlock crumb = Ui.Lbl(Brand.Name, 11, true);
    readonly Button back;
    public static string Version => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public MainWindow() {
        try { App.SetAccent(Color.Parse(Cfg.Accent)); } catch { }
        Title = Brand.Name;
        Icon = Brand.Icon();
        Width = 1180;
        Height = 720;
        MinWidth = 1000;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brand.B(Brand.Bg);
        back = Ui.Btn("‹  Main Menu", Main, 130, 32);
        crumb.Margin = new Thickness(12, 0);
        var top = new Border {
            Background = Brand.B(Brand.Panel),
            Height = 44,
            Padding = new Thickness(4, 0),
            Child = Ui.Row(back, crumb)
        };
        Content = new DockPanel { Children = { top.Dock(Avalonia.Controls.Dock.Top), host } };
        if (Cfg.Installs.Count == 0) {
            Cfg.Installs.AddRange(Detect.FindAll());
            Cfg.Save();
        }
        Main();
        Opened += async (_, _) => {
            if (Shot()) return;
            if (Cfg.AutoUpdateApp && Environment.GetEnvironmentVariable("MOUNTZANTAS_DATA") == null) await CheckUpdate(false, _ => { });
            if (Cfg.UpdateOnStartup && Cfg.Current != null) {
                try { await ModUpdate.All(Cfg.Current, Cfg, _ => { }); } catch { }
            }
        };
    }

    public void Main() => Show(new MainPage(this), "");
    public void Show(Control page, string title) {
        host.Content = page;
        crumb.Text = title == "" ? Brand.Name : title;
        back.IsVisible = title != "";
    }

    public void LaunchCeleste(string args = null) {
        var i = Cfg.Current;
        if (i == null) {
            _ = Dlg.Info(this, "No Celeste install found. Open Manage and add one.");
            return;
        }
        try { GameLauncher.Launch(i, args); } catch (Exception ex) {
            _ = Dlg.Info(this, ex.Message);
            return;
        }
        if (Cfg.CloseOnLaunch) Close();
    }

    // Linux and macOS builds don't replace themselves: they say when a newer release exists and link to it.
    public async Task CheckUpdate(bool manual, Action<string> say) {
        try {
            var j = JsonDocument.Parse(await Net.Http.GetStringAsync($"https://api.github.com/repos/{SelfUpdate.Repo}/releases/latest")).RootElement;
            var ver = j.GetProperty("tag_name").GetString().TrimStart('v', 'V');
            if (!ModDb.Newer(Version, ver)) {
                if (manual) say($"You have the latest version ({Version}).");
                return;
            }
            if (await Dlg.YesNo(this, $"Mount Zantas {ver} is available (you have {Version}).\nOpen the download page?")) Platform.OpenUrl($"https://github.com/{SelfUpdate.Repo}/releases/latest");
        } catch (Exception ex) { if (manual) say("Couldn't check for updates: " + ex.Message); }
    }

    // Renders a page to a PNG for visual checks: MountZantas --shot main|installs|everest|mods|download|gb|loenn|topo|options out.png
    bool Shot() {
        var a = App.ShotArgs;
        int si = Array.IndexOf(a, "--shot");
        if (si < 0 || a.Length <= si + 2) return false;
        switch (a[si + 1]) {
            case "installs":
                Show(new InstallsPage(this), "Install Manager");
                break;
            case "everest":
                Show(new EverestPage(this), "Everest Installer");
                break;
            case "mods":
                Show(new ModsPage(this), "Mod Manager");
                break;
            case "download":
                Show(new DownloadPage(this), "Download Mods");
                break;
            case "gb":
                Show(new GameBananaPage(this), "GameBanana");
                break;
            case "loenn":
                Show(new LoennPage(this), "Lönn");
                break;
            case "topo":
                Show(new TopoPage(this), "Topo");
                break;
            case "options":
                Show(new OptionsPage(this), "Options");
                break;
        }
        DispatcherTimer.RunOnce(() => {
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var rtb = new RenderTargetBitmap(size);
            rtb.Render(this);
            rtb.Save(a[si + 2]);
            Close();
        }, TimeSpan.FromSeconds(9));
        return true;
    }
}

abstract class Page : DockPanel {
    protected readonly MainWindow App;
    protected Page(MainWindow app) {
        App = app;
        Margin = new Thickness(14);
    }
    protected Install Inst => App.Cfg.Current;
    protected async void Run(LogBox log, Func<Task> work) {
        try {
            log.Reset();
            await work();
        } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }
    protected async Task<string> PickFile(string title, string typeName, params string[] patterns) {
        var r = await App.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = patterns } }
        });
        return r.Count > 0 ? r[0].TryGetLocalPath() : null;
    }
    protected void OpenFolder(string dir) {
        try {
            Directory.CreateDirectory(dir);
            Platform.OpenFolder(dir);
        } catch { }
    }
}

// ---------- Main menu ----------
class MainPage : Page {
    public MainPage(MainWindow app) : base(app) {
        Margin = new Thickness(8);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("290,*,320") };

        // Left: installations
        var list = new ListBox { Background = Brand.B(Brand.Panel) };
        Ui.Compact(list);
        list.ItemTemplate = new FuncDataTemplate<Install>((i, _) => {
            if (i == null) return null;
            var ev = EverestSvc.EverestVersion(i);
            return new StackPanel { Margin = new Thickness(8, 6), Children = { Ui.Lbl(i.Name, 11, true), Ui.Lbl($"Celeste {EverestSvc.CelesteVersion(i)}" + (ev != null ? $" + Everest {ev}" : " (vanilla)"), 8.5, false, Brand.Dim) } };
        });
        list.ItemsSource = App.Cfg.Installs.ToList();
        if (App.Cfg.Installs.Count > 0) list.SelectedIndex = Math.Clamp(App.Cfg.Selected, 0, App.Cfg.Installs.Count - 1);
        list.SelectionChanged += (_, _) => {
            if (list.SelectedIndex >= 0) {
                App.Cfg.Selected = list.SelectedIndex;
                App.Cfg.Save();
            }
        };
        var manage = Ui.Btn("Manage", () => App.Show(new InstallsPage(App), "Install Manager"));
        manage.HorizontalAlignment = HorizontalAlignment.Stretch;
        var left = Ui.Card(
            new DockPanel { Children = { Ui.Lbl("Installations", 16, true).Dock(Avalonia.Controls.Dock.Top), manage.Dock(Avalonia.Controls.Dock.Bottom), list } },
            10);
        left.Child.Margin = new Thickness(0);
        ((TextBlock)((DockPanel)left.Child).Children[0]).Margin = new Thickness(0, 0, 0, 8);

        // Middle: actions
        var mid = new Grid { RowDefinitions = new RowDefinitions("112,*,*,*,*,*,*"), Margin = new Thickness(4) };
        var title = Ui.Row(
            new Logo(96),
            Ui.Col(Ui.Lbl(Brand.Name, 30, true), Ui.Lbl("Celeste launcher and mod manager", 10, false, Brand.Dim)));
        ((StackPanel)title.Children[1]).Margin = new Thickness(14, 10, 0, 0);
        var row1 = new UniformGrid { Columns = 3, Children = { Ui.Big("Everest", () => App.Show(new EverestPage(App), "Everest Installer")), Ui.Big("Celeste", () => App.LaunchCeleste()), Ui.Big("Zantas Debug", () => App.LaunchCeleste("--debug")) } };
        mid.Children.Add(title.At(0, 0));
        mid.Children.Add(row1.At(0, 1));
        mid.Children.Add(Ui.Big("Download Mods", () => App.Show(new DownloadPage(App), "Download Mods")).At(0, 2));
        mid.Children.Add(Ui.Big("Manage Installed Mods", () => App.Show(new ModsPage(App), "Mod Manager")).At(0, 3));
        mid.Children.Add(Ui.Big("Lönn (Map Editor)", () => App.Show(new LoennPage(App), "Lönn")).At(0, 4));
        var topo = Ui.Big("Topo (CEL Map Editor)", () => App.Show(new TopoPage(App), "Topo"));
        topo.IsEnabled = Topo.Supported;
        if (!Topo.Supported) ToolTip.SetTip(topo, "Topo is a Windows-only program.");
        mid.Children.Add(topo.At(0, 5));
        mid.Children.Add(Ui.Big("Options & Updates", () => App.Show(new OptionsPage(App), "Options")).At(0, 6));

        // Right: community news with pictures, then the latest Everest builds
        var feed = new StackPanel { Width = 280 };
        feed.Children.Add(Ui.Lbl("News", 16, true));
        var status = Ui.Lbl("Loading the news...", 9.5, false, Brand.Dim);
        feed.Children.Add(status);
        var right = Ui.Card(new ScrollViewer { Content = feed, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _ = LoadNews(feed, status);

        root.Children.Add(left.At(0));
        root.Children.Add(mid.At(1));
        root.Children.Add(right.At(2));
        Children.Add(root);
    }
    static async Task LoadNews(StackPanel feed, TextBlock status) {
        try {
            var items = (await News.Get()).Take(8).ToList();
            feed.Children.Remove(status);
            foreach (var n in items) {
                var pic = new Image {
                    Width = 280,
                    MaxHeight = 220,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 6)
                };
                feed.Children.Add(new TextBlock {
                    Text = n.Title,
                    FontSize = Ui.Pt(12.5),
                    FontWeight = FontWeight.Bold,
                    Foreground = Brand.B(Brand.Text),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 2)
                });
                feed.Children.Add(pic);
                feed.Children.Add(Ui.Lbl(n.Text, 9.5, false, Brand.Dim, true));
                if (n.Link != null && n.Link.StartsWith("https://gamebanana.com/")) {
                    var o = Ui.Btn("Open in browser", () => Platform.OpenUrl(n.Link), 150, 32);
                    o.Margin = new Thickness(0, 6, 0, 8);
                    o.HorizontalAlignment = HorizontalAlignment.Left;
                    feed.Children.Add(o);
                }
                _ = SetImage(pic, n.Image);
            }
            feed.Children.Add(new TextBlock {
                Text = "Latest Everest builds",
                FontSize = Ui.Pt(13),
                FontWeight = FontWeight.Bold,
                Foreground = Brand.B(Brand.Text),
                Margin = new Thickness(0, 14, 0, 6)
            });
            var v = (await EverestSvc.Versions()).Take(6).Select(b => $"Everest {b.Version} ({b.Branch})\n{b.Date:yyyy-MM-dd}  {b.Author}\n{b.Description}\n");
            feed.Children.Add(Ui.Lbl(string.Join("\n", v), 9.5, false, Brand.Dim, true));
        } catch { status.Text = "Couldn't load news (offline?)."; }
    }
    static async Task SetImage(Image pic, string url) {
        var b = await Images.Get(url);
        if (b == null) pic.IsVisible = false;
        else pic.Source = b;
    }
}

// ---------- Install manager ----------
class InstallsPage : Page {
    readonly StackPanel flow = new() { Spacing = 8 };
    public InstallsPage(MainWindow app) : base(app) {
        var top = Ui.Wrap(
            Ui.Btn("Find installations automatically", () => {
                foreach (var i in Detect.FindAll())
                    if (!App.Cfg.Installs.Any(x => x.Path.Equals(i.Path, StringComparison.OrdinalIgnoreCase))) App.Cfg.Installs.Add(i);
                App.Cfg.Save();
                Build();
            }),
            Ui.Btn(Platform.Mac ? "Manually select Celeste.app" : "Manually select the Celeste folder", () => _ = Manual()));
        top.Margin = new Thickness(0, 0, 0, 8);
        Children.Add(top.Dock(Avalonia.Controls.Dock.Top));
        Children.Add(new ScrollViewer { Content = flow });
        Build();
    }
    async Task Manual() {
        var r = await App.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Platform.Mac ? "Select Celeste.app" : "Select the folder that holds Celeste" });
        var root = r.Count > 0 ? Detect.ResolveRoot(r[0].TryGetLocalPath()) : null;
        if (r.Count == 0) return;
        if (root == null) {
            await Dlg.Info(App, "That folder doesn't contain Celeste (Celeste.exe wasn't found in it).");
            return;
        }
        App.Cfg.Installs.Add(new Install {
            Name = "Custom (#" + (App.Cfg.Installs.Count + 1) + ")",
            Path = root,
            Store = "Custom"
        });
        App.Cfg.Save();
        Build();
    }
    void Build() {
        flow.Children.Clear();
        foreach (var inst in App.Cfg.Installs.ToList()) {
            var name = Ui.Text();
            name.Text = inst.Name;
            name.Width = 480;
            name.HorizontalAlignment = HorizontalAlignment.Left;
            name.LostFocus += (_, _) => {
                inst.Name = name.Text;
                App.Cfg.Save();
            };
            var ev = EverestSvc.EverestVersion(inst);
            var info = Ui.Col(
                name,
                Ui.Lbl(inst.Path, 9.5, false, Brand.Dim),
                Ui.Lbl($"Celeste {EverestSvc.CelesteVersion(inst)}  +  " + (ev != null ? "Everest " + ev : "no Everest"), 9, false, Brand.Dim));
            var btns = Ui.Col(Ui.Btn("Remove", () => {
                App.Cfg.Installs.Remove(inst);
                App.Cfg.Save();
                Build();
            }, 90), Ui.Btn("Browse", () => OpenFolder(inst.Path), 90, 30));
            var row = new DockPanel {
                Width = 900,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children = { btns.Dock(Avalonia.Controls.Dock.Right), info }
            };
            flow.Children.Add(new Border {
                Background = Brand.B(Brand.Panel),
                Padding = new Thickness(8),
                Child = row,
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(4)
            });
        }
        if (App.Cfg.Installs.Count == 0) flow.Children.Add(Ui.Lbl("No Celeste installs yet. Use the buttons above.", 11, false, Brand.Dim));
    }
}

// ---------- Everest installer ----------
class EverestPage : Page {
    readonly LogBox log = new();
    readonly CheckBox unverified = Ui.Check("Allow unverified builds (no published checksum)");
    readonly TextBlock cur = Ui.Lbl("", 11, false, Brand.Dim);
    readonly Table<EverestBuild> table = new("90,80,150,150,*", new[] { "Build", "Branch", "Date", "Author", "Description" },
        b => new Control[] { Ui.Cell(b.Version.ToString()), Ui.Cell(b.Branch), Ui.Cell(b.Date.ToString("yyyy-MM-dd HH:mm")), Ui.Cell(b.Author), Ui.Cell(b.Description) },
        b => b.Branch == "stable" ? Brand.B(Brand.Green) : b.Branch == "beta" ? Brand.B(Brand.Orange) : null);
    public EverestPage(MainWindow app) : base(app) {
        var top = Ui.Col(Ui.Lbl("Install Everest", 18, true), cur);
        top.Margin = new Thickness(0, 0, 0, 8);
        var bar = Ui.Wrap(
            Ui.Btn("Install selected version", Install),
            Ui.Btn("Uninstall", () => _ = Uninstall()),
            Ui.Btn("Reload versions list", () => _ = LoadList()),
            unverified);
        table.Activated += _ => Install();
        Children.Add(top.Dock(Avalonia.Controls.Dock.Top));
        Children.Add(log.Dock(Avalonia.Controls.Dock.Bottom));
        Children.Add(bar.Dock(Avalonia.Controls.Dock.Bottom));
        Children.Add(table);
        Refresh2();
        _ = LoadList();
    }
    void Refresh2() {
        var i = Inst;
        cur.Text = i == null ? "No Celeste install selected." : $"{i.Name}: " + (EverestSvc.EverestVersion(i) is string v ? "Everest " + v : "Everest not installed");
    }
    async Task LoadList() {
        try { table.Items = (await EverestSvc.Versions()).Take(120); } catch (Exception ex) { log.Log("Couldn't load versions: " + ex.Message); }
    }
    void Install() {
        if (Inst == null) {
            log.Log("Select a Celeste install first.");
            return;
        }
        var b = table.Selected;
        if (b == null) {
            log.Log("Pick a version in the list.");
            return;
        }
        var inst = Inst;
        bool unv = unverified.On();
        Run(log, async () => {
            await EverestSvc.Install(inst, b, log.Log, log.Prog, unv);
            Refresh2();
        });
    }
    async Task Uninstall() {
        if (Inst == null) return;
        if (!await Dlg.YesNo(App, "Remove Everest from this install? Mods and saves stay.")) return;
        try {
            EverestSvc.Uninstall(Inst, log.Log);
            Refresh2();
        } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }
}

// ---------- Lönn ----------
class LoennPage : Page {
    readonly LogBox log = new();
    readonly TextBlock status = Ui.Lbl("", 11, false, Brand.Dim, true);
    public LoennPage(MainWindow app) : base(app) {
        var play = Ui.Green(Ui.Btn("Launch Lönn", () => {
            try { Loenn.Launch(); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }, 320, 44));
        var flow = Ui.Col(Ui.Lbl("Lönn (Map Editor)", 20, true), status, play,
            Ui.Btn("Install / update Lönn", () => Run(log, async () => {
                await Loenn.Install(log.Log, log.Prog);
                Refresh2();
            }), 320, 44),
            Ui.Btn("Open Lönn folder", () => OpenFolder(Loenn.Dir), 320, 44));
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("440,*") };
        split.Children.Add(flow.At(0));
        split.Children.Add(new MapPanel(App, "Lönn", map => {
            try { Loenn.Launch(map); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }).At(1));
        Children.Add(log.Dock(Avalonia.Controls.Dock.Bottom));
        Children.Add(split);
        Refresh2();
        _ = Latest();
    }
    void Refresh2() => status.Text = "Currently installed version: " + (Loenn.Installed() ?? "not installed") + "\nInstall folder: " + Loenn.Dir;
    async Task Latest() {
        try {
            var l = await Loenn.Latest();
            log.Log("Latest version: " + l.tag);
        } catch { }
    }
}

// ---------- Topo (CEL map editor, Windows only) ----------
class TopoPage : Page {
    readonly LogBox log = new();
    readonly TextBlock status = Ui.Lbl("", 11, false, Brand.Dim, true);
    readonly ComboBox edition = Ui.Combo("Topo (no AI features)", "Topo + AI (optional MCP server and agent panel)");
    public TopoPage(MainWindow app) : base(app) {
        edition.Width = 380;
        var flow = Ui.Col(Ui.Lbl("Topo (CEL Map Editor)", 20, true), status, Ui.Lbl("Edition", 10, false, Brand.Dim), edition,
            Ui.Green(Ui.Btn("Launch Topo", () => {
                try { Topo.Launch(Inst); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
            }, 380, 44)),
            Ui.Btn("Install / update Topo", () => Run(log, async () => {
                await Topo.Install(edition.SelectedIndex == 1, log.Log, log.Prog);
                Refresh2();
            }), 380, 44),
            Ui.Btn("Install from a local zip...", () => _ = Local(), 380, 44),
            Ui.Btn("Open Topo folder", () => OpenFolder(Topo.Dir), 380, 44),
            Ui.Lbl("Topo opens .bin maps with Lönn plugins, links live to a running Celeste, and has a PCG room generator. Launching from here points Topo at the Celeste install selected in Mount Zantas.", 9.5, false, Brand.Dim, true));
        flow.MaxWidth = 420;
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("440,*") };
        split.Children.Add(flow.At(0));
        split.Children.Add(new MapPanel(App, "Topo", map => {
            try { Topo.Launch(Inst, map); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }).At(1));
        Children.Add(log.Dock(Avalonia.Controls.Dock.Bottom));
        Children.Add(split);
        Refresh2();
        _ = Latest();
    }
    async Task Local() {
        var f = await PickFile("Select Topo-<version>-win-x64.zip", "Topo zip", "Topo*.zip");
        if (f != null)
            try {
                Topo.InstallLocal(f, log.Log);
                Refresh2();
            } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }
    void Refresh2() => status.Text = "Installed: " + (Topo.Installed() ?? "not installed") + "\nCeleste install: " + (Inst?.Name ?? "none selected");
    async Task Latest() {
        try {
            var l = await Topo.Latest(false);
            log.Log("Latest version: " + l.tag);
        } catch { }
    }
}

// ---------- Options ----------
class OptionsPage : Page {
    static readonly (string name, string hex)[] Accents = { ("Violet (default)", "#6a4cff"), ("Teal", "#1fa89a"), ("Crimson", "#c0392b"), ("Gold", "#c9a227"), ("Rose", "#d6457a") };
    readonly LogBox log = new(110);
    public OptionsPage(MainWindow app) : base(app) {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        var head = Ui.Lbl("Options", 20, true);
        head.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetColumnSpan(head, 2);
        g.Children.Add(head);

        var ac = Ui.Combo(Accents.Select(a => a.name).ToArray());
        ac.SelectedIndex = Math.Max(
            0,
            Array.FindIndex(Accents, a => a.hex.Equals(App.Cfg.Accent, StringComparison.OrdinalIgnoreCase)));
        ac.SelectionChanged += (_, _) => {
            if (ac.SelectedIndex < 0) return;
            App.Cfg.Accent = Accents[ac.SelectedIndex].hex;
            App.Cfg.Save();
            MountZantas.App.SetAccent(Color.Parse(App.Cfg.Accent));
            App.Show(new OptionsPage(App), "Options");
        };
        g.Children.Add(Ui.Section("Appearance", Ui.Lbl("Accent colour (applies immediately)", 10, false, Brand.Dim), ac, new Border {
            Width = 320,
            Height = 6,
            Background = Brand.B(Brand.Accent),
            HorizontalAlignment = HorizontalAlignment.Left
        }).At(0, 1));

        CheckBox Opt(string t, bool v, Action<bool> set) {
            var c = Ui.Check(t, v);
            c.Margin = new Thickness(0, 2);
            c.IsCheckedChanged += (_, _) => {
                set(c.On());
                App.Cfg.Save();
            };
            return c;
        }
        g.Children.Add(Ui.Section("Behaviour",
            Opt("Update mods on startup", App.Cfg.UpdateOnStartup, v => App.Cfg.UpdateOnStartup = v),
            Opt("Close Mount Zantas after starting Celeste", App.Cfg.CloseOnLaunch, v => App.Cfg.CloseOnLaunch = v),
            Opt("Tell me when a new Mount Zantas is out", App.Cfg.AutoUpdateApp, v => App.Cfg.AutoUpdateApp = v),
            Ui.Btn("Check for a Mount Zantas update", () => _ = App.CheckUpdate(true, log.Log), 320)).At(1, 1));

        var mi = Ui.Combo("0x0a.de mirror (default)", "GameBanana (original links)");
        mi.SelectedIndex = App.Cfg.Mirror == "gamebanana" ? 1 : 0;
        mi.SelectionChanged += (_, _) => {
            App.Cfg.Mirror = mi.SelectedIndex == 1 ? "gamebanana" : "0x0a.de";
            App.Cfg.Save();
        };
        g.Children.Add(Ui.Section("Downloads", Ui.Lbl("Where mods are downloaded from. Every download is checked against a published checksum.", 9.5, false, Brand.Dim, true), mi, Ui.Btn("Run connectivity test", () => Run(log, Connectivity), 320)).At(0, 2));

        g.Children.Add(Ui.Section("Folders",
            Ui.Btn("Open Mount Zantas data folder", () => OpenFolder(Config.DataDir), 320),
            Ui.Btn("Open selected Celeste folder", () => { if (Inst != null) OpenFolder(Inst.Path); }, 320),
            Ui.Btn("Clear downloaded preview images", () => log.Log($"Removed {ImageStore.Clear()} cached image(s)."), 320)).At(1, 2));

        var about = Ui.Row(
            new Logo(56),
            Ui.Lbl($"{Brand.Name} {MainWindow.Version} ({Platform.Name})\nA Celeste launcher and mod manager that uses the Everest mod loader (GPL-3.0).\nCeleste is made by Maddy Makes Games. This launcher is fan-made.", 9.5, false, Brand.Dim, true));
        ((Control)about.Children[1]).Margin = new Thickness(12, 0);
        var ab = Ui.Section("About", about);
        Grid.SetColumnSpan(ab, 2);
        g.Children.Add(ab.At(0, 3));
        Children.Add(log.Dock(Avalonia.Controls.Dock.Bottom));
        Children.Add(new ScrollViewer { Content = g });
    }
    async Task Connectivity() {
        foreach (var u in new[] { "https://maddie480.ovh/celeste/everest-versions", "https://maddie480.ovh/celeste/everest_update.yaml", "https://celestemodupdater.0x0a.de/banana-mirror/1368600.zip", "https://api.github.com/repos/EverestAPI/Everest", "https://github.com" }) {
            try {
                using var r = await Net.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, u));
                log.Log($"{(r.IsSuccessStatusCode ? "OK  " : "FAIL")} {(int)r.StatusCode}  {u}");
            } catch (Exception ex) { log.Log($"FAIL  {u}  ({ex.Message})"); }
        }
    }
}
