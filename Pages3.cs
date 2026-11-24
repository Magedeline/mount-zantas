using System.Diagnostics;

namespace MountZantas;

static class Dlg {
    public static Form Base(string title, int w, int h) => new() {
        Text = title, Width = w, Height = h, StartPosition = FormStartPosition.CenterParent, BackColor = Brand.Bg, ForeColor = Brand.Text, Font = Ui.F(), ShowInTaskbar = false, Icon = Brand.AppIcon(), MinimizeBox = false
    };
    public static void OpenUrl(string url) {
        if (url != null && url.StartsWith("https://gamebanana.com/")) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

// The mod description page: preview image(s), title, text, Install / Open in browser / Close.
class ModDetailsDialog : Form {
    public GbFile Chosen;
    readonly PictureBox pic = new() {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(0x0b, 0x0c, 0x12)
    };
    readonly Label counter = Ui.Lbl("", 9, false, Brand.Dim);
    readonly GbProfile p;
    int idx;

    public ModDetailsDialog(GbProfile p) {
        this.p = p;
        Text = p.Name;
        Width = 900;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Brand.Bg;
        ForeColor = Brand.Text;
        Font = Ui.F();
        ShowInTaskbar = false;
        Icon = Brand.AppIcon();
        MinimizeBox = false;
        var head = new Panel { Dock = DockStyle.Top, Height = 78 };
        var t = Ui.Lbl(p.Name, 22, true);
        t.Location = new Point(26, 12);
        var meta = $"by {p.Submitter}    ·    {p.Downloads:N0} downloads    ·    {p.Likes:N0} likes" + (p.Added != default ? $"    ·    added {p.Added:yyyy-MM-dd}" : "") + $"    ·    {p.Files.Count} file(s)";
        var s = Ui.Lbl(meta, 10, false, Brand.Dim);
        s.Location = new Point(28, 50);
        head.Controls.AddRange(new Control[] { t, s });

        var imgPanel = new Panel {
            Dock = DockStyle.Top,
            Height = 270,
            BackColor = pic.BackColor,
            Visible = p.Images.Count > 0
        };
        imgPanel.Controls.Add(pic);
        if (p.Images.Count > 1) {
            var prev = Ui.Btn("‹", () => Show(idx - 1), 44, 40);
            prev.Dock = DockStyle.Left;
            prev.Height = 270;
            var next = Ui.Btn("›", () => Show(idx + 1), 44, 40);
            next.Dock = DockStyle.Right;
            next.Height = 270;
            prev.Font = next.Font = Ui.F(20, true);
            imgPanel.Controls.Add(prev);
            imgPanel.Controls.Add(next);
        }
        counter.Dock = DockStyle.Bottom;
        counter.TextAlign = ContentAlignment.MiddleCenter;
        counter.AutoSize = false;
        counter.Height = 20;
        counter.BackColor = pic.BackColor;
        imgPanel.Controls.Add(counter);

        var body = new TextBox {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Brand.Bg,
            ForeColor = Brand.Text,
            BorderStyle = BorderStyle.None,
            Font = Ui.F(10.5f),
            Text = p.Text.Replace("\r", "").Replace("\n", "\r\n"),
            TabStop = false
        };
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(26, 8, 20, 8) };
        pad.Controls.Add(body);
        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 8, 16, 8)
        };
        var close = Ui.Btn("Close", () => Close(), 100);
        var web = Ui.Btn("Open in browser", () => Dlg.OpenUrl(p.Url));
        var inst = Ui.Btn("Install...", () => {
            using var f = new FilesDialog(p);
            if (f.ShowDialog(this) == DialogResult.OK) {
                Chosen = f.Chosen;
                DialogResult = DialogResult.OK;
            }
        }, 120);
        inst.BackColor = Brand.Green;
        inst.Enabled = p.Files.Count > 0;
        bar.Controls.Add(close);
        bar.Controls.Add(web);
        bar.Controls.Add(inst);
        Controls.Add(pad);
        Controls.Add(imgPanel);
        Controls.Add(head);
        Controls.Add(bar);
        if (p.Images.Count > 0) Show(0);
    }
    async void Show(int i) {
        if (p.Images.Count == 0) return;
        idx = (i + p.Images.Count) % p.Images.Count;
        counter.Text = p.Images.Count > 1 ? $"{idx + 1} / {p.Images.Count}" : "";
        int my = idx;
        var b = await Images.Get(p.Images[idx]);
        if (IsDisposed || my != idx) {
            b?.Dispose();
            return;
        }
        var old = pic.Image;
        pic.Image = b;
        old?.Dispose();
    }
    protected override void OnFormClosed(FormClosedEventArgs e) {
        pic.Image?.Dispose();
        pic.Image = null;
        base.OnFormClosed(e);
    }
}

