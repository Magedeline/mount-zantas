using System.Diagnostics;

namespace MountZantas;

static class Program {
    // Unexpected UI errors are written to %LOCALAPPDATA%\MountZantas\error.log and shown once, instead of the raw .NET crash dialog.
    static bool shownError;
    static void OnError(Exception ex) {
        try { Directory.CreateDirectory(Config.DataDir); File.AppendAllText(Path.Combine(Config.DataDir, "error.log"), $"[{DateTime.Now:s}] {ex}\r\n\r\n"); } catch { }
        if (shownError) return; shownError = true;
        MessageBox.Show("Something went wrong in this screen, but Mount Zantas is still running.\r\n\r\n" + ex.Message + "\r\n\r\nDetails were saved to error.log in the Mount Zantas data folder (Options > Open data folder).", Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); shownError = false;
    }

    [STAThread]
    static int Main(string[] args) {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); Application.ThreadException += (_, e) => OnError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception x) try { File.AppendAllText(Path.Combine(Config.DataDir, "error.log"), $"[{DateTime.Now:s}] FATAL {x}\r\n\r\n"); } catch { } };
        if (args.Contains("--selftest")) return SelfTest().GetAwaiter().GetResult();
        int si = Array.IndexOf(args, "--shot"); if (si >= 0 && args.Length > si + 2) return Shot(args[si + 1], args[si + 2]);
        int sp = Array.IndexOf(args, "--splash-shot");
        if (sp >= 0 && args.Length > sp + 1) { ApplicationConfiguration.Initialize(); using var s = new SplashForm("Starting Mount Zantas..."); s.Show(); Application.DoEvents(); Thread.Sleep(400); Application.DoEvents(); using var bmp = new Bitmap(s.Width, s.Height); s.DrawToBitmap(bmp, new Rectangle(0, 0, s.Width, s.Height)); bmp.Save(args[sp + 1], System.Drawing.Imaging.ImageFormat.Png); return 0; }
        int di = Array.IndexOf(args, "--shot-dialog");
        if (di >= 0 && args.Length > di + 2) {   // MountZantas.exe --shot-dialog details|files out.png  (renders the GameBanana dialogs for Glyph)
            ApplicationConfiguration.Initialize(); var prof = GameBanana.Profile(150453).GetAwaiter().GetResult();
            Form dlg = args[di + 1] == "files" ? new FilesDialog(prof) : new ModDetailsDialog(prof); dlg.StartPosition = FormStartPosition.CenterScreen; dlg.Show(); for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(50); }
            using var bmp = new Bitmap(dlg.Width, dlg.Height); dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height)); bmp.Save(args[di + 2], System.Drawing.Imaging.ImageFormat.Png); return 0;
        }
        int ii = Array.IndexOf(args, "--make-icon"); if (ii >= 0 && args.Length > ii + 1) { MakeIcon(args[ii + 1]); return 0; }
        ApplicationConfiguration.Initialize();
        var splash = new SplashForm("Starting Mount Zantas..."); splash.Show(); Application.DoEvents();
        var started = Environment.TickCount64; var main = new MainForm();
        main.Shown += async (_, _) => { await Task.Delay((int)Math.Max(0, 1100 - (Environment.TickCount64 - started))); splash.Close(); main.Activate(); };
        Application.Run(main); return 0;
    }

    // Writes a multi-size .ico (PNG-compressed entries) from the logo, for the exe, taskbar and shortcuts.
    static void MakeIcon(string path) {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = sizes.Select(s => { using var b = Brand.Logo(s); using var ms = new MemoryStream(); b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }).ToList();
        using var fs = File.Create(path); using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++) { byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32); w.Write(pngs[i].Length); w.Write(offset); offset += pngs[i].Length; }
        foreach (var p in pngs) w.Write(p);
    }

    // Renders a page to a PNG (for visual checks): MountZantas.exe --shot main|installs|everest|mods|download|loenn|options out.png
    static int Shot(string page, string path) {
        ApplicationConfiguration.Initialize(); var f = new MainForm();
        f.Shown += (_, _) => {
            switch (page) {
                case "installs": f.Show(new InstallsPage(f), "Install Manager"); break; case "everest": f.Show(new EverestPage(f), "Everest Installer"); break;
                case "mods": f.Show(new ModsPage(f), "Mod Manager"); break; case "download": f.Show(new DownloadPage(f), "Download Mods"); break;
                case "gb": f.Show(new GameBananaPage(f), "GameBanana"); break; case "loenn": f.Show(new LoennPage(f), "Lönn"); break; case "topo": f.Show(new TopoPage(f), "Topo"); break; case "options": f.Show(new OptionsPage(f), "Options"); break;
            }
            var t = new System.Windows.Forms.Timer { Interval = 9000 };
            t.Tick += (_, _) => { t.Stop(); using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); f.Close(); };
            t.Start();
        };
        Application.Run(f); return 0;
    }

    // Non-UI checks of the core logic. Writes to %TEMP%\mountzantas-selftest.txt.
    static async Task<int> SelfTest() {
        var o = new List<string>(); int fail = 0; void Ok(bool c, string m) { o.Add((c ? "PASS " : "FAIL ") + m); if (!c) fail++; }
        try {
            var db = await ModDb.Get(); Ok(db.Count > 5000, $"mod database parsed ({db.Count} mods)"); Ok(db.TryGetValue("VortexHelper", out var v) && v.Hashes.Length > 0 && v.Mirror.Length > 0, "database entry has mirror + checksum");
            var ev = await EverestSvc.Versions(); Ok(ev.Count > 20 && ev.Any(b => b.Branch == "stable"), $"Everest versions ({ev.Count}, stable present)");
            Ok(ModDb.Newer("1.0.0", "1.0.1") && !ModDb.Newer("1.2.0", "1.1.9"), "version compare");
            var (n, ver, deps) = Mods.ParseYaml("- Name: Foo\n  Version: 1.2.3\n  Dependencies:\n    - Name: Everest\n      Version: 1.0\n    - Name: BarHelper\n      Version: 2\n  OptionalDependencies:\n    - Name: Opt\n");
            Ok(n == "Foo" && ver == "1.2.3" && deps.SequenceEqual(new[] { "Everest", "BarHelper" }), "everest.yaml parse (optional deps excluded)");
            var found = Detect.FindAll(); o.Add($"INFO detected installs: {string.Join("; ", found.Select(i => i.Name + " = " + i.Path))}");
            if (found.Count > 0) { var mods = Mods.Scan(found[0]); Ok(mods.Count >= 0, $"scanned {mods.Count} mods in first install ({mods.Count(m => m.Enabled)} enabled)"); }
            if (found.Count > 0) {
                var maps = MapFinder.Scan(found[0]); Ok(maps.Count > 10, $"found {maps.Count} maps in mods ({maps.Count(m => m.Loose)} loose, {maps.Count(m => !m.Loose)} in zips)");
                int okc = 0, bad = 0; var errs = new List<string>();
                foreach (var m in maps.Where(m => m.Loose && m.Display.Contains("CEL", StringComparison.OrdinalIgnoreCase) || m.Loose).Take(40)) {
                    try { var mi = MapFinder.Load(m); var (bmp, txt) = MapRender.Render(mi, Brand.Accent); if (bmp.Width > 0 && mi.Rooms.Count > 0) okc++; else bad++; bmp.Dispose(); } catch (Exception ex) { bad++; errs.Add(m.Display + ": " + ex.Message); }
                }
                Ok(okc > 0 && bad == 0, $"rendered {okc} loose maps ({bad} failed){(errs.Count > 0 ? " e.g. " + errs[0] : "")}");
                var zm = maps.FirstOrDefault(m => !m.Loose); if (zm != null) { try { var mi = MapFinder.Load(zm); var (bmp, txt) = MapRender.Render(mi, Brand.Accent); Ok(mi.Rooms.Count > 0, $"rendered a map from a zip: {zm.Mod} / {zm.Display} → {txt}"); bmp.Dispose(); } catch (Exception ex) { Ok(false, "zip map failed: " + ex.Message); } }
                if (zm != null) {
                    var dest = MapFinder.CopyPath(zm); bool existed = File.Exists(dest); var modDir = Path.GetDirectoryName(dest);
                    try {
                        if (!existed) MapFinder.ExtractTo(zm, dest); Ok(MapFinder.SameAsZip(zm, dest), "extracted single map is identical to the zip entry (" + new FileInfo(dest).Length + " bytes)");
                        using (var fsx = File.OpenRead(dest)) Ok(MapBin.Read(fsx).Rooms.Count > 0, "extracted copy is a readable map");
                        if (!existed) { File.AppendAllText(dest, "x"); Ok(!MapFinder.SameAsZip(zm, dest), "an edited copy is detected as different"); var u = MapFinder.UniquePath(dest); Ok(u.EndsWith(" (2).bin") && !File.Exists(u), "fresh-copy name does not overwrite (" + Path.GetFileName(u) + ")"); }
                        try { MapFinder.CopyPath(new MapSource("evil", "../../escape.bin", null, "x.zip", "x")); Ok(false, "unsafe zip path was accepted!"); } catch { Ok(true, "unsafe path inside a zip is refused"); }
                        if (Environment.GetCommandLineArgs().Any(a => a is "--with-topo" or "--with-editors") && File.Exists(Path.Combine(Topo.Dir, "Topo.exe"))) {
                            if (!existed) File.WriteAllBytes(dest, File.ReadAllBytes(zm.Zip is null ? dest : dest)); var before = new HashSet<int>(System.Diagnostics.Process.GetProcessesByName("Topo").Select(p => p.Id));
                            if (existed == false) MapFinder.ExtractTo(zm, dest); Topo.Launch(found[0], dest); await Task.Delay(7000);
                            var np = System.Diagnostics.Process.GetProcessesByName("Topo").Where(p => !before.Contains(p.Id)).ToList(); Ok(np.Count > 0, "Topo launched with --map pointing at the extracted copy");
                            foreach (var x in np) { try { x.CloseMainWindow(); await Task.Delay(1500); if (!x.HasExited) x.Kill(); } catch { } }
                        }
                        if (Environment.GetCommandLineArgs().Contains("--with-editors")) {
                            if (Loenn.Installed() == null) await Loenn.Install(s => o.Add("INFO " + s), null);
                            var lb = new HashSet<int>(System.Diagnostics.Process.GetProcessesByName("Lönn").Select(p => p.Id)); Loenn.Launch(dest); await Task.Delay(9000);
                            var lp = System.Diagnostics.Process.GetProcessesByName("Lönn").Where(p => !lb.Contains(p.Id)).ToList(); Ok(lp.Count > 0 && lp.All(p => !p.HasExited), "Lönn launched with the extracted map as its start-up file");
                            foreach (var x in lp) { try { x.CloseMainWindow(); await Task.Delay(1500); if (!x.HasExited) x.Kill(); } catch { } }
                        }
                    } finally { if (!existed) { try { File.Delete(dest); foreach (var f in Directory.GetFiles(modDir, "*.bin")) File.Delete(f); if (!Directory.EnumerateFileSystemEntries(modDir).Any()) Directory.Delete(modDir); } catch { } } }
                }
                var city = maps.FirstOrDefault(m => m.Loose && m.Display.EndsWith("01_City.bin")); if (city != null) { var mi = MapFinder.Load(city); o.Add($"INFO {city.Mod}/{city.Display}: {mi.Rooms.Count} rooms, {mi.Fillers.Count} fillers, first room {mi.Rooms.FirstOrDefault()?.Name}"); }
            }
            var l = await Loenn.Latest(); Ok(l.sha != null && l.url.EndsWith(".zip"), "Lönn latest release " + l.tag + " has checksum");
            var (gbItems, gbTotal) = await GameBanana.Search("glyph", 1); Ok(gbItems.Any(m => m.Name == "Glyph") && gbTotal > 0, $"GameBanana search ({gbItems.Count} results)");
            var (gbTop, _) = await GameBanana.Search("", 1); Ok(gbTop.Count > 10, $"GameBanana most-downloaded list ({gbTop.Count})");
            Ok(gbTop.Count(m => !string.IsNullOrEmpty(m.Thumb)) > 20 && gbTop.All(m => m.Thumb == null || Images.Allowed(m.Thumb)), $"GameBanana results carry thumbnails from an allowed host ({gbTop.Count(m => m.Thumb != null)}/{gbTop.Count})");
            int okThumbs = 0; var badThumbs = new List<string>(); foreach (var gm in gbTop.Where(m => m.Thumb != null)) { var tb = await Images.Get(gm.Thumb); if (tb != null) { okThumbs++; tb.Dispose(); } else badThumbs.Add(gm.Name); }
            Ok(badThumbs.Count == 0, $"all {okThumbs + badThumbs.Count} thumbnails decode, including WebP{(badThumbs.Count > 0 ? " (failed: " + string.Join(", ", badThumbs) + ")" : "")}");
            var thumb = await Images.Get(gbTop.First(m => m.Thumb != null).Thumb); Ok(thumb != null && thumb.Width > 50, $"thumbnail downloads and decodes ({thumb?.Width}x{thumb?.Height})"); thumb?.Dispose();
            Ok(!Images.Allowed("http://images.gamebanana.com/x.jpg") && !Images.Allowed("https://evil.example.com/a.png") && !Images.Allowed("https://raw.githubusercontent.com/someone/else/main/a.png") && Images.Allowed("https://images.gamebanana.com/img/ss/mods/a.jpg"), "image host allow-list (https + known hosts only)");
            var news = await News.Get(); Ok(news.Count >= 5 && news.All(n => n.Image.Length == 0 || Images.Allowed(n.Image)), $"news feed: {news.Count} items, images on allowed hosts");
            var nimg = await Images.Get(news[0].Image); { var nb = await ImageStore.GetBytes(news[0].Image); o.Add($"INFO news image bytes: {nb?.Length.ToString() ?? "null"} from {news[0].Image} ({ImageStore.LastError})"); } Ok(nimg != null && nimg.Width > 100, $"news image loads ({nimg?.Width}x{nimg?.Height})"); nimg?.Dispose();
            var prof = await GameBanana.Profile(150453); Ok(prof.Images.Count > 0 && prof.Images.All(Images.Allowed) && prof.Downloads > 1000, $"profile has {prof.Images.Count} preview image(s), {prof.Downloads:N0} downloads"); Ok(prof.Name == "Glyph" && prof.Text.Length > 100 && prof.Files.Count >= 3 && prof.Files.All(f => f.Md5.Length == 32), $"GameBanana profile: {prof.Files.Count} files, all with MD5");
            Ok(!prof.Text.Contains("<span") && !prof.Text.Contains("&amp;"), "description converted from HTML to plain text");
            var small = prof.Files.OrderBy(f => f.Size).First(); if (small.Size > 0 && small.Size < 120_000_000) {
                var tmpf = Path.Combine(Path.GetTempPath(), "mz-selftest-gb.zip"); await GameBanana.DownloadVerified(small, tmpf, null); Ok(File.Exists(tmpf) && GameBanana.Md5(tmpf) == small.Md5, $"verified download of {small.Name} ({small.Size / 1048576} MB)");
                try { var bad = small with { Md5 = new string('0', 32) }; File.Delete(tmpf); await GameBanana.DownloadVerified(bad, tmpf, null); Ok(false, "bad checksum was accepted!"); } catch { Ok(!File.Exists(tmpf), "wrong checksum is rejected and the file deleted"); }
            }
            var tp = await Topo.Latest(false); Ok(tp.sha != null && tp.url.EndsWith(".zip"), "Topo latest release " + tp.tag + " has checksum");
            var tpa = await Topo.Latest(true); Ok(tpa.sha != null && tpa.url.Contains("Topo-AI"), "Topo + AI edition found");
            if (Environment.GetCommandLineArgs().Contains("--with-topo")) {   // real download + install + launch of Topo
                await Topo.Install(false, s => o.Add("INFO " + s), null); Ok(File.Exists(Path.Combine(Topo.Dir, "Topo.exe")), "Topo installed (" + Topo.Installed() + ")");
                var inst = Detect.FindAll().FirstOrDefault(); Topo.Launch(inst); await Task.Delay(6000);
                var pr = System.Diagnostics.Process.GetProcessesByName("Topo"); Ok(pr.Length > 0, "Topo launched with CELESTE_DIR=" + inst?.Path); foreach (var x in pr) { try { x.CloseMainWindow(); await Task.Delay(1500); if (!x.HasExited) x.Kill(); } catch { } }
            }
            var logo = Brand.Logo(64); Ok(logo.Width == 64, "logo renders");
        } catch (Exception ex) { Ok(false, "exception: " + ex); }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "mountzantas-selftest.txt"), o); return fail;
    }
}

