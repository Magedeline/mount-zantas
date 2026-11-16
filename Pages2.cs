using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace MountZantas;

// ---------- Installed mods ----------
class ModsPage : Page {
    static readonly Color Warn = Color.FromArgb(0xe3, 0xa2, 0x4a);
    readonly DataGridView grid = new() { Dock = DockStyle.Fill };
    readonly TextBox search = Ui.Text("Search by file name, mod title or everest.yaml ID");
    readonly CheckBox onlyOn = Ui.Check("Enabled only"), onlyFav = Ui.Check("Favorites only"), onlyIssues = Ui.Check("Needs attention"), onlyUpd = Ui.Check("Updates available");
    readonly LogBox log = new() { Height = 90 }; readonly Label count = Ui.Lbl("", 10, false, Brand.Dim); readonly Button updateAll;
    Button presetsBtn; List<ModInfo> all = new(); readonly Dictionary<string, ModEntry> updates = new(StringComparer.OrdinalIgnoreCase); readonly Dictionary<ModInfo, string> issues = new(); bool binding;

    public ModsPage(MainForm app) : base(app) {
        Ui.Grid(grid);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Fav", Width = 44, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "On", Width = 50, SortMode = DataGridViewColumnSortMode.NotSortable });
        foreach (var c in new[] { ("Name", 280), ("Version", 100), ("Update", 100), ("Needs attention", 250), ("File", 300) }) grid.Columns.Add(c.Item1, c.Item1);
        grid.Columns[2].Width = 280; grid.Columns[3].Width = 100; grid.Columns[4].Width = 100; grid.Columns[5].Width = 250; grid.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        for (int i = 2; i < 7; i++) grid.Columns[i].ReadOnly = true;
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += OnCell; grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 2 && grid.Rows[e.RowIndex].Tag is ModInfo m) { m.Enabled = !m.Enabled; Save(); grid.Rows[e.RowIndex].Cells[1].Value = m.Enabled; } };
        grid.CellMouseUp += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { if (!grid.Rows[e.RowIndex].Selected) { grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; } RowMenu(); } };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        top.Controls.Add(Ui.Btn("Open mods folder", () => { if (Inst != null) { Directory.CreateDirectory(Mods.Dir(Inst)); Process.Start("explorer.exe", Mods.Dir(Inst)); } }));
        top.Controls.Add(Ui.Btn("Install mod file", InstallFile)); presetsBtn = Ui.Btn("Mod presets", ShowPresets); top.Controls.Add(presetsBtn); top.Controls.Add(Ui.Btn("Delete selected", Delete));
        top.Controls.Add(Ui.Btn("Enable selected", () => SetSelected(true))); top.Controls.Add(Ui.Btn("Disable selected", () => SetSelected(false)));
        top.Controls.Add(Ui.Btn("Fix dependencies", () => Run(log, FixDeps)));
        updateAll = Ui.Btn("Update All", () => Run(log, async () => { int n = await ModUpdate.All(Inst, App.Cfg, log.Log, log.Prog); log.Log($"{n} mod(s) updated."); Reload(); _ = CheckUpdates(); })); updateAll.BackColor = Brand.Green; top.Controls.Add(updateAll);
        var filt = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        search.Width = 400; search.Margin = new Padding(8, 8, 8, 4); search.TextChanged += (_, _) => Bind();
        foreach (var c in new[] { onlyOn, onlyFav, onlyIssues, onlyUpd }) c.CheckedChanged += (_, _) => Bind();
        count.Margin = new Padding(12, 10, 0, 0); filt.Controls.AddRange(new Control[] { search, onlyOn, onlyFav, onlyIssues, onlyUpd, count });
        Controls.Add(grid); Controls.Add(log); Controls.Add(filt); Controls.Add(top); Reload(); _ = CheckUpdates();
    }

    void Reload() { if (Inst == null) { log.Log("Select a Celeste install first."); return; } all = Mods.Scan(Inst); Bind(); }
    void ComputeIssues() {
        issues.Clear(); var by = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase); foreach (var m in all) by[m.Name] = m;
        foreach (var m in all.Where(m => m.Enabled)) {
            var p = new List<string>();
            foreach (var d in m.Deps) { if (d is "Everest" or "EverestCore") continue; if (!by.TryGetValue(d, out var dm)) p.Add("missing " + d); else if (!dm.Enabled) p.Add(d + " is off"); }
            if (p.Count > 0) issues[m] = string.Join(", ", p);
        }
    }
    void StyleRow(DataGridViewRow r) {
        var m = (ModInfo)r.Tag; r.Cells[5].Value = issues.TryGetValue(m, out var t) ? "⚠ " + t : "";
        r.DefaultCellStyle.ForeColor = !m.Enabled ? Brand.Dim : issues.ContainsKey(m) ? Warn : Brand.Text;
    }
    void Bind() {
        binding = true; ComputeIssues(); grid.Rows.Clear(); var q = search.Text.Trim();
        bool Upd(ModInfo m) => updates.TryGetValue(m.Name, out var e) && ModDb.Newer(m.Version, e.Version);
        foreach (var m in all.Where(m => (q.Length == 0 || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.File.Contains(q, StringComparison.OrdinalIgnoreCase))
                  && (!onlyOn.Checked || m.Enabled) && (!onlyFav.Checked || App.Cfg.Favorites.Contains(m.Name)) && (!onlyIssues.Checked || issues.ContainsKey(m)) && (!onlyUpd.Checked || Upd(m)))) {
            int r = grid.Rows.Add(App.Cfg.Favorites.Contains(m.Name), m.Enabled, m.Name, m.Version, Upd(m) ? "→ " + updates[m.Name].Version : "", "", m.File);
            grid.Rows[r].Tag = m; StyleRow(grid.Rows[r]);
        }
        RefreshCounts(); binding = false;
    }
    void RefreshCounts() {
        int n = updates.Count(kv => all.Any(m => m.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) && ModDb.Newer(m.Version, kv.Value.Version)));
        count.Text = $"{all.Count(m => m.Enabled)} of {all.Count} enabled · {issues.Count} need attention"; updateAll.Text = n > 0 ? $"Update All ({n})" : "Update All";
    }
    async Task CheckUpdates() {
        try { var db = await ModDb.Get(); updates.Clear(); foreach (var m in all) if (!m.IsDir && db.TryGetValue(m.Name, out var e) && ModDb.Newer(m.Version, e.Version)) updates[m.Name] = e; if (!IsDisposed) Bind(); } catch { }
    }
    void OnCell(object s, DataGridViewCellEventArgs e) {
        if (binding || e.RowIndex < 0 || grid.Rows[e.RowIndex].Tag is not ModInfo m) return; bool v = (bool)(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value ?? false);
        if (e.ColumnIndex == 0) { if (v) App.Cfg.Favorites.Add(m.Name); else App.Cfg.Favorites.Remove(m.Name); App.Cfg.Save(); }
        else if (e.ColumnIndex == 1) { m.Enabled = v; Save(); ComputeIssues(); binding = true; foreach (DataGridViewRow r in grid.Rows) StyleRow(r); binding = false; RefreshCounts(); }
    }
    void Save() { try { Mods.WriteDisabled(Inst, all.Where(m => !m.Enabled).Select(m => m.File)); } catch (Exception ex) { log.Log("Couldn't save blacklist: " + ex.Message); } }
    void SetSelected(bool on) {
        var rows = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as ModInfo).Where(m => m != null).ToList(); if (rows.Count == 0) { log.Log("Select one or more mods first."); return; }
        foreach (var m in rows) m.Enabled = on; Save(); Bind(); log.Log($"{(on ? "Enabled" : "Disabled")} {rows.Count} mod(s). Restart Celeste for it to take effect.");
    }
    async Task FixDeps() {
        ComputeIssues(); if (issues.Count == 0) { log.Log("No dependency problems found."); return; }
        var by = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase); foreach (var m in all) by[m.Name] = m; var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int enabled = 0;
        foreach (var m in all.Where(m => m.Enabled)) foreach (var d in m.Deps) { if (d is "Everest" or "EverestCore") continue; if (!by.TryGetValue(d, out var dm)) missing.Add(d); else if (!dm.Enabled) { dm.Enabled = true; enabled++; } }
        if (enabled > 0) { Save(); log.Log($"Enabled {enabled} required mod(s)."); }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var n in missing) await Mods.InstallWithDeps(Inst, n, App.Cfg, log.Log, log.Prog, seen);
        Reload(); _ = CheckUpdates(); log.Log("Dependencies fixed.");
    }
    void InstallFile() {
        if (Inst == null) return; using var d = new OpenFileDialog { Filter = "Mod zip|*.zip", Multiselect = true };
        if (d.ShowDialog() != DialogResult.OK) return; Directory.CreateDirectory(Mods.Dir(Inst));
        foreach (var f in d.FileNames) { File.Copy(f, Path.Combine(Mods.Dir(Inst), Path.GetFileName(f)), true); log.Log("Installed " + Path.GetFileName(f)); } Reload();
    }
    void Delete() {
        var rows = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as ModInfo).Where(m => m != null).ToList(); if (rows.Count == 0 || Inst == null) return;
        if (MessageBox.Show($"Move {rows.Count} mod(s) to the Recycle Bin?", Brand.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        foreach (var m in rows) { var p = Path.Combine(Mods.Dir(Inst), m.File); try { if (m.IsDir) FileSystem.DeleteDirectory(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); else FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); log.Log("Deleted " + m.Name); } catch (Exception ex) { log.Log("Couldn't delete " + m.Name + ": " + ex.Message); } }
        Reload();
    }
    void RowMenu() {
        if (grid.SelectedRows.Count == 0 || grid.SelectedRows[0].Tag is not ModInfo m) return; var menu = Ui.DarkMenu();
        menu.Items.Add(m.Enabled ? "Disable" : "Enable", null, (_, _) => SetSelected(!m.Enabled));
        menu.Items.Add("Show in Explorer", null, (_, _) => Process.Start("explorer.exe", $"/select,\"{Path.Combine(Mods.Dir(Inst), m.File)}\""));
        menu.Items.Add("Copy name", null, (_, _) => Clipboard.SetText(m.Name));
        menu.Items.Add("Required mods: " + (m.Deps.Where(d => d != "Everest").Any() ? string.Join(", ", m.Deps.Where(d => d != "Everest")) : "none"), null, null).Enabled = false;
        if (updates.TryGetValue(m.Name, out var e) && ModDb.Newer(m.Version, e.Version) && !m.IsDir) menu.Items.Add($"Update to {e.Version}", null, (_, _) => Run(log, async () => { await ModUpdate.Zip(Inst, m, e, App.Cfg, log.Log, log.Prog); Reload(); _ = CheckUpdates(); }));
        menu.Show(Cursor.Position);
    }
    void ShowPresets() {
        if (Inst == null) return; var menu = Ui.DarkMenu();
        menu.Items.Add("Save current mods as a preset...", null, (_, _) => { var n = Ui.Prompt("Save preset", "Preset name:"); if (n != null) { App.Cfg.Presets[n] = all.Where(m => !m.Enabled).Select(m => m.File).ToList(); App.Cfg.Save(); log.Log($"Saved preset \"{n}\"."); } });
        if (App.Cfg.Presets.Count > 0) menu.Items.Add(new ToolStripSeparator());
        foreach (var kv in App.Cfg.Presets.ToList()) {
            var name = kv.Key; menu.Items.Add($"Apply: {name} ({kv.Value.Count} off)", null, (_, _) => { var off = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase); foreach (var m in all) m.Enabled = !off.Contains(m.File); Save(); Bind(); log.Log($"Applied \"{name}\". Restart Celeste for it to take effect."); });
            menu.Items.Add($"Delete: {name}", null, (_, _) => { App.Cfg.Presets.Remove(name); App.Cfg.Save(); });
        }
        menu.Show(presetsBtn, new Point(0, presetsBtn.Height));   // anchored under the button
    }
}

