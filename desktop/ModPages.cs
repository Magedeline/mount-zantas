using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace MountZantas;

// ---------- Installed mods ----------
class ModsPage : Page {
    class Row {
        public ModInfo M;
        public Action Refresh;
    }
    readonly Table<Row> table;
    readonly TextBox search = Ui.Text("Search by file name, mod title or everest.yaml ID");
    readonly CheckBox onlyOn = Ui.Check("Enabled only"), onlyFav = Ui.Check("Favorites only"), onlyIssues = Ui.Check("Needs attention"), onlyUpd = Ui.Check("Updates available");
    readonly LogBox log = new(90);
    readonly TextBlock count = Ui.Lbl("", 10, false, Brand.Dim);
    readonly Button updateAll, presetsBtn;
    List<ModInfo> all = new();
    List<Row> rows = new();
    readonly Dictionary<string, ModEntry> updates = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ModInfo, string> issues = new();

    public ModsPage(MainWindow app) : base(app) {
        table = new Table<Row>(
            "44,44,280,100,110,250,*",
            new[] { "Fav", "On", "Name", "Version", "Update", "Needs attention", "File" },
            Cells);
        table.List.SelectionMode = SelectionMode.Multiple;
        table.Activated += r => {
            r.M.Enabled = !r.M.Enabled;
            Save();
            Restyle();
            if (!r.M.Enabled) _ = OfferUnused(new[] { r.M });
        };
        table.List.ContextRequested += (_, e) => {
            RowMenu();
            e.Handled = true;
        };

        updateAll = Ui.Green(Ui.Btn("Update All", () => Run(log, async () => {
            int n = await ModUpdate.All(Inst, App.Cfg, log.Log, log.Prog);
            log.Log($"{n} mod(s) updated.");
            Reload();
            _ = CheckUpdates();
        })));
        presetsBtn = Ui.Btn("Mod presets", ShowPresets);
        var top = Ui.Wrap(Ui.Btn("Open mods folder", () => { if (Inst != null) OpenFolder(Mods.Dir(Inst)); }), Ui.Btn("Install mod file", () => _ = InstallFile()), presetsBtn,
            Ui.Btn("Delete selected", () => _ = Delete()), Ui.Btn("Enable selected", () => SetSelected(true)), Ui.Btn("Disable selected", () => SetSelected(false)), Ui.Btn("Fix dependencies", () => Run(log, FixDeps)), updateAll);
        search.Width = 400;
        search.TextChanged += (_, _) => Bind();
        foreach (var c in new[] { onlyOn, onlyFav, onlyIssues, onlyUpd }) c.IsCheckedChanged += (_, _) => Bind();
        count.Margin = new Thickness(12, 0);
        Children.Add(top.Dock(Dock.Top));
        Children.Add(Ui.Wrap(search, onlyOn, onlyFav, onlyIssues, onlyUpd, count).Dock(Dock.Top));
        Children.Add(log.Dock(Dock.Bottom));
        Children.Add(table);
        Reload();
        _ = CheckUpdates();
    }

    Control[] Cells(Row r) {
        var m = r.M;
        var fav = new CheckBox {
            IsChecked = App.Cfg.Favorites.Contains(m.Name),
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 0
        };
        var on = new CheckBox { IsChecked = m.Enabled, Margin = new Thickness(10, 0, 0, 0), MinWidth = 0 };
        var name = Ui.Cell(m.Name);
        var ver = Ui.Cell(m.Version);
        var upd = Ui.Cell("");
        var iss = Ui.Cell("");
        var file = Ui.Cell(m.File);
        fav.IsCheckedChanged += (_, _) => {
            if (fav.On()) App.Cfg.Favorites.Add(m.Name);
            else App.Cfg.Favorites.Remove(m.Name);
            App.Cfg.Save();
        };
        on.IsCheckedChanged += (_, _) => {
            if (on.On() == m.Enabled) return;
            m.Enabled = on.On();
            Save();
            Restyle();
            if (!m.Enabled) _ = OfferUnused(new[] { m });
        };
        r.Refresh = () => {
            on.IsChecked = m.Enabled;
            upd.Text = Upd(m) ? "→ " + updates[m.Name].Version : "";
            upd.Foreground = Brand.B(Brand.Warn);
            iss.Text = issues.TryGetValue(m, out var t) ? "⚠ " + t : "";
            var fg = Brand.B(!m.Enabled ? Brand.Dim : issues.ContainsKey(m) ? Brand.Warn : Brand.Text);
            name.Foreground = ver.Foreground = iss.Foreground = file.Foreground = fg;
        };
        r.Refresh();
        return new Control[] { fav, on, name, ver, upd, iss, file };
    }
    bool Upd(ModInfo m) => updates.TryGetValue(m.Name, out var e) && ModDb.Newer(m.Version, e.Version);