class MainForm : Form {
    public Config Cfg = Config.Load();
    readonly Panel host = new() { Dock = DockStyle.Fill };
    readonly Label crumb = Ui.Lbl(Brand.Name, 11, true); readonly Button back;

    public MainForm() {
        try { Brand.Accent = ColorTranslator.FromHtml(Cfg.Accent); } catch { }
        Text = Brand.Name; Icon = Brand.AppIcon(); Width = 1180; Height = 720; MinimumSize = new Size(1000, 640); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Brand.Bg; ForeColor = Brand.Text; Font = Ui.F();
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Brand.Panel };
        back = Ui.Btn("‹  Main Menu", () => Main(), 130, 32); back.Location = new Point(8, 6); crumb.Location = new Point(150, 12);
        top.Controls.Add(back); top.Controls.Add(crumb);
        Controls.Add(host); Controls.Add(top);
        if (Cfg.Installs.Count == 0) { Cfg.Installs.AddRange(Detect.FindAll()); Cfg.Save(); }
        Main();
        try { SelfUpdate.Cleanup(Environment.ProcessPath); } catch { }
        Shown += async (_, _) => {
            if (Cfg.AutoUpdateApp && Environment.GetEnvironmentVariable("MOUNTZANTAS_DATA") == null) await CheckSelfUpdate(false, s => { });
            if (Cfg.UpdateOnStartup && Cfg.Current != null) { try { await ModUpdate.All(Cfg.Current, Cfg, s => { }); } catch { } }
        };
    }
    // Looks at the GitHub releases for a newer Mount Zantas. Asks first, verifies the checksum, then restarts into the new version.
    public async Task CheckSelfUpdate(bool manual, Action<string> say) {
        try {
            var exe = Environment.ProcessPath; if (exe == null || !exe.EndsWith("MountZantas.exe", StringComparison.OrdinalIgnoreCase)) { if (manual) say("Updating only works from the installed MountZantas.exe."); return; }
            var cur = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
            var r = await SelfUpdate.Check(cur); if (r == null) { if (manual) say($"You have the latest version ({cur})."); return; }
            if (MessageBox.Show($"Mount Zantas {r.Version} is available (you have {cur}).\nUpdate now? It downloads from GitHub, is checked against its published checksum, and restarts.", Brand.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            say($"Downloading Mount Zantas {r.Version}...");
            var tmp = await SelfUpdate.Download(r, exe); SelfUpdate.Swap(exe, tmp);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); Close();
        } catch (Exception ex) { if (manual) say("Update failed: " + ex.Message); }
    }
    public void Main() => Show(new MainPage(this), "");
    public void Show(Control page, string title) {
        host.Controls.Clear(); page.Dock = DockStyle.Fill; host.Controls.Add(page);
        crumb.Text = title == "" ? Brand.Name : title; back.Visible = title != ""; crumb.Left = title == "" ? 14 : 150;
    }
    public void LaunchCeleste(string args = null) {
        var i = Cfg.Current; if (i == null) { MessageBox.Show("No Celeste install found. Open Manage and add one.", Brand.Name); return; }
        var exe = Path.Combine(i.Path, "Celeste.exe"); if (!File.Exists(exe)) { MessageBox.Show("Celeste.exe is missing from " + i.Path, Brand.Name); return; }
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = i.Path, UseShellExecute = true, Arguments = args ?? "" });
        SplashForm.ShowFor("Starting Celeste...", 2500);
        if (Cfg.CloseOnLaunch) Close();
    }
}