// ---------- Download mods ----------
class DownloadPage : Page {
    readonly ListView lv = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BackColor = Brand.Panel, ForeColor = Brand.Text, BorderStyle = BorderStyle.None, HideSelection = false, Font = Ui.F(10) };
    readonly TextBox search = Ui.Text("Search the public Everest mod database"); readonly LogBox log = new() { Height = 100 }; readonly DropButton sort = Ui.Combo("Recently updated", "Name (A–Z)", "Largest first");
    readonly Label shown = Ui.Lbl("", 10, false, Brand.Dim); Dictionary<string, ModEntry> db; Dictionary<string, string> have = new(StringComparer.OrdinalIgnoreCase);

    public DownloadPage(MainForm app) : base(app) {
        foreach (var h in new[] { ("Name", 340), ("Version", 110), ("Updated", 110), ("Size", 90), ("Status", 220) }) lv.Columns.Add(h.Item1, h.Item2); Ui.DarkList(lv);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        search.Width = 440; search.Margin = new Padding(6, 8, 10, 6); search.TextChanged += (_, _) => Fill(); sort.Width = 210; sort.Margin = new Padding(0, 6, 10, 6); sort.SelectedIndexChanged += (_, _) => Fill();
        var go = Ui.Btn("Install selected", Install); go.BackColor = Brand.Green; shown.Margin = new Padding(10, 12, 0, 0);
        var gb = Ui.Btn("Browse GameBanana...", () => App.Show(new GameBananaPage(App), "GameBanana"));
        top.Controls.AddRange(new Control[] { search, sort, go, gb, shown });
        lv.DoubleClick += (_, _) => Install();
        Controls.Add(lv); Controls.Add(log); Controls.Add(top); log.Log("Loading the mod database..."); _ = LoadList();
    }
    async Task LoadList() { try { db = await ModDb.Get(); log.Log($"{db.Count} mods available. Double-click a mod to install it with the helper mods it needs."); Fill(); } catch (Exception ex) { log.Log("Couldn't load the database: " + ex.Message); } }
    void Fill() {
        if (db == null) return; if (Inst != null) have = Mods.Scan(Inst).GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Version, StringComparer.OrdinalIgnoreCase);
        var q = search.Text.Trim(); var src = db.Values.Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var ordered = sort.SelectedIndex == 1 ? src.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase) : sort.SelectedIndex == 2 ? src.OrderByDescending(e => e.Size) : src.OrderByDescending(e => e.Updated);
        lv.BeginUpdate(); lv.Items.Clear();
        foreach (var e in ordered.Take(400)) {
            string st = have.TryGetValue(e.Name, out var v) ? (ModDb.Newer(v, e.Version) ? $"Update available ({v} → {e.Version})" : "Installed " + v) : "";
            var it = new ListViewItem(new[] { e.Name, e.Version, DateTimeOffset.FromUnixTimeSeconds(e.Updated).LocalDateTime.ToString("yyyy-MM-dd"), e.Size > 0 ? $"{e.Size / 1048576.0:0.0} MB" : "", st }) { Tag = e };
            if (st.StartsWith("Update")) it.ForeColor = Color.FromArgb(0xe3, 0xa2, 0x4a); else if (st.StartsWith("Installed")) it.ForeColor = Color.FromArgb(0x7f, 0xd0, 0x92);
            lv.Items.Add(it);
        }
        lv.EndUpdate(); Ui.FitList(lv); shown.Text = $"Showing {Math.Min(400, src.Count)} of {src.Count}";
    }
    void Install() {
        if (Inst == null) { log.Log("Select a Celeste install first."); return; } var picks = lv.SelectedItems.Cast<ListViewItem>().Select(i => ((ModEntry)i.Tag).Name).ToList(); if (picks.Count == 0) { log.Log("Pick a mod first."); return; }
        var inst = Inst; Run(log, async () => { var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var n in picks) await Mods.InstallWithDeps(inst, n, App.Cfg, log.Log, log.Prog, seen); log.Log("Done."); Fill(); });
    }
}

