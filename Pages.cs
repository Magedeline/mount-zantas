using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace MountZantas;

abstract class Page : UserControl {
    protected readonly MainForm App;
    protected Page(MainForm app) {
        App = app;
        BackColor = Brand.Bg;
        ForeColor = Brand.Text;
        Padding = new Padding(14);
        Font = Ui.F();
    }
    protected Install Inst => App.Cfg.Current;
    protected async void Run(LogBox log, Func<Task> work) {
        try {
            log.Reset();
            await work();
        } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }
}

// ---------- Main menu ----------
class MainPage : Page {
    readonly ListBox list = new() {
        Dock = DockStyle.Fill,
        BackColor = Brand.Panel,
        BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 52
    };
    public MainPage(MainForm app) : base(app) {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

        // Left: installations
        var left = new Panel {
            Dock = DockStyle.Fill,
            BackColor = Brand.Panel,
            Margin = new Padding(6),
            Padding = new Padding(10)
        };
        var hdr = Ui.Lbl("Installations", 16, true);
        hdr.Dock = DockStyle.Top;
        hdr.Height = 40;
        var manage = Ui.Btn("Manage", () => App.Show(new InstallsPage(App), "Install Manager"));
        manage.Dock = DockStyle.Bottom;
        list.DrawItem += (s, e) => {
            if (e.Index < 0) return;
            var i = App.Cfg.Installs[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using var bg = new SolidBrush(sel ? Brand.Accent : Brand.Panel2);
            e.Graphics.FillRectangle(bg, e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width, e.Bounds.Height - 4);
            var ev = EverestSvc.EverestVersion(i);
            e.Graphics.DrawString(i.Name, Ui.F(11, true), Brushes.White, e.Bounds.X + 8, e.Bounds.Y + 6);
            e.Graphics.DrawString(
                $"Celeste {EverestSvc.CelesteVersion(i)}" + (ev != null ? $" + Everest {ev}" : " (vanilla)"),
                Ui.F(8.5f),
                sel ? Brushes.White : Brushes.Silver,
                e.Bounds.X + 8,
                e.Bounds.Y + 28);
        };
        foreach (var i in App.Cfg.Installs) list.Items.Add(i.Name);
        if (list.Items.Count > 0) list.SelectedIndex = Math.Clamp(App.Cfg.Selected, 0, list.Items.Count - 1);
        list.SelectedIndexChanged += (_, _) => {
            App.Cfg.Selected = Math.Max(0, list.SelectedIndex);
            App.Cfg.Save();
        };
        left.Controls.Add(list);
        left.Controls.Add(manage);
        left.Controls.Add(hdr);

        // Middle: actions
        var mid = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(4)
        };
        mid.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        for (int r = 1; r < 7; r++) mid.RowStyles.Add(new RowStyle(SizeType.Percent, 16.66f));
        var title = new Panel { Dock = DockStyle.Fill };
        var logo = new PictureBox {
            Image = Brand.Logo(96),
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 96,
            Height = 96,
            Dock = DockStyle.Left
        };
        var name = Ui.Lbl(Brand.Name, 30, true);
        name.Location = new Point(110, 14);
        var tag = Ui.Lbl("Celeste launcher and mod manager", 10, false, Brand.Dim);
        tag.Location = new Point(114, 64);
        title.Controls.AddRange(new Control[] { logo, name, tag });
        var row1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (int c = 0; c < 3; c++) row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        row1.Controls.Add(Ui.Big("Everest", null, () => App.Show(new EverestPage(App), "Everest Installer")), 0, 0);
        row1.Controls.Add(Ui.Big("Celeste", null, () => App.LaunchCeleste()), 1, 0);
        row1.Controls.Add(Ui.Big("Zantas Debug", null, () => App.LaunchCeleste("--debug")), 2, 0);
        mid.Controls.Add(title, 0, 0);
        mid.Controls.Add(row1, 0, 1);
        mid.Controls.Add(Ui.Big("Download Mods", null, () => App.Show(new DownloadPage(App), "Download Mods")), 0, 2);
        mid.Controls.Add(Ui.Big("Manage Installed Mods", null, () => App.Show(new ModsPage(App), "Mod Manager")), 0, 3);
        mid.Controls.Add(Ui.Big("Lönn (Map Editor)", null, () => App.Show(new LoennPage(App), "Lönn")), 0, 4);
        mid.Controls.Add(Ui.Big("Topo (CEL Map Editor)", null, () => App.Show(new TopoPage(App), "Topo")), 0, 5);
        mid.Controls.Add(Ui.Big("Options && Updates", null, () => App.Show(new OptionsPage(App), "Options")), 0, 6);

        // Right: community news with pictures (public Olympus news feed), then the latest Everest builds
        var right = new Panel {
            Dock = DockStyle.Fill,
            BackColor = Brand.Panel,
            Margin = new Padding(6),
            Padding = new Padding(12),
            AutoScroll = true
        };
        var feed = new FlowLayoutPanel {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Width = 280
        };
        var nh = Ui.Lbl("News", 16, true);
        nh.Margin = new Padding(0, 0, 0, 8);
        feed.Controls.Add(nh);
        var status = new Label {
            Text = "Loading the news...",
            ForeColor = Brand.Dim,
            Font = Ui.F(9.5f),
            AutoSize = true
        };
        feed.Controls.Add(status);
        right.Controls.Add(feed);
        _ = LoadNews(feed, status);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(mid, 1, 0);
        root.Controls.Add(right, 2, 0);
        Controls.Add(root);
    }
    static async Task LoadNews(FlowLayoutPanel feed, Label status) {
        try {
            var items = (await News.Get()).Take(8).ToList();
            if (feed.IsDisposed) return;
            feed.Controls.Remove(status);
            foreach (var n in items) {
                var pic = new PictureBox {
                    Width = 280,
                    Height = 120,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Brand.Bg,
                    Margin = new Padding(0, 6, 0, 6)
                };
                var title = new Label {
                    Text = n.Title,
                    Font = Ui.F(12.5f, true),
                    ForeColor = Brand.Text,
                    AutoSize = true,
                    UseMnemonic = false,
                    MaximumSize = new Size(280, 0),
                    Margin = new Padding(0, 4, 0, 2)
                };
                var text = new Label {
                    Text = n.Text,
                    Font = Ui.F(9.5f),
                    ForeColor = Brand.Dim,
                    AutoSize = true,
                    UseMnemonic = false,
                    MaximumSize = new Size(280, 0),
                    Margin = new Padding(0, 0, 0, 6)
                };
                var open = Ui.Btn("Open in browser", () => Dlg.OpenUrl(n.Link), 150, 32);
                open.Margin = new Padding(0, 0, 0, 18);
                feed.Controls.Add(title);
                feed.Controls.Add(pic);
                feed.Controls.Add(text);
                if (n.Link != null && n.Link.StartsWith("https://gamebanana.com/")) feed.Controls.Add(open);
                _ = SetNewsImage(pic, n.Image);
            }
            var eb = new Label {
                Text = "Latest Everest builds",
                Font = Ui.F(13, true),
                ForeColor = Brand.Text,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 6)
            };
            feed.Controls.Add(eb);
            var v = (await EverestSvc.Versions()).Take(6).Select(b => $"Everest {b.Version} ({b.Branch})\r\n{b.Date:yyyy-MM-dd}  {b.Author}\r\n{b.Description}\r\n");
            if (!feed.IsDisposed) feed.Controls.Add(new Label {
                Text = string.Join("\r\n", v),
                ForeColor = Brand.Dim,
                Font = Ui.F(9.5f),
                AutoSize = true,
                MaximumSize = new Size(280, 0)
            });
        } catch { if (!status.IsDisposed) status.Text = "Couldn't load news (offline?)."; }
    }
    static async Task SetNewsImage(PictureBox pic, string url) {
        var b = await Images.Get(url);
        if (pic.IsDisposed) {
            b?.Dispose();
            return;
        }
        if (b == null) {
            pic.Visible = false;
            return;
        }
        pic.Height = Math.Min(220, Math.Max(60, 280 * b.Height / b.Width));
        pic.Image = b;
    }
}