// The file-version picker: newest file first (green), with date and download count.
class FilesDialog : Form {
    public GbFile Chosen;
    public FilesDialog(GbProfile p) {
        Text = p.Name + " - choose a file";
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Brand.Bg;
        ForeColor = Brand.Text;
        Font = Ui.F();
        ShowInTaskbar = false;
        Icon = Brand.AppIcon();
        MinimizeBox = false;
        var t = Ui.Lbl(p.Name, 22, true);
        t.Location = new Point(24, 14);
        var n = Ui.Lbl(
            "Every file is checked against the MD5 GameBanana publishes before it is installed.",
            9.5f,
            false,
            Brand.Dim);
        n.Location = new Point(26, 52);
        var lv = new ListView {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            BackColor = Brand.Panel,
            ForeColor = Brand.Text,
            BorderStyle = BorderStyle.None,
            Font = Ui.F(10),
            Location = new Point(24, 82),
            Size = new Size(700, 330),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        foreach (var c in new[] { ("File", 250), ("Added", 140), ("Downloads", 105), ("Version / notes", 200) }) lv.Columns.Add(c.Item1, c.Item2);
        Ui.DarkList(lv);
        for (int i = 0; i < p.Files.Count; i++) {
            var f = p.Files[i];
            var it = new ListViewItem(new[] { f.Name, f.Date.ToString("yyyy-MM-dd HH:mm"), f.Downloads.ToString("N0"), f.Description }) { Tag = f };
            if (i == 0) it.BackColor = Brand.Green;
            lv.Items.Add(it);
        }
        if (lv.Items.Count > 0) lv.Items[0].Selected = true;
        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 8, 16, 8)
        };
        void Go() {
            if (lv.SelectedItems.Count > 0) {
                Chosen = (GbFile)lv.SelectedItems[0].Tag;
                DialogResult = DialogResult.OK;
            }
        }
        var ok = Ui.Btn("Install selected", Go);
        ok.BackColor = Brand.Green;
        bar.Controls.Add(Ui.Btn("Close", () => Close(), 100));
        bar.Controls.Add(ok);
        lv.DoubleClick += (_, _) => Go();
        Controls.Add(lv);
        Controls.Add(t);
        Controls.Add(n);
        Controls.Add(bar);
    }
}