    void Reload() {
        if (Inst == null) {
            log.Log("Select a Celeste install first.");
            return;
        }
        all = Mods.Scan(Inst);
        Bind();
    }
    void ComputeIssues() {
        issues.Clear();
        var by = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in all) by[m.Name] = m;
        foreach (var m in all.Where(m => m.Enabled)) {
            var p = new List<string>();
            foreach (var d in m.Deps) {
                if (d is "Everest" or "EverestCore") continue;
                if (!by.TryGetValue(d, out var dm)) p.Add("missing " + d);
                else if (!dm.Enabled) p.Add(d + " is off");
            }
            if (p.Count > 0) issues[m] = string.Join(", ", p);
        }
    }
    void Bind() {
        ComputeIssues();
        var q = search.Text?.Trim() ?? "";
        rows = all.Where(m => (q.Length == 0 || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.File.Contains(q, StringComparison.OrdinalIgnoreCase))
                  && (!onlyOn.On() || m.Enabled) && (!onlyFav.On() || App.Cfg.Favorites.Contains(m.Name)) && (!onlyIssues.On() || issues.ContainsKey(m)) && (!onlyUpd.On() || Upd(m)))
            .Select(m => new Row { M = m }).ToList();
        table.Items = rows;
        RefreshCounts();
    }
    // Updates the visible rows in place (keeps the scroll position and selection).
    void Restyle() {
        ComputeIssues();
        foreach (var r in rows) r.Refresh?.Invoke();
        RefreshCounts();
    }
    void RefreshCounts() {
        int n = all.Count(Upd);
        count.Text = $"{all.Count(m => m.Enabled)} of {all.Count} enabled · {issues.Count} need attention";
        updateAll.Content = n > 0 ? $"Update All ({n})" : "Update All";
    }
    async Task CheckUpdates() {
        try {
            var db = await ModDb.Get();
            updates.Clear();
            foreach (var m in all)
                if (!m.IsDir && db.TryGetValue(m.Name, out var e) && ModDb.Newer(m.Version, e.Version)) updates[m.Name] = e;
            Bind();
        } catch { }
    }
    // After disabling a mod, offer to also disable helpers that nothing enabled needs any more.
    async Task OfferUnused(IEnumerable<ModInfo> justDisabled) {
        var unused = Mods.UnusedAfterDisable(all, justDisabled);
        if (unused.Count == 0) return;
        var lines = string.Join(
            "\n",
            unused.Select(m => "- " + m.Name + (m.Name != m.File ? " · " + Path.GetFileNameWithoutExtension(m.File) : "")));
        if (!await Dlg.YesNo(App, $"{unused.Count} other mod{(unused.Count == 1 ? " is" : "s are")} no longer required for any enabled mod.\nDo you want to disable {(unused.Count == 1 ? "it" : "them")} as well?\n\n{lines}")) return;
        foreach (var m in unused) m.Enabled = false;
        Save();
        Restyle();
        log.Log($"Also disabled {unused.Count} unused mod(s).");
    }
    void Save() {
        try { Mods.WriteDisabled(Inst, all.Where(m => !m.Enabled).Select(m => m.File)); } catch (Exception ex) { log.Log("Couldn't save blacklist: " + ex.Message); }
    }
    List<ModInfo> Picked() => table.SelectedAll.Select(r => r.M).ToList();
    void SetSelected(bool on) {
        var sel = Picked();
        if (sel.Count == 0) {
            log.Log("Select one or more mods first (Ctrl or Shift + click picks several).");
            return;
        }
        foreach (var m in sel) m.Enabled = on;
        Save();
        Restyle();
        log.Log($"{(on ? "Enabled" : "Disabled")} {sel.Count} mod(s). Restart Celeste for it to take effect.");
        if (!on) _ = OfferUnused(sel);
    }
    async Task FixDeps() {
        ComputeIssues();
        if (issues.Count == 0) {
            log.Log("No dependency problems found.");
            return;
        }
        var by = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in all) by[m.Name] = m;
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int enabled = 0;
        foreach (var m in all.Where(m => m.Enabled))
            foreach (var d in m.Deps) {
                if (d is "Everest" or "EverestCore") continue;
                if (!by.TryGetValue(d, out var dm)) missing.Add(d);
                else if (!dm.Enabled) {
                    dm.Enabled = true;
                    enabled++;
                }
            }
        if (enabled > 0) {
            Save();
            log.Log($"Enabled {enabled} required mod(s).");
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in missing) await Mods.InstallWithDeps(Inst, n, App.Cfg, log.Log, log.Prog, seen);
        Reload();
        _ = CheckUpdates();
        log.Log("Dependencies fixed.");
    }
    async Task InstallFile() {
        if (Inst == null) return;
        var files = await App.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Install mod zip",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Mod zip") { Patterns = new[] { "*.zip" } } }
        });
        if (files.Count == 0) return;
        Directory.CreateDirectory(Mods.Dir(Inst));
        foreach (var f in files) {
            var p = f.TryGetLocalPath();
            if (p == null) continue;
            File.Copy(p, Path.Combine(Mods.Dir(Inst), Path.GetFileName(p)), true);
            log.Log("Installed " + Path.GetFileName(p));
        }
        Reload();
    }
    async Task Delete() {
        var sel = Picked();
        if (sel.Count == 0 || Inst == null) return;
        if (!await Dlg.YesNo(App, $"Move {sel.Count} mod(s) to the {(Platform.Windows ? "Recycle Bin" : "Trash")}?")) return;
        foreach (var m in sel) {
            try {
                Platform.Trash(Path.Combine(Mods.Dir(Inst), m.File));
                log.Log("Deleted " + m.Name);
            } catch (Exception ex) { log.Log("Couldn't delete " + m.Name + ": " + ex.Message); }
        }
        Reload();
    }
    static MenuItem Item(string header, Action click, bool enabled = true) {
        var i = new MenuItem { Header = header, IsEnabled = enabled };
        if (click != null) i.Click += (_, _) => click();
        return i;
    }
    void RowMenu() {
        var r = table.Selected;
        if (r == null) return;
        var m = r.M;
        var menu = new ContextMenu();
        menu.Items.Add(Item(m.Enabled ? "Disable" : "Enable", () => SetSelected(!m.Enabled)));
        menu.Items.Add(Item(Platform.Windows ? "Show in Explorer" : "Show in file manager", () => Platform.Reveal(Path.Combine(Mods.Dir(Inst), m.File))));
        menu.Items.Add(Item("Copy name", () => _ = App.Clipboard?.SetTextAsync(m.Name)));
        var deps = m.Deps.Where(d => d != "Everest").ToList();
        menu.Items.Add(Item("Required mods: " + (deps.Any() ? string.Join(", ", deps) : "none"), null, false));
        if (updates.TryGetValue(m.Name, out var e) && ModDb.Newer(m.Version, e.Version) && !m.IsDir) menu.Items.Add(Item($"Update to {e.Version}", () => Run(log, async () => {
            await ModUpdate.Zip(Inst, m, e, App.Cfg, log.Log, log.Prog);
            Reload();
            _ = CheckUpdates();
        })));
        menu.Open(table.List);
    }
    void ShowPresets() {
        if (Inst == null) return;
        var f = new MenuFlyout();
        f.Items.Add(Item("Save current mods as a preset...", async () => {
            var n = await Dlg.Prompt(App, "Save preset", "Preset name:");
            if (n != null) {
                App.Cfg.Presets[n] = all.Where(m => !m.Enabled).Select(m => m.File).ToList();
                App.Cfg.Save();
                log.Log($"Saved preset \"{n}\".");
            }
        }));
        if (App.Cfg.Presets.Count > 0) f.Items.Add(new Separator());
        foreach (var kv in App.Cfg.Presets.ToList()) {
            var name = kv.Key;
            f.Items.Add(Item($"Apply: {name} ({kv.Value.Count} off)", () => {
                var off = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var m in all) m.Enabled = !off.Contains(m.File);
                Save();
                Bind();
                log.Log($"Applied \"{name}\". Restart Celeste for it to take effect.");
            }));
            f.Items.Add(Item($"Delete: {name}", () => {
                App.Cfg.Presets.Remove(name);
                App.Cfg.Save();
            }));
        }
        f.ShowAt(presetsBtn);
    }
}