// ---------- Install manager ----------
class InstallsPage : Page {
    readonly FlowLayoutPanel flow = new() {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true
    };
    public InstallsPage(MainForm app) : base(app) {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, FlowDirection = FlowDirection.LeftToRight };
        top.Controls.Add(Ui.Btn("Find installations automatically", () => {
            foreach (var i in Detect.FindAll())
                if (!App.Cfg.Installs.Any(x => x.Path.Equals(i.Path, StringComparison.OrdinalIgnoreCase))) App.Cfg.Installs.Add(i);
            App.Cfg.Save();
            Build();
        }));
        top.Controls.Add(Ui.Btn("Manually select Celeste.exe", () => {
            using var d = new OpenFileDialog { Filter = "Celeste.exe|Celeste.exe", Title = "Select Celeste.exe" };
            if (d.ShowDialog() == DialogResult.OK) {
                App.Cfg.Installs.Add(new Install {
                    Name = "Custom (#" + (App.Cfg.Installs.Count + 1) + ")",
                    Path = Path.GetDirectoryName(d.FileName),
                    Store = "Custom"
                });
                App.Cfg.Save();
                Build();
            }
        }));
        Controls.Add(flow);
        Controls.Add(top);
        Build();
    }
    void Build() {
        flow.Controls.Clear();
        foreach (var inst in App.Cfg.Installs.ToList()) {
            var row = new Panel { Width = 900, Height = 78, BackColor = Brand.Panel, Margin = new Padding(0, 0, 0, 8) };
            var name = Ui.Text();
            name.Text = inst.Name;
            name.SetBounds(12, 8, 480, 28);
            name.Leave += (_, _) => {
                inst.Name = name.Text;
                App.Cfg.Save();
            };
            var ev = EverestSvc.EverestVersion(inst);
            var path = Ui.Lbl(inst.Path, 9.5f, false, Brand.Dim);
            path.Location = new Point(12, 40);
            var ver = Ui.Lbl(
                $"Celeste {EverestSvc.CelesteVersion(inst)}  +  " + (ev != null ? "Everest " + ev : "no Everest"),
                9,
                false,
                Brand.Dim);
            ver.Location = new Point(12, 58);
            var rm = Ui.Btn("Remove", () => {
                App.Cfg.Installs.Remove(inst);
                App.Cfg.Save();
                Build();
            }, 90);
            rm.Location = new Point(780, 8);
            var br = Ui.Btn("Browse", () => Process.Start("explorer.exe", inst.Path), 90);
            br.Location = new Point(780, 44);
            br.Height = 28;
            row.Controls.AddRange(new Control[] { name, path, ver, rm, br });
            flow.Controls.Add(row);
        }
        if (App.Cfg.Installs.Count == 0) flow.Controls.Add(Ui.Lbl("No Celeste installs yet. Use the buttons above.", 11, false, Brand.Dim));
    }
}

