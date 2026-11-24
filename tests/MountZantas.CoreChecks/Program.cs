using System.IO.Compression;
using System.Text;
using MountZantas;

// Portable checks of the Mount Zantas core. Run on Windows, Linux and macOS (see .github/workflows/build.yml).
// Exit code 0 = every check passed. Network checks talk to the same public services the app uses.
static class Checks {
    static int pass, fail;
    static readonly List<string> failures = new();
    static void Ok(bool c, string m) {
        Console.WriteLine((c ? "PASS " : "FAIL ") + m);
        if (c) pass++;
        else {
            fail++;
            failures.Add(m);
        }
    }
    static async Task Net(string name, Func<Task> body) {   // public services can hiccup: one retry before calling it a failure
        for (int i = 1; ; i++) {
            try {
                await body();
                return;
            } catch (Exception ex) when (i < 2) {
                Console.WriteLine($"  (retrying {name}: {ex.Message})");
                await Task.Delay(1500);
            } catch (Exception ex) {
                Ok(false, $"{name}: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }
    }
    static string Temp(string n) {
        var d = Path.Combine(Path.GetTempPath(), "mzchecks-" + Guid.NewGuid().ToString("N").Substring(0, 8), n);
        Directory.CreateDirectory(d);
        return d;
    }
    static void Touch(string path, string text = "x") {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }
    static string FakeCeleste(string dir) {
        Touch(Path.Combine(dir, "Celeste.exe"));
        Directory.CreateDirectory(Path.Combine(dir, "Content"));
        return dir;
    }

    static async Task<int> Main() {
        try { return await Run(); } catch (Exception ex) {
            Console.WriteLine("FAIL unexpected exception: " + ex);
            return 2;
        }
    }

    static async Task<int> Run() {
        Console.WriteLine($"Mount Zantas core checks on {Platform.Name} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
        var data = Temp("data");
        Environment.SetEnvironmentVariable("MOUNTZANTAS_DATA", data);

        // ----- platform -----
        Ok(
            Platform.Windows ^ Platform.Mac ^ Platform.Linux
            && new[] { Platform.Windows, Platform.Mac, Platform.Linux }.Count(x => x) == 1,
            "exactly one OS flag is set (" + Platform.Name + ")");
        Ok(
            Platform.MiniInstallerNames(false, false).SequenceEqual(new[] { "MiniInstaller-linux" })
            && Platform.MiniInstallerNames(false, true).SequenceEqual(new[] { "MiniInstaller-osx" })
            && Platform.MiniInstallerNames(true, false)[0] == "MiniInstaller-win64.exe",
            "Everest patcher names for Linux, macOS and Windows");
        Ok(
            Loenn.IsAssetFor("Loenn-v1.0.13-windows.zip", true, false)
            && Loenn.IsAssetFor("Loenn-v1.0.13-macos.app.zip", false, true)
            && Loenn.IsAssetFor("Loenn-v1.0.13-linux.AppImage", false, false)
            && !Loenn.IsAssetFor("Loenn-v1.0.13-linux.zip", false, false),
            "Lönn asset choice per OS");
        Ok(Config.DataDir == data, "MOUNTZANTAS_DATA relocates the data folder");
        {
            var c = new Config { Mirror = "gamebanana", Accent = "#123456" };
            c.Installs.Add(new Install { Name = "x", Path = "/tmp/x" });
            c.Save();
            var l = Config.Load();
            Ok(
                l.Mirror == "gamebanana" && l.Accent == "#123456" && l.Installs.Count == 1,
                "settings round-trip through config.json");
        }

        // ----- version compare and yaml -----
        Ok(
            ModDb.Newer("1.0.0", "1.0.1")
            && !ModDb.Newer("1.2.0", "1.1.9")
            && ModDb.Newer("1.9", "1.10")
            && ModDb.Newer("0.8.0", "0.8.0b")
            && ModDb.Newer("0.8.0a", "0.8.0b")
            && !ModDb.Newer("0.8.0b", "0.8.0")
            && !ModDb.Newer("0.8.0b", "0.8.0b")
            && ModDb.Newer("0.8.0b", "0.8.1")
            && !ModDb.Newer("1.0.0-beta", "1.0.0"),
            "version compare");
        {
            var (n, v, d) = Mods.ParseYaml("- Name: Foo\n  Version: 1.2.3\n  Dependencies:\n    - Name: Everest\n      Version: 1.0\n    - Name: BarHelper\n      Version: 2\n  OptionalDependencies:\n    - Name: Opt\n");
            Ok(
                n == "Foo" && v == "1.2.3" && d.SequenceEqual(new[] { "Everest", "BarHelper" }),
                "everest.yaml parse (optional deps excluded)");
        }

        // ----- mods folder: scan, enable/disable -----
        {
            var game = FakeCeleste(Temp("game"));
            var inst = new Install { Name = "t", Path = game };
            var mods = Path.Combine(game, "Mods");
            Directory.CreateDirectory(mods);
            void MakeMod(string file, string yaml) {
                using var z = ZipFile.Open(Path.Combine(mods, file), ZipArchiveMode.Create);
                using var w = new StreamWriter(z.CreateEntry("everest.yaml").Open());
                w.Write(yaml);
            }
            MakeMod(
                "Alpha.zip",
                "- Name: Alpha\n  Version: 1.0.0\n  Dependencies:\n    - Name: Beta\n      Version: 1.0.0\n");
            MakeMod("Beta.zip", "- Name: Beta\n  Version: 2.0.0\n");
            Touch(Path.Combine(mods, "LooseMod", "everest.yaml"), "- Name: Loose\n  Version: 0.1.0\n");
            var scan = Mods.Scan(inst);
            Ok(
                scan.Count == 3
                && scan.Single(m => m.Name == "Alpha").Deps.SequenceEqual(new[] { "Beta" })
                && scan.Single(m => m.Name == "Loose").IsDir,
                "mods scan finds zip and folder mods with dependencies");
            Mods.WriteDisabled(inst, new[] { "Beta.zip" });
            var again = Mods.Scan(inst);
            Ok(
                !again.Single(m => m.Name == "Beta").Enabled
                && again.Single(m => m.Name == "Alpha").Enabled,
                "blacklist.txt disables a mod and round-trips");
        }

        // ----- unused helpers after disabling a mod (the Olympus-style prompt) -----
        {
            ModInfo M(string n, params string[] deps) => new ModInfo {
                File = n + ".zip",
                Name = n,
                Enabled = true,
                Deps = deps
            };
            var game = M("Game", "Everest", "HelperA", "HelperB");
            var other = M("Other", "HelperB");
            var a = M("HelperA", "Lib");
            var b = M("HelperB");
            var lib = M("Lib");
            var all = new List<ModInfo> { game, other, a, b, lib };
            game.Enabled = false;
            var un = Mods.UnusedAfterDisable(all, new[] { game }).Select(m => m.Name).ToArray();
            Ok(
                un.SequenceEqual(new[] { "HelperA", "Lib" }),
                "disabling a mod offers its helpers that nothing else needs (and their helpers), but keeps shared ones: " + string.Join(",", un));
            Ok(
                Mods.UnusedAfterDisable(all, new[] { other }).Count == 0,
                "no offer when the helpers are still required by an enabled mod");
        }

        // ----- detection on fake home folders (Linux and macOS layouts) -----
        {
            var home = Temp("home");
            var lib2 = Temp("steamlib2");
            FakeCeleste(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "Celeste"));
            FakeCeleste(Path.Combine(lib2, "steamapps", "common", "Celeste"));
            FakeCeleste(Path.Combine(home, ".config", "itch", "apps", "celeste"));
            Touch(
                Path.Combine(home, ".local", "share", "Steam", "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib2.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
            var c = new List<(string, string)>();
            Detect.DetectUnix(c, home, false);
            var found = Detect.Normalize(c);
            Ok(
                found.Count == 3
                && found.Any(f => f.Store == "itch")
                && found.Count(f => f.Store == "Steam") == 2,
                "Linux: finds Steam (default + extra library from libraryfolders.vdf) and itch");
            var mhome = Temp("machome");
            var res = Path.Combine(
                mhome,
                "Library",
                "Application Support",
                "Steam",
                "steamapps",
                "common",
                "Celeste",
                "Celeste.app",
                "Contents",
                "Resources");
            FakeCeleste(res);
            var mc = new List<(string, string)>();
            Detect.DetectUnix(mc, mhome, true);
            var mf = Detect.Normalize(mc);
            Ok(
                mf.Count == 1 && mf[0].Path == Path.GetFullPath(res),
                "macOS: finds Steam's Celeste.app and patches Contents/Resources");
            Ok(
                Detect.Normalize(new[] { ("Steam", Path.Combine(home, "nope")) }).Count == 0,
                "a missing folder is not reported as an install");
            var stripped = Temp("stripped");
            Touch(Path.Combine(stripped, "Celeste.exe"));
            Ok(
                Detect.Normalize(new[] { ("Other", stripped) }).Count == 0,
                "a folder without Content (stripped libs) is skipped");
        }

        // ----- launching: finds the right launcher for this OS -----
        {
            var inst = new Install { Path = Temp("launch") };
            if (Platform.Windows) Touch(Path.Combine(inst.Path, "Celeste.exe"));
            else if (Platform.Mac) {
                inst.Path = Path.Combine(Temp("app"), "Celeste.app", "Contents", "Resources");
                Directory.CreateDirectory(inst.Path);
                Touch(Path.Combine(inst.Path, "..", "MacOS", "Celeste"));
            } else Touch(Path.Combine(inst.Path, "Celeste"));
            var exe = GameLauncher.FindExecutable(inst);
            Ok(
                exe != null && File.Exists(exe),
                "launcher for " + Platform.Name + " is found: " + Path.GetFileName(exe ?? "(none)"));
            Ok(
                GameLauncher.FindExecutable(new Install { Path = Temp("empty") }) == null,
                "no launcher reported for an empty folder");
            if (!OperatingSystem.IsWindows()) {
                var f = Path.Combine(Temp("exec"), "run.sh");
                Touch(f, "#!/bin/sh\necho hi\n");
                Platform.MakeExecutable(f);
                Ok(
                    (File.GetUnixFileMode(f) & UnixFileMode.UserExecute) != 0,
                    "executable permission is set (zip extraction drops it)");
            }
        }

        // ----- Everest uninstall restores the originals (works on any OS) -----
        {
            var game = FakeCeleste(Temp("everest"));
            Touch(Path.Combine(game, "Celeste.exe"), "PATCHED");
            Touch(Path.Combine(game, "orig", "Celeste.exe"), "ORIGINAL");
            Touch(Path.Combine(game, "Celeste.Mod.mm.dll"));
            Touch(Path.Combine(game, "update-build.txt"), "6696");
            Touch(Path.Combine(game, "Mods", "keep.zip"));
            var inst = new Install { Path = game };
            Ok(EverestSvc.EverestVersion(inst) == "6696", "Everest build number comes from update-build.txt");
            EverestSvc.Uninstall(inst, _ => { });
            Ok(
                File.ReadAllText(Path.Combine(game, "Celeste.exe")) == "ORIGINAL"
                && !File.Exists(Path.Combine(game, "Celeste.Mod.mm.dll"))
                && !File.Exists(Path.Combine(game, "update-build.txt"))
                && File.Exists(Path.Combine(game, "Mods", "keep.zip")),
                "uninstall restores original files, removes Everest, keeps Mods");
        }

        // ----- Zantas Loader: install, load report, preset launch lists -----
        {
            var game = FakeCeleste(Temp("zantas"));
            var inst = new Install { Path = game };
            var mods = Path.Combine(game, "Mods");
            Touch(Path.Combine(mods, "blacklist.txt"), "ZantasLoader.zip\nOffMod.zip\n");
            Touch(Path.Combine(mods, "OnMod", "everest.yaml"), "- Name: OnMod\n  Version: 1.0.0\n");
            Touch(Path.Combine(mods, "OffMod", "everest.yaml"), "- Name: OffMod\n  Version: 1.0.0\n");

            Ok(ZantasLoader.IsBundled, "Zantas Loader zip is embedded in the core");
            Ok(ZantasLoader.BundledVersion() != null, "bundled Zantas Loader has a version in its everest.yaml");
            Ok(ZantasLoader.InstalledVersion(inst) == null && ZantasLoader.NeedsInstall(inst), "fresh install needs Zantas Loader");
            ZantasLoader.Install(inst, _ => { });
            Ok(ZantasLoader.InstalledVersion(inst) == ZantasLoader.BundledVersion(), "Zantas Loader installed with the bundled version");
            Ok(!ZantasLoader.NeedsInstall(inst), "no reinstall needed right after installing");
            Ok(!Mods.Disabled(inst).Contains("ZantasLoader.zip") && Mods.Disabled(inst).Contains("OffMod.zip"), "install takes Zantas Loader off the blacklist and keeps the rest");

            Ok(ZantasLoader.ReadSession(inst) == null, "no load report before the game has run");
            Touch(Path.Combine(game, "zantas-session.json"),
                "{\"Loader\":\"0.1.0\",\"Everest\":\"1.5000.0\",\"Profile\":\"Speedrun\",\"Started\":\"2026-11-23T20:00:00\","
                + "\"Mods\":[{\"Name\":\"OnMod\",\"Version\":\"1.0.0\",\"File\":\"OnMod\",\"Code\":false}],\"Problems\":[\"X: not loaded\"]}");
            var s = ZantasLoader.ReadSession(inst);
            Ok(s != null && s.Profile == "Speedrun" && s.Mods.Count == 1 && s.Problems.Count == 1, "load report from the game is read");
            Ok(ZantasLoader.Summary(s).Contains("1 mods loaded") && ZantasLoader.Summary(s).Contains("1 problem"), "load report summary");

            var args = ZantasLoader.PrepareProfile(inst, "Speedrun", new[] { "OnMod", "ZantasLoader.zip" });
            var off = File.ReadAllLines(Path.Combine(mods, ZantasLoader.ProfileOffFile));
            var on = File.ReadAllLines(Path.Combine(mods, ZantasLoader.ProfileOnFile));
            Ok(args == "--blacklist zantas-profile-off.txt --whitelist zantas-profile-on.txt", "preset launch arguments");
            Ok(off[0] == "# Zantas profile: Speedrun" && off.Contains("OnMod") && !off.Contains("ZantasLoader.zip"), "preset off-list names the preset and never turns the loader off");
            Ok(on.Contains("OffMod") && on.Contains("ZantasLoader.zip") && !on.Contains("OnMod"), "preset on-list re-enables blacklisted mods the preset wants");
            Ok(File.ReadAllText(Path.Combine(mods, "blacklist.txt")).Contains("OffMod.zip"), "preset launch leaves blacklist.txt alone");

            ZantasLoader.Remove(inst, _ => { });
            Ok(ZantasLoader.InstalledVersion(inst) == null, "Zantas Loader can be removed");
        }

        // ----- loading screen: follows Everest's log.txt while Celeste starts -----
        {
            var game = FakeCeleste(Temp("loadscreen"));
            var inst = new Install { Path = game };
            var mods = Path.Combine(game, "Mods");
            Touch(Path.Combine(mods, "blacklist.txt"), "Skipped\n");
            Touch(Path.Combine(mods, "Alpha", "everest.yaml"), "- Name: AlphaHelper\n  Version: 1.0.0\n");
            Touch(Path.Combine(mods, "Beta", "everest.yaml"), "- Name: BetaHelper\n  Version: 1.0.0\n");
            Touch(Path.Combine(mods, "Skipped", "everest.yaml"), "- Name: Skipped\n  Version: 1.0.0\n");
            var log = Path.Combine(game, "log.txt");
            Touch(log, "(11/24/2026 09:00:00) [Everest] [Info] [core] Booting Everest\nDONE LOADING (in 1000ms)\n");

            Ok(GameStart.WithoutEverestSplash(null) == "--disable-splash", "loading screen switches Everest's splash off");
            Ok(GameStart.WithoutEverestSplash("--debug") == "--debug --disable-splash", "loading screen keeps other arguments");

            var oldLog = GameStart.SnapshotLog(inst);
            var start = new GameStart(inst, null, oldLog, "");
            var st = start.Poll();
            Ok(!st.Done && st.Detail == "Booting Everest", "loading screen ignores the previous game's log");

            Touch(log, "(11/24/2026 10:00:00) [Everest] [Info] [core] Booting Everest\n"
                + "(11/24/2026 10:00:01) [Everest] [Info] [core] Registered code module Celeste.Mod.Beta.BetaModule for module BetaHelper 1.0.0.\n"
                + "(11/24/2026 10:00:02) [Everest] [Info] [core] Registered code mo");
            st = start.Poll();
            Ok(st.Detail == "Loading BetaHelper [2/2]" && st.Progress > 0.5 && st.Progress < 0.6, "loading screen shows the mod and its place in the load order");

            File.AppendAllText(log, "dule X for module AlphaHelper 1.0.0.\n"
                + "(11/24/2026 10:00:03) [Everest] [Info] [loader] Loaded 3 modules\n"
                + " - GFX LOAD: 100ms\n - MTN LOAD: 10ms\n");
            st = start.Poll();
            Ok(st.Detail == "Loading audio" && st.Progress > 0.6 && st.Progress < 0.7, "loading screen follows game content steps after the mods");

            File.AppendAllText(log, "DONE LOADING (in 2000ms)\n");
            st = start.Poll();
            Ok(st.Progress == 1 && st.Detail == "Almost done...", "loading screen is full at DONE LOADING");
        }

        // ----- map reader: build a tiny .bin, read, render, and find it inside a zip -----
        {
            var bin = TinyMap();
            var info = MapBin.Read(new MemoryStream(bin));
            Ok(
                info.Rooms.Count == 1 && info.Rooms[0].Name == "a-00" && info.Rooms[0].W == 24,
                "map reader parses a .bin (room name and size)");
            var img = MapBin.Render(info, 0x6a, 0x4c, 0xff);
            Ok(img.Width == 3 && img.Height == 3 && img.Bgra.Length == 36, "map renders at one pixel per tile (3 x 3)");
            Ok(
                img.Bgra[0] > 150 && img.Bgra[3] == 255 && img.Bgra[(1 * 3 + 1) * 4 + 2] == 0x2c,
                "solid tiles and empty room tiles get different colours");
            var game = FakeCeleste(Temp("mapgame"));
            var mods = Path.Combine(game, "Mods");
            Directory.CreateDirectory(mods);
            using (var z = ZipFile.Open(Path.Combine(mods, "MapMod.zip"), ZipArchiveMode.Create)) {
                using (var s = z.CreateEntry("Maps/Mod/Area/demo.bin").Open()) s.Write(bin);
                using (var w = new StreamWriter(z.CreateEntry("everest.yaml").Open())) w.Write("- Name: MapMod\n  Version: 1.0.0\n");
            }
            var inst = new Install { Path = game };
            var maps = MapFinder.Scan(inst);
            Ok(
                maps.Count == 1 && !maps[0].Loose && MapFinder.Load(maps[0]).Rooms.Count == 1,
                "finds and reads a map inside a mod zip");
            var dest = MapFinder.CopyPath(maps[0]);
            MapFinder.ExtractTo(maps[0], dest);
            Ok(
                MapFinder.SameAsZip(maps[0], dest) && dest.StartsWith(data),
                "extracting one map writes an identical copy under the data folder");
            File.AppendAllText(dest, "x");
            Ok(
                !MapFinder.SameAsZip(maps[0], dest)
                && MapFinder.UniquePath(dest).EndsWith(" (2).bin"),
                "an edited copy is detected and a fresh one gets a new name");
            bool refused = false;
            try { MapFinder.CopyPath(new MapSource("evil", "../../escape.bin", null, "x.zip", "x")); } catch { refused = true; }
            Ok(refused, "unsafe paths inside a zip are refused");
        }

        // ----- network: the same services the app uses -----
        await Net("mod database", async () => {
            var db = await ModDb.Get();
            Ok(
                db.Count > 5000
                && db.TryGetValue("VortexHelper", out var v)
                && v.Hashes.Length > 0
                && v.Mirror.Length > 0,
                $"mod database parsed ({db.Count} mods, with checksums)");
        });
        await Net("everest builds", async () => {
            var ev = await EverestSvc.Versions();
            Ok(ev.Count > 20 && ev.Any(b => b.Branch == "stable"), $"Everest builds list ({ev.Count}, stable present)");
        });
        await Net("everest download", async () => {
            // The real Everest release: verify its checksum, and that it ships the patcher for EVERY OS (so the names above are right).
            var stable = (await EverestSvc.Versions()).Where(b => b.Branch == "stable" && b.Download.Contains("github.com/EverestAPI/Everest/releases/download/")).OrderByDescending(b => b.Version).First();
            var tag = System.Text.RegularExpressions.Regex.Match(stable.Download, "download/([^/]+)/main.zip").Groups[1].Value;
            var want = await Net_.GitHubDigest(
                "https://api.github.com/repos/EverestAPI/Everest/releases/tags/" + tag,
                "main.zip");
            var zip = Path.Combine(Temp("dl"), "main.zip");
            await Net_.Download(stable.Download, zip);
            Ok(
                want != null
                && Net_.Sha256(zip).Equals(want, StringComparison.OrdinalIgnoreCase),
                $"Everest {stable.Version} download matches GitHub's published SHA-256");
            using var zf = ZipFile.OpenRead(zip);
            var names = zf.Entries.Select(e => Path.GetFileName(e.FullName)).ToHashSet();
            Ok(
                new[] { "MiniInstaller-win64.exe", "MiniInstaller-linux", "MiniInstaller-osx" }.All(names.Contains),
                "Everest ships the patcher for Windows, Linux and macOS");
            var ex = Temp("extract");
            var entry = zf.Entries.First(e => Path.GetFileName(e.FullName) == Platform.MiniInstallerNames(Platform.Windows, Platform.Mac)[0]);
            var target = Path.Combine(ex, Path.GetFileName(entry.FullName));
            entry.ExtractToFile(target);
            Ok(
                File.Exists(Platform.MiniInstallerPath(ex)),
                "the patcher for this OS is found by name after extraction");
            if (!OperatingSystem.IsWindows()) {
                Platform.MakeExecutable(target);
                Ok(
                    (File.GetUnixFileMode(target) & UnixFileMode.UserExecute) != 0,
                    "the extracted patcher can be made executable");
            }
        });
        await Net("gamebanana", async () => {
            var (items, total) = await GameBanana.Search("glyph", 1);
            Ok(items.Any(m => m.Name == "Glyph") && total > 0, $"GameBanana search ({items.Count} results)");
            var (top, _) = await GameBanana.Search("", 1);
            Ok(
                top.Count > 10 && top.Count(m => m.Thumb != null) > 20,
                $"GameBanana most-downloaded list with thumbnails ({top.Count})");
            var prof = await GameBanana.Profile(150453);
            Ok(
                prof.Name == "Glyph"
                && prof.Text.Length > 100
                && prof.Files.Count >= 3
                && prof.Files.All(f => f.Md5.Length == 32)
                && prof.Images.Count > 0,
                $"GameBanana profile: {prof.Files.Count} files with MD5, {prof.Images.Count} image(s)");
            Ok(
                !prof.Text.Contains("<span") && !prof.Text.Contains("&amp;"),
                "description converted from HTML to plain text");
            var small = prof.Files.OrderBy(f => f.Size).First();
            var tmp = Path.Combine(Temp("gb"), "f.zip");
            await GameBanana.DownloadVerified(small, tmp, null);
            Ok(GameBanana.Md5(tmp) == small.Md5, $"verified download of {small.Name} ({small.Size / 1048576} MB)");
            try {
                File.Delete(tmp);
                await GameBanana.DownloadVerified(small with { Md5 = new string('0', 32) }, tmp, null);
                Ok(false, "wrong checksum was accepted");
            } catch { Ok(!File.Exists(tmp), "a wrong checksum is rejected and the file deleted"); }
        });
        await Net("images", async () => {
            Ok(
                !ImageStore.Allowed("http://images.gamebanana.com/x.jpg")
                && !ImageStore.Allowed("https://evil.example.com/a.png")
                && !ImageStore.Allowed("https://raw.githubusercontent.com/someone/else/main/a.png")
                && ImageStore.Allowed("https://images.gamebanana.com/img/ss/mods/a.jpg"),
                "image host allow-list (https + known hosts only)");
            var (top, _) = await GameBanana.Search("", 1);
            int ok = 0, webp = 0;
            foreach (var m in top.Where(m => m.Thumb != null).Take(12)) {
                var b = await ImageStore.GetBytes(m.Thumb);
                if (b != null && b.Length > 100) ok++;
                if (ImageStore.IsWebP(b)) webp++;
            }
            Ok(ok == 12, $"thumbnails download through the image store ({ok}/12, {webp} are WebP)");
        });
        await Net("news", async () => {
            var news = await News.Get();
            Ok(
                news.Count >= 5
                && news.All(n => n.Image.Length == 0 || ImageStore.Allowed(n.Image)),
                $"news feed: {news.Count} items, images on allowed hosts");
            var ni = news.First(n => n.Image.Length > 0).Image;
            var nb = await ImageStore.GetBytes(ni);
            Ok(nb != null && nb.Length > 1000, $"the newest news image downloads ({nb?.Length ?? -1} bytes)");
        });
        await Net("loenn", async () => {
            var l = await Loenn.Latest();
            Ok(
                l.sha != null && Loenn.IsMyAsset(l.name),
                $"Lönn {l.tag} has a checksummed build for {Platform.Name}: {l.name}");
        });
        await Net("self-update", async () => {
            var r = await SelfUpdate.Check("0.0.1");
            Ok(
                r != null
                && r.Sha256.Length == 64
                && r.Url.StartsWith("https://github.com/" + SelfUpdate.Repo + "/releases/download/"),
                "an older build finds the GitHub release with a checksum");
            Ok(await SelfUpdate.Check("99.0.0") == null, "no update offered when already newest");
            if (r != null) {
                var exe = Path.Combine(Temp("selfupd"), "MountZantas.exe");
                Touch(exe, "old");
                var tmp = await SelfUpdate.Download(r, exe);
                Ok(Net_.Sha256(tmp) == r.Sha256, "the update downloads and matches its checksum");
                SelfUpdate.Swap(exe, tmp);
                Ok(
                    File.Exists(exe + ".old") && new FileInfo(exe).Length > 1000,
                    "swap keeps the old exe aside and installs the new one");
                SelfUpdate.Cleanup(exe);
                Ok(!File.Exists(exe + ".old"), "cleanup removes the old exe");
            }
        });
        await Net("extras", async () => {
            foreach (var m in Extras.All) {
                var l = await Extras.Latest(m);
                Ok(
                    l.sha is { Length: 64 } && l.asset.StartsWith(m.Name),
                    $"{m.Name} {l.tag}: {l.asset} has a published SHA-256");
            }
            var pcg = Extras.All.First(m => m.Name == "PCGHelper");
            var game = Temp("xgame");
            var inst = new Install { Name = "x", Path = game };
            var mods = Path.Combine(game, "Mods");
            Directory.CreateDirectory(mods);
            await Extras.Install(inst, pcg, new Config(), _ => { }, null);
            Ok(
                Mods.Scan(inst).Any(x => x.Name == "PCGHelper" && !x.IsDir && x.File == "PCGHelper.zip"),
                "PCGHelper installs as a verified zip into Mods");
            var g2 = Temp("xgame2");
            var i2 = new Install { Name = "y", Path = g2 };
            var dev = Path.Combine(g2, "Mods", "PCGHelper");
            Directory.CreateDirectory(dev);
            File.WriteAllText(Path.Combine(dev, "everest.yaml"), "- Name: PCGHelper\n  Version: 9.9.9\n");
            await Extras.Install(i2, pcg, new Config(), _ => { }, null);
            Ok(
                !File.Exists(Path.Combine(g2, "Mods", "PCGHelper.zip")),
                "a folder (dev) copy of the mod is left alone, no duplicate zip");
        });
        await Net("topo", async () => {
            Ok(Topo.Supported == Platform.Windows, "Topo is offered on Windows only");
            if (Topo.Supported) {
                var t = await Topo.Latest(false);
                Ok(t.sha != null, "Topo release has a checksum");
            } else {
                bool refused = false;
                try { await Topo.Install(false, _ => { }, null); } catch (Exception ex) { refused = ex.Message.Contains("Windows"); }
                Ok(refused, "Topo install is refused with a clear message off Windows");
            }
        });

        Console.WriteLine($"\n{pass} passed, {fail} failed on {Platform.Name}");
        foreach (var f in failures) Console.WriteLine("  FAILED: " + f);
        return fail == 0 ? 0 : 1;
    }

    // A minimal valid Celeste .bin: one room "a-00", 24x24 px (3x3 tiles), solid ring around an empty centre.
    static byte[] TinyMap() {
        var lookup = new List<string> { "Map", "levels", "level", "solids", "name", "x", "y", "width", "height", "innerText" };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write("CELESTE MAP");
        w.Write("TinyMap");
        w.Write((short)lookup.Count);
        foreach (var s in lookup) w.Write(s);
        void Name(string n) => w.Write((short)lookup.IndexOf(n));
        Name("Map");
        w.Write((byte)0);
        w.Write((short)1);                  // root: no attributes, one child
        Name("levels");
        w.Write((byte)0);
        w.Write((short)1);               // levels: one level
        Name("level");
        w.Write((byte)5);
        Name("name");
        w.Write((byte)6);
        w.Write("a-00");
        Name("x");
        w.Write((byte)2);
        w.Write((short)0);
        Name("y");
        w.Write((byte)2);
        w.Write((short)0);
        Name("width");
        w.Write((byte)2);
        w.Write((short)24);
        Name("height");
        w.Write((byte)2);
        w.Write((short)24);
        w.Write((short)1);                                                  // level: one child (solids)
        Name("solids");
        w.Write((byte)1);
        Name("innerText");
        w.Write((byte)6);
        w.Write("111\n101\n111");
        w.Write((short)0);
        return ms.ToArray();
    }
}

// `Net` is a method name above, so give the core's static class a second name here.
static class Net_ {
    public static Task<string> GitHubDigest(string url, string asset) => MountZantas.Net.GitHubDigest(url, asset);
    public static Task Download(string url, string dest) => MountZantas.Net.Download(url, dest);
    public static string Sha256(string f) => MountZantas.Net.Sha256(f);
}