// ---------- Options ----------
class OptionsPage : Page {
    static readonly (string name, string hex)[] Accents = { ("Violet (default)", "#6a4cff"), ("Teal", "#1fa89a"), ("Crimson", "#c0392b"), ("Gold", "#c9a227"), ("Rose", "#d6457a") };
    readonly LogBox log = new() { Height = 110 };
    public OptionsPage(MainForm app) : base(app) {
        var f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(0, 0, 0, 8) };
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var head = Ui.Lbl("Options", 20, true); head.Margin = new Padding(0, 0, 0, 10); f.Controls.Add(head, 0, 0); f.SetColumnSpan(head, 2);
        void Put(Control c, int col, int row, int span = 1) { c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; c.MinimumSize = new Size(0, 0); f.Controls.Add(c, col, row); if (span > 1) f.SetColumnSpan(c, span); }

        var ac = Ui.Combo(Accents.Select(a => a.name).ToArray()); ac.SelectedIndex = Math.Max(0, Array.FindIndex(Accents, a => a.hex.Equals(App.Cfg.Accent, StringComparison.OrdinalIgnoreCase)));
        ac.SelectedIndexChanged += (_, _) => { App.Cfg.Accent = Accents[ac.SelectedIndex].hex; App.Cfg.Save(); Brand.Accent = ColorTranslator.FromHtml(App.Cfg.Accent); App.Icon = Brand.AppIcon(); App.Show(new OptionsPage(App), "Options"); };
        var sw = new Panel { Width = 320, Height = 6, BackColor = Brand.Accent, Margin = new Padding(0, 0, 0, 4) };
        Put(Ui.Section("Appearance", Ui.Lbl("Accent colour (applies immediately)", 10, false, Brand.Dim), ac, sw), 0, 1);