// ---------- Everest installer ----------
class EverestPage : Page {
    readonly ListView lv = new() {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BackColor = Brand.Panel,
        ForeColor = Brand.Text,
        BorderStyle = BorderStyle.None,
        HideSelection = false,
        Font = Ui.F(10)
    };
    readonly LogBox log = new();
    readonly CheckBox unverified = Ui.Check("Allow unverified builds (no published checksum)");
    readonly Label cur = Ui.Lbl("", 11, false, Brand.Dim);
    readonly Label loader = Ui.Lbl("", 10, false, Brand.Dim);
    List<EverestBuild> builds = new();
    public EverestPage(MainForm app) : base(app) {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 104, FlowDirection = FlowDirection.TopDown };
        top.Controls.Add(Ui.Lbl("Install Everest", 18, true));
        top.Controls.Add(cur);
        top.Controls.Add(loader);
        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight
        };
        bar.Controls.Add(Ui.Btn("Install selected version", Install));
        bar.Controls.Add(Ui.Btn("Uninstall", Uninstall));
        bar.Controls.Add(Ui.Btn("Reload versions list", () => _ = LoadList()));
        bar.Controls.Add(Ui.Btn("Install Zantas Loader", InstallLoader));
        bar.Controls.Add(Ui.Btn("Load report", ShowReport));
        bar.Controls.Add(unverified);
        foreach (var h in new[] { ("Build", 90), ("Branch", 80), ("Date", 150), ("Author", 150), ("Description", 600) }) lv.Columns.Add(h.Item1, h.Item2);
        Ui.DarkList(lv);
        Controls.Add(lv);
        Controls.Add(bar);
        Controls.Add(log);
        Controls.Add(top);
        Refresh2();
        _ = LoadList();
    }
    void Refresh2() {
        var i = Inst;
        cur.Text = i == null ? "No Celeste install selected." : $"{i.Name}: " + (EverestSvc.EverestVersion(i) is string v ? "Everest " + v : "Everest not installed");
        loader.Text = LoaderText(i);
    }

    // One line about Zantas Loader: installed version and the last load report.
    static string LoaderText(Install i) {
        if (i == null) {
            return "";
        }
        var have = ZantasLoader.InstalledVersion(i);
        if (have == null) {
            return "Zantas Loader: not installed (it is added automatically when you install Everest).";
        }
        var text = "Zantas Loader " + have + ". " + ZantasLoader.Summary(ZantasLoader.ReadSession(i));
        if (ZantasLoader.NeedsInstall(i)) {
            text += " Update available: " + ZantasLoader.BundledVersion() + ".";
        }
        return text;
    }

    void InstallLoader() {
        if (Inst == null) {
            log.Log("Select a Celeste install first.");
            return;
        }
        try {
            ZantasLoader.Install(Inst, log.Log);
            Refresh2();
        } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }

    // Writes the last load report from the game into the log box.
    void ShowReport() {
        if (Inst == null) {
            return;
        }
        var s = ZantasLoader.ReadSession(Inst);
        log.Log(ZantasLoader.Summary(s));
        if (s == null) {
            return;
        }
        log.Log($"Zantas Loader {s.Loader} on Everest {s.Everest}.");
        foreach (var problem in s.Problems) {
            log.Log("PROBLEM: " + problem);
        }
    }
    async Task LoadList() {
        try {
            builds = await EverestSvc.Versions();
            lv.Items.Clear();
            foreach (var b in builds.Take(120)) {
                var it = new ListViewItem(new[] { b.Version.ToString(), b.Branch, b.Date.ToString("yyyy-MM-dd HH:mm"), b.Author, b.Description }) { Tag = b };
                it.BackColor = b.Branch == "stable" ? Brand.Green : b.Branch == "beta" ? Brand.Orange : Brand.Panel;
                lv.Items.Add(it);
            }
        } catch (Exception ex) { log.Log("Couldn't load versions: " + ex.Message); }
    }
    void Install() {
        if (Inst == null) {
            log.Log("Select a Celeste install first.");
            return;
        }
        if (lv.SelectedItems.Count == 0) {
            log.Log("Pick a version in the list.");
            return;
        }
        var b = (EverestBuild)lv.SelectedItems[0].Tag;
        var inst = Inst;
        Run(log, async () => {
            await EverestSvc.Install(inst, b, log.Log, log.Prog, unverified.Checked);
            Refresh2();
        });
    }
    void Uninstall() {
        if (Inst == null) return;
        if (MessageBox.Show("Remove Everest from this install? Mods and saves stay.", Brand.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try {
            EverestSvc.Uninstall(Inst, log.Log);
            Refresh2();
        } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }
}

// ---------- Lönn ----------
class LoennPage : Page {
    readonly LogBox log = new();
    readonly Label status = Ui.Lbl("", 12, false, Brand.Dim);
    public LoennPage(MainForm app) : base(app) {
        var flow = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        flow.Controls.Add(Ui.Lbl("Lönn (Map Editor)", 20, true));
        flow.Controls.Add(status);
        var play = Ui.Btn("Launch Lönn", () => {
            try { Loenn.Launch(); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }, 320, 44);
        play.BackColor = Brand.Green;
        flow.Controls.Add(play);
        flow.Controls.Add(Ui.Btn("Install / update Lönn", () => Run(log, async () => {
            await Loenn.Install(log.Log, log.Prog);
            Refresh2();
        }), 320, 44));
        flow.Controls.Add(Ui.Btn("Open Lönn folder", () => {
            Directory.CreateDirectory(Loenn.Dir);
            Process.Start("explorer.exe", Loenn.Dir);
        }, 320, 44));
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 440));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.Controls.Add(flow, 0, 0);
        split.Controls.Add(new MapPanel(App, "Lönn", map => {
            try { Loenn.Launch(map); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }), 1, 0);
        Controls.Add(split);
        Controls.Add(log);
        Refresh2();
        _ = Latest();
    }
    void Refresh2() => status.Text = "Currently installed version: " + (Loenn.Installed() ?? "not installed") + "    Install folder: " + Loenn.Dir;
    async Task Latest() {
        try {
            var l = await Loenn.Latest();
            if (!IsDisposed) log.Log("Latest version: " + l.tag);
        } catch { }
    }
}

// ---------- Topo (CEL map editor) ----------
class TopoPage : Page {
    readonly LogBox log = new();
    readonly Label status = Ui.Lbl("", 12, false, Brand.Dim);
    readonly DropButton edition = Ui.Combo("Topo (no AI features)", "Topo + AI (optional MCP server and agent panel)");
    public TopoPage(MainForm app) : base(app) {
        var flow = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        flow.Controls.Add(Ui.Lbl("Topo (CEL Map Editor)", 20, true));
        flow.Controls.Add(status);
        flow.Controls.Add(Ui.Lbl("Edition", 10, false, Brand.Dim));
        edition.SelectedIndex = 0;
        edition.Width = 380;
        flow.Controls.Add(edition);
        var play = Ui.Btn("Launch Topo", () => {
            try { Topo.Launch(Inst); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }, 380, 44);
        play.BackColor = Brand.Green;
        flow.Controls.Add(play);
        flow.Controls.Add(Ui.Btn("Install / update Topo", () => Run(log, async () => {
            await Topo.Install(edition.SelectedIndex == 1, log.Log, log.Prog);
            Refresh2();
        }), 380, 44));
        flow.Controls.Add(Ui.Btn("Install from a local zip...", () => {
            using var d = new OpenFileDialog { Filter = "Topo zip|Topo*.zip", Title = "Select Topo-<version>-win-x64.zip" };
            if (d.ShowDialog() == DialogResult.OK)
                try {
                    Topo.InstallLocal(d.FileName, log.Log);
                    Refresh2();
                } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }, 380, 44));
        flow.Controls.Add(Ui.Btn("Open Topo folder", () => {
            Directory.CreateDirectory(Topo.Dir);
            Process.Start("explorer.exe", Topo.Dir);
        }, 380, 44));
        flow.Controls.Add(Ui.Lbl("\r\nTopo opens .bin maps with Lönn plugins, links live to a running Celeste, and has a PCG room generator.\r\nLaunching from here points Topo at the Celeste install selected in Mount Zantas.", 9.5f, false, Brand.Dim));
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 440));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.Controls.Add(flow, 0, 0);
        split.Controls.Add(new MapPanel(App, "Topo", map => {
            try { Topo.Launch(Inst, map); } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
        }), 1, 0);
        Controls.Add(split);
        Controls.Add(log);
        Refresh2();
        _ = Latest();
    }
    void Refresh2() => status.Text = "Installed: " + (Topo.Installed() ?? "not installed") + "    Celeste install: " + (Inst?.Name ?? "none selected");
    async Task Latest() {
        try {
            var l = await Topo.Latest(false);
            if (!IsDisposed) log.Log("Latest version: " + l.tag);
        } catch { }
    }
}