// One mod as a card: thumbnail, name, author, date and view/like counts (like the Olympus mod browser).
class ModCard : Panel {
    public readonly GbMod Mod;
    readonly PictureBox pic;
    public ModCard(GbMod m, Action<ModCard> select, Action<ModCard> open) {
        Mod = m;
        Size = new Size(336, 238);
        Margin = new Padding(0, 0, 12, 12);
        BackColor = Brand.Panel;
        Cursor = Cursors.Hand;
        pic = new PictureBox {
            Left = 10,
            Top = 10,
            Width = 316,
            Height = 134,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Brand.Bg
        };
        var title = new Label {
            Text = m.Name,
            Left = 10,
            Top = 150,
            Width = 316,
            Height = 24,
            AutoEllipsis = true,
            UseMnemonic = false,
            Font = Ui.F(11.5f, true),
            ForeColor = Brand.Text
        };
        var sub = new Label {
            Text = $"{m.Submitter}" + (m.Category.Length > 0 ? $"   ·   {m.Category}" : ""),
            Left = 10,
            Top = 176,
            Width = 316,
            Height = 20,
            AutoEllipsis = true,
            UseMnemonic = false,
            Font = Ui.F(9.5f),
            ForeColor = Brand.Dim
        };
        var meta = new Label {
            Text = (m.Added != default ? m.Added.ToString("yyyy-MM-dd") + "   ·   " : "") + $"{m.Views:N0} views   ·   {m.Likes:N0} likes",
            Left = 10,
            Top = 200,
            Width = 316,
            Height = 20,
            AutoEllipsis = true,
            Font = Ui.F(9),
            ForeColor = Brand.Dim
        };
        Controls.AddRange(new Control[] { pic, title, sub, meta });
        void Hook(Control c) {
            c.Click += (_, _) => select(this);
            c.DoubleClick += (_, _) => open(this);
            c.Cursor = Cursors.Hand;
        }
        Hook(this);
        foreach (Control c in Controls) Hook(c);
        _ = LoadThumb();
    }
    async Task LoadThumb() {
        var b = await Images.Get(Mod.Thumb);
        if (IsDisposed) {
            b?.Dispose();
            return;
        }
        if (b != null) pic.Image = b;
    }
    public void SetSelected(bool on) { BackColor = on ? Brand.Hover : Brand.Panel; }
    protected override void Dispose(bool d) {
        if (d) {
            pic.Image?.Dispose();
            pic.Image = null;
        }
        base.Dispose(d);
    }
}

// ---------- GameBanana browser ----------
class GameBananaPage : Page {
    readonly FlowLayoutPanel cards = new() {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Brand.Bg,
        Padding = new Padding(2)
    };
    readonly TextBox search = Ui.Text("Search GameBanana for Celeste mods (empty = most downloaded)");
    readonly LogBox log = new() { Height = 100 };
    readonly Label info = Ui.Lbl("", 10, false, Brand.Dim);
    int page = 1, total;
    Button prev, next;
    ModCard selected;
    int loadToken;

    public GameBananaPage(MainForm app) : base(app) {
        var top = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        search.Width = 480;
        search.Margin = new Padding(6, 8, 10, 6);
        search.KeyDown += (_, e) => {
            if (e.KeyCode == Keys.Enter) {
                page = 1;
                _ = Load();
                e.SuppressKeyPress = true;
            }
        };
        var go = Ui.Btn("Search", () => {
            page = 1;
            _ = Load();
        });
        go.BackColor = Brand.Green;
        var det = Ui.Btn("Details && install", () => _ = Details());
        prev = Ui.Btn("‹ Prev", () => {
            if (page > 1) {
                page--;
                _ = Load();
            }
        }, 80);
        next = Ui.Btn("Next ›", () => {
            if (page * 30 < total) {
                page++;
                _ = Load();
            }
        }, 80);
        info.Margin = new Padding(10, 12, 0, 0);
        top.Controls.AddRange(new Control[] { search, go, det, prev, next, info });
        Controls.Add(cards);
        Controls.Add(log);
        Controls.Add(top);
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
            var (items, t) = await GameBanana.Search(search.Text, page);
            if (my != loadToken || IsDisposed) return;
            total = t;
            cards.SuspendLayout();
            foreach (Control c in cards.Controls.Cast<Control>().ToList()) c.Dispose();
            cards.Controls.Clear();
            selected = null;
            foreach (var m in items) cards.Controls.Add(new ModCard(m, Select, c => {
                Select(c);
                _ = Details();
            }));
            cards.ResumeLayout();
            cards.AutoScrollPosition = new Point(0, 0);
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
            using var d = new ModDetailsDialog(prof);
            if (d.ShowDialog(FindForm()) != DialogResult.OK || d.Chosen == null) return;
            if (Inst == null) {
                log.Log("Select a Celeste install first.");
                return;
            }
            var inst = Inst;
            var file = d.Chosen;
            Run(log, async () => {
                await GameBanana.InstallFile(inst, file, App.Cfg, log.Log, log.Prog);
                log.Log("Done.");
            });
        } catch (Exception ex) { log.Log("ERROR: " + ex.Message); }
    }
}