        var up = Ui.Check("Update mods on startup", App.Cfg.UpdateOnStartup); up.CheckedChanged += (_, _) => { App.Cfg.UpdateOnStartup = up.Checked; App.Cfg.Save(); };
        var cl = Ui.Check("Close Mount Zantas after starting Celeste", App.Cfg.CloseOnLaunch); cl.CheckedChanged += (_, _) => { App.Cfg.CloseOnLaunch = cl.Checked; App.Cfg.Save(); };
        up.Margin = cl.Margin = new Padding(0, 4, 0, 4);
        Put(Ui.Section("Behaviour", up, cl), 1, 1);

        var mi = Ui.Combo("0x0a.de mirror (default)", "GameBanana (original links)"); mi.SelectedIndex = App.Cfg.Mirror == "gamebanana" ? 1 : 0;
        mi.SelectedIndexChanged += (_, _) => { App.Cfg.Mirror = mi.SelectedIndex == 1 ? "gamebanana" : "0x0a.de"; App.Cfg.Save(); };
        var ct = Ui.Btn("Run connectivity test", () => Run(log, Connectivity), 320); ct.Margin = new Padding(0, 4, 0, 0);
        Put(Ui.Section("Downloads", Ui.Lbl("Where mods are downloaded from. Every download is checked against a published checksum.", 9.5f, false, Brand.Dim), mi, ct), 0, 2);