// ---------- Download mods ----------
class DownloadPage : Page {
    readonly TextBox search = Ui.Text("Search the public Everest mod database");
    readonly LogBox log = new(100);
    readonly ComboBox sort = Ui.Combo("Recently updated", "Name (A–Z)", "Largest first");
    readonly TextBlock shown = Ui.Lbl("", 10, false, Brand.Dim);
    Dictionary<string, ModEntry> db;
    Dictionary<string, string> have = new(StringComparer.OrdinalIgnoreCase);
    readonly Table<ModEntry> table;
    readonly Dictionary<string, Button> extraBtns = new();

    public DownloadPage(MainWindow app) : base(app) {
        table = new Table<ModEntry>("340,110,110,90,*", new[] { "Name", "Version", "Updated", "Size", "Status" }, e => {
            string st = have.TryGetValue(e.Name, out var v) ? (ModDb.Newer(v, e.Version) ? $"Update available ({v} → {e.Version})" : "Installed " + v) : "";
            var fg = st.StartsWith("Update") ? Brand.B(Brand.Warn) : st.StartsWith("Installed") ? Brand.B(Brand.Good) : null;
            return new Control[] { Ui.Cell(e.Name, fg), Ui.Cell(e.Version, fg), Ui.Cell(DateTimeOffset.FromUnixTimeSeconds(e.Updated).LocalDateTime.ToString("yyyy-MM-dd"), fg), Ui.Cell(e.Size > 0 ? $"{e.Size / 1048576.0:0.0} MB" : "", fg), Ui.Cell(st, fg) };
        });
        table.List.SelectionMode = SelectionMode.Multiple;
        table.Activated += _ => Install();
        search.Width = 440;
        search.TextChanged += (_, _) => Fill();
        sort.Width = 210;
        sort.SelectionChanged += (_, _) => Fill();
        shown.Margin = new Thickness(10, 0);
        var top = Ui.Wrap(
            search,
            sort,
            Ui.Green(Ui.Btn("Install selected", Install)),
            Ui.Btn("Browse GameBanana...", () => App.Show(new GameBananaPage(App), "GameBanana")),
            shown);
        // Optional extras: helper mods published on GitHub (not in the Everest database). Nothing is installed unless clicked.
        var extras = Ui.Wrap(Ui.Lbl("Optional extras:", 10, true));
        foreach (var m in Extras.All) {
            var b = Ui.Btn("Get " + m.Name, () => InstallExtra(m));
            ToolTip.SetTip(b, m.Blurb + $" (github.com/{m.Repo})");
            extras.Children.Add(b);
            extraBtns[m.Name] = b;
        }
        Children.Add(top.Dock(Dock.Top));
        Children.Add(extras.Dock(Dock.Top));
        Children.Add(log.Dock(Dock.Bottom));
        Children.Add(table);
        log.Log("Loading the mod database...");
        _ = LoadList();
        MarkExtras();
    }
    void MarkExtras() {
        if (Inst == null) return;
        var h = Mods.Scan(Inst).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(
            g => g.Key,
            g => g.First().Version,
            StringComparer.OrdinalIgnoreCase);
        foreach (var m in Extras.All)
            if (extraBtns.TryGetValue(m.Name, out var b)) b.Content = h.TryGetValue(m.Name, out var v) ? $"{m.Name} ✓ {v}" : "Get " + m.Name;
    }
    void InstallExtra(ExtraMod m) {
        if (Inst == null) {
            log.Log("Select a Celeste install first.");
            return;
        }
        var inst = Inst;
        Run(log, async () => {
            await Extras.Install(inst, m, App.Cfg, log.Log, log.Prog);
            log.Log("Done.");
            Fill();
            MarkExtras();
        });
    }
    async Task LoadList() {
        try {
            db = await ModDb.Get();
            log.Log($"{db.Count} mods available. Double-click a mod to install it with the helper mods it needs.");
            Fill();
        } catch (Exception ex) { log.Log("Couldn't load the database: " + ex.Message); }
    }
    void Fill() {
        if (db == null) return;
        if (Inst != null) have = Mods.Scan(Inst).GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(
            g => g.Key,
            g => g.First().Version,
            StringComparer.OrdinalIgnoreCase);
        var q = search.Text?.Trim() ?? "";
        var src = db.Values.Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var ordered = sort.SelectedIndex == 1 ? src.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase) : sort.SelectedIndex == 2 ? src.OrderByDescending(e => e.Size) : src.OrderByDescending(e => e.Updated);
        table.Items = ordered.Take(400);
        shown.Text = $"Showing {Math.Min(400, src.Count)} of {src.Count}";
    }
    void Install() {
        if (Inst == null) {
            log.Log("Select a Celeste install first.");
            return;
        }
        var picks = table.SelectedAll.Select(e => e.Name).ToList();
        if (picks.Count == 0) {
            log.Log("Pick a mod first.");
            return;
        }
        var inst = Inst;
        Run(log, async () => {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in picks) await Mods.InstallWithDeps(inst, n, App.Cfg, log.Log, log.Prog, seen);
            log.Log("Done.");
            Fill();
        });
    }
}