        var d1 = Ui.Btn("Open Mount Zantas data folder", () => { Directory.CreateDirectory(Config.DataDir); Process.Start("explorer.exe", Config.DataDir); }, 320); d1.Margin = new Padding(0, 4, 0, 4);
        var d2 = Ui.Btn("Open selected Celeste folder", () => { if (Inst != null) Process.Start("explorer.exe", Inst.Path); }, 320); d2.Margin = new Padding(0, 0, 0, 4);
        var d3 = Ui.Btn("Clear downloaded preview images", () => log.Log($"Removed {Images.Clear()} cached image(s)."), 320); d3.Margin = new Padding(0, 0, 0, 4);
        Put(Ui.Section("Folders", d1, d2, d3), 1, 2);

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version; var about = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        about.Controls.Add(new PictureBox { Image = Brand.Logo(56), SizeMode = PictureBoxSizeMode.Zoom, Width = 56, Height = 56 });
        about.Controls.Add(Ui.Lbl($"{Brand.Name} {ver?.ToString(3)}\r\nA Celeste launcher and mod manager that uses the Everest mod loader (MIT).\r\nCeleste is made by Maddy Makes Games. This launcher is fan-made.", 9.5f, false, Brand.Dim));
        Put(Ui.Section("About", about), 0, 3, 2);
        Controls.Add(f); Controls.Add(log);
    }
    async Task Connectivity() {
        foreach (var u in new[] { "https://maddie480.ovh/celeste/everest-versions", "https://maddie480.ovh/celeste/everest_update.yaml", "https://celestemodupdater.0x0a.de/banana-mirror/1368600.zip", "https://api.github.com/repos/EverestAPI/Everest", "https://github.com" }) {
            try { using var r = await Net.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, u)); log.Log($"{(r.IsSuccessStatusCode ? "OK  " : "FAIL")} {(int)r.StatusCode}  {u}"); } catch (Exception ex) { log.Log($"FAIL  {u}  ({ex.Message})"); }
        }
    }
}
